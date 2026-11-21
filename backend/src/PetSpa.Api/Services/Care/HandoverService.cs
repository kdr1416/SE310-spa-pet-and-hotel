using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetSpa.Api.Data;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Care;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Enums;
using PetSpa.Core.Interfaces.Care;

namespace PetSpa.Api.Services.Care;

public class HandoverService(PetSpaDbContext dbContext) : IHandoverService
{
    private const string RequiredPermission = "PET_HANDOVER";
    private const string AuditAction = "PET_HANDOVER";
    private const string AuditTargetType = "ChiTietDon";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<HandoverResult> HandoverAsync(
        int chiTietDonId,
        int actorId,
        Handover request,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await dbContext.NhanVienSet
            .Include(nv => nv.NhanVienQuyenNhanVienItems)
            .FirstOrDefaultAsync(nv => nv.Id == actorId, cancellationToken);

        if (actor is null || !actor.IsActive ||
            !actor.NhanVienQuyenNhanVienItems.Any(q => q.MaQuyen == RequiredPermission))
        {
            return new HandoverResult(
                StatusCodes.Status403Forbidden,
                Message: "Bạn không có quyền bàn giao thú cưng cho khách.",
                ErrorCode: "FORBIDDEN");
        }

        var validationErrors = ValidateRequest(chiTietDonId, request);
        if (validationErrors.Count > 0)
        {
            return new HandoverResult(
                StatusCodes.Status400BadRequest,
                Message: "Dữ liệu bàn giao thú cưng không hợp lệ.",
                ErrorCode: "VALIDATION_FAILED",
                Errors: validationErrors);
        }

        var fingerprint = ComputeFingerprint(chiTietDonId, request);

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
                return new HandoverResult(
                    StatusCodes.Status409Conflict,
                    Message: "RequestId đã được sử dụng với dữ liệu khác.",
                    ErrorCode: "IDEMPOTENCY_CONFLICT");
            }

            if (!string.IsNullOrWhiteSpace(existingAudit.KetQua))
            {
                var cached = JsonSerializer.Deserialize<BookingPet>(existingAudit.KetQua, JsonOptions);
                if (cached is not null)
                {
                    return new HandoverResult(
                        StatusCodes.Status200OK,
                        Data: cached,
                        Message: "Bàn giao thú cưng thành công.");
                }
            }
        }

        var chiTietDon = await dbContext.ChiTietDonSet
            .Include(c => c.DonDatLich)
                .ThenInclude(d => d.ChiTietDonDonDatLichItems)
            .Include(c => c.DonDatLich)
                .ThenInclude(d => d.HoaDonDonDatLichItems)
            .Include(c => c.ThuCung)
            .Include(c => c.NhanVien)
            .Include(c => c.DichVuTrongDonChiTietDonItems)
                .ThenInclude(dv => dv.ThanhPhanDichVuTrongDonDichVuTrongDonItems)
                    .ThenInclude(tp => tp.DichVu)
            .Include(c => c.NhatKyChamSocChiTietDonItems)
                .ThenInclude(nk => nk.NhanVien)
            .FirstOrDefaultAsync(c => c.Id == chiTietDonId, cancellationToken);

        if (chiTietDon is null)
        {
            return new HandoverResult(
                StatusCodes.Status404NotFound,
                Message: "Không tìm thấy thú cưng trong đơn đặt lịch.",
                ErrorCode: "NOT_FOUND");
        }

        if (chiTietDon.DonDatLich.TrangThai != BookingState.IN_PROGRESS)
        {
            return new HandoverResult(
                StatusCodes.Status409Conflict,
                Message: "Đơn đặt lịch không ở trạng thái IN_PROGRESS để bàn giao thú cưng.",
                ErrorCode: "INVALID_TRANSITION");
        }

        if (chiTietDon.TrangThaiChamSoc != PetCareState.READY)
        {
            return new HandoverResult(
                StatusCodes.Status409Conflict,
                Message: $"Không thể bàn giao khi thú cưng đang ở trạng thái {chiTietDon.TrangThaiChamSoc}.",
                ErrorCode: "INVALID_TRANSITION");
        }

        var hasPaidInvoice = chiTietDon.DonDatLich.HoaDonDonDatLichItems
            .Any(h => h.TrangThai == InvoiceState.PAID);

        if (!hasPaidInvoice)
        {
            return new HandoverResult(
                StatusCodes.Status409Conflict,
                Message: "Hóa đơn của đơn đặt lịch chưa được thanh toán (PAID).",
                ErrorCode: "INVALID_TRANSITION");
        }

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var now = DateTimeOffset.UtcNow;

        chiTietDon.TrangThaiChamSoc = PetCareState.DONE;
        chiTietDon.BanGiaoAt = now;
        chiTietDon.BanGiaoBoiId = actor.Id;
        chiTietDon.UpdatedAt = now;

        var nonCancelledPets = chiTietDon.DonDatLich.ChiTietDonDonDatLichItems
            .Where(p => p.TrangThaiChamSoc != PetCareState.CANCELLED)
            .ToList();

        if (nonCancelledPets.Count > 0 &&
            nonCancelledPets.All(p => p.TrangThaiChamSoc == PetCareState.DONE))
        {
            chiTietDon.DonDatLich.TrangThai = BookingState.COMPLETED;
            chiTietDon.DonDatLich.HoanTatAt = now;
            chiTietDon.DonDatLich.UpdatedAt = now;
        }

        var responseDto = MapBookingPet(chiTietDon);

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

        return new HandoverResult(
            StatusCodes.Status200OK,
            Data: responseDto,
            Message: "Bàn giao thú cưng thành công.");
    }

    private static List<ApiErrorItem> ValidateRequest(int chiTietDonId, Handover request)
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

        if (!request.XacNhanDaNhan)
        {
            errors.Add(new ApiErrorItem { Field = "xacNhanDaNhan", Message = "xacNhanDaNhan bắt buộc phải là true." });
        }

        return errors;
    }

    private static string ComputeFingerprint(int chiTietDonId, Handover request)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            chiTietDonId,
            request.RequestId,
            request.XacNhanDaNhan
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
