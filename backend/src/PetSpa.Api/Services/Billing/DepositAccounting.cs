using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Enums;

namespace PetSpa.Api.Services.Billing;

internal sealed record DepositAmounts(decimal ThucNhan, decimal KhauTru, decimal GiuLai, decimal DanhHoan, decimal DaHoan);

internal static class DepositAccounting
{
    internal static DepositAmounts Calculate(DonDatLich booking)
    {
        var activeInvoices = booking.HoaDonDonDatLichItems.Where(x => x.TrangThai != InvoiceState.REVOKED).ToArray();
        if (activeInvoices.Length > 1)
            throw new InvalidOperationException("Đơn có nhiều hóa đơn hiệu lực.");
        var deposit = booking.TienCocDonDatLich;
        if (deposit is null) return new(0m, 0m, 0m, 0m, 0m);
        var received = deposit.GiaoDichThuTienCocItems.Sum(x => x.SoTien);
        var allocated = activeInvoices.SingleOrDefault()?.CocKhauTru ?? 0m;
        var reserved = deposit.HoanCocTienCocItems.Sum(x => x.SoTien);
        var refunded = deposit.HoanCocTienCocItems.Where(x => x.TrangThai == RefundState.DA_HOAN).Sum(x => x.SoTien);
        var wholeCancellation = booking.LoaiHuy is CancellationType.CUSTOMER_CANCELLED or
            CancellationType.STAFF_CANCELLED or CancellationType.NO_SHOW;
        var forfeited = booking.TrangThai == BookingState.CANCELLED && wholeCancellation &&
            !booking.ChiTietDonDonDatLichItems.Any(x => x.CheckInAt.HasValue) &&
            deposit.TrangThaiYeuCau != DepositRequestState.HET_HAN &&
            (booking.LoaiHuy == CancellationType.NO_SHOW ||
                booking.HuyAt.HasValue && booking.BatDauDuKien - booking.HuyAt.Value < TimeSpan.FromHours(2));
        return new(received, allocated, forfeited ? Math.Max(0m, received - allocated - reserved) : 0m, reserved, refunded);
    }
}
