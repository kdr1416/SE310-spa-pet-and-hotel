using Microsoft.EntityFrameworkCore;
using PetSpa.Api.Data;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Billing;
using PetSpa.Core.DTOs.Booking;
using PetSpa.Core.DTOs.Care;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Enums;
using PetSpa.Core.Interfaces.Billing;
using PetSpa.Core.Interfaces.Care;

namespace PetSpa.Api.Services.Care;

public class HandoverService(
    PetSpaDbContext dbContext,
    CareOperation operation,
    IBillingSettlementHelper billingSettlement) : IHandoverService
{
    private const string HandoverPermission = "PET_HANDOVER";
    private const string CancelPermission = "PET_CANCEL_LINE";
    private const string EarlyHandoverPermission = "EARLY_HANDOVER_APPROVE";

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
        var command = new CareCommand(chiTietDonId, actorId, request.RequestId, HandoverPermission, HandoverPermission, traceId, fingerprint);
        var result = await operation.ExecuteAsync<BookingPet>(command, "Bàn giao thú cưng thành công.", null,
            (actor, now, _, _, ct) => ApplyHandoverAsync(chiTietDonId, actor, now, ct), cancellationToken);
        return new HandoverResult(result.StatusCode, result.Data, result.Message, result.ErrorCode, result.Errors);
    }

    public async Task<HandoverResult> CancelPetAsync(
        int chiTietDonId,
        int actorId,
        PetCancelRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<ApiErrorItem>();
        if (chiTietDonId <= 0)
            errors.Add(new ApiErrorItem { Field = "id", Message = "ID chi tiết đơn phải lớn hơn 0." });
        if (request.RequestId == Guid.Empty)
            errors.Add(new ApiErrorItem { Field = "requestId", Message = "requestId không được để trống." });
        if (string.IsNullOrWhiteSpace(request.LyDo) || request.LyDo.Trim().Length > 1000)
            errors.Add(new ApiErrorItem { Field = "lyDo", Message = "Lý do hủy phải từ 1 đến 1000 ký tự." });
        if (request.SoDemDaO < 0)
            errors.Add(new ApiErrorItem { Field = "soDemDaO", Message = "Số đêm đã ở không được âm." });
        if (request.MaThanhPhanDaLam is null ||
            request.MaThanhPhanDaLam.Any(id => id <= 0) ||
            request.MaThanhPhanDaLam.Distinct().Count() != request.MaThanhPhanDaLam.Length)
        {
            errors.Add(new ApiErrorItem { Field = "maThanhPhanDaLam", Message = "Danh sách mã thành phần đã làm phải dương và không trùng." });
        }
        if (errors.Count > 0)
            return new HandoverResult(400, Message: "Dữ liệu hủy thú cưng không hợp lệ.",
                ErrorCode: "VALIDATION_FAILED", Errors: errors);

        var preActor = await operation.FindEmployeeWithGrantAsync(actorId, CancelPermission, cancellationToken);
        if (preActor is null || preActor.VaiTro != Role.ADMIN)
        {
            return new HandoverResult(403, Message: "Bạn không có quyền hủy riêng thú cưng.", ErrorCode: "FORBIDDEN");
        }

        var fingerprint = CareOperation.Fingerprint(new
        {
            id = chiTietDonId,
            request.RequestId,
            request.LyDo,
            request.SoDemDaO,
            maThanhPhan = request.MaThanhPhanDaLam!.OrderBy(x => x).ToArray()
        });
        var command = new CareCommand(chiTietDonId, actorId, request.RequestId, CancelPermission, CancelPermission, traceId, fingerprint);
        var result = await operation.ExecuteAsync<BookingPet>(command, "Hủy riêng thú cưng thành công.", null,
            (actor, now, _, _, ct) => ApplyCancelPetAsync(chiTietDonId, actor, request, now, ct), cancellationToken);
        return new HandoverResult(result.StatusCode, result.Data, result.Message, result.ErrorCode, result.Errors);
    }

    public async Task<HandoverResult> EarlyHandoverAsync(
        int chiTietDonId,
        int actorId,
        ReasonCommand request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<ApiErrorItem>();
        if (chiTietDonId <= 0)
            errors.Add(new ApiErrorItem { Field = "id", Message = "ID chi tiết đơn phải lớn hơn 0." });
        if (request.RequestId == Guid.Empty)
            errors.Add(new ApiErrorItem { Field = "requestId", Message = "requestId không được để trống." });
        if (string.IsNullOrWhiteSpace(request.LyDo) || request.LyDo.Trim().Length > 1000)
            errors.Add(new ApiErrorItem { Field = "lyDo", Message = "Lý do nhận sớm phải từ 1 đến 1000 ký tự." });
        if (errors.Count > 0)
            return new HandoverResult(400, Message: "Dữ liệu nhận sớm thú cưng không hợp lệ.",
                ErrorCode: "VALIDATION_FAILED", Errors: errors);

        var preActor = await operation.FindEmployeeWithGrantAsync(actorId, EarlyHandoverPermission, cancellationToken);
        if (preActor is null || preActor.VaiTro != Role.ADMIN)
        {
            return new HandoverResult(403, Message: "Bạn không có quyền duyệt nhận sớm thú cưng.", ErrorCode: "FORBIDDEN");
        }

        var fingerprint = CareOperation.Fingerprint(new { id = chiTietDonId, request.RequestId, request.LyDo });
        var command = new CareCommand(chiTietDonId, actorId, request.RequestId, EarlyHandoverPermission, EarlyHandoverPermission, traceId, fingerprint);
        var result = await operation.ExecuteAsync<BookingPet>(command, "Nhận sớm thú cưng thành công.", null,
            (actor, now, _, _, ct) => ApplyEarlyHandoverAsync(chiTietDonId, actor, request, now, ct), cancellationToken);
        return new HandoverResult(result.StatusCode, result.Data, result.Message, result.ErrorCode, result.Errors);
    }

    public async Task<HandoverResult> ReturnCancelledPetAsync(
        int chiTietDonId,
        int actorId,
        BookingCommand request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<ApiErrorItem>();
        if (chiTietDonId <= 0)
            errors.Add(new ApiErrorItem { Field = "id", Message = "ID chi tiết đơn phải lớn hơn 0." });
        if (request.RequestId == Guid.Empty)
            errors.Add(new ApiErrorItem { Field = "requestId", Message = "requestId không được để trống." });
        if (errors.Count > 0)
            return new HandoverResult(400, Message: "Dữ liệu trả thú cưng đã hủy không hợp lệ.",
                ErrorCode: "VALIDATION_FAILED", Errors: errors);

        var fingerprint = CareOperation.Fingerprint(new { id = chiTietDonId, request.RequestId });
        var command = new CareCommand(chiTietDonId, actorId, request.RequestId, HandoverPermission, "PET_RETURN_CANCELLED", traceId, fingerprint);
        var result = await operation.ExecuteAsync<BookingPet>(command, "Trả thú cưng đã hủy thành công.", null,
            (actor, now, _, _, ct) => ApplyReturnCancelledPetAsync(chiTietDonId, actor, now, ct), cancellationToken);
        return new HandoverResult(result.StatusCode, result.Data, result.Message, result.ErrorCode, result.Errors);
    }

    private async Task<CareResult<BookingPet>> ApplyHandoverAsync(
        int petId, NhanVien actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var pet = await IncludeBookingSettlementGraph(CareProjection.IncludeDetails(dbContext.ChiTietDonSet))
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

        pet.TrangThaiChamSoc = PetCareState.DONE;
        pet.BanGiaoAt = now;
        pet.BanGiaoBoiId = actor.Id;
        pet.UpdatedAt = now;

        billingSettlement.TryTransitionBookingTerminalState(booking, now);

        return new CareResult<BookingPet>(200, CareProjection.Pet(pet, actor), AssigneeId: pet.NhanVienId);
    }

    private async Task<CareResult<BookingPet>> ApplyCancelPetAsync(
        int petId, NhanVien actor, PetCancelRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (actor.VaiTro != Role.ADMIN)
            return CareResult<BookingPet>.Forbidden();

        var pet = await IncludeBookingSettlementGraph(CareProjection.IncludeDetails(dbContext.ChiTietDonSet))
            .AsSplitQuery()
            .SingleOrDefaultAsync(p => p.Id == petId, cancellationToken);
        if (pet is null)
            return CareResult<BookingPet>.NotFound();

        var booking = pet.DonDatLich;
        var slotIds = pet.DichVuTrongDonChiTietDonItems
            .Select(l => l.KhungGioId)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        if (dbContext.Database.IsRelational())
        {
            if (slotIds.Length > 0)
            {
                await dbContext.Database.SqlQuery<int>($"""
                    SELECT id AS "Value" FROM khung_gio_dat_lich
                    WHERE id = ANY({slotIds})
                    ORDER BY id FOR UPDATE
                    """).ToListAsync(cancellationToken);
            }
            if (pet.LoaiPhongId.HasValue)
            {
                await dbContext.Database.SqlQuery<int>($"""
                    SELECT id AS "Value" FROM loai_phong
                    WHERE id = {pet.LoaiPhongId.Value}
                    FOR UPDATE
                    """).ToListAsync(cancellationToken);
            }
            await dbContext.Database.SqlQuery<int>($"""
                SELECT id AS "Value" FROM tien_coc
                WHERE don_dat_lich_id = {booking.Id}
                FOR UPDATE
                """).ToListAsync(cancellationToken);
        }

        if (booking.TrangThai is not (BookingState.CONFIRMED or BookingState.IN_PROGRESS))
            return CareResult<BookingPet>.InvalidTransition("Chỉ có thể hủy riêng thú cưng khi đơn đang ở trạng thái CONFIRMED hoặc IN_PROGRESS.");

        if (booking.HoaDonDonDatLichItems.Any(i => i.TrangThai != InvoiceState.REVOKED))
            return CareResult<BookingPet>.InvalidTransition("Đơn đặt lịch đã có hóa đơn hiệu lực.");

        if (pet.TrangThaiChamSoc is PetCareState.DONE or PetCareState.CANCELLED || pet.NhanSom)
            return CareResult<BookingPet>.InvalidTransition("Thú cưng đã hoàn tất, đã hủy hoặc đã nhận sớm.");

        var hasEarlyHandover = booking.ChiTietDonDonDatLichItems.Any(other => other.Id != pet.Id && other.NhanSom);
        if (hasEarlyHandover)
        {
            var hasAnotherPetInClinic = booking.ChiTietDonDonDatLichItems.Any(other =>
                other.Id != pet.Id &&
                other.CheckInAt != null &&
                other.TrangThaiChamSoc != PetCareState.DONE &&
                other.TrangThaiChamSoc != PetCareState.CANCELLED);
            if (!hasAnotherPetInClinic)
            {
                return CareResult<BookingPet>.InvalidTransition(
                    "Không thể hủy thú cưng cuối cùng còn lại tại tiệm sau khi đã có thú cưng nhận sớm mà đơn chưa thanh toán.");
            }
        }

        var serverCompletedIds = pet.DichVuTrongDonChiTietDonItems
            .SelectMany(s => s.ThanhPhanDichVuTrongDonDichVuTrongDonItems)
            .Where(c => c.TrangThai == OrderServiceState.HOAN_TAT)
            .Select(c => c.Id)
            .ToHashSet();
        if (!request.MaThanhPhanDaLam.ToHashSet().SetEquals(serverCompletedIds))
            return CareResult<BookingPet>.InvalidTransition("Danh sách thành phần dịch vụ đã hoàn tất không khớp dữ liệu hệ thống.");

        if (!pet.LoaiPhongId.HasValue || pet.TrangThaiChamSoc == PetCareState.NOT_ARRIVED || pet.CheckInAt is null)
        {
            if (request.SoDemDaO != 0)
                return CareResult<BookingPet>.InvalidTransition("Thú cưng chưa check-in hoặc không đặt phòng chỉ được ghi nhận 0 đêm đã ở.");
        }
        else if (request.SoDemDaO < 0 || request.SoDemDaO > (pet.SoDemChot ?? 0))
        {
            return CareResult<BookingPet>.InvalidTransition("Số đêm đã ở không hợp lệ so với số đêm đã chốt.");
        }

        if (slotIds.Length > 0)
        {
            var slots = await dbContext.KhungGioDatLichSet
                .Where(s => slotIds.Contains(s.Id))
                .OrderBy(s => s.Id)
                .ToDictionaryAsync(s => s.Id, cancellationToken);

            foreach (var group in pet.DichVuTrongDonChiTietDonItems.GroupBy(l => l.KhungGioId))
            {
                if (!slots.TryGetValue(group.Key, out var slot))
                    continue;

                var unperformedMinutes = 0;
                var hasCompletedInSlot = false;
                var hasPendingInSlot = false;

                foreach (var line in group)
                {
                    if (line.TrangThai == OrderServiceState.HOAN_TAT)
                    {
                        hasCompletedInSlot = true;
                        continue;
                    }

                    if (line.TrangThai != OrderServiceState.CHO_THUC_HIEN)
                        continue;

                    hasPendingInSlot = true;
                    if (!line.ComboId.HasValue)
                    {
                        unperformedMinutes += line.ThoiLuongSnapshot;
                    }
                    else
                    {
                        var components = line.ThanhPhanDichVuTrongDonDichVuTrongDonItems;
                        if (components.Any(c => c.TrangThai == OrderServiceState.HOAN_TAT))
                            hasCompletedInSlot = true;

                        if (components.Count > 0)
                        {
                            unperformedMinutes += components
                                .Where(c => c.TrangThai == OrderServiceState.CHO_THUC_HIEN)
                                .Sum(c => c.DichVu.ThoiLuongPhut);
                        }
                        else
                        {
                            unperformedMinutes += line.ThoiLuongSnapshot;
                        }
                    }
                }

                if (unperformedMinutes > 0)
                    slot.PhutDaGiu = Math.Max(0, slot.PhutDaGiu - unperformedMinutes);

                if (hasPendingInSlot && !hasCompletedInSlot && slot.SlotDaDat >= 1)
                    slot.SlotDaDat -= 1;

                slot.UpdatedAt = now;
            }
        }

        billingSettlement.SettleCancelledPetInMemory(booking, pet, serverCompletedIds, request.SoDemDaO, now);

        pet.TrangThaiChamSoc = PetCareState.CANCELLED;
        pet.HuyBoiId = actor.Id;
        pet.HuyAt = now;
        pet.LyDoHuy = request.LyDo.Trim();
        pet.UpdatedAt = now;
        if (pet.CheckInAt is null)
            pet.TraLaiAt = now;

        billingSettlement.TryTransitionBookingTerminalState(booking, now);

        return new CareResult<BookingPet>(
            200,
            CareProjection.Pet(pet, actor),
            AssigneeId: pet.NhanVienId,
            OverrideReason: request.LyDo.Trim(),
            Action: CancelPermission);
    }

    private async Task<CareResult<BookingPet>> ApplyEarlyHandoverAsync(
        int petId, NhanVien actor, ReasonCommand request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (actor.VaiTro != Role.ADMIN)
            return CareResult<BookingPet>.Forbidden();

        var pet = await CareProjection.IncludeDetails(dbContext.ChiTietDonSet)
            .Include(p => p.DonDatLich)
                .ThenInclude(b => b.ChiTietDonDonDatLichItems)
            .AsSplitQuery()
            .SingleOrDefaultAsync(p => p.Id == petId, cancellationToken);
        if (pet is null)
            return CareResult<BookingPet>.NotFound();

        var booking = pet.DonDatLich;
        if (booking.TrangThai != BookingState.IN_PROGRESS)
            return CareResult<BookingPet>.InvalidTransition("Đơn đặt lịch không ở trạng thái IN_PROGRESS để nhận sớm thú cưng.");
        if (pet.TrangThaiChamSoc != PetCareState.READY)
            return CareResult<BookingPet>.InvalidTransition($"Chỉ có thể nhận sớm khi thú cưng đang ở trạng thái READY (hiện tại: {pet.TrangThaiChamSoc}).");

        var hasAnotherPetInClinic = booking.ChiTietDonDonDatLichItems.Any(other =>
            other.Id != pet.Id &&
            other.TrangThaiChamSoc != PetCareState.CANCELLED &&
            other.TrangThaiChamSoc != PetCareState.DONE &&
            other.CheckInAt != null);
        if (!hasAnotherPetInClinic)
            return CareResult<BookingPet>.InvalidTransition("Không thể nhận sớm khi không còn thú cưng nào khác đang ở tại tiệm.");

        pet.TrangThaiChamSoc = PetCareState.DONE;
        pet.NhanSom = true;
        pet.NhanSomBoiId = actor.Id;
        pet.NhanSomLyDo = request.LyDo.Trim();
        pet.BanGiaoAt = now;
        pet.BanGiaoBoiId = actor.Id;
        pet.UpdatedAt = now;

        return new CareResult<BookingPet>(
            200,
            CareProjection.Pet(pet, actor),
            AssigneeId: pet.NhanVienId,
            OverrideReason: request.LyDo.Trim(),
            Action: EarlyHandoverPermission);
    }

    private async Task<CareResult<BookingPet>> ApplyReturnCancelledPetAsync(
        int petId, NhanVien actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var pet = await IncludeBookingSettlementGraph(CareProjection.IncludeDetails(dbContext.ChiTietDonSet))
            .AsSplitQuery()
            .SingleOrDefaultAsync(p => p.Id == petId, cancellationToken);
        if (pet is null)
            return CareResult<BookingPet>.NotFound();

        var booking = pet.DonDatLich;
        if (dbContext.Database.IsRelational())
        {
            await dbContext.Database.SqlQuery<int>($"""
                SELECT id AS "Value" FROM tien_coc
                WHERE don_dat_lich_id = {booking.Id}
                FOR UPDATE
                """).ToListAsync(cancellationToken);
            await dbContext.Database.SqlQuery<int>($"""
                SELECT id AS "Value" FROM hoa_don
                WHERE don_dat_lich_id = {booking.Id}
                FOR UPDATE
                """).ToListAsync(cancellationToken);
        }

        if (booking.TrangThai is not (BookingState.CONFIRMED or BookingState.IN_PROGRESS or BookingState.COMPLETED))
            return CareResult<BookingPet>.InvalidTransition("Trạng thái đơn đặt lịch không hợp lệ để trả thú cưng đã hủy.");
        if (pet.TrangThaiChamSoc != PetCareState.CANCELLED || pet.TraLaiAt is not null)
            return CareResult<BookingPet>.InvalidTransition("Thú cưng không ở trạng thái CANCELLED hoặc đã được trả lại trước đó.");

        if (booking.TongTamTinh > 0m)
        {
            if (!booking.HoaDonDonDatLichItems.Any(i => i.TrangThai == InvoiceState.PAID))
                return CareResult<BookingPet>.InvalidTransition("Đơn đặt lịch có phát sinh chi phí nhưng chưa có hóa đơn đã thanh toán (PAID).");
        }
        else
        {
            if (booking.HoaDonDonDatLichItems.Any(i => i.TrangThai == InvoiceState.UNPAID))
                return CareResult<BookingPet>.InvalidTransition("Đơn đặt lịch vẫn còn hóa đơn chưa thanh toán (UNPAID).");
        }

        pet.TraLaiAt = now;
        pet.BanGiaoBoiId = actor.Id;
        pet.UpdatedAt = now;

        billingSettlement.TryTransitionBookingTerminalState(booking, now);

        return new CareResult<BookingPet>(
            200,
            CareProjection.Pet(pet, actor),
            AssigneeId: pet.NhanVienId,
            Action: "PET_RETURN_CANCELLED");
    }

    private static IQueryable<ChiTietDon> IncludeBookingSettlementGraph(IQueryable<ChiTietDon> query) =>
        query.Include(p => p.DonDatLich)
                .ThenInclude(b => b.ChiTietDonDonDatLichItems)
                    .ThenInclude(cp => cp.DichVuTrongDonChiTietDonItems)
                        .ThenInclude(l => l.ThanhPhanDichVuTrongDonDichVuTrongDonItems)
            .Include(p => p.DonDatLich)
                .ThenInclude(b => b.PhuPhiDonDatLichItems)
            .Include(p => p.DonDatLich)
                .ThenInclude(b => b.HoaDonDonDatLichItems)
            .Include(p => p.DonDatLich)
                .ThenInclude(b => b.TienCocDonDatLich)
                    .ThenInclude(d => d!.GiaoDichThuTienCocItems)
            .Include(p => p.DonDatLich)
                .ThenInclude(b => b.TienCocDonDatLich)
                    .ThenInclude(d => d!.HoanCocTienCocItems);
}
