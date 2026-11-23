using Microsoft.EntityFrameworkCore;
using PetSpa.Api.Data;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Billing;
using PetSpa.Core.DTOs.Booking;
using PetSpa.Core.DTOs.Care;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Enums;
using PetSpa.Core.Interfaces.Care;

namespace PetSpa.Api.Services.Care;

public class CareService(PetSpaDbContext dbContext, CareOperation operation) : ICareService
{
    public async Task<CareActionResult> ExecuteCareActionAsync(
        int chiTietDonId,
        int actorId,
        CareAction request,
        CarePhotoInput? photo,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateCareActionRequest(chiTietDonId, request, photo);
        if (errors.Count > 0)
            return new CareActionResult(400, Message: "Dữ liệu cập nhật chăm sóc không hợp lệ.",
                ErrorCode: "VALIDATION_FAILED", Errors: errors);

        var result = CareOperation.ValidatePhoto<BookingPet>(photo?.Content, "anhThanhPham");
        if (result is null)
        {
            var fingerprint = CareOperation.Fingerprint(new
            {
                chiTietDonId,
                request.RequestId,
                request.HanhDong,
                GhiChu = request.GhiChu?.Trim(),
                request.MaNgoaiLeAnh,
                PhotoFileName = photo?.FileName,
                PhotoLength = photo?.Length,
                PhotoHash = CareOperation.PhotoFingerprint(photo?.Content)
            });
            var command = new CareCommand(chiTietDonId, actorId, request.RequestId, "CARE_UPDATE", "CARE_ACTION", traceId, fingerprint);
            result = await operation.ExecuteAsync<BookingPet>(command, "Cập nhật trạng thái chăm sóc thành công.", photo?.Content,
                (actor, now, path, preflight, ct) =>
                    ApplyCareActionAsync(chiTietDonId, actor, request, photo is not null, now, path, preflight, ct),
                cancellationToken);
        }

        return new CareActionResult(result.StatusCode, result.Data, result.Message, result.ErrorCode, result.Errors);
    }

    public async Task<CareLogCreateResult> CreateCareLogAsync(
        int chiTietDonId,
        int actorId,
        CareLogCreate request,
        CarePhotoInput? photo,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateCareLogRequest(chiTietDonId, request, photo);
        if (errors.Count > 0)
            return new CareLogCreateResult(400, Message: "Dữ liệu nhật ký chăm sóc không hợp lệ.",
                ErrorCode: "VALIDATION_FAILED", Errors: errors);

        var result = CareOperation.ValidatePhoto<CareLog>(photo?.Content, "anh");
        if (result is null)
        {
            var fingerprint = CareOperation.Fingerprint(new
            {
                chiTietDonId,
                request.RequestId,
                GhiChu = request.GhiChu?.Trim(),
                request.CanNangTheoDoi,
                PhotoFileName = photo?.FileName,
                PhotoLength = photo?.Length,
                PhotoHash = CareOperation.PhotoFingerprint(photo?.Content)
            });
            var command = new CareCommand(chiTietDonId, actorId, request.RequestId, "PHOTO_UPLOAD", "CARE_LOG_CREATE", traceId, fingerprint);
            result = await operation.ExecuteAsync<CareLog>(command, "Ghi nhật ký chăm sóc thành công.", photo?.Content,
                (actor, now, path, preflight, ct) => WriteCareLogAsync(chiTietDonId, actor, request, now, path, preflight, ct),
                cancellationToken);
        }

        return new CareLogCreateResult(result.StatusCode, result.Data, result.Message, result.ErrorCode, result.Errors);
    }

