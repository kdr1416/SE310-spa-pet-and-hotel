using Microsoft.EntityFrameworkCore;
using PetSpa.Api.Data;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Care;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Enums;
using PetSpa.Core.Interfaces.Care;

namespace PetSpa.Api.Services.Care;

public class HandoverService(PetSpaDbContext dbContext, CareOperation operation) : IHandoverService
{
    private const string Permission = "PET_HANDOVER";

    public async Task<HandoverResult> HandoverAsync(
        int chiTietDonId,
        int actorId,
        Handover request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<ApiErrorItem>();
        if (chiTietDonId <= 0)
            errors.Add(new ApiErrorItem { Field = "id", Message = "ID chi tiết đơn phải lớn hơn 0." });
        if (request.RequestId == Guid.Empty)
            errors.Add(new ApiErrorItem { Field = "requestId", Message = "requestId không được để trống." });
        if (!request.XacNhanDaNhan)
            errors.Add(new ApiErrorItem { Field = "xacNhanDaNhan", Message = "Phải xác nhận khách hàng đã nhận thú cưng." });
        if (errors.Count > 0)
            return new HandoverResult(400, Message: "Dữ liệu bàn giao thú cưng không hợp lệ.",
                ErrorCode: "VALIDATION_FAILED", Errors: errors);

        var fingerprint = CareOperation.Fingerprint(new { chiTietDonId, request.RequestId, request.XacNhanDaNhan });
        var command = new CareCommand(chiTietDonId, actorId, request.RequestId, Permission, Permission, traceId, fingerprint);
        var result = await operation.ExecuteAsync<BookingPet>(command, "Bàn giao thú cưng thành công.", null,
            (actor, now, _, _, ct) => ApplyHandoverAsync(chiTietDonId, actor, now, ct), cancellationToken);
        return new HandoverResult(result.StatusCode, result.Data, result.Message, result.ErrorCode, result.Errors);
    }

    private async Task<CareResult<BookingPet>> ApplyHandoverAsync(
        int petId, NhanVien actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var pet = await CareProjection.IncludeDetails(dbContext.ChiTietDonSet)
            .Include(pet => pet.DonDatLich)
            .AsSplitQuery()
            .SingleOrDefaultAsync(pet => pet.Id == petId, cancellationToken);
        if (pet is null)
            return CareResult<BookingPet>.NotFound();

        var booking = pet.DonDatLich;
        if (booking.TrangThai != BookingState.IN_PROGRESS)
            return CareResult<BookingPet>.InvalidTransition("Đơn đặt lịch không ở trạng thái IN_PROGRESS để bàn giao thú cưng.");
        if (pet.TrangThaiChamSoc != PetCareState.READY)
            return CareResult<BookingPet>.InvalidTransition($"Không thể bàn giao khi thú cưng đang ở trạng thái {pet.TrangThaiChamSoc}.");

        // Checkout and handover both acquire the booking row before the invoice row.
        var hasPaidInvoice = dbContext.Database.IsRelational()
            ? (await dbContext.Database.SqlQuery<int>($"""
                SELECT id AS "Value" FROM hoa_don
                WHERE don_dat_lich_id = {booking.Id} AND trang_thai = 'PAID'
                FOR UPDATE
                """).ToListAsync(cancellationToken)).Count > 0
            : await dbContext.HoaDonSet.AnyAsync(invoice =>
                invoice.DonDatLichId == booking.Id && invoice.TrangThai == InvoiceState.PAID, cancellationToken);
        if (!hasPaidInvoice)
            return CareResult<BookingPet>.InvalidTransition("Hóa đơn của đơn đặt lịch chưa được thanh toán (PAID).");

        var otherPetsRemain = await dbContext.ChiTietDonSet.AnyAsync(other =>
            other.DonDatLichId == booking.Id && other.Id != pet.Id &&
            other.TrangThaiChamSoc != PetCareState.CANCELLED && other.TrangThaiChamSoc != PetCareState.DONE, cancellationToken);
        pet.TrangThaiChamSoc = PetCareState.DONE;
        pet.BanGiaoAt = now;
        pet.BanGiaoBoiId = actor.Id;
        pet.UpdatedAt = now;

        if (!otherPetsRemain)
        {
            booking.TrangThai = BookingState.COMPLETED;
            booking.HoanTatAt = now;
            booking.UpdatedAt = now;
        }

        return new CareResult<BookingPet>(200, CareProjection.Pet(pet, actor), AssigneeId: pet.NhanVienId);
    }
}
