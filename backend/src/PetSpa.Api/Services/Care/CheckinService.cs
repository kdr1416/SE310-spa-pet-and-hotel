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

public class CheckinService(PetSpaDbContext dbContext, IWebHostEnvironment environment) : ICheckinService
{
    private const long MaxPhotoBytes = 5 * 1024 * 1024;
    private const string RequiredPermission = "CHECKIN_PERFORM";
    private const string AuditAction = "CHECKIN_PERFORM";
    private const string AuditTargetType = "ChiTietDon";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<CheckinResult> CheckinAsync(
        int chiTietDonId,
        int actorId,
        Checkin request,
        CheckinPhotoInput photo,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await dbContext.NhanVienSet
            .Include(nv => nv.NhanVienQuyenNhanVienItems)
            .FirstOrDefaultAsync(nv => nv.Id == actorId, cancellationToken);

        if (actor is null || !actor.IsActive ||
            !actor.NhanVienQuyenNhanVienItems.Any(q => q.MaQuyen == RequiredPermission))
        {
            return new CheckinResult(
                StatusCodes.Status403Forbidden,
                Message: "Bạn không có quyền thực hiện check-in thú cưng.",
                ErrorCode: "FORBIDDEN");
        }

        var validationErrors = ValidateRequest(chiTietDonId, request, photo);
        if (validationErrors.Count > 0)
        {
            return new CheckinResult(
                StatusCodes.Status400BadRequest,
                Message: "Dữ liệu check-in không hợp lệ.",
                ErrorCode: "VALIDATION_FAILED",
                Errors: validationErrors);
        }

        if (photo.Length > MaxPhotoBytes)
        {
            return new CheckinResult(
                StatusCodes.Status413PayloadTooLarge,
                Message: "Ảnh tiếp nhận vượt quá dung lượng tối đa 5 MiB.",
                ErrorCode: "FILE_TOO_LARGE",
                Errors: [new ApiErrorItem { Field = "anhTiepNhan", Message = "Kích thước ảnh tối đa 5 MiB." }]);
        }

        var photoExtension = DetectImageExtension(photo.Content);
        if (photoExtension is null)
        {
            return new CheckinResult(
                StatusCodes.Status415UnsupportedMediaType,
                Message: "Định dạng ảnh tiếp nhận không được hỗ trợ (chỉ chấp nhận JPEG, PNG, WebP).",
                ErrorCode: "UNSUPPORTED_FILE",
                Errors: [new ApiErrorItem { Field = "anhTiepNhan", Message = "Chỉ chấp nhận JPEG, PNG hoặc WebP." }]);
        }

        var normalizedFurConditions = request.TinhTrangLong
            .Select(c => c.Trim())
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var furConfigResult = await ValidateFurConditionsAsync(normalizedFurConditions, cancellationToken);
        if (furConfigResult is not null)
        {
            return furConfigResult;
        }

        var fingerprint = ComputeFingerprint(chiTietDonId, request, normalizedFurConditions, photo);

        var existingAudit = await dbContext.AuditLogSet
            .AsNoTracking()
            .FirstOrDefaultAsync(a =>
                a.ActorId == actorId &&
                a.HanhDong == AuditAction &&
                a.LoaiDoiTuong == AuditTargetType &&
                a.DoiTuongId == chiTietDonId &&
                a.RequestId == request.RequestId,
                cancellationToken);

        if (existingAudit is not null)
        {
            if (!string.Equals(existingAudit.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                return new CheckinResult(
                    StatusCodes.Status409Conflict,
                    Message: "RequestId đã được sử dụng với dữ liệu khác.",
                    ErrorCode: "IDEMPOTENCY_CONFLICT");
            }

            if (!string.IsNullOrWhiteSpace(existingAudit.KetQua))
            {
                var cached = JsonSerializer.Deserialize<BookingPet>(existingAudit.KetQua, JsonOptions);
                if (cached is not null)
                {
                    return new CheckinResult(StatusCodes.Status200OK, Data: cached, Message: "Check-in thú cưng thành công.");
                }
            }
        }

        var chiTietDon = await dbContext.ChiTietDonSet
            .Include(c => c.DonDatLich)
                .ThenInclude(d => d.KhungGio)
            .Include(c => c.DonDatLich)
                .ThenInclude(d => d.HoaDonDonDatLichItems)
            .Include(c => c.ThuCung)
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
            return new CheckinResult(
                StatusCodes.Status404NotFound,
                Message: "Không tìm thấy thú cưng trong đơn đặt lịch.",
                ErrorCode: "NOT_FOUND");
        }

        var booking = chiTietDon.DonDatLich;
        if (booking.TrangThai is not (BookingState.CONFIRMED or BookingState.IN_PROGRESS))
        {
            return new CheckinResult(
                StatusCodes.Status409Conflict,
                Message: "Chỉ có thể check-in khi đơn đặt lịch đang ở trạng thái CONFIRMED hoặc IN_PROGRESS.",
                ErrorCode: "INVALID_TRANSITION");
        }

        if (chiTietDon.TrangThaiChamSoc != PetCareState.NOT_ARRIVED)
        {
            return new CheckinResult(
                StatusCodes.Status409Conflict,
                Message: "Thú cưng không ở trạng thái NOT_ARRIVED để thực hiện check-in.",
                ErrorCode: "INVALID_TRANSITION");
        }

        if (booking.HoaDonDonDatLichItems.Any(h => h.TrangThai != InvoiceState.REVOKED))
        {
            return new CheckinResult(
                StatusCodes.Status409Conflict,
                Message: "Đơn đặt lịch đã có hóa đơn hiệu lực, không thể check-in.",
                ErrorCode: "INVALID_TRANSITION");
        }

        if (request.MaHoSoTiemChung.HasValue)
        {
            var vaccinationExists = await dbContext.HoSoTiemChungSet
                .AnyAsync(h => h.Id == request.MaHoSoTiemChung.Value && h.ThuCungId == chiTietDon.ThuCungId, cancellationToken);

            if (!vaccinationExists)
            {
                return new CheckinResult(
                    StatusCodes.Status404NotFound,
                    Message: "Không tìm thấy hồ sơ tiêm chủng của thú cưng.",
                    ErrorCode: "NOT_FOUND");
            }
        }

        if (IsVaccinationRequired(chiTietDon) &&
            !request.MaHoSoTiemChung.HasValue &&
            request.XacNhanGiayHopLe != true)
        {
            return new CheckinResult(
                StatusCodes.Status400BadRequest,
                Message: "Dịch vụ yêu cầu xác nhận hồ sơ hoặc giấy tiêm chủng hợp lệ.",
                ErrorCode: "VALIDATION_FAILED",
                Errors:
                [
                    new ApiErrorItem
                    {
                        Field = "xacNhanGiayHopLe",
                        Message = "Cần cung cấp maHoSoTiemChung hoặc xacNhanGiayHopLe = true."
                    }
                ]);
        }

        var assignee = await dbContext.NhanVienSet
            .FirstOrDefaultAsync(nv => nv.Id == request.MaNhanVien, cancellationToken);

        if (assignee is null || !assignee.IsActive)
        {
            return new CheckinResult(
                StatusCodes.Status409Conflict,
                Message: "Nhân viên được phân công không tồn tại hoặc không hoạt động.",
                ErrorCode: "ASSIGNMENT_UNAVAILABLE");
        }

        var requiredShifts = chiTietDon.DichVuTrongDonChiTietDonItems
            .Where(d => d.TrangThai != OrderServiceState.HUY)
            .Select(d => (d.KhungGio.Ngay, d.KhungGio.CaLamViecId))
            .Distinct()
            .ToList();

        if (requiredShifts.Count == 0)
        {
            requiredShifts.Add((booking.KhungGio.Ngay, booking.KhungGio.CaLamViecId));
        }

        var approvedShifts = await dbContext.DangKyCaSet
            .Where(dk => dk.NhanVienId == assignee.Id && dk.TrangThai == ShiftRegistrationState.APPROVED)
            .Select(dk => new { dk.Ngay, dk.CaLamViecId })
            .ToListAsync(cancellationToken);

        var hasMatchingShift = requiredShifts.All(rs =>
            approvedShifts.Any(ap => ap.Ngay == rs.Ngay && ap.CaLamViecId == rs.CaLamViecId));

        if (!hasMatchingShift)
        {
            return new CheckinResult(
                StatusCodes.Status409Conflict,
                Message: "Nhân viên được phân công chưa có ca làm việc APPROVED phù hợp.",
                ErrorCode: "ASSIGNMENT_UNAVAILABLE");
        }

        var sizeBands = await dbContext.HeSoCanNangSet
            .OrderBy(h => h.CanNangTu)
            .ToListAsync(cancellationToken);

        var actualBand = sizeBands.FirstOrDefault(b =>
            request.CanNangThucTe >= b.CanNangTu &&
            (!b.CanNangDen.HasValue || request.CanNangThucTe < b.CanNangDen.Value));

        if (actualBand is null)
        {
            return new CheckinResult(
                StatusCodes.Status400BadRequest,
                Message: "Không xác định được khoảng size phù hợp với cân nặng thực tế.",
                ErrorCode: "VALIDATION_FAILED",
                Errors: [new ApiErrorItem { Field = "canNangThucTe", Message = "Cân nặng không khớp bảng size." }]);
        }

        // Lưu file ảnh bên ngoài transaction SQL để tránh giữ lock khi ghi file.
        var savedRelativePath = await SavePhotoOutsideTransactionAsync(photo.Content, photoExtension, cancellationToken);

        try
        {
            await using var transaction = dbContext.Database.IsRelational()
                ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
                : null;

            var now = DateTimeOffset.UtcNow;
            var sizeChot = actualBand.MaSize;

            chiTietDon.CanNangChot = request.CanNangThucTe;
            chiTietDon.SizeThucTe = actualBand.MaSize;
            chiTietDon.SizeChot = sizeChot;
            chiTietDon.ThoaThuanGia = request.QuyetDinhGia == PriceDecision.THU_GON
                ? request.NoiDungThuGon!.Trim()
                : PriceDecision.SIZE_THUC_TE.ToString();
            chiTietDon.TinhTrangLong = JsonSerializer.Serialize(normalizedFurConditions);
            chiTietDon.GhiChuSucKhoe = request.GhiChuSucKhoe.Trim();
            chiTietDon.NhanVienId = assignee.Id;
            chiTietDon.HoSoTiemChungId = request.MaHoSoTiemChung;
            chiTietDon.CheckInAt = now;
            chiTietDon.CheckInBoiId = actor.Id;
            chiTietDon.TrangThaiChamSoc = PetCareState.WAITING;
            chiTietDon.UpdatedAt = now;

            if (request.MaHoSoTiemChung.HasValue || request.XacNhanGiayHopLe == true)
            {
                chiTietDon.XacMinhTiemAt = now;
                chiTietDon.XacMinhTiemBoiId = actor.Id;
            }

            chiTietDon.ThuCung.CanNang = request.CanNangThucTe;
            chiTietDon.ThuCung.UpdatedAt = now;

            decimal priceDelta = 0m;
            foreach (var orderService in chiTietDon.DichVuTrongDonChiTietDonItems.Where(d => d.TrangThai != OrderServiceState.HUY))
            {
                var previousPrice = orderService.GiaChot ?? 0m;
                orderService.SizeChot = sizeChot;
                var resolvedPrice = await ResolveServicePriceAsync(orderService, actualBand.Id, sizeChot, cancellationToken);
                if (resolvedPrice.HasValue)
                {
                    orderService.GiaChot = resolvedPrice.Value;
                    priceDelta += resolvedPrice.Value - previousPrice;
                }
                orderService.UpdatedAt = now;
            }

            if (priceDelta != 0m)
            {
                booking.TongTamTinh = Math.Max(0m, booking.TongTamTinh + priceDelta);
            }

            if (booking.TrangThai == BookingState.CONFIRMED)
            {
                booking.TrangThai = BookingState.IN_PROGRESS;
            }
            booking.UpdatedAt = now;

            var careLog = new NhatKyChamSoc
            {
                ChiTietDonId = chiTietDon.Id,
                NhanVienId = actor.Id,
                LoaiNhatKy = "CHECK_IN",
                GhiNhanAt = now,
                ObjectPath = savedRelativePath,
                GhiChu = request.GhiChuSucKhoe.Trim(),
                CanNangGhiNhan = request.CanNangThucTe,
                CreatedAt = now,
                UpdatedAt = now,
                NhanVien = actor
            };
            dbContext.NhatKyChamSocSet.Add(careLog);
            await dbContext.SaveChangesAsync(cancellationToken);

            var responseDto = MapBookingPet(chiTietDon, assignee.TenNhanVien, normalizedFurConditions);

            dbContext.AuditLogSet.Add(new AuditLog
            {
                ActorLoai = actor.VaiTro.ToString(),
                ActorId = actor.Id,
                HanhDong = AuditAction,
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

            return new CheckinResult(StatusCodes.Status200OK, Data: responseDto, Message: "Check-in thú cưng thành công.");
        }
        catch
        {
            DeletePhotoIfExists(savedRelativePath);
            throw;
        }
    }

    private async Task<CheckinResult?> ValidateFurConditionsAsync(
        List<string> normalizedFurConditions,
        CancellationToken cancellationToken)
    {
        if (normalizedFurConditions.Count is < 1 or > 20)
        {
            return new CheckinResult(
                StatusCodes.Status400BadRequest,
                Message: "Tình trạng lông phải có từ 1 đến 20 mã hợp lệ.",
                ErrorCode: "VALIDATION_FAILED",
                Errors: [new ApiErrorItem { Field = "tinhTrangLong", Message = "Phải chọn từ 1 đến 20 tình trạng lông." }]);
        }

        var furConfig = await dbContext.CauHinhHeThongSet
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Khoa == "FUR_CONDITIONS", cancellationToken);

        var allowedCodes = new HashSet<string>(StringComparer.Ordinal);
        if (furConfig is not null && !string.IsNullOrWhiteSpace(furConfig.GiaTri))
        {
            var items = JsonSerializer.Deserialize<List<FurConditionItem>>(furConfig.GiaTri, JsonOptions) ?? [];
            foreach (var item in items.Where(i => i.DangHoatDong && !string.IsNullOrWhiteSpace(i.Ma)))
            {
                allowedCodes.Add(item.Ma);
            }
        }

        var invalidCodes = normalizedFurConditions.Where(c => !allowedCodes.Contains(c)).ToList();
        if (invalidCodes.Count > 0)
        {
            return new CheckinResult(
                StatusCodes.Status400BadRequest,
                Message: "Danh sách tình trạng lông chứa mã không hợp lệ hoặc đã ngừng hoạt động.",
                ErrorCode: "VALIDATION_FAILED",
                Errors:
                [
                    new ApiErrorItem
                    {
                        Field = "tinhTrangLong",
                        Message = $"Mã tình trạng lông không hợp lệ: {string.Join(", ", invalidCodes)}."
                    }
                ]);
        }

        return null;
    }

    private static List<ApiErrorItem> ValidateRequest(int chiTietDonId, Checkin request, CheckinPhotoInput photo)
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

        if (request.CanNangThucTe <= 0 || decimal.Round(request.CanNangThucTe, 2) != request.CanNangThucTe)
        {
            errors.Add(new ApiErrorItem { Field = "canNangThucTe", Message = "Cân nặng thực tế phải > 0 và tối đa 2 chữ số thập phân." });
        }

        if (request.TinhTrangLong is null || request.TinhTrangLong.Count < 1 || request.TinhTrangLong.Count > 20 ||
            request.TinhTrangLong.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add(new ApiErrorItem { Field = "tinhTrangLong", Message = "tinhTrangLong phải có từ 1 đến 20 mã không rỗng." });
        }

        if (request.MaNhanVien <= 0)
        {
            errors.Add(new ApiErrorItem { Field = "maNhanVien", Message = "maNhanVien phải lớn hơn 0." });
        }

        if (string.IsNullOrWhiteSpace(request.GhiChuSucKhoe) || request.GhiChuSucKhoe.Trim().Length > 2000)
        {
            errors.Add(new ApiErrorItem { Field = "ghiChuSucKhoe", Message = "ghiChuSucKhoe bắt buộc và tối đa 2000 ký tự." });
        }

        if (!request.QuyetDinhGia.HasValue)
        {
            errors.Add(new ApiErrorItem { Field = "quyetDinhGia", Message = "quyetDinhGia bắt buộc." });
        }

        if (!request.XacNhanKhach)
        {
            errors.Add(new ApiErrorItem { Field = "xacNhanKhach", Message = "Khách hàng phải xác nhận đồng thuận trước khi check-in." });
        }

        if (request.QuyetDinhGia == PriceDecision.THU_GON &&
            (string.IsNullOrWhiteSpace(request.NoiDungThuGon) || request.NoiDungThuGon.Trim().Length > 1000))
        {
            errors.Add(new ApiErrorItem { Field = "noiDungThuGon", Message = "noiDungThuGon bắt buộc (1..1000 ký tự) khi chọn THU_GON." });
        }

        if (photo.Length <= 0 || photo.Content.Length == 0)
        {
            errors.Add(new ApiErrorItem { Field = "anhTiepNhan", Message = "Ảnh tiếp nhận bắt buộc." });
        }

        return errors;
    }