    public async Task<OrderServiceActionResult> CompleteComboComponentAsync(
        int orderServiceId,
        int componentId,
        int actorId,
        BookingCommand request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<ApiErrorItem>();
        if (orderServiceId <= 0)
            errors.Add(new ApiErrorItem { Field = "id", Message = "ID dịch vụ trong đơn phải lớn hơn 0." });
        if (componentId <= 0)
            errors.Add(new ApiErrorItem { Field = "componentId", Message = "ID thành phần dịch vụ phải lớn hơn 0." });
        if (request.RequestId == Guid.Empty)
            errors.Add(new ApiErrorItem { Field = "requestId", Message = "requestId không được để trống." });

        if (errors.Count > 0)
            return new OrderServiceActionResult(400, Message: "Dữ liệu hoàn tất thành phần combo không hợp lệ.",
                ErrorCode: "VALIDATION_FAILED", Errors: errors);

        if (await operation.FindEmployeeWithGrantAsync(actorId, "CARE_UPDATE", cancellationToken) is null)
            return new OrderServiceActionResult(403, Message: "Bạn không có quyền thực hiện thao tác cho thú cưng này.", ErrorCode: "FORBIDDEN");

        var petId = await dbContext.DichVuTrongDonSet.AsNoTracking()
            .Where(line => line.Id == orderServiceId)
            .Select(line => (int?)line.ChiTietDonId)
            .SingleOrDefaultAsync(cancellationToken);
        if (petId is null)
            return new OrderServiceActionResult(404, Message: "Không tìm thấy dịch vụ trong đơn.", ErrorCode: "NOT_FOUND");

        var fingerprint = CareOperation.Fingerprint(new
        {
            orderServiceId,
            componentId,
            request.RequestId
        });
        var command = new CareCommand(petId.Value, actorId, request.RequestId, "CARE_UPDATE", "COMBO_COMPONENT_COMPLETE", traceId, fingerprint);
        var result = await operation.ExecuteAsync<OrderService>(command, "Hoàn tất thành phần combo thành công.", null,
            (actor, now, _, _, ct) => ApplyCompleteComboComponentAsync(orderServiceId, componentId, actor, now, ct),
            cancellationToken);

        return new OrderServiceActionResult(result.StatusCode, result.Data, result.Message, result.ErrorCode, result.Errors);
    }

    public async Task<OrderServiceActionResult> ExecuteExtraServiceActionAsync(
        int orderServiceId,
        int actorId,
        ServiceActionRequest request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateExtraServiceActionRequest(orderServiceId, request);
        if (errors.Count > 0)
            return new OrderServiceActionResult(400, Message: "Dữ liệu cập nhật dịch vụ phát sinh không hợp lệ.",
                ErrorCode: "VALIDATION_FAILED", Errors: errors);

        var permission = request.HanhDong == "COMPLETE" ? "CARE_UPDATE" : "PET_CANCEL_LINE";
        if (await operation.FindEmployeeWithGrantAsync(actorId, permission, cancellationToken) is null)
            return new OrderServiceActionResult(403, Message: "Bạn không có quyền thực hiện thao tác cho thú cưng này.", ErrorCode: "FORBIDDEN");

        var petId = await dbContext.DichVuTrongDonSet.AsNoTracking()
            .Where(line => line.Id == orderServiceId)
            .Select(line => (int?)line.ChiTietDonId)
            .SingleOrDefaultAsync(cancellationToken);
        if (petId is null)
            return new OrderServiceActionResult(404, Message: "Không tìm thấy dịch vụ phát sinh trong đơn.", ErrorCode: "NOT_FOUND");

        var successMessage = request.HanhDong == "COMPLETE"
            ? "Hoàn tất dịch vụ phát sinh thành công."
            : "Hủy dịch vụ phát sinh thành công.";
        var fingerprint = CareOperation.Fingerprint(new
        {
            orderServiceId,
            request.RequestId,
            request.HanhDong,
            LyDo = request.LyDo?.Trim()
        });
        var command = new CareCommand(petId.Value, actorId, request.RequestId, permission, "EXTRA_SERVICE_ACTION", traceId, fingerprint);
        var result = await operation.ExecuteAsync<OrderService>(command, successMessage, null,
            (actor, now, _, _, ct) => ApplyExtraServiceActionAsync(orderServiceId, actor, request, now, ct),
            cancellationToken);

        return new OrderServiceActionResult(result.StatusCode, result.Data, result.Message, result.ErrorCode, result.Errors);
    }

