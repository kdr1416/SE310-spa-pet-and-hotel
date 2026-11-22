using Microsoft.EntityFrameworkCore;
using PetSpa.Api.Data;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Scheduling;
using PetSpa.Core.Enums;
using PetSpa.Core.Interfaces.Billing;

namespace PetSpa.Api.Services.Billing;

public sealed class DepositExpiryService(PetSpaDbContext context, DbContextOptions<PetSpaDbContext> contextOptions,
    TimeProvider clock) : IDepositExpiryService
{
    public async Task<int> ExpireOverdueDepositsAsync(DateTimeOffset now, int? targetBookingId, CancellationToken ct,
        IReadOnlyCollection<int>? targetKhungGioIds = null, IReadOnlyCollection<int>? targetRoomTypeIds = null)
    {
        if (context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("Quét hết hạn phải commit trước transaction của writer.");
        var filtered = targetKhungGioIds is not null || targetRoomTypeIds is not null;
        if (targetBookingId.HasValue && (targetBookingId <= 0 || filtered))
            throw new ArgumentException("Chọn booking hoặc tài nguyên, không kết hợp cả hai.");
        var slots = targetKhungGioIds?.Distinct().ToArray() ?? [];
        var rooms = targetRoomTypeIds?.Distinct().ToArray() ?? [];
        if (slots.Concat(rooms).Any(id => id <= 0)) throw new ArgumentException("Mã tài nguyên phải dương.");
        if (filtered && slots.Length == 0 && rooms.Length == 0) return 0;

        // Independent tracked graph: never save or detach the caller's pending changes or stale entities.
        await using var cleanupContext = new PetSpaDbContext(contextOptions);
        var cleanup = new DepositExpiryService(cleanupContext, contextOptions, clock);
        return await cleanup.SweepAsync(now, targetBookingId, filtered, slots, rooms, ct);
    }

    private async Task<int> SweepAsync(DateTimeOffset cutoff, int? targetBookingId, bool filtered,
        int[] targetSlots, int[] targetRooms, CancellationToken ct)
    {
        var total = 0;
        var lastId = 0;
        while (true)
        {
            var candidates = context.DonDatLichSet.AsNoTracking().Where(b => b.Id > lastId &&
                b.TrangThai == BookingState.PENDING && b.TienCocDonDatLich != null &&
                b.TienCocDonDatLich.TrangThaiYeuCau == DepositRequestState.CHO_THU &&
                b.TienCocDonDatLich.HetHanAt <= cutoff);
            if (targetBookingId.HasValue) candidates = candidates.Where(b => b.Id == targetBookingId.Value);
            if (filtered) candidates = candidates.Where(b => b.ChiTietDonDonDatLichItems.Any(p =>
                p.LoaiPhongId.HasValue && targetRooms.Contains(p.LoaiPhongId.Value) ||
                p.DichVuTrongDonChiTietDonItems.Any(s => s.TrangThai != OrderServiceState.HUY && targetSlots.Contains(s.KhungGioId))));
            var ids = await candidates.OrderBy(b => b.Id).Select(b => b.Id).Take(20).ToArrayAsync(ct);
            if (ids.Length == 0) return total;
            lastId = ids[^1];
            await using var tx = await context.Database.BeginTransactionAsync(ct);
            await context.Database.SqlQuery<int>($"SELECT id AS \"Value\" FROM don_dat_lich WHERE id = ANY({ids}) ORDER BY id FOR UPDATE").ToListAsync(ct);

            var intakeSlots = await context.DonDatLichSet.AsNoTracking().Where(b => ids.Contains(b.Id)).Select(b => b.KhungGioId).ToArrayAsync(ct);
            var serviceSlots = await context.DichVuTrongDonSet.AsNoTracking().Where(s => ids.Contains(s.ChiTietDon.DonDatLichId))
                .Select(s => s.KhungGioId).ToArrayAsync(ct);
            var slots = intakeSlots.Concat(serviceSlots).Distinct().Order().ToArray();
            await context.Database.SqlQuery<int>($"SELECT id AS \"Value\" FROM khung_gio_dat_lich WHERE id = ANY({slots}) ORDER BY id FOR UPDATE").ToListAsync(ct);
            await context.KhungGioDatLichSet.Where(s => slots.Contains(s.Id)).LoadAsync(ct);
            var rooms = await context.ChiTietDonSet.AsNoTracking().Where(p => ids.Contains(p.DonDatLichId) && p.LoaiPhongId.HasValue)
                .Select(p => p.LoaiPhongId!.Value).Distinct().ToArrayAsync(ct);
            await new HotelCapacityCalculator(context).LockRoomTypesAsync(rooms, ct);
            await context.Database.SqlQuery<int>($"SELECT id AS \"Value\" FROM tien_coc WHERE don_dat_lich_id = ANY({ids}) ORDER BY don_dat_lich_id FOR UPDATE").ToListAsync(ct);
            var bookings = await context.DonDatLichSet.Where(b => ids.Contains(b.Id))
                .Include(b => b.TienCocDonDatLich).ThenInclude(d => d!.GiaoDichThuTienCocItems)
                .Include(b => b.TienCocDonDatLich).ThenInclude(d => d!.HoanCocTienCocItems)
                .Include(b => b.ChiTietDonDonDatLichItems).ThenInclude(p => p.DichVuTrongDonChiTietDonItems)
                .AsSplitQuery().OrderBy(b => b.Id).ToListAsync(ct);
            var afterLock = clock.GetUtcNow();
            var now = afterLock > cutoff ? afterLock : cutoff;
            var changed = 0;
            foreach (var booking in bookings)
            {
                if (!IsDue(booking, now)) continue;
                await ExpireLockedBookingDepositAsync(booking, now, ct);
                changed++;
            }
            await context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            total += changed;
            // This context is private to the sweep; no caller tracking is cleared.
            context.ChangeTracker.Clear();
        }
    }

    public Task ExpireLockedBookingDepositAsync(DonDatLich booking, DateTimeOffset now, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (context.Database.CurrentTransaction is null) throw new InvalidOperationException("Hết hạn dưới lock cần transaction của caller.");
        if (!IsDue(booking, now)) return Task.CompletedTask;
        var deposit = booking.TienCocDonDatLich!;
        var received = deposit.GiaoDichThuTienCocItems.Sum(x => x.SoTien);
        var reserved = deposit.HoanCocTienCocItems.Sum(x => x.SoTien);
        if (reserved > received) throw new InvalidOperationException("Tổng hoàn cọc vượt thực nhận.");
        var latest = deposit.GiaoDichThuTienCocItems.OrderByDescending(x => x.XacNhanAt).ThenByDescending(x => x.Id).FirstOrDefault();
        if (received > reserved && (latest is null || latest.XacNhanBoiId <= 0))
            throw new InvalidOperationException("Khoản thu thiếu người xác nhận hợp lệ.");
        var releases = booking.ChiTietDonDonDatLichItems.SelectMany(p => p.DichVuTrongDonChiTietDonItems)
            .Where(s => s.TrangThai != OrderServiceState.HUY).GroupBy(s => s.KhungGioId)
            .Select(group => new { Slot = context.ChangeTracker.Entries<KhungGioDatLich>().SingleOrDefault(e => e.Entity.Id == group.Key)?.Entity
                    ?? throw new InvalidOperationException("Chưa nạp khung giờ đã khóa."),
                Pets = group.Select(s => s.ChiTietDonId).Distinct().Count(), Minutes = group.Sum(s => (long)s.ThoiLuongSnapshot) }).ToArray();
        if (releases.Any(x => x.Pets > x.Slot.SlotDaDat || x.Minutes > x.Slot.PhutDaGiu))
            throw new InvalidOperationException("Bộ đếm giữ chỗ Spa không khớp reservation.");
        foreach (var release in releases)
        {
            release.Slot.SlotDaDat -= release.Pets;
            release.Slot.PhutDaGiu -= checked((int)release.Minutes);
            release.Slot.UpdatedAt = now;
        }
        booking.TrangThai = BookingState.CANCELLED;
        booking.LoaiHuy = CancellationType.NO_SHOW;
        booking.LyDoHuy = "Quá hạn thanh toán tiền cọc 30 phút";
        booking.HuyAt = now;
        booking.HuyBoiId = null;
        booking.UpdatedAt = now;
        deposit.TrangThaiYeuCau = DepositRequestState.HET_HAN;
        deposit.KetThucAt = now;
        deposit.UpdatedAt = now;
        if (received > reserved) deposit.HoanCocTienCocItems.Add(new HoanCoc
        {
            RequestId = Guid.NewGuid(), SoTien = received - reserved, TrangThai = RefundState.CHO_HOAN,
            LyDo = "Hoàn tiền cọc do quá hạn giữ chỗ", TaoBoiId = latest!.XacNhanBoiId,
            CreatedAt = now, UpdatedAt = now
        });
        return Task.CompletedTask;
    }

    private static bool IsDue(DonDatLich booking, DateTimeOffset now) => booking.TrangThai == BookingState.PENDING &&
        booking.TienCocDonDatLich is { TrangThaiYeuCau: DepositRequestState.CHO_THU } deposit && deposit.HetHanAt <= now;
}
