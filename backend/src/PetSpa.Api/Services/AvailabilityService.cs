using Microsoft.EntityFrameworkCore;
using PetSpa.Api.Data;
using PetSpa.Core.DTOs.Availability;
using PetSpa.Core.Services;

namespace PetSpa.Api.Services;

public sealed class AvailabilityService(PetSpaDbContext context, SpaCapacityCalculator capacity) : IAvailabilityService
{
    public async Task<AvailabilityCheck> CheckAvailabilityAsync(AvailabilityQuery query, CancellationToken cancellationToken = default)
    {
        if (query.MaKhungGio <= 0 || query.NgayTra == default || query.ThuCung is not { Count: > 0 }
            || query.ThuCung.Any(pet => pet is null || pet.MaLoaiPhong.HasValue || pet.DichVu is not { Count: > 0 }
                || pet.DichVu.Any(service => service is null || service.MaCombo.HasValue
                    || service.MaDichVu is not > 0 || service.MaKhungGio <= 0)))
            return Invalid("Chọn ít nhất một dịch vụ Spa đơn cho mỗi thú cưng.");

        var lines = query.ThuCung.SelectMany((pet, index) =>
            pet.DichVu.Select(service => new { PetIndex = index, ServiceId = service.MaDichVu!.Value, SlotId = service.MaKhungGio }))
            .ToArray();
        var slotIds = lines.Select(line => line.SlotId).Append(query.MaKhungGio).Distinct().ToArray();
        var slots = await context.KhungGioDatLichSet.AsNoTracking().Include(slot => slot.CaLamViec)
            .Where(slot => slotIds.Contains(slot.Id)).ToDictionaryAsync(slot => slot.Id, cancellationToken);
        if (!slots.TryGetValue(query.MaKhungGio, out var intake))
            return new AvailabilityCheck(null, "NOT_FOUND", "Không tìm thấy khung giờ đặt lịch.");

        if (slots.Count != slotIds.Length || query.NgayTra != intake.Ngay
            || slots.Values.Any(slot => slot.Ngay != intake.Ngay || slot.CaLamViec.GioBatDau < intake.CaLamViec.GioBatDau))
            return Invalid("Ca thực hiện phải từ ca tiếp nhận trở đi, trong cùng ngày.");

        var serviceIds = lines.Select(line => line.ServiceId).Distinct().ToArray();
        var durations = await context.DichVuSet.AsNoTracking()
            .Where(service => serviceIds.Contains(service.Id) && service.IsActive
                && service.LoaiDichVu != "SPA_ADVANCED" && service.TenDichVu != "Cắt tỉa tạo kiểu"
                && service.ThoiLuongPhut > 0)
            .ToDictionaryAsync(service => service.Id, service => service.ThoiLuongPhut, cancellationToken);
        if (durations.Count != serviceIds.Length)
            return Invalid("Dịch vụ không tồn tại hoặc không thể đặt riêng.");

        var demands = lines.GroupBy(line => line.SlotId).ToDictionary(group => group.Key, group => new SpaSlotDemand(
            slots[group.Key], group.Select(line => line.PetIndex).Distinct().Count(),
            group.Sum(line => (long)durations[line.ServiceId])));
        // A booking with work only in a later shift still needs an open intake shift.
        demands.TryAdd(intake.Id, new SpaSlotDemand(intake, 0, 0));
        var result = await capacity.CheckAsync(demands.Values, cancellationToken);
        var failure = result.Values.FirstOrDefault(slot => slot.CapacityError is not null);
        if (failure is not null)
            return new AvailabilityCheck(null, "CAPACITY_EXCEEDED", failure.CapacityError);

        var main = result[intake.Id];
        return new AvailabilityCheck(new AvailabilityResponse
        {
            MaKhungGio = intake.Id, SlotConLai = main.SeatsLeft, PhutConLai = main.MinutesLeft, CoTheDat = true
        });
    }

    private static AvailabilityCheck Invalid(string message) => new(null, "VALIDATION_FAILED", message);
}