    private async Task<CareResult<BookingPet>> ApplyCareActionAsync(
        int petId, NhanVien actor, CareAction request, bool hasPhoto,
        DateTimeOffset now, string? objectPath, bool preflight, CancellationToken cancellationToken)
    {
        var pet = await CareProjection.IncludeDetails(dbContext.ChiTietDonSet)
            .Include(pet => pet.DonDatLich)
            .AsSplitQuery()
            .SingleOrDefaultAsync(pet => pet.Id == petId, cancellationToken);
        if (pet is null)
            return CareResult<BookingPet>.NotFound();

        var (allowed, overrideReason) = await AuthorizeAssignmentAsync(actor, pet, now, cancellationToken);
        if (!allowed)
            return CareResult<BookingPet>.Forbidden();
        if (pet.DonDatLich.TrangThai != BookingState.IN_PROGRESS)
            return CareResult<BookingPet>.InvalidTransition("Đơn đặt lịch không ở trạng thái IN_PROGRESS để thực hiện chăm sóc.");

        var action = request.HanhDong!.Value;
        var targetState = ResolveTargetCareState(pet.TrangThaiChamSoc, action);
        if (targetState is null)
            return CareResult<BookingPet>.InvalidTransition(
                $"Không thể thực hiện hành động {action} khi thú cưng đang ở trạng thái {pet.TrangThaiChamSoc}.");

        AuditLog? photoException = null;
        if (action == CareActionType.READY)
        {
            var incompleteComboOrExtra = pet.DichVuTrongDonChiTietDonItems.Any(line =>
                line.TrangThai != OrderServiceState.HUY &&
                (line.ComboId.HasValue || line.ThanhPhanDichVuTrongDonDichVuTrongDonItems.Count > 0 || line.LaPhatSinh) &&
                (line.TrangThai == OrderServiceState.CHO_THUC_HIEN ||
                    line.ThanhPhanDichVuTrongDonDichVuTrongDonItems.Any(part => part.TrangThai == OrderServiceState.CHO_THUC_HIEN)));
            if (incompleteComboOrExtra)
                return CareResult<BookingPet>.InvalidTransition("Dịch vụ combo hoặc dịch vụ phát sinh chưa hoàn tất trước khi chuyển READY.");

            if (!hasPhoto)
            {
                photoException = await dbContext.AuditLogSet.AsNoTracking().SingleOrDefaultAsync(audit =>
                    audit.Id == request.MaNgoaiLeAnh && audit.HanhDong == "PHOTO_EXCEPTION_APPROVE" &&
                    audit.LoaiDoiTuong == "ChiTietDon" && audit.DoiTuongId == pet.Id && audit.ActorLoai == "ADMIN",
                    cancellationToken);
                if (photoException is null)
                    return CareResult<BookingPet>.InvalidTransition("Cần ảnh thành phẩm hoặc ngoại lệ ảnh hợp lệ để chuyển sang READY.");
            }
        }
        if (preflight)
            return new CareResult<BookingPet>(200);

        pet.TrangThaiChamSoc = targetState.Value;
        pet.UpdatedAt = now;
        if (action == CareActionType.READY)
        {
            pet.ReadyAt = now;
            foreach (var line in pet.DichVuTrongDonChiTietDonItems.Where(line =>
                         line.TrangThai == OrderServiceState.CHO_THUC_HIEN &&
                         !line.ComboId.HasValue &&
                         !line.LaPhatSinh &&
                         line.ThanhPhanDichVuTrongDonDichVuTrongDonItems.Count == 0))
            {
                line.TrangThai = OrderServiceState.HOAN_TAT;
                line.HoanTatAt = now;
                line.UpdatedAt = now;
            }
        }

        if (action == CareActionType.START && objectPath is null)
            objectPath = pet.NhatKyChamSocChiTietDonItems.Where(log => log.LoaiNhatKy == "CHECK_IN")
                .OrderByDescending(log => log.GhiNhanAt).ThenByDescending(log => log.Id)
                .Select(log => log.ObjectPath).FirstOrDefault();

        var note = request.GhiChu?.Trim();
        if (note is null && photoException is not null)
            note = string.IsNullOrWhiteSpace(photoException.LyDo) ? "Hoàn tất chăm sóc (ngoại lệ ảnh được duyệt)" : photoException.LyDo;
        if (objectPath is not null || note is not null)
            dbContext.NhatKyChamSocSet.Add(NewLog(pet, actor, $"CARE_{action}", now, note, objectPath));

        await dbContext.SaveChangesAsync(cancellationToken);
        return new CareResult<BookingPet>(200, CareProjection.Pet(pet, actor), AssigneeId: pet.NhanVienId,
            OverrideReason: overrideReason, Action: action.ToString());
    }

