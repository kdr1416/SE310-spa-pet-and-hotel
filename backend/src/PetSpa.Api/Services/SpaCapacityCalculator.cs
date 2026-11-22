using Microsoft.EntityFrameworkCore;
using PetSpa.Api.Data;
using PetSpa.Core.Entities.Scheduling;
using PetSpa.Core.Enums;

namespace PetSpa.Api.Services;

public sealed record SpaSlotDemand(KhungGioDatLich Slot, int Pets, long Minutes);

public sealed record SpaSlotCapacity(int SeatsLeft, int MinutesLeft, string? CapacityError);

public sealed class SpaCapacityCalculator(PetSpaDbContext context)
{
    // Callers that reserve capacity load the slots after obtaining their transaction locks.
    public async Task<IReadOnlyDictionary<int, SpaSlotCapacity>> CheckAsync(
        IReadOnlyCollection<SpaSlotDemand> demands, CancellationToken cancellationToken, DateTimeOffset? readAsOfNow = null)
    {
        var effectiveUsage = new Dictionary<int, (int Pets, long Minutes)>();
        if (readAsOfNow.HasValue)
        {
            var slotIds = demands.Select(x => x.Slot.Id).Distinct().ToArray();
            var cutoff = readAsOfNow.Value;
            var expired = context.DichVuTrongDonSet.Where(s => s.TrangThai != OrderServiceState.HUY &&
                s.ChiTietDon.DonDatLich.TrangThai == BookingState.PENDING &&
                s.ChiTietDon.DonDatLich.TienCocDonDatLich != null &&
                s.ChiTietDon.DonDatLich.TienCocDonDatLich.TrangThaiYeuCau == DepositRequestState.CHO_THU &&
                s.ChiTietDon.DonDatLich.TienCocDonDatLich.HetHanAt <= cutoff);
            // Counters and compensation share one PostgreSQL statement snapshot, even during worker cleanup.
            var usage = await context.KhungGioDatLichSet.AsNoTracking().Where(slot => slotIds.Contains(slot.Id))
                .Select(slot => new { slot.Id, slot.SlotDaDat, slot.PhutDaGiu,
                    ExpiredPets = expired.Where(s => s.KhungGioId == slot.Id).Select(s => s.ChiTietDonId).Distinct().Count(),
                    ExpiredMinutes = expired.Where(s => s.KhungGioId == slot.Id).Sum(s => (long?)s.ThoiLuongSnapshot) ?? 0L })
                .ToListAsync(cancellationToken);
            foreach (var slot in usage)
            {
                if (slot.ExpiredPets > slot.SlotDaDat || slot.ExpiredMinutes > slot.PhutDaGiu)
                    throw new InvalidOperationException("Bộ đếm giữ chỗ Spa không khớp reservation.");
                effectiveUsage[slot.Id] = (slot.SlotDaDat - slot.ExpiredPets, slot.PhutDaGiu - slot.ExpiredMinutes);
            }
        }
        var days = demands.Select(demand => demand.Slot.Ngay).Distinct().ToArray();
        var shiftIds = demands.Select(demand => demand.Slot.CaLamViecId).Distinct().ToArray();
        var holidays = await context.NgayNghiSet.AsNoTracking()
            .Where(holiday => days.Contains(holiday.Ngay))
            .Select(holiday => holiday.Ngay).ToListAsync(cancellationToken);
        var staffing = await context.DangKyCaSet.AsNoTracking()
            .Where(registration => days.Contains(registration.Ngay) && shiftIds.Contains(registration.CaLamViecId)
                && registration.TrangThai == ShiftRegistrationState.APPROVED && registration.NhanVien.IsActive
                && registration.NhanVien.NhanVienQuyenNhanVienItems.Any(grant => grant.MaQuyen == "CARE_UPDATE"))
            .Select(registration => new { registration.Ngay, registration.CaLamViecId, registration.NhanVienId })
            .Distinct().ToListAsync(cancellationToken);
        var staffByShift = staffing.ToLookup(staff => (staff.Ngay, staff.CaLamViecId));

        return demands.ToDictionary(demand => demand.Slot.Id, demand =>
        {
            var slot = demand.Slot;
            var shift = slot.CaLamViec;
            var staffCount = staffByShift[(slot.Ngay, slot.CaLamViecId)].Count();
            var shiftMinutes = (int)(shift.GioKetThuc - shift.GioBatDau).TotalMinutes;
            var held = effectiveUsage.GetValueOrDefault(slot.Id, (slot.SlotDaDat, slot.PhutDaGiu));
            var seatsLeft = Math.Max(0, slot.TongSlot - held.Pets);
            var minutesLeft = (int)Math.Max(0, Math.Min(slot.TranPhut, (long)staffCount * shiftMinutes) - held.Minutes);
            var error = (slot.MoNhanKhach && shift.IsActive, holidays.Contains(slot.Ngay), staffCount > 0,
                demand.Pets <= seatsLeft, demand.Minutes <= minutesLeft) switch
            {
                (false, _, _, _, _) => "Khung giờ đang đóng tiếp nhận.",
                (_, true, _, _, _) => "Ngày đã chọn là ngày nghỉ.",
                (_, _, false, _, _) => "Ca chưa có nhân viên chăm sóc được phê duyệt.",
                (_, _, _, false, _) => "Số thú cưng vượt quá sức chứa của ca.",
                (_, _, _, _, false) => "Tổng thời gian dịch vụ vượt quá số phút còn lại của ca.",
                _ => null
            };
            return new SpaSlotCapacity(seatsLeft, minutesLeft, error);
        });
    }
}
