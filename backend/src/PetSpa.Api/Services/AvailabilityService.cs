using Microsoft.EntityFrameworkCore;
using PetSpa.Api.Data;
using PetSpa.Core.DTOs.Availability;
using PetSpa.Core.Services;

namespace PetSpa.Api.Services;

public sealed class AvailabilityService(
    PetSpaDbContext context,
    SpaCapacityCalculator capacity,
    HotelCapacityCalculator hotelCapacity,
    TimeProvider clock) : IAvailabilityService
{
    public async Task<AvailabilityCheck> CheckAvailabilityAsync(AvailabilityQuery query, CancellationToken cancellationToken = default)
    {
        if (query.MaKhungGio <= 0 || query.NgayTra == default || query.ThuCung is not { Count: > 0 }
            || query.ThuCung.Any(pet => pet is null || (pet.DichVu.Count == 0 && !pet.MaLoaiPhong.HasValue)
                || pet.DichVu.Any(service => service is null
                    || (!service.MaDichVu.HasValue && !service.MaCombo.HasValue)
                    || (service.MaDichVu.HasValue && service.MaCombo.HasValue)
                    || (service.MaDichVu.HasValue && service.MaDichVu.Value <= 0)
                    || (service.MaCombo.HasValue && service.MaCombo.Value <= 0)
                    || service.MaKhungGio <= 0)
                || (pet.MaLoaiPhong.HasValue && pet.MaLoaiPhong.Value <= 0)))
        {
            return Invalid("Chọn ít nhất một dịch vụ Spa đơn hoặc phòng Hotel cho mỗi thú cưng.");
        }

        var lines = query.ThuCung.SelectMany((pet, index) =>
            pet.DichVu.Select(service => new
            {
                PetIndex = index,
                ServiceId = service.MaDichVu,
                ComboId = service.MaCombo,
                SlotId = service.MaKhungGio
            })).ToArray();

        var slotIds = lines.Select(line => line.SlotId).Append(query.MaKhungGio).Distinct().ToList();
        var slots = await context.KhungGioDatLichSet.AsNoTracking().Include(slot => slot.CaLamViec)
            .Where(slot => slotIds.Contains(slot.Id)).ToDictionaryAsync(slot => slot.Id, cancellationToken);
        if (!slots.TryGetValue(query.MaKhungGio, out var intake))
            return new AvailabilityCheck(null, "NOT_FOUND", "Không tìm thấy khung giờ đặt lịch.");

        if (slots.Count != slotIds.Count)
            return Invalid("Khung giờ dịch vụ không tồn tại.");

        var hasHotel = query.ThuCung.Any(pet => pet.MaLoaiPhong.HasValue);

        if (hasHotel)
        {
            if (query.NgayTra <= intake.Ngay)
            {
                return query.NgayTra < intake.Ngay
                    ? new AvailabilityCheck(null, "INVALID_DATE_RANGE", "Ngày trả phòng phải sau ngày nhận phòng.")
                    : Invalid("Đặt phòng Hotel phải có ngày trả sau ngày nhận.");
            }

            if (slots.Values.Any(slot => slot.Ngay < intake.Ngay || slot.Ngay > query.NgayTra
                || (slot.Ngay == intake.Ngay && slot.CaLamViec.GioBatDau < intake.CaLamViec.GioBatDau)))
            {
                return Invalid("Ca thực hiện phải nằm trong khoảng thời gian lưu trú và từ ca tiếp nhận trở đi.");
            }

            // D17: intake.Ngay and NgayTra cannot fall on a holiday
            var holidayDates = await context.NgayNghiSet.AsNoTracking()
                .Where(h => h.Ngay == intake.Ngay || h.Ngay == query.NgayTra)
                .Select(h => h.Ngay).ToListAsync(cancellationToken);
            if (holidayDates.Contains(intake.Ngay) || holidayDates.Contains(query.NgayTra))
            {
                return new AvailabilityCheck(null, "CAPACITY_EXCEEDED", "Ngày tiếp nhận hoặc ngày trả phòng rơi vào ngày nghỉ.");
            }
        }
        else
        {
            if (query.NgayTra != intake.Ngay
                || slots.Values.Any(slot => slot.Ngay != intake.Ngay || slot.CaLamViec.GioBatDau < intake.CaLamViec.GioBatDau))
            {
                return Invalid("Ca thực hiện phải từ ca tiếp nhận trở đi, trong cùng ngày.");
            }
        }

        var singleServiceIds = lines.Where(l => l.ServiceId.HasValue).Select(l => l.ServiceId!.Value).Distinct().ToList();
        var singleServices = await context.DichVuSet.AsNoTracking()
            .Where(service => singleServiceIds.Contains(service.Id) && service.IsActive
                && service.LoaiDichVu != "SPA_ADVANCED" && service.TenDichVu != "Cắt tỉa tạo kiểu"
                && service.ThoiLuongPhut > 0)
            .ToDictionaryAsync(service => service.Id, service => service.ThoiLuongPhut, cancellationToken);
        if (singleServices.Count != singleServiceIds.Count)
            return Invalid("Dịch vụ không tồn tại hoặc không thể đặt riêng.");

        var comboIds = lines.Where(l => l.ComboId.HasValue).Select(l => l.ComboId!.Value).Distinct().ToList();
        var combos = await context.ComboSet.AsNoTracking()
            .Where(combo => comboIds.Contains(combo.Id) && combo.IsActive)
            .Include(combo => combo.ChiTietComboComboItems).ThenInclude(ct => ct.DichVu)
            .ToDictionaryAsync(combo => combo.Id, cancellationToken);
        if (combos.Count != comboIds.Count)
            return Invalid("Combo không tồn tại hoặc đã ngừng hoạt động.");

        var comboDurations = combos.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.ChiTietComboComboItems.Where(ct => ct.DichVu.IsActive).Sum(ct => ct.DichVu.ThoiLuongPhut));

        var demands = lines.GroupBy(line => line.SlotId).ToDictionary(
            group => group.Key,
            group => new SpaSlotDemand(
                slots[group.Key],
                group.Select(line => line.PetIndex).Distinct().Count(),
                group.Sum(line => line.ServiceId.HasValue
                    ? (long)singleServices[line.ServiceId.Value]
                    : (long)comboDurations[line.ComboId!.Value])));

        demands.TryAdd(intake.Id, new SpaSlotDemand(intake, 0, 0));

        var now = clock.GetUtcNow();
        var spaResult = await capacity.CheckAsync(demands.Values, cancellationToken, readAsOfNow: now);
        var spaFailure = spaResult.Values.FirstOrDefault(slot => slot.CapacityError is not null);
        if (spaFailure is not null)
            return new AvailabilityCheck(null, "CAPACITY_EXCEEDED", spaFailure.CapacityError);

        var roomAvailabilities = new List<RoomAvailability>();
        if (hasHotel)
        {
            var roomTypeIds = query.ThuCung
                .Where(pet => pet.MaLoaiPhong.HasValue)
                .Select(pet => pet.MaLoaiPhong!.Value)
                .Distinct().ToList();

            var roomCheck = await hotelCapacity.CheckRoomAvailabilityAsync(
                intake.Ngay, query.NgayTra, roomTypeIds, excludeBookingId: null, now: now, ct: cancellationToken);

            roomAvailabilities.AddRange(roomCheck);

            foreach (var roomTypeId in roomTypeIds)
            {
                var neededRooms = query.ThuCung.Count(pet => pet.MaLoaiPhong == roomTypeId);
                for (var day = intake.Ngay; day < query.NgayTra; day = day.AddDays(1))
                {
                    var avail = roomCheck.FirstOrDefault(r => r.MaLoaiPhong == roomTypeId && r.Ngay == day);
                    if (avail is null || avail.SuatConLai < neededRooms)
                    {
                        return new AvailabilityCheck(null, "CAPACITY_EXCEEDED", "Không đủ công suất phòng đã chọn.");
                    }
                }
            }
        }

        var main = spaResult[intake.Id];
        return new AvailabilityCheck(new AvailabilityResponse
        {
            MaKhungGio = intake.Id,
            SlotConLai = main.SeatsLeft,
            PhutConLai = main.MinutesLeft,
            Phong = roomAvailabilities,
            CoTheDat = true
        });
    }

    private static AvailabilityCheck Invalid(string message) => new(null, "VALIDATION_FAILED", message);
}
