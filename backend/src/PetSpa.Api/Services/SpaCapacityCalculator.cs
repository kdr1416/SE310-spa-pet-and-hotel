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
        IReadOnlyCollection<SpaSlotDemand> demands, CancellationToken cancellationToken)
    {
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
            var seatsLeft = Math.Max(0, slot.TongSlot - slot.SlotDaDat);
            var minutesLeft = (int)Math.Max(0, Math.Min(slot.TranPhut, (long)staffCount * shiftMinutes) - slot.PhutDaGiu);
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
