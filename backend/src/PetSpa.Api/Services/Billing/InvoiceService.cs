using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetSpa.Api.Data;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Billing;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Enums;

namespace PetSpa.Api.Services.Billing;

public interface IInvoiceService
{
    Task<ApiResponse<InvoiceResponseDto>> CreateInvoiceAsync(int bookingId, int actorStaffId, CancellationToken cancellationToken = default);
    Task<ApiResponse<InvoiceResponseDto>> GetInvoiceByIdAsync(int invoiceId, int actorStaffId, CancellationToken cancellationToken = default);
    Task<ApiResponse<InvoiceResponseDto>> CheckoutInvoiceAsync(int invoiceId, CheckoutRequestDto request, int actorStaffId, CancellationToken cancellationToken = default);
}

public class InvoiceService(PetSpaDbContext context) : IInvoiceService
{
    private const string InvoiceCreatePermission = "INVOICE_CREATE";
    private const string InvoiceCollectPermission = "INVOICE_COLLECT";

    public async Task<ApiResponse<InvoiceResponseDto>> CreateInvoiceAsync(
        int bookingId,
        int actorStaffId,
        CancellationToken cancellationToken = default)
    {
        if (!await HasPermissionAsync(actorStaffId, InvoiceCreatePermission, cancellationToken))
        {
            return ApiResponse<InvoiceResponseDto>.Fail("Nhân viên không có quyền lập hóa đơn.", "FORBIDDEN");
        }

        var booking = await context.DonDatLichSet
            .Include(b => b.ChiTietDonDonDatLichItems)
                .ThenInclude(p => p.DichVuTrongDonChiTietDonItems)
            .Include(b => b.PhuPhiDonDatLichItems)
            .Include(b => b.TienCocDonDatLich)
            .Include(b => b.HoaDonDonDatLichItems)
            .FirstOrDefaultAsync(b => b.Id == bookingId, cancellationToken);

        if (booking is null)
        {
            return ApiResponse<InvoiceResponseDto>.Fail("Không tìm thấy đơn đặt lịch.", "BOOKING_NOT_FOUND");
        }

        var activeInvoice = booking.HoaDonDonDatLichItems.FirstOrDefault(x => x.TrangThai != InvoiceState.REVOKED);
        if (activeInvoice is not null)
        {
            return ApiResponse<InvoiceResponseDto>.Fail("Đơn đặt lịch đã có hóa đơn hiệu lực.", "INVOICE_ALREADY_EXISTS");
        }

        var notReadyPets = booking.ChiTietDonDonDatLichItems
            .Where(p => p.TrangThaiChamSoc != PetCareState.CANCELLED
                     && p.TrangThaiChamSoc != PetCareState.READY
                     && p.TrangThaiChamSoc != PetCareState.DONE)
            .ToList();

        if (notReadyPets.Count > 0)
        {
            return ApiResponse<InvoiceResponseDto>.Fail(
                "Thú cưng chưa hoàn tất chăm sóc (phải ở trạng thái READY).",
                "INVALID_TRANSITION");
        }

        var now = DateTimeOffset.UtcNow;
        var lineItems = new List<CtHoaDon>();
        decimal tongTien = 0;

        foreach (var pet in booking.ChiTietDonDonDatLichItems)
        {
            foreach (var service in pet.DichVuTrongDonChiTietDonItems.Where(d => d.TrangThai != OrderServiceState.HUY))
            {
                decimal donGia = service.GiaChot ?? 0m;
                lineItems.Add(new CtHoaDon
                {
                    LoaiKhoan = "SPA",
                    TenPetSnapshot = pet.TenPetSnapshot,
                    TenKhoanMuc = service.TenSnapshot,
                    SizeSnapshot = pet.SizeChot,
                    SoLuong = 1,
                    DonGiaSnapshot = donGia,
                    ThanhTien = donGia,
                    DienGiai = service.LyDo,
                    ChiTietDonId = pet.Id,
                    DichVuTrongDonId = service.Id,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                tongTien += donGia;
            }
        }

        foreach (var fee in booking.PhuPhiDonDatLichItems.Where(p => p.HuyAt == null))
        {
            lineItems.Add(new CtHoaDon
            {
                LoaiKhoan = "PHU_PHI",
                TenKhoanMuc = fee.TenPhuPhi,
                SizeSnapshot = null,
                SoLuong = 1,
                DonGiaSnapshot = fee.SoTien,
                ThanhTien = fee.SoTien,
                DienGiai = fee.LyDo,
                PhuPhiId = fee.Id,
                CreatedAt = now,
                UpdatedAt = now
            });
            tongTien += fee.SoTien;
        }

        if (lineItems.Count == 0)
        {
            return ApiResponse<InvoiceResponseDto>.Fail(
                "Đơn đặt lịch không có khoản mục hợp lệ để lập hóa đơn.",
                "INVALID_TRANSITION");
        }

        decimal cocKhauTru = 0;
        if (booking.TienCocDonDatLich is not null &&
            booking.TienCocDonDatLich.TrangThaiYeuCau == DepositRequestState.DA_DAP_UNG)
        {
            cocKhauTru = booking.TienCocDonDatLich.SoTienYeuCau;
        }

        decimal conPhaiTra = Math.Max(0, tongTien - cocKhauTru);

        var invoice = new HoaDon
        {
            MaHoaDon = $"INV-{now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}",
            TrangThai = InvoiceState.UNPAID,
            TongTien = tongTien,
            CocKhauTru = cocKhauTru,
            ConPhaiTra = conPhaiTra,
            DonDatLichId = booking.Id,
            TaoBoiId = actorStaffId,
            KhachSnapshot = JsonSerializer.Serialize(new
            {
                hoTen = booking.TenNguoiDat,
                sdt = booking.SoDienThoaiSnapshot
            }),
            CreatedAt = now,
            UpdatedAt = now,
            CtHoaDonHoaDonItems = lineItems
        };

        context.HoaDonSet.Add(invoice);
        await context.SaveChangesAsync(cancellationToken);

        return await LoadInvoiceDtoAsync(invoice.Id, "Lập hóa đơn thành công.", cancellationToken);
    }

    public async Task<ApiResponse<InvoiceResponseDto>> GetInvoiceByIdAsync(
        int invoiceId,
        int actorStaffId,
        CancellationToken cancellationToken = default)
    {
        if (!await HasPermissionAsync(actorStaffId, InvoiceCreatePermission, cancellationToken))
        {
            return ApiResponse<InvoiceResponseDto>.Fail("Nhân viên không có quyền xem hóa đơn.", "FORBIDDEN");
        }

        return await LoadInvoiceDtoAsync(invoiceId, "Lấy thông tin hóa đơn thành công.", cancellationToken);
    }

    public async Task<ApiResponse<InvoiceResponseDto>> CheckoutInvoiceAsync(
        int invoiceId,
        CheckoutRequestDto request,
        int actorStaffId,
        CancellationToken cancellationToken = default)
    {
        if (!await HasPermissionAsync(actorStaffId, InvoiceCollectPermission, cancellationToken))
        {
            return ApiResponse<InvoiceResponseDto>.Fail("Nhân viên không có quyền thu tiền hóa đơn.", "FORBIDDEN");
        }

        if (request.RequestId == Guid.Empty)
        {
            return ApiResponse<InvoiceResponseDto>.Fail("Thiếu requestId hợp lệ.", "VALIDATION_FAILED");
        }

        if (request.SoTienThucNhan < 0 || request.SoTienDaTraLai < 0)
        {
            return ApiResponse<InvoiceResponseDto>.Fail("Số tiền thu và tiền trả lại không được âm.", "VALIDATION_FAILED");
        }

        decimal netAmount = request.SoTienThucNhan - request.SoTienDaTraLai;

        var existingTx = await context.GiaoDichThuSet
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.RequestId == request.RequestId, cancellationToken);

        if (existingTx is not null)
        {
            bool samePayload = existingTx.HoaDonId == invoiceId
                && existingTx.SoTien == netAmount
                && existingTx.PhuongThuc == request.PhuongThuc
                && string.Equals(existingTx.NganHang, request.NganHang, StringComparison.Ordinal)
                && string.Equals(existingTx.MaGiaoDich, request.MaGiaoDich, StringComparison.Ordinal)
                && string.Equals(existingTx.GhiChu, request.GhiChu, StringComparison.Ordinal);

            if (samePayload)
            {
                return await LoadInvoiceDtoAsync(invoiceId, "Thanh toán hóa đơn thành công.", cancellationToken);
            }

            return ApiResponse<InvoiceResponseDto>.Fail(
                "Trùng lặp requestId với nội dung thanh toán khác.",
                "IDEMPOTENCY_CONFLICT");
        }

        var invoice = await context.HoaDonSet.FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
        if (invoice is null)
        {
            return ApiResponse<InvoiceResponseDto>.Fail("Không tìm thấy hóa đơn.", "INVOICE_NOT_FOUND");
        }

        if (invoice.TrangThai == InvoiceState.PAID)
        {
            return ApiResponse<InvoiceResponseDto>.Fail("Hóa đơn đã được thanh toán trước đó.", "INVALID_TRANSITION");
        }

        if (invoice.TrangThai == InvoiceState.REVOKED)
        {
            return ApiResponse<InvoiceResponseDto>.Fail("Hóa đơn này đã bị thu hồi.", "INVALID_TRANSITION");
        }

        if (netAmount != invoice.ConPhaiTra)
        {
            return ApiResponse<InvoiceResponseDto>.Fail(
                "Số tiền thu không khớp với số tiền cần thanh toán.",
                "AMOUNT_MISMATCH");
        }

        var now = DateTimeOffset.UtcNow;
        await using var tx = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            invoice.TrangThai = InvoiceState.PAID;
            invoice.PaidAt = now;
            invoice.ThuBoiId = actorStaffId;
            invoice.UpdatedAt = now;

            var giaoDich = new GiaoDichThu
            {
                RequestId = request.RequestId,
                SoTien = netAmount,
                PhuongThuc = request.PhuongThuc,
                NganHang = request.NganHang,
                MaGiaoDich = request.MaGiaoDich,
                GiaoDichAt = now,
                XacNhanAt = now,
                GhiChu = request.GhiChu,
                HoaDonId = invoice.Id,
                XacNhanBoiId = actorStaffId,
                CreatedAt = now,
                UpdatedAt = now
            };

            context.GiaoDichThuSet.Add(giaoDich);
            await context.SaveChangesAsync(cancellationToken);

            if (tx is not null)
            {
                await tx.CommitAsync(cancellationToken);
            }

            return await LoadInvoiceDtoAsync(invoice.Id, "Thanh toán hóa đơn thành công.", cancellationToken);
        }
        catch
        {
            if (tx is not null)
            {
                await tx.RollbackAsync(cancellationToken);
            }
            throw;
        }
    }

