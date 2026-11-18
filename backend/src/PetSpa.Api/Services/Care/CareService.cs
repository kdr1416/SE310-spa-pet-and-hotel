using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

public class CareService(PetSpaDbContext dbContext, IWebHostEnvironment environment) : ICareService
{
    private const long MaxPhotoBytes = 5 * 1024 * 1024;
    private const string CareUpdatePermission = "CARE_UPDATE";
    private const string PhotoUploadPermission = "PHOTO_UPLOAD";
    private const string CareActionAuditAction = "CARE_ACTION";
    private const string CareLogAuditAction = "CARE_LOG_CREATE";
    private const string PhotoExceptionAuditAction = "PHOTO_EXCEPTION_APPROVE";
    private const string AuditTargetType = "ChiTietDon";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<CareActionResult> ExecuteCareActionAsync(
        int chiTietDonId,
        int actorId,
        CareAction request,
        CarePhotoInput? photo,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await dbContext.NhanVienSet
            .Include(nv => nv.NhanVienQuyenNhanVienItems)
            .FirstOrDefaultAsync(nv => nv.Id == actorId, cancellationToken);

        if (actor is null || !actor.IsActive ||
            !actor.NhanVienQuyenNhanVienItems.Any(q => q.MaQuyen == CareUpdatePermission))
        {
            return new CareActionResult(
                StatusCodes.Status403Forbidden,
                Message: "Bạn không có quyền cập nhật trạng thái chăm sóc thú cưng.",
                ErrorCode: "FORBIDDEN");
        }

        var validationErrors = ValidateCareActionRequest(chiTietDonId, request, photo);
        if (validationErrors.Count > 0)
        {
            return new CareActionResult(
                StatusCodes.Status400BadRequest,
                Message: "Dữ liệu cập nhật chăm sóc không hợp lệ.",
                ErrorCode: "VALIDATION_FAILED",
                Errors: validationErrors);
        }

        string? photoExtension = null;
        if (photo is not null)
        {
            if (photo.Length > MaxPhotoBytes)
            {
                return new CareActionResult(
                    StatusCodes.Status413PayloadTooLarge,
                    Message: "Ảnh thành phẩm vượt quá dung lượng tối đa 5 MiB.",
                    ErrorCode: "FILE_TOO_LARGE",
                    Errors: [new ApiErrorItem { Field = "anhThanhPham", Message = "Kích thước ảnh tối đa 5 MiB." }]);
            }

            photoExtension = DetectImageExtension(photo.Content);
            if (photoExtension is null)
            {
                return new CareActionResult(
                    StatusCodes.Status415UnsupportedMediaType,
                    Message: "Định dạng ảnh thành phẩm không được hỗ trợ (chỉ chấp nhận JPEG, PNG, WebP).",
                    ErrorCode: "UNSUPPORTED_FILE",
                    Errors: [new ApiErrorItem { Field = "anhThanhPham", Message = "Chỉ chấp nhận JPEG, PNG hoặc WebP." }]);
            }
        }

        var fingerprint = ComputeCareActionFingerprint(chiTietDonId, request, photo);

        var existingAudit = await dbContext.AuditLogSet
            .AsNoTracking()
            .FirstOrDefaultAsync(a =>
                a.ActorId == actorId &&
                a.HanhDong == CareActionAuditAction &&
                a.LoaiDoiTuong == AuditTargetType &&
                a.DoiTuongId == chiTietDonId &&
                a.RequestId == request.RequestId,
                cancellationToken);

        if (existingAudit is not null)
        {
            if (!string.Equals(existingAudit.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                return new CareActionResult(
                    StatusCodes.Status409Conflict,
                    Message: "RequestId đã được sử dụng với dữ liệu khác.",
                    ErrorCode: "IDEMPOTENCY_CONFLICT");
            }

            if (!string.IsNullOrWhiteSpace(existingAudit.KetQua))
            {
                var cached = JsonSerializer.Deserialize<BookingPet>(existingAudit.KetQua, JsonOptions);
                if (cached is not null)
                {
                    return new CareActionResult(StatusCodes.Status200OK, Data: cached, Message: "Cập nhật trạng thái chăm sóc thành công.");
                }
            }
        }

        var chiTietDon = await dbContext.ChiTietDonSet
            .Include(c => c.DonDatLich)
                .ThenInclude(d => d.KhungGio)
            .Include(c => c.ThuCung)
            .Include(c => c.NhanVien)
            .Include(c => c.DichVuTrongDonChiTietDonItems)
                .ThenInclude(dv => dv.DichVu)
            .Include(c => c.DichVuTrongDonChiTietDonItems)
                .ThenInclude(dv => dv.KhungGio)
            .Include(c => c.DichVuTrongDonChiTietDonItems)
                .ThenInclude(dv => dv.ThanhPhanDichVuTrongDonDichVuTrongDonItems)
                    .ThenInclude(tp => tp.DichVu)
            .Include(c => c.NhatKyChamSocChiTietDonItems)
                .ThenInclude(nk => nk.NhanVien)
            .FirstOrDefaultAsync(c => c.Id == chiTietDonId, cancellationToken);

        if (chiTietDon is null)
        {
            return new CareActionResult(
                StatusCodes.Status404NotFound,
                Message: "Không tìm thấy thú cưng trong đơn đặt lịch.",
                ErrorCode: "NOT_FOUND");
        }

        if (!await IsAuthorizedActorAsync(actor, chiTietDon, cancellationToken))
        {
            return new CareActionResult(
                StatusCodes.Status403Forbidden,
                Message: "Bạn không phải nhân viên được phân công hoặc không có ca làm việc APPROVED phù hợp.",
                ErrorCode: "FORBIDDEN");
        }

        if (chiTietDon.DonDatLich.TrangThai != BookingState.IN_PROGRESS)
        {
            return new CareActionResult(
                StatusCodes.Status409Conflict,
                Message: "Đơn đặt lịch không ở trạng thái IN_PROGRESS để thực hiện chăm sóc.",
                ErrorCode: "INVALID_TRANSITION");
        }

        var action = request.HanhDong!.Value;
        var targetState = ResolveTargetCareState(chiTietDon.TrangThaiChamSoc, action);
        if (!targetState.HasValue)
        {
            return new CareActionResult(
                StatusCodes.Status409Conflict,
                Message: $"Không thể thực hiện hành động {action} khi thú cưng đang ở trạng thái {chiTietDon.TrangThaiChamSoc}.",
                ErrorCode: "INVALID_TRANSITION");
        }

        AuditLog? exceptionAudit = null;
        if (action == CareActionType.READY)
        {
            var activeServices = chiTietDon.DichVuTrongDonChiTietDonItems
                .Where(d => d.TrangThai != OrderServiceState.HUY)
                .ToList();

            var hasIncompleteCombo = activeServices.Any(d =>
                (d.ComboId.HasValue || d.ThanhPhanDichVuTrongDonDichVuTrongDonItems.Count > 0) &&
                (d.TrangThai == OrderServiceState.CHO_THUC_HIEN ||
                 d.ThanhPhanDichVuTrongDonDichVuTrongDonItems.Any(tp => tp.TrangThai == OrderServiceState.CHO_THUC_HIEN)));

            if (hasIncompleteCombo)
            {
                return new CareActionResult(
                    StatusCodes.Status409Conflict,
                    Message: "Dịch vụ combo chưa hoàn tất toàn bộ thành phần trước khi chuyển READY.",
                    ErrorCode: "INVALID_TRANSITION");
            }

            if (photo is null)
            {
                if (!request.MaNgoaiLeAnh.HasValue)
                {
                    return new CareActionResult(
                        StatusCodes.Status409Conflict,
                        Message: "Cần ảnh thành phẩm hoặc ngoại lệ ảnh hợp lệ để chuyển sang READY.",
                        ErrorCode: "INVALID_TRANSITION");
                }

                exceptionAudit = await dbContext.AuditLogSet
                    .AsNoTracking()
                    .FirstOrDefaultAsync(a =>
                        a.Id == request.MaNgoaiLeAnh.Value &&
                        a.HanhDong == PhotoExceptionAuditAction &&
                        a.LoaiDoiTuong == AuditTargetType &&
                        a.DoiTuongId == chiTietDonId,
                        cancellationToken);

                if (exceptionAudit is null)
                {
                    return new CareActionResult(
                        StatusCodes.Status409Conflict,
                        Message: "Mã ngoại lệ ảnh không tồn tại hoặc không thuộc thú cưng này.",
                        ErrorCode: "INVALID_TRANSITION");
                }
            }
        }

        string? savedRelativePath = null;
        if (photo is not null && photoExtension is not null)
        {
            savedRelativePath = await SavePhotoOutsideTransactionAsync(photo.Content, photoExtension, cancellationToken);
        }

        try
        {
            await using var transaction = dbContext.Database.IsRelational()
                ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
                : null;

            var now = DateTimeOffset.UtcNow;

            chiTietDon.TrangThaiChamSoc = targetState.Value;
            chiTietDon.UpdatedAt = now;

            if (action == CareActionType.READY)
            {
                chiTietDon.ReadyAt = now;

                foreach (var service in chiTietDon.DichVuTrongDonChiTietDonItems
                             .Where(d => d.TrangThai == OrderServiceState.CHO_THUC_HIEN && !d.ComboId.HasValue))
                {
                    service.TrangThai = OrderServiceState.HOAN_TAT;
                    service.HoanTatAt = now;
                    service.UpdatedAt = now;
                }
            }

            var logNote = !string.IsNullOrWhiteSpace(request.GhiChu)
                ? request.GhiChu.Trim()
                : action == CareActionType.READY && exceptionAudit is not null
                    ? (string.IsNullOrWhiteSpace(exceptionAudit.LyDo)
                        ? "Hoàn tất chăm sóc (ngoại lệ ảnh được duyệt)"
                        : exceptionAudit.LyDo)
                    : null;

            if (savedRelativePath is not null || !string.IsNullOrWhiteSpace(logNote))
            {
                var careLog = new NhatKyChamSoc
                {
                    ChiTietDonId = chiTietDon.Id,
                    NhanVienId = actor.Id,
                    LoaiNhatKy = $"CARE_{action}",
                    GhiNhanAt = now,
                    ObjectPath = savedRelativePath,
                    GhiChu = logNote,
                    CreatedAt = now,
                    UpdatedAt = now,
                    NhanVien = actor
                };
                dbContext.NhatKyChamSocSet.Add(careLog);
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            var responseDto = MapBookingPet(chiTietDon);

            dbContext.AuditLogSet.Add(new AuditLog
            {
                ActorLoai = actor.VaiTro.ToString(),
                ActorId = actor.Id,
                HanhDong = CareActionAuditAction,
                LoaiDoiTuong = AuditTargetType,
                DoiTuongId = chiTietDon.Id,
                TraceId = traceId,
                RequestId = request.RequestId,
                Fingerprint = fingerprint,
                KetQua = JsonSerializer.Serialize(responseDto, JsonOptions),
                CreatedAt = now
            });

            await dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return new CareActionResult(
                StatusCodes.Status200OK,
                Data: responseDto,
                Message: "Cập nhật trạng thái chăm sóc thành công.");
        }
        catch
        {
            if (savedRelativePath is not null)
            {
                DeletePhotoIfExists(savedRelativePath);
            }
            throw;
        }
    }

    public async Task<CareLogCreateResult> CreateCareLogAsync(
        int chiTietDonId,
        int actorId,
        CareLogCreate request,
        CarePhotoInput? photo,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await dbContext.NhanVienSet
            .Include(nv => nv.NhanVienQuyenNhanVienItems)
            .FirstOrDefaultAsync(nv => nv.Id == actorId, cancellationToken);

        if (actor is null || !actor.IsActive ||
            !actor.NhanVienQuyenNhanVienItems.Any(q => q.MaQuyen == PhotoUploadPermission))
        {
            return new CareLogCreateResult(
                StatusCodes.Status403Forbidden,
                Message: "Bạn không có quyền ghi nhật ký chăm sóc và tải ảnh.",
                ErrorCode: "FORBIDDEN");
        }

        var validationErrors = ValidateCareLogRequest(chiTietDonId, request, photo);
        if (validationErrors.Count > 0)
        {
            return new CareLogCreateResult(
                StatusCodes.Status400BadRequest,
                Message: "Dữ liệu nhật ký chăm sóc không hợp lệ.",
                ErrorCode: "VALIDATION_FAILED",
                Errors: validationErrors);
        }

        string? photoExtension = null;
        if (photo is not null)
        {
            if (photo.Length > MaxPhotoBytes)
            {
                return new CareLogCreateResult(
                    StatusCodes.Status413PayloadTooLarge,
                    Message: "Ảnh nhật ký vượt quá dung lượng tối đa 5 MiB.",
                    ErrorCode: "FILE_TOO_LARGE",
                    Errors: [new ApiErrorItem { Field = "anh", Message = "Kích thước ảnh tối đa 5 MiB." }]);
            }

            photoExtension = DetectImageExtension(photo.Content);
            if (photoExtension is null)
            {
                return new CareLogCreateResult(
                    StatusCodes.Status415UnsupportedMediaType,
                    Message: "Định dạng ảnh nhật ký không được hỗ trợ (chỉ chấp nhận JPEG, PNG, WebP).",
                    ErrorCode: "UNSUPPORTED_FILE",
                    Errors: [new ApiErrorItem { Field = "anh", Message = "Chỉ chấp nhận JPEG, PNG hoặc WebP." }]);
            }
        }

        var fingerprint = ComputeCareLogFingerprint(chiTietDonId, request, photo);

        var existingAudit = await dbContext.AuditLogSet
            .AsNoTracking()
            .FirstOrDefaultAsync(a =>
                a.ActorId == actorId &&
                a.HanhDong == CareLogAuditAction &&
                a.LoaiDoiTuong == AuditTargetType &&
                a.DoiTuongId == chiTietDonId &&
                a.RequestId == request.RequestId,
                cancellationToken);

        if (existingAudit is not null)
        {
            if (!string.Equals(existingAudit.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                return new CareLogCreateResult(
                    StatusCodes.Status409Conflict,
                    Message: "RequestId đã được sử dụng với dữ liệu khác.",
                    ErrorCode: "IDEMPOTENCY_CONFLICT");
            }

            if (!string.IsNullOrWhiteSpace(existingAudit.KetQua))
            {
                var cached = JsonSerializer.Deserialize<CareLog>(existingAudit.KetQua, JsonOptions);
                if (cached is not null)
                {
                    return new CareLogCreateResult(StatusCodes.Status200OK, Data: cached, Message: "Ghi nhật ký chăm sóc thành công.");
                }
            }
        }

        var chiTietDon = await dbContext.ChiTietDonSet
            .Include(c => c.DonDatLich)
                .ThenInclude(d => d.KhungGio)
            .Include(c => c.DichVuTrongDonChiTietDonItems)
                .ThenInclude(dv => dv.KhungGio)
            .FirstOrDefaultAsync(c => c.Id == chiTietDonId, cancellationToken);

        if (chiTietDon is null)
        {
            return new CareLogCreateResult(
                StatusCodes.Status404NotFound,
                Message: "Không tìm thấy thú cưng trong đơn đặt lịch.",
                ErrorCode: "NOT_FOUND");
        }

        if (!await IsAuthorizedActorAsync(actor, chiTietDon, cancellationToken))
        {
            return new CareLogCreateResult(
                StatusCodes.Status403Forbidden,
                Message: "Bạn không phải nhân viên được phân công hoặc không có ca làm việc APPROVED phù hợp.",
                ErrorCode: "FORBIDDEN");
        }

        if (chiTietDon.TrangThaiChamSoc is not (
                PetCareState.WAITING or
                PetCareState.IN_PROGRESS or
                PetCareState.PAUSED or
                PetCareState.READY))
        {
            return new CareLogCreateResult(
                StatusCodes.Status409Conflict,
                Message: $"Không thể ghi nhật ký khi thú cưng đang ở trạng thái {chiTietDon.TrangThaiChamSoc}.",
                ErrorCode: "INVALID_TRANSITION");
        }

        string? savedRelativePath = null;
        if (photo is not null && photoExtension is not null)
        {
            savedRelativePath = await SavePhotoOutsideTransactionAsync(photo.Content, photoExtension, cancellationToken);
        }

        try
        {
            await using var transaction = dbContext.Database.IsRelational()
                ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
                : null;

            var now = DateTimeOffset.UtcNow;
            var normalizedNote = string.IsNullOrWhiteSpace(request.GhiChu) ? null : request.GhiChu.Trim();

            var careLog = new NhatKyChamSoc
            {
                ChiTietDonId = chiTietDon.Id,
                NhanVienId = actor.Id,
                LoaiNhatKy = "CARE_LOG",
                GhiNhanAt = now,
                ObjectPath = savedRelativePath,
                GhiChu = normalizedNote,
                CanNangGhiNhan = request.CanNangTheoDoi,
                CreatedAt = now,
                UpdatedAt = now
            };

            dbContext.NhatKyChamSocSet.Add(careLog);
            await dbContext.SaveChangesAsync(cancellationToken);

            var responseDto = new CareLog
            {
                MaNhatKy = careLog.Id,
                LoaiNhatKy = careLog.LoaiNhatKy,
                GhiChu = careLog.GhiChu,
                AnhUrl = string.IsNullOrWhiteSpace(careLog.ObjectPath) ? null : $"/storage/{careLog.ObjectPath}",
                CanNangTheoDoi = careLog.CanNangGhiNhan,
                TenTacGia = actor.TenNhanVien,
                GhiNhanAt = careLog.GhiNhanAt
            };

            dbContext.AuditLogSet.Add(new AuditLog
            {
                ActorLoai = actor.VaiTro.ToString(),
                ActorId = actor.Id,
                HanhDong = CareLogAuditAction,
                LoaiDoiTuong = AuditTargetType,
                DoiTuongId = chiTietDon.Id,
                TraceId = traceId,
                RequestId = request.RequestId,
                Fingerprint = fingerprint,
                KetQua = JsonSerializer.Serialize(responseDto, JsonOptions),
                CreatedAt = now
            });

            await dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return new CareLogCreateResult(
                StatusCodes.Status201Created,
                Data: responseDto,
                Message: "Ghi nhật ký chăm sóc thành công.");
        }
        catch
        {
            if (savedRelativePath is not null)
            {
                DeletePhotoIfExists(savedRelativePath);
            }
            throw;
        }
    }

    private async Task<bool> IsAuthorizedActorAsync(
        NhanVien actor,
        ChiTietDon chiTietDon,
        CancellationToken cancellationToken)
    {
        if (actor.VaiTro == Role.ADMIN)
        {
            return true;
        }

        if (chiTietDon.NhanVienId != actor.Id)
        {
            return false;
        }

        var requiredShifts = chiTietDon.DichVuTrongDonChiTietDonItems
            .Where(d => d.TrangThai != OrderServiceState.HUY)
            .Select(d => (d.KhungGio.Ngay, d.KhungGio.CaLamViecId))
            .Distinct()
            .ToList();

        if (requiredShifts.Count == 0)
        {
            requiredShifts.Add((chiTietDon.DonDatLich.KhungGio.Ngay, chiTietDon.DonDatLich.KhungGio.CaLamViecId));
        }

        var approvedShifts = await dbContext.DangKyCaSet
            .Where(dk => dk.NhanVienId == actor.Id && dk.TrangThai == ShiftRegistrationState.APPROVED)
            .Select(dk => new { dk.Ngay, dk.CaLamViecId })
            .ToListAsync(cancellationToken);

        return requiredShifts.All(rs =>
            approvedShifts.Any(ap => ap.Ngay == rs.Ngay && ap.CaLamViecId == rs.CaLamViecId));
    }

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

    private static string? DetectImageExtension(byte[] content)
    {
        if (content.Length >= 3 &&
            content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF)
        {
            return ".jpg";
        }

        if (content.Length >= 8 &&
            content[0] == 0x89 && content[1] == 0x50 && content[2] == 0x4E && content[3] == 0x47 &&
            content[4] == 0x0D && content[5] == 0x0A && content[6] == 0x1A && content[7] == 0x0A)
        {
            return ".png";
        }

        if (content.Length >= 12 &&
            content[0] == 0x52 && content[1] == 0x49 && content[2] == 0x46 && content[3] == 0x46 &&
            content[8] == 0x57 && content[9] == 0x45 && content[10] == 0x42 && content[11] == 0x50)
        {
            return ".webp";
        }

        return null;
    }

    private string GetStorageFullPath(string relativePath)
    {
        var root = environment.IsEnvironment("Testing")
            ? Path.Combine(Path.GetTempPath(), "petspa-test-storage")
            : Path.Combine(environment.ContentRootPath, "storage");
        return Path.Combine(root, relativePath);
    }

    private async Task<string> SavePhotoOutsideTransactionAsync(
        byte[] content,
        string extension,
        CancellationToken cancellationToken)
    {
        var relativePath = Path.Combine("care-logs", $"{Guid.NewGuid():N}{extension}").Replace('\\', '/');
        var fullPath = GetStorageFullPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllBytesAsync(fullPath, content, cancellationToken);
        return relativePath;
    }

    private void DeletePhotoIfExists(string relativePath)
    {
        var fullPath = GetStorageFullPath(relativePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    private static string ComputeCareActionFingerprint(
        int chiTietDonId,
        CareAction request,
        CarePhotoInput? photo)
    {
        var photoHash = photo is null ? null : Convert.ToHexString(SHA256.HashData(photo.Content));
        var canonical = JsonSerializer.Serialize(new
        {
            chiTietDonId,
            request.RequestId,
            request.HanhDong,
            GhiChu = request.GhiChu?.Trim(),
            request.MaNgoaiLeAnh,
            PhotoFileName = photo?.FileName,
            PhotoLength = photo?.Length,
            PhotoHash = photoHash
        });

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash);
    }

    private static string ComputeCareLogFingerprint(
        int chiTietDonId,
        CareLogCreate request,
        CarePhotoInput? photo)
    {
        var photoHash = photo is null ? null : Convert.ToHexString(SHA256.HashData(photo.Content));
        var canonical = JsonSerializer.Serialize(new
        {
            chiTietDonId,
            request.RequestId,
            GhiChu = request.GhiChu?.Trim(),
            request.CanNangTheoDoi,
            PhotoFileName = photo?.FileName,
            PhotoLength = photo?.Length,
            PhotoHash = photoHash
        });

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash);
    }

    private static List<string>? ParseFurConditions(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(rawJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static BookingPet MapBookingPet(ChiTietDon entity)
    {
        return new BookingPet
        {
            MaChiTietDon = entity.Id,
            MaThuCung = entity.ThuCungId,
            TenThuCung = entity.TenPetSnapshot,
            TrangThai = entity.TrangThaiChamSoc.ToString(),
            CanNangChot = entity.CanNangChot,
            SizeThucTe = entity.SizeThucTe?.ToString(),
            SizeChot = entity.SizeChot?.ToString(),
            TinhTrangLong = ParseFurConditions(entity.TinhTrangLong),
            MaLoaiPhong = entity.LoaiPhongId,
            TenNhanVienPhuTrach = entity.NhanVien?.TenNhanVien,
            DichVu = entity.DichVuTrongDonChiTietDonItems
                .OrderBy(d => d.Id)
                .Select(d => new OrderService
                {
                    MaDichVuTrongDon = d.Id,
                    MaDichVu = d.DichVuId,
                    MaCombo = d.ComboId,
                    MaKhungGio = d.KhungGioId,
                    TenDichVu = d.TenSnapshot,
                    TrangThai = d.TrangThai.ToString(),
                    SizeChot = d.SizeChot?.ToString(),
                    GiaChot = d.GiaChot,
                    ThanhPhan = d.ThanhPhanDichVuTrongDonDichVuTrongDonItems
                        .OrderBy(tp => tp.Id)
                        .Select(tp => new Component
                        {
                            MaThanhPhan = tp.Id,
                            MaDichVu = tp.DichVuId,
                            TenDichVu = tp.DichVu.TenDichVu,
                            TrangThai = tp.TrangThai.ToString(),
                            HoanTatAt = tp.HoanTatAt
                        })
                        .ToList()
                })
                .ToList(),
            NhatKy = entity.NhatKyChamSocChiTietDonItems
                .OrderBy(nk => nk.GhiNhanAt)
                .ThenBy(nk => nk.Id)
                .Select(nk => new CareLog
                {
                    MaNhatKy = nk.Id,
                    LoaiNhatKy = nk.LoaiNhatKy,
                    GhiChu = nk.GhiChu,
                    AnhUrl = string.IsNullOrWhiteSpace(nk.ObjectPath) ? null : $"/storage/{nk.ObjectPath}",
                    CanNangTheoDoi = nk.CanNangGhiNhan,
                    TenTacGia = nk.NhanVien?.TenNhanVien ?? string.Empty,
                    GhiNhanAt = nk.GhiNhanAt
                })
                .ToList(),
            NhanSom = entity.NhanSom,
            BanGiaoAt = entity.BanGiaoAt,
            TraLaiAt = entity.TraLaiAt
        };
    }
}