    private static bool IsVaccinationRequired(ChiTietDon chiTietDon)
    {
        if (chiTietDon.DichVuTrongDonChiTietDonItems.Any(d =>
                d.TrangThai != OrderServiceState.HUY && d.DichVu?.YeuCauTiemChung == true))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(chiTietDon.DonDatLich.ChinhSachSnapshot))
        {
            try
            {
                using var doc = JsonDocument.Parse(chiTietDon.DonDatLich.ChinhSachSnapshot);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("yeuCauTiemChung", out var prop) &&
                    prop.ValueKind == JsonValueKind.True)
                {
                    return true;
                }
            }
            catch (JsonException)
            {
            }
        }

        return false;
    }

    private async Task<decimal?> ResolveServicePriceAsync(
        PetSpa.Core.Entities.Billing.DichVuTrongDon orderService,
        int sizeId,
        SizeBand sizeChot,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(orderService.BangGiaSizeSnapshot))
        {
            try
            {
                var snapshotPrices = JsonSerializer.Deserialize<Dictionary<string, decimal>>(orderService.BangGiaSizeSnapshot, JsonOptions);
                if (snapshotPrices is not null)
                {
                    var match = snapshotPrices.FirstOrDefault(kv =>
                        string.Equals(kv.Key, sizeChot.ToString(), StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrEmpty(match.Key))
                    {
                        return match.Value;
                    }
                }
            }
            catch (JsonException)
            {
            }
        }

        if (orderService.DichVuId.HasValue)
        {
            return await dbContext.BangGiaSizeSet
                .Where(b => b.DichVuId == orderService.DichVuId.Value && b.SizeId == sizeId)
                .Select(b => (decimal?)b.DonGia)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return orderService.GiaChot;
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
        var relativePath = Path.Combine("check-in", $"{Guid.NewGuid():N}{extension}").Replace('\\', '/');
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

    private static string ComputeFingerprint(
        int chiTietDonId,
        Checkin request,
        List<string> normalizedFurConditions,
        CheckinPhotoInput photo)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            chiTietDonId,
            request.RequestId,
            request.CanNangThucTe,
            TinhTrangLong = normalizedFurConditions,
            request.MaNhanVien,
            GhiChuSucKhoe = request.GhiChuSucKhoe.Trim(),
            request.QuyetDinhGia,
            request.XacNhanKhach,
            NoiDungThuGon = request.NoiDungThuGon?.Trim(),
            request.MaHoSoTiemChung,
            request.XacNhanGiayHopLe,
            photo.FileName,
            photo.Length
        });

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash);
    }

    private static BookingPet MapBookingPet(
        ChiTietDon entity,
        string assigneeName,
        List<string> furConditions)
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
            TinhTrangLong = furConditions,
            MaLoaiPhong = entity.LoaiPhongId,
            TenNhanVienPhuTrach = assigneeName,
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