    private async Task<bool> HasPermissionAsync(int actorStaffId, string permissionCode, CancellationToken cancellationToken)
    {
        var actor = await context.NhanVienSet
            .AsNoTracking()
            .Include(nv => nv.NhanVienQuyenNhanVienItems)
            .FirstOrDefaultAsync(nv => nv.Id == actorStaffId, cancellationToken);

        return actor is not null
            && actor.IsActive
            && actor.NhanVienQuyenNhanVienItems.Any(q => q.MaQuyen == permissionCode);
    }

    private async Task<ApiResponse<InvoiceResponseDto>> LoadInvoiceDtoAsync(
        int invoiceId,
        string successMessage,
        CancellationToken cancellationToken)
    {
        var invoice = await context.HoaDonSet
            .AsNoTracking()
            .Include(i => i.CtHoaDonHoaDonItems)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);

        if (invoice is null)
        {
            return ApiResponse<InvoiceResponseDto>.Fail("Không tìm thấy hóa đơn.", "INVOICE_NOT_FOUND");
        }

        var dto = new InvoiceResponseDto
        {
            Id = invoice.Id,
            MaHoaDon = invoice.MaHoaDon,
            TrangThai = invoice.TrangThai.ToString(),
            TongTien = invoice.TongTien,
            CocKhauTru = invoice.CocKhauTru,
            ConPhaiTra = invoice.ConPhaiTra,
            DonDatLichId = invoice.DonDatLichId,
            PaidAt = invoice.PaidAt,
            CreatedAt = invoice.CreatedAt,
            ChiTiet = invoice.CtHoaDonHoaDonItems.Select(c => new InvoiceLineItemDto
            {
                Id = c.Id,
                LoaiKhoan = c.LoaiKhoan,
                TenPetSnapshot = c.TenPetSnapshot,
                TenKhoanMuc = c.TenKhoanMuc,
                SizeSnapshot = c.SizeSnapshot?.ToString(),
                SoLuong = c.SoLuong,
                DonGiaSnapshot = c.DonGiaSnapshot,
                ThanhTien = c.ThanhTien,
                DienGiai = c.DienGiai
            }).ToList()
        };

        return ApiResponse<InvoiceResponseDto>.Ok(dto, successMessage);
    }
}
