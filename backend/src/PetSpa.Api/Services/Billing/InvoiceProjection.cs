using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using PetSpa.Core.DTOs.Billing;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Enums;

namespace PetSpa.Api.Services.Billing;

internal static class InvoiceProjection
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static InvoiceResponseDto Project(HoaDon invoice, IConfiguration configuration, bool includeInternalDetails)
    {
        var contact = JsonSerializer.Deserialize<InvoiceContact>(invoice.KhachSnapshot, JsonOptions);
        // Early invoice snapshots used sdt and omitted the email.
        contact = new InvoiceContact(
            contact?.HoTen ?? invoice.DonDatLich.TenNguoiDat,
            contact?.SoDienThoai ?? invoice.DonDatLich.SoDienThoaiSnapshot,
            contact?.Email ?? invoice.DonDatLich.EmailSnapshot);
        var lines = invoice.CtHoaDonHoaDonItems.OrderBy(line => line.Id).Select(line => new InvoiceLineItemDto(
            line.Id, line.LoaiKhoan, line.ChiTietDonId, line.DichVuTrongDonId, line.PhuPhiId, line.TenPetSnapshot,
            line.TenKhoanMuc, line.SizeSnapshot?.ToString(), line.SoLuong, line.DonGiaSnapshot, line.ThanhTien,
            includeInternalDetails ? line.DienGiai : null)).ToList();

        return new InvoiceResponseDto(invoice.Id, invoice.MaHoaDon, invoice.DonDatLichId, invoice.ThayTheHoaDonId,
            invoice.TrangThai.ToString(), invoice.CreatedAt, invoice.TaoBoi.TenNhanVien, contact, invoice.DonDatLich.KhungGio.Ngay,
            lines, invoice.TongTien, invoice.CocKhauTru, invoice.ConPhaiTra, invoice.PaidAt, invoice.ThuHoiAt,
            BuildQr(invoice, configuration));
    }

    private static InvoiceQr? BuildQr(HoaDon invoice, IConfiguration configuration)
    {
        if (invoice.TrangThai != InvoiceState.UNPAID || invoice.ConPhaiTra <= 0
            || !Uri.TryCreate(configuration["Billing:VietQrImageUrl"], UriKind.Absolute, out var image)
            || image.Scheme != Uri.UriSchemeHttps)
            return null;

        var note = $"{invoice.DonDatLichId} {invoice.MaHoaDon}";
        var url = QueryHelpers.AddQueryString(image.AbsoluteUri, new Dictionary<string, string?>
        {
            ["amount"] = invoice.ConPhaiTra.ToString("0", CultureInfo.InvariantCulture),
            ["addInfo"] = note
        });
        return new InvoiceQr(url, invoice.ConPhaiTra, note);
    }
}
