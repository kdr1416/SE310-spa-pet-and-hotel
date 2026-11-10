using PetSpa.Core.Enums;

namespace PetSpa.Core.DTOs.Billing;

public class InvoiceLineItemDto
{
    public int Id { get; set; }
    public string LoaiKhoan { get; set; } = string.Empty; // SPA, HOTEL, PHU_PHI
    public string? TenPetSnapshot { get; set; }
    public string TenKhoanMuc { get; set; } = string.Empty;
    public string? SizeSnapshot { get; set; }
    public int SoLuong { get; set; }
    public decimal DonGiaSnapshot { get; set; }
    public decimal ThanhTien { get; set; }
    public string? DienGiai { get; set; }
}

public class InvoiceResponseDto
{
    public int Id { get; set; }
    public string MaHoaDon { get; set; } = string.Empty;
    public string TrangThai { get; set; } = string.Empty; // UNPAID, PAID, REVOKED
    public decimal TongTien { get; set; }
    public decimal CocKhauTru { get; set; }
    public decimal ConPhaiTra { get; set; }
    public int DonDatLichId { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<InvoiceLineItemDto> ChiTiet { get; set; } = [];
}

public class CheckoutRequestDto
{
    public Guid RequestId { get; set; }
    public decimal SoTienThucNhan { get; set; }
    public decimal SoTienDaTraLai { get; set; }
    public PaymentMethod PhuongThuc { get; set; } // CASH, BANK_TRANSFER
    public string? NganHang { get; set; }
    public string? MaGiaoDich { get; set; }
    public string? GhiChu { get; set; }
}