    private async Task<CareResult<CareLog>> WriteCareLogAsync(
        int petId, NhanVien actor, CareLogCreate request, DateTimeOffset now, string? objectPath,
        bool preflight, CancellationToken cancellationToken)
    {
        var pet = await dbContext.ChiTietDonSet.Include(pet => pet.DonDatLich)
            .SingleOrDefaultAsync(pet => pet.Id == petId, cancellationToken);
        if (pet is null)
            return CareResult<CareLog>.NotFound();

        var (allowed, overrideReason) = await AuthorizeAssignmentAsync(actor, pet, now, cancellationToken);
        if (!allowed)
            return CareResult<CareLog>.Forbidden();
        if (pet.DonDatLich.TrangThai != BookingState.IN_PROGRESS ||
            pet.TrangThaiChamSoc is not (PetCareState.WAITING or PetCareState.IN_PROGRESS or PetCareState.PAUSED or PetCareState.READY))
            return CareResult<CareLog>.InvalidTransition($"Không thể ghi nhật ký khi thú cưng đang ở trạng thái {pet.TrangThaiChamSoc}.");
        if (preflight)
            return new CareResult<CareLog>(200);

        var log = NewLog(pet, actor, "CARE_LOG", now, request.GhiChu?.Trim(), objectPath);
        log.CanNangGhiNhan = request.CanNangTheoDoi;
        dbContext.NhatKyChamSocSet.Add(log);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CareResult<CareLog>(201, CareProjection.Log(log, actor),
            AssigneeId: pet.NhanVienId, OverrideReason: overrideReason);
    }

