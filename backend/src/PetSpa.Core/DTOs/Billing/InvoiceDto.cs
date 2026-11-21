using System.Text.Json.Serialization;
using PetSpa.Core.Enums;

namespace PetSpa.Core.DTOs.Billing;

public sealed record CreateInvoiceCommand(Guid RequestId);

public sealed record InvoiceContact(string HoTen, string SoDienThoai, string Email);

public sealed record InvoiceQr(string ImageUrl, decimal SoTien, string NoiDungChuyenKhoan);

public sealed record InvoiceLineItemDto(
    int MaDongHoaDon, string LoaiKhoan, int? MaChiTietDon, int? MaDichVuTrongDon, int? MaPhuPhi,
    string? TenPet, string TenKhoan, string? Size, int SoLuong, decimal DonGia, decimal ThanhTien,
    string? DienGiai);

public sealed record InvoiceResponseDto(
    int MaHoaDon, string SoHoaDon, int MaDon, int? ThayTheHoaDon, string TrangThai,
    DateTimeOffset LapAt, string NguoiLap, InvoiceContact Khach, DateOnly NgayHen,
    List<InvoiceLineItemDto> DongTien, decimal TongTien, decimal CocKhauTru, decimal ConPhaiTra,
    DateTimeOffset? PaidAt, DateTimeOffset? ThuHoiAt, InvoiceQr? Qr);

public sealed class CheckoutRequestDto
{
    public Guid RequestId { get; init; }
    [JsonRequired]
    public decimal SoTienThucNhan { get; init; }
    [JsonRequired]
    public decimal SoTienDaTraLai { get; init; }
    [JsonRequired]
    [JsonConverter(typeof(PaymentMethodConverter))]
    public PaymentMethod PhuongThuc { get; init; }
    public string? ThamChieu { get; init; }
    public string? BangChungTraThua { get; init; }
}

public sealed class PaymentMethodConverter() : JsonStringEnumConverter<PaymentMethod>(allowIntegerValues: false);
