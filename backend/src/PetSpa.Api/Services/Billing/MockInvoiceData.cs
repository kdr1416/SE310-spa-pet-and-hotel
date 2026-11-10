using PetSpa.Core.DTOs.Billing;

namespace PetSpa.Api.Services.Billing;

public static class MockInvoiceData
{
    public static readonly List<InvoiceResponseDto> Invoices =
    [
        new()
        {
            Id = 1,
            MaHoaDon = "INV-202611-0001",
            TrangThai = "UNPAID",
            TongTien = 450000,
            CocKhauTru = 0,
            ConPhaiTra = 450000,
            DonDatLichId = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            ChiTiet =
            [
                new()
                {
                    Id = 1,
                    LoaiKhoan = "SPA",
                    TenPetSnapshot = "Corgi Miu",
                    TenKhoanMuc = "Tắm sấy vệ sinh",
                    SizeSnapshot = "M",
                    SoLuong = 1,
                    DonGiaSnapshot = 200000,
                    ThanhTien = 200000
                },
                new()
                {
                    Id = 2,
                    LoaiKhoan = "SPA",
                    TenPetSnapshot = "Corgi Miu",
                    TenKhoanMuc = "Cắt tỉa tạo kiểu",
                    SizeSnapshot = "M",
                    SoLuong = 1,
                    DonGiaSnapshot = 250000,
                    ThanhTien = 250000
                }
            ]
        }
    ];

    public static InvoiceResponseDto? FindById(int id) =>
        Invoices.FirstOrDefault(x => x.Id == id);

    public static InvoiceResponseDto? FindByBookingId(int bookingId) =>
        Invoices.FirstOrDefault(x => x.DonDatLichId == bookingId && x.TrangThai != "REVOKED");

    public static InvoiceResponseDto AddMockInvoice(int bookingId)
    {
        var newInvoice = new InvoiceResponseDto
        {
            Id = Invoices.Count + 1,
            MaHoaDon = $"INV-202611-{Invoices.Count + 1:D4}",
            TrangThai = "UNPAID",
            TongTien = 350000,
            CocKhauTru = 0,
            ConPhaiTra = 350000,
            DonDatLichId = bookingId,
            CreatedAt = DateTimeOffset.UtcNow,
            ChiTiet =
            [
                new()
                {
                    Id = Invoices.Count * 10,
                    LoaiKhoan = "SPA",
                    TenPetSnapshot = "Thú Cưng Test",
                    TenKhoanMuc = "Tắm sấy vệ sinh",
                    SizeSnapshot = "M",
                    SoLuong = 1,
                    DonGiaSnapshot = 350000,
                    ThanhTien = 350000
                }
            ]
        };
        Invoices.Add(newInvoice);
        return newInvoice;
    }
}