    private async Task<CareResult<OrderService>> ApplyCompleteComboComponentAsync(
        int orderServiceId, int componentId, NhanVien actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var line = await dbContext.DichVuTrongDonSet
            .Include(l => l.ChiTietDon)
                .ThenInclude(p => p.DonDatLich)
                    .ThenInclude(b => b.HoaDonDonDatLichItems)
            .Include(l => l.ThanhPhanDichVuTrongDonDichVuTrongDonItems)
                .ThenInclude(c => c.DichVu)
            .AsSplitQuery()
            .SingleOrDefaultAsync(l => l.Id == orderServiceId, cancellationToken);
        if (line is null)
            return new CareResult<OrderService>(404, Message: "Không tìm thấy dịch vụ trong đơn.", ErrorCode: "NOT_FOUND");

        var pet = line.ChiTietDon;
        var (allowed, overrideReason) = await AuthorizeAssignmentAsync(actor, pet, now, cancellationToken);
        if (!allowed)
            return CareResult<OrderService>.Forbidden();

        var component = line.ThanhPhanDichVuTrongDonDichVuTrongDonItems.SingleOrDefault(c => c.Id == componentId);
        if (component is null)
            return new CareResult<OrderService>(404, Message: "Không tìm thấy thành phần dịch vụ trong đơn.", ErrorCode: "NOT_FOUND");

        var booking = pet.DonDatLich;
        if (booking.TrangThai != BookingState.IN_PROGRESS)
            return CareResult<OrderService>.InvalidTransition("Đơn đặt lịch không ở trạng thái IN_PROGRESS.");
        if (booking.HoaDonDonDatLichItems.Any(i => i.TrangThai != InvoiceState.REVOKED))
            return CareResult<OrderService>.InvalidTransition("Đơn đặt lịch đã có hóa đơn hiệu lực.");
        if (pet.TrangThaiChamSoc != PetCareState.IN_PROGRESS)
            return CareResult<OrderService>.InvalidTransition("Thú cưng không ở trạng thái IN_PROGRESS.");
        if (!line.ComboId.HasValue || line.TrangThai != OrderServiceState.CHO_THUC_HIEN)
            return CareResult<OrderService>.InvalidTransition("Dịch vụ trong đơn không phải combo đang chờ thực hiện.");
        if (component.TrangThai != OrderServiceState.CHO_THUC_HIEN)
            return CareResult<OrderService>.InvalidTransition("Thành phần combo không ở trạng thái CHO_THUC_HIEN.");

        component.TrangThai = OrderServiceState.HOAN_TAT;
        component.HoanTatAt = now;
        component.UpdatedAt = now;

        var allCompleted = line.ThanhPhanDichVuTrongDonDichVuTrongDonItems
            .Where(c => c.TrangThai != OrderServiceState.HUY)
            .All(c => c.TrangThai == OrderServiceState.HOAN_TAT);
        if (allCompleted)
        {
            line.TrangThai = OrderServiceState.HOAN_TAT;
            line.HoanTatAt = now;
            line.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new CareResult<OrderService>(200, CareProjection.OrderService(line),
            AssigneeId: pet.NhanVienId, OverrideReason: overrideReason, Action: "COMBO_COMPONENT_COMPLETE");
    }

    private async Task<CareResult<OrderService>> ApplyExtraServiceActionAsync(
        int orderServiceId, NhanVien actor, ServiceActionRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var line = await dbContext.DichVuTrongDonSet
            .Include(l => l.ChiTietDon)
                .ThenInclude(p => p.DonDatLich)
                    .ThenInclude(b => b.HoaDonDonDatLichItems)
            .Include(l => l.ThanhPhanDichVuTrongDonDichVuTrongDonItems)
                .ThenInclude(c => c.DichVu)
            .AsSplitQuery()
            .SingleOrDefaultAsync(l => l.Id == orderServiceId, cancellationToken);
        if (line is null)
            return new CareResult<OrderService>(404, Message: "Không tìm thấy dịch vụ phát sinh trong đơn.", ErrorCode: "NOT_FOUND");

        var pet = line.ChiTietDon;
        var booking = pet.DonDatLich;

        if (request.HanhDong == "COMPLETE")
        {
            var (allowed, overrideReason) = await AuthorizeAssignmentAsync(actor, pet, now, cancellationToken);
            if (!allowed)
                return CareResult<OrderService>.Forbidden();

            if (!line.LaPhatSinh)
                return CareResult<OrderService>.InvalidTransition("Chỉ áp dụng cho dịch vụ phát sinh.");
            if (booking.HoaDonDonDatLichItems.Any(i => i.TrangThai != InvoiceState.REVOKED))
                return CareResult<OrderService>.InvalidTransition("Đơn đặt lịch đã có hóa đơn hiệu lực.");
            if (booking.TrangThai != BookingState.IN_PROGRESS ||
                pet.TrangThaiChamSoc is PetCareState.NOT_ARRIVED or PetCareState.DONE or PetCareState.CANCELLED)
                return CareResult<OrderService>.InvalidTransition("Trạng thái đơn hoặc thú cưng không hợp lệ để hoàn tất dịch vụ phát sinh.");
            if (line.TrangThai != OrderServiceState.CHO_THUC_HIEN)
                return CareResult<OrderService>.InvalidTransition("Dịch vụ phát sinh không ở trạng thái CHO_THUC_HIEN.");

            line.TrangThai = OrderServiceState.HOAN_TAT;
            line.HoanTatAt = now;
            line.UpdatedAt = now;
            foreach (var component in line.ThanhPhanDichVuTrongDonDichVuTrongDonItems.Where(c => c.TrangThai == OrderServiceState.CHO_THUC_HIEN))
            {
                component.TrangThai = OrderServiceState.HOAN_TAT;
                component.HoanTatAt = now;
                component.UpdatedAt = now;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return new CareResult<OrderService>(200, CareProjection.OrderService(line),
                AssigneeId: pet.NhanVienId, OverrideReason: overrideReason, Action: "EXTRA_SERVICE_COMPLETE");
        }

        if (!line.LaPhatSinh)
            return CareResult<OrderService>.InvalidTransition("Chỉ áp dụng cho dịch vụ phát sinh.");
        if (booking.HoaDonDonDatLichItems.Any(i => i.TrangThai != InvoiceState.REVOKED))
            return CareResult<OrderService>.InvalidTransition("Đơn đặt lịch đã có hóa đơn hiệu lực.");
        if (pet.TrangThaiChamSoc is PetCareState.DONE or PetCareState.CANCELLED)
            return CareResult<OrderService>.InvalidTransition("Không thể hủy dịch vụ phát sinh khi thú cưng đã DONE hoặc CANCELLED.");
        if (line.TrangThai != OrderServiceState.CHO_THUC_HIEN ||
            line.ThanhPhanDichVuTrongDonDichVuTrongDonItems.Any(c => c.TrangThai == OrderServiceState.HOAN_TAT))
            return CareResult<OrderService>.InvalidTransition("Không thể hủy dịch vụ phát sinh đã hoàn tất hoặc đã có thành phần hoàn tất.");

        var slot = dbContext.Database.IsRelational()
            ? await dbContext.KhungGioDatLichSet
                .FromSqlInterpolated($"SELECT * FROM khung_gio_dat_lich WHERE id = {line.KhungGioId} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken)
            : await dbContext.KhungGioDatLichSet.SingleOrDefaultAsync(s => s.Id == line.KhungGioId, cancellationToken);
        if (slot is null || slot.PhutDaGiu < line.ThoiLuongSnapshot)
            return CareResult<OrderService>.InvalidTransition("Không thể đối chiếu thời lượng đã giữ của khung giờ để hủy dịch vụ.");

        var hasOtherActiveLineInSlot = await dbContext.DichVuTrongDonSet.AsNoTracking()
            .AnyAsync(other =>
                other.ChiTietDonId == line.ChiTietDonId &&
                other.Id != line.Id &&
                other.KhungGioId == line.KhungGioId &&
                other.TrangThai != OrderServiceState.HUY,
                cancellationToken);
        if (!hasOtherActiveLineInSlot)
        {
            if (slot.SlotDaDat < 1)
                return CareResult<OrderService>.InvalidTransition("Không thể đối chiếu suất đã đặt của khung giờ để hủy dịch vụ.");
            slot.SlotDaDat -= 1;
        }
        slot.PhutDaGiu -= line.ThoiLuongSnapshot;
        slot.UpdatedAt = now;

        booking.TongTamTinh = Math.Max(0m, booking.TongTamTinh - (line.GiaChot ?? 0m));
        booking.UpdatedAt = now;

        line.TrangThai = OrderServiceState.HUY;
        line.HuyAt = now;
        line.LyDo = request.LyDo!.Trim();
        line.UpdatedAt = now;
        foreach (var component in line.ThanhPhanDichVuTrongDonDichVuTrongDonItems)
        {
            component.TrangThai = OrderServiceState.HUY;
            component.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new CareResult<OrderService>(200, CareProjection.OrderService(line),
            AssigneeId: pet.NhanVienId, Action: "EXTRA_SERVICE_CANCEL");
    }

    private async Task<(bool Allowed, string? OverrideReason)> AuthorizeAssignmentAsync(
        NhanVien actor, ChiTietDon pet, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var isAssigned = pet.NhanVienId == actor.Id;
        var isAdmin = actor.VaiTro == Role.ADMIN;
        if (!isAssigned && !isAdmin)
            return (false, null);

        var hasShift = await operation.HasCurrentShiftAsync(actor.Id, now, cancellationToken);
        if (isAssigned && hasShift)
            return (true, null);
        if (!isAdmin)
            return (false, null);

        var reason = isAssigned ? "ADMIN_OVERRIDE_SHIFT"
            : hasShift ? "ADMIN_OVERRIDE_ASSIGNMENT" : "ADMIN_OVERRIDE_ASSIGNMENT_AND_SHIFT";
        return (true, reason);
    }

    private static NhatKyChamSoc NewLog(
        ChiTietDon pet, NhanVien actor, string type, DateTimeOffset now, string? note, string? objectPath) =>
        new()
        {
            ChiTietDonId = pet.Id,
            NhanVienId = actor.Id,
            LoaiNhatKy = type,
            GhiNhanAt = now,
            GhiChu = note,
            ObjectPath = objectPath,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static PetCareState? ResolveTargetCareState(PetCareState current, CareActionType action) =>
        (current, action) switch
        {
            (PetCareState.WAITING, CareActionType.START) => PetCareState.IN_PROGRESS,
            (PetCareState.IN_PROGRESS, CareActionType.PAUSE) => PetCareState.PAUSED,
            (PetCareState.PAUSED, CareActionType.RESUME) => PetCareState.IN_PROGRESS,
            (PetCareState.IN_PROGRESS, CareActionType.READY) => PetCareState.READY,
            _ => null
        };

    private static List<ApiErrorItem> ValidateCareActionRequest(
        int chiTietDonId,
        CareAction request,
        CarePhotoInput? photo)
    {
        var errors = new List<ApiErrorItem>();

        if (chiTietDonId <= 0)
        {
            errors.Add(new ApiErrorItem { Field = "id", Message = "ID chi tiết đơn phải lớn hơn 0." });
        }

        if (request.RequestId == Guid.Empty)
        {
            errors.Add(new ApiErrorItem { Field = "requestId", Message = "requestId không được để trống." });
        }

        if (!request.HanhDong.HasValue || !Enum.IsDefined(request.HanhDong.Value))
        {
            errors.Add(new ApiErrorItem { Field = "hanhDong", Message = "hanhDong phải là START, PAUSE, RESUME hoặc READY." });
        }

        if (request.HanhDong == CareActionType.PAUSE)
        {
            if (string.IsNullOrWhiteSpace(request.GhiChu) || request.GhiChu.Trim().Length > 2000)
            {
                errors.Add(new ApiErrorItem { Field = "ghiChu", Message = "ghiChu bắt buộc (1..2000 ký tự) khi tạm dừng (PAUSE)." });
            }
        }
        else if (request.GhiChu is not null && (string.IsNullOrWhiteSpace(request.GhiChu) || request.GhiChu.Trim().Length > 2000))
        {
            errors.Add(new ApiErrorItem { Field = "ghiChu", Message = "ghiChu nếu truyền phải từ 1 đến 2000 ký tự." });
        }

        if (request.MaNgoaiLeAnh.HasValue && request.MaNgoaiLeAnh.Value <= 0)
        {
            errors.Add(new ApiErrorItem { Field = "maNgoaiLeAnh", Message = "maNgoaiLeAnh phải lớn hơn 0." });
        }

        if (photo is not null && (photo.Length <= 0 || photo.Content.Length == 0))
        {
            errors.Add(new ApiErrorItem { Field = "anhThanhPham", Message = "File ảnh thành phẩm không được rỗng." });
        }

        return errors;
    }

    private static List<ApiErrorItem> ValidateCareLogRequest(
        int chiTietDonId,
        CareLogCreate request,
        CarePhotoInput? photo)
    {
        var errors = new List<ApiErrorItem>();

        if (chiTietDonId <= 0)
        {
            errors.Add(new ApiErrorItem { Field = "id", Message = "ID chi tiết đơn phải lớn hơn 0." });
        }

        if (request.RequestId == Guid.Empty)
        {
            errors.Add(new ApiErrorItem { Field = "requestId", Message = "requestId không được để trống." });
        }

        if (request.GhiChu is not null && (string.IsNullOrWhiteSpace(request.GhiChu) || request.GhiChu.Trim().Length > 2000))
        {
            errors.Add(new ApiErrorItem { Field = "ghiChu", Message = "ghiChu nếu truyền phải từ 1 đến 2000 ký tự." });
        }

        if (request.CanNangTheoDoi.HasValue &&
            (request.CanNangTheoDoi.Value <= 0 ||
             request.CanNangTheoDoi.Value > 999.99m ||
             decimal.Round(request.CanNangTheoDoi.Value, 2) != request.CanNangTheoDoi.Value))
        {
            errors.Add(new ApiErrorItem { Field = "canNangTheoDoi", Message = "canNangTheoDoi phải > 0, <= 999.99 và tối đa 2 chữ số thập phân." });
        }

        if (photo is not null && (photo.Length <= 0 || photo.Content.Length == 0))
        {
            errors.Add(new ApiErrorItem { Field = "anh", Message = "File ảnh nhật ký không được rỗng." });
        }

        var hasNote = !string.IsNullOrWhiteSpace(request.GhiChu);
        var hasPhoto = photo is not null && photo.Length > 0 && photo.Content.Length > 0;
        if (!hasNote && !hasPhoto)
        {
            errors.Add(new ApiErrorItem { Field = "payload", Message = "Nhật ký chăm sóc phải có ít nhất ghiChu hoặc anh." });
        }

        return errors;
    }

    private static List<ApiErrorItem> ValidateExtraServiceActionRequest(int orderServiceId, ServiceActionRequest request)
    {
        var errors = new List<ApiErrorItem>();
        if (orderServiceId <= 0)
            errors.Add(new ApiErrorItem { Field = "id", Message = "ID dịch vụ trong đơn phải lớn hơn 0." });
        if (request.RequestId == Guid.Empty)
            errors.Add(new ApiErrorItem { Field = "requestId", Message = "requestId không được để trống." });
        if (request.HanhDong is not ("COMPLETE" or "CANCEL"))
            errors.Add(new ApiErrorItem { Field = "hanhDong", Message = "hanhDong phải là COMPLETE hoặc CANCEL." });
        if (request.HanhDong == "CANCEL" && (string.IsNullOrWhiteSpace(request.LyDo) || request.LyDo.Trim().Length > 1000))
            errors.Add(new ApiErrorItem { Field = "lyDo", Message = "lyDo bắt buộc (1..1000 ký tự) khi hủy dịch vụ phát sinh." });
        return errors;
    }

}
