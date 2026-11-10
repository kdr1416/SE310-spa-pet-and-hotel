using Microsoft.EntityFrameworkCore;
using PetSpa.Api.Data;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Billing;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Enums;

namespace PetSpa.Api.Services.Billing;

public interface IInvoiceService
{
    Task<ApiResponse<InvoiceResponseDto>> CreateInvoiceAsync(int bookingId, int actorStaffId);
    Task<ApiResponse<InvoiceResponseDto>> GetInvoiceByIdAsync(int invoiceId);
    Task<ApiResponse<InvoiceResponseDto>> CheckoutInvoiceAsync(int invoiceId, CheckoutRequestDto request, int actorStaffId);
}

/// <summary>
/// Mock Implementation: Điều phối và gọi sang MockInvoiceData
/// </summary>
public class MockInvoiceService : IInvoiceService
{
    public Task<ApiResponse<InvoiceResponseDto>> CreateInvoiceAsync(int bookingId, int actorStaffId)
    {
        var existing = MockInvoiceData.FindByBookingId(bookingId);
        if (existing != null)
        {
            return Task.FromResult(ApiResponse<InvoiceResponseDto>.Fail("Đơn đặt lịch này đã có hóa đơn hiệu lực.", "INVOICE_ALREADY_EXISTS"));
        }

        var newInvoice = MockInvoiceData.AddMockInvoice(bookingId);
        return Task.FromResult(ApiResponse<InvoiceResponseDto>.Ok(newInvoice, "Lập hóa đơn thành công"));
    }

    public Task<ApiResponse<InvoiceResponseDto>> GetInvoiceByIdAsync(int invoiceId)
    {
        var invoice = MockInvoiceData.FindById(invoiceId);
        if (invoice == null)
        {
            return Task.FromResult(ApiResponse<InvoiceResponseDto>.Fail("Không tìm thấy hóa đơn.", "INVOICE_NOT_FOUND"));
        }
        return Task.FromResult(ApiResponse<InvoiceResponseDto>.Ok(invoice, "Lấy thông tin hóa đơn thành công"));
    }

    public Task<ApiResponse<InvoiceResponseDto>> CheckoutInvoiceAsync(int invoiceId, CheckoutRequestDto request, int actorStaffId)
    {
        var invoice = MockInvoiceData.FindById(invoiceId);
        if (invoice == null)
        {
            return Task.FromResult(ApiResponse<InvoiceResponseDto>.Fail("Không tìm thấy hóa đơn.", "INVOICE_NOT_FOUND"));
        }

        if (invoice.TrangThai == "PAID")
        {
            return Task.FromResult(ApiResponse<InvoiceResponseDto>.Fail("Hóa đơn này đã được thanh toán.", "INVALID_TRANSITION"));
        }

        var netPayment = request.SoTienThucNhan - request.SoTienDaTraLai;
        if (netPayment != invoice.ConPhaiTra)
        {
            return Task.FromResult(ApiResponse<InvoiceResponseDto>.Fail("Số tiền thanh toán không khớp với số tiền còn phải trả.", "AMOUNT_MISMATCH"));
        }

        invoice.TrangThai = "PAID";
        invoice.PaidAt = DateTimeOffset.UtcNow;

        return Task.FromResult(ApiResponse<InvoiceResponseDto>.Ok(invoice, "Thanh toán hóa đơn thành công"));
    }
}

/// <summary>
/// Real Database Implementation: Tuân thủ đầy đủ snapshot bất biến và transaction trong PostgreSQL
/// </summary>
public class InvoiceService : IInvoiceService
{
    private readonly PetSpaDbContext _context;

