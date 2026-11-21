using Microsoft.EntityFrameworkCore;
using PetSpa.Api.Data;
using PetSpa.Core.Common;
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
            var incompleteCombo = pet.DichVuTrongDonChiTietDonItems.Any(line => line.TrangThai != OrderServiceState.HUY &&
                (line.ComboId.HasValue || line.ThanhPhanDichVuTrongDonDichVuTrongDonItems.Count > 0) &&
                (line.TrangThai == OrderServiceState.CHO_THUC_HIEN ||
                    line.ThanhPhanDichVuTrongDonDichVuTrongDonItems.Any(part => part.TrangThai == OrderServiceState.CHO_THUC_HIEN)));
            if (incompleteCombo)
                return CareResult<BookingPet>.InvalidTransition("Dịch vụ combo chưa hoàn tất toàn bộ thành phần trước khi chuyển READY.");

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
                         line.TrangThai == OrderServiceState.CHO_THUC_HIEN && !line.ComboId.HasValue))
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

}
