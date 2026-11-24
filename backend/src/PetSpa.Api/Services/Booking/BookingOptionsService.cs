using Microsoft.EntityFrameworkCore;
using Npgsql;
using PetSpa.Api.Data;
using PetSpa.Api.Services.Care;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Booking;

namespace PetSpa.Api.Services.Booking;

public sealed class BookingOptionsService(PetSpaDbContext db, TimeProvider clock)
{
    public async Task<ApiResponse<BookingOptions>> GetAsync(DateOnly ngay, CancellationToken cancellationToken)
    {
        var today = CareOperation.HcmDate(clock.GetUtcNow());
        if (ngay < today || ngay > today.AddDays(90))
            return ApiResponse<BookingOptions>.Fail("Ngày phải nằm trong 90 ngày tới.", "VALIDATION_FAILED",
                [new() { Field = "ngay", Message = "Chọn ngày từ hôm nay đến 90 ngày tới." }]);

        try
        {
            var holiday = await db.NgayNghiSet.AsNoTracking().AnyAsync(x => x.Ngay == ngay, cancellationToken);
            var shifts = await (
                from shift in db.CaLamViecSet.AsNoTracking()
                where shift.IsActive
                join slot in db.KhungGioDatLichSet.AsNoTracking().Where(x => x.Ngay == ngay)
                    on shift.Id equals slot.CaLamViecId into slots
                from slot in slots.DefaultIfEmpty()
                orderby shift.GioBatDau, shift.Id
                select new BookingShiftOption
                {
                    MaCa = shift.Id, TenCa = shift.TenCa,
                    GioBatDau = shift.GioBatDau, GioKetThuc = shift.GioKetThuc,
                    MaKhungGio = slot == null ? null : slot.Id,
                    MoNhanKhach = !holiday && slot != null && slot.MoNhanKhach
                }).ToListAsync(cancellationToken);
            return ApiResponse<BookingOptions>.Ok(new() { Ngay = ngay, Ca = shifts });
        }
        catch (Exception error) when (error is NpgsqlException { IsTransient: true } or
            InvalidOperationException { InnerException: NpgsqlException { IsTransient: true } })
        {
            return ApiResponse<BookingOptions>.Fail("Dịch vụ tạm thời không khả dụng.", "TEMPORARY_FAILURE");
        }
    }
}