    public InvoiceService(PetSpaDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<InvoiceResponseDto>> CreateInvoiceAsync(int bookingId, int actorStaffId)
    {
        // 1. Kiểm tra đơn đặt lịch
        var booking = await _context.DonDatLichSet
            .Include(b => b.ChiTietDonDonDatLichItems)
                .ThenInclude(p => p.DichVuTrongDonChiTietDonItems)
            .Include(b => b.PhuPhiDonDatLichItems)
            .Include(b => b.TienCocDonDatLich)
            .Include(b => b.HoaDonDonDatLichItems)
            .FirstOrDefaultAsync(b => b.Id == bookingId);

        if (booking == null)
        {
            return ApiResponse<InvoiceResponseDto>.Fail("Không tìm thấy đơn đặt lịch.", "BOOKING_NOT_FOUND");
        }

        // Kiểm tra hóa đơn hiệu lực đã tồn tại
        var activeInvoice = booking.HoaDonDonDatLichItems.FirstOrDefault(x => x.TrangThai != InvoiceState.REVOKED);
        if (activeInvoice != null)
        {
            return ApiResponse<InvoiceResponseDto>.Fail("Đơn đặt lịch đã có hóa đơn có hiệu lực.", "INVOICE_ALREADY_EXISTS");
        }

        // Guard: Mọi thú cưng phải đang ở trạng thái READY (hoặc DONE)
        var notReadyPets = booking.ChiTietDonDonDatLichItems
            .Where(p => p.TrangThaiChamSoc != PetCareState.READY && p.TrangThaiChamSoc != PetCareState.DONE)
            .ToList();

        if (notReadyPets.Any())
        {
            return ApiResponse<InvoiceResponseDto>.Fail("Thú cưng chưa hoàn tất chăm sóc (phải ở trạng thái READY).", "INVALID_TRANSITION");
        }

        // 2. Tính toán tiền và lập các dòng chi tiết snapshot
        var lineItems = new List<CtHoaDon>();
        decimal tongTien = 0;

        foreach (var pet in booking.ChiTietDonDonDatLichItems)
        {
            foreach (var s in pet.DichVuTrongDonChiTietDonItems.Where(d => d.TrangThai == OrderServiceState.HOAN_TAT || d.TrangThai == OrderServiceState.CHO_THUC_HIEN))
            {
                decimal donGia = s.GiaChot ?? 0;
                var item = new CtHoaDon
                {
                    LoaiKhoan = "SPA",
                    TenPetSnapshot = pet.TenPetSnapshot,
                    TenKhoanMuc = s.TenSnapshot,
                    SizeSnapshot = pet.SizeChot,
                    SoLuong = 1,
                    DonGiaSnapshot = donGia,
                    ThanhTien = donGia,
                    DienGiai = s.LyDo,
                    ChiTietDonId = pet.Id,
                    DichVuTrongDonId = s.Id
                };
                tongTien += donGia;
                lineItems.Add(item);
            }
        }

        foreach (var pp in booking.PhuPhiDonDatLichItems.Where(p => p.HuyAt == null))
        {
            var item = new CtHoaDon
            {
                LoaiKhoan = "PHU_PHI",
                TenKhoanMuc = pp.TenPhuPhi,
                SizeSnapshot = null,
                SoLuong = 1,
                DonGiaSnapshot = pp.SoTien,
                ThanhTien = pp.SoTien,
                DienGiai = pp.LyDo,
                PhuPhiId = pp.Id
            };
            tongTien += pp.SoTien;
            lineItems.Add(item);
        }

        decimal cocKhauTru = 0;
        if (booking.TienCocDonDatLich != null && booking.TienCocDonDatLich.TrangThaiYeuCau == DepositRequestState.DA_DAP_UNG)
        {
            cocKhauTru = booking.TienCocDonDatLich.SoTienYeuCau;
        }

        decimal conPhaiTra = Math.Max(0, tongTien - cocKhauTru);

        // 3. Khởi tạo đối tượng Hóa Đơn
        var invoice = new HoaDon
        {
            MaHoaDon = $"INV-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..4].ToUpper()}",
            TrangThai = InvoiceState.UNPAID,
            TongTien = tongTien,
            CocKhauTru = cocKhauTru,
            ConPhaiTra = conPhaiTra,
            DonDatLichId = booking.Id,
            TaoBoiId = actorStaffId,
            KhachSnapshot = $"{{\"hoTen\":\"{booking.TenNguoiDat}\",\"sdt\":\"{booking.SoDienThoaiSnapshot}\"}}",
            CtHoaDonHoaDonItems = lineItems
        };

        _context.HoaDonSet.Add(invoice);
        await _context.SaveChangesAsync();

        return await GetInvoiceByIdAsync(invoice.Id);
    }

    public async Task<ApiResponse<InvoiceResponseDto>> GetInvoiceByIdAsync(int invoiceId)
    {
        var invoice = await _context.HoaDonSet
            .AsNoTracking()
            .Include(i => i.CtHoaDonHoaDonItems)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice == null)
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

        return ApiResponse<InvoiceResponseDto>.Ok(dto, "Lấy thông tin hóa đơn thành công");
    }

    public async Task<ApiResponse<InvoiceResponseDto>> CheckoutInvoiceAsync(int invoiceId, CheckoutRequestDto request, int actorStaffId)
    {
        // 1. Kiểm tra Idempotency thông qua RequestId
        var existingTx = await _context.GiaoDichThuSet
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.RequestId == request.RequestId);

        if (existingTx != null)
        {
            if (existingTx.HoaDonId == invoiceId && existingTx.SoTien == (request.SoTienThucNhan - request.SoTienDaTraLai))
            {
                return await GetInvoiceByIdAsync(invoiceId);
            }
            return ApiResponse<InvoiceResponseDto>.Fail("Trùng lặp requestId với nội dung thanh toán khác.", "IDEMPOTENCY_CONFLICT");
        }

        // 2. Tìm hóa đơn
        var invoice = await _context.HoaDonSet.FirstOrDefaultAsync(i => i.Id == invoiceId);
        if (invoice == null)
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

        // 3. Ràng buộc: Thu đủ 100% tiền (Thực nhận - Trả lại == Còn phải trả)
        decimal netAmount = request.SoTienThucNhan - request.SoTienDaTraLai;
        if (netAmount != invoice.ConPhaiTra)
        {
            return ApiResponse<InvoiceResponseDto>.Fail("Số tiền thu không khớp với số tiền cần thanh toán.", "AMOUNT_MISMATCH");
        }

        // 4. Cập nhật trạng thái trong Transaction
        using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            invoice.TrangThai = InvoiceState.PAID;
            invoice.PaidAt = DateTimeOffset.UtcNow;
            invoice.ThuBoiId = actorStaffId;

            var giaoDich = new GiaoDichThu
            {
                RequestId = request.RequestId,
                SoTien = netAmount,
                PhuongThuc = request.PhuongThuc,
                NganHang = request.NganHang,
                MaGiaoDich = request.MaGiaoDich,
                GiaoDichAt = DateTimeOffset.UtcNow,
                XacNhanAt = DateTimeOffset.UtcNow,
                GhiChu = request.GhiChu,
                HoaDonId = invoice.Id,
                XacNhanBoiId = actorStaffId
            };

            _context.GiaoDichThuSet.Add(giaoDich);
            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            return await GetInvoiceByIdAsync(invoice.Id);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }
}