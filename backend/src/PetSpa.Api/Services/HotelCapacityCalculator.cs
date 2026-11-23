using Microsoft.EntityFrameworkCore;
using PetSpa.Api.Data;
using PetSpa.Core.DTOs.Availability;
using PetSpa.Core.Enums;

namespace PetSpa.Api.Services;

public sealed class HotelCapacityCalculator(PetSpaDbContext context)
{
    private static readonly TimeZoneInfo BusinessZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

    public async Task<IReadOnlyList<RoomAvailability>> CheckRoomAvailabilityAsync(DateOnly checkInDate,
        DateOnly checkOutDate, IReadOnlyCollection<int> roomTypeIds, int? excludeBookingId,
        DateTimeOffset now, CancellationToken ct)
    {
        if (checkOutDate <= checkInDate) throw new ArgumentException("Ngày trả phải sau ngày nhận.");
        if (roomTypeIds.Any(id => id <= 0)) throw new ArgumentException("Mã loại phòng phải dương.");
        if (roomTypeIds.Count == 0) return [];
        var ids = roomTypeIds.Distinct().ToList();
        var rooms = await context.LoaiPhongSet.AsNoTracking().Where(x => ids.Contains(x.Id)).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.SoLuongPhong }).ToListAsync(ct);
        var firstNight = new DateTimeOffset(checkInDate.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)).ToUniversalTime();
        var endNight = new DateTimeOffset(checkOutDate.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)).ToUniversalTime();
        var holds = await context.ChiTietDonSet.AsNoTracking()
            .Where(p => p.LoaiPhongId.HasValue && ids.Contains(p.LoaiPhongId.Value) && p.DonDatLichId != excludeBookingId &&
                (p.DonDatLich.TrangThai == BookingState.PENDING || p.DonDatLich.TrangThai == BookingState.CONFIRMED ||
                    p.DonDatLich.TrangThai == BookingState.IN_PROGRESS) &&
                p.DonDatLich.BatDauDuKien < endNight && p.DonDatLich.KetThucDuKien > firstNight &&
                !(p.DonDatLich.TrangThai == BookingState.PENDING && p.DonDatLich.TienCocDonDatLich != null &&
                    p.DonDatLich.TienCocDonDatLich.TrangThaiYeuCau == DepositRequestState.CHO_THU &&
                    p.DonDatLich.TienCocDonDatLich.HetHanAt <= now))
            .Select(p => new { RoomId = p.LoaiPhongId!.Value, Start = p.DonDatLich.BatDauDuKien,
                End = p.DonDatLich.KetThucDuKien, p.SoDemChot, p.SoDemDaO, p.CheckInAt, p.TraLaiAt, p.TrangThaiChamSoc })
            .ToListAsync(ct);
        var occupied = new Dictionary<(int Room, DateOnly Day), int>();
        foreach (var hold in holds)
        {
            var start = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(hold.Start, BusinessZone).DateTime);
            var end = hold.SoDemChot.HasValue ? start.AddDays(hold.SoDemChot.Value) :
                DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(hold.End, BusinessZone).DateTime);
            if (hold.TrangThaiChamSoc == PetCareState.CANCELLED)
            {
                if (hold.CheckInAt is null) continue;
                if (hold.TraLaiAt.HasValue) end = start.AddDays(hold.SoDemDaO ?? 0);
            }
            var from = start > checkInDate ? start : checkInDate;
            var to = end < checkOutDate ? end : checkOutDate;
            for (var day = from; day < to; day = day.AddDays(1))
                occupied[(hold.RoomId, day)] = occupied.GetValueOrDefault((hold.RoomId, day)) + 1;
        }
        var result = new List<RoomAvailability>();
        foreach (var room in rooms)
            for (var day = checkInDate; day < checkOutDate; day = day.AddDays(1))
                result.Add(new RoomAvailability { MaLoaiPhong = room.Id, Ngay = day,
                    SuatConLai = Math.Max(0, room.SoLuongPhong - occupied.GetValueOrDefault((room.Id, day))) });
        return result;
    }

    public async Task LockRoomTypesAsync(int[] roomTypeIds, CancellationToken ct)
    {
        if (context.Database.CurrentTransaction is null) throw new InvalidOperationException("Khóa phòng cần transaction của caller.");
        if (roomTypeIds.Any(id => id <= 0)) throw new ArgumentException("Mã loại phòng phải dương.");
        var ids = roomTypeIds.Distinct().Order().ToArray();
        if (ids.Length == 0) return;
        await context.Database.SqlQuery<int>($"SELECT id AS \"Value\" FROM loai_phong WHERE id = ANY({ids}) ORDER BY id FOR UPDATE")
            .ToListAsync(ct);
    }
}
