using PetSpa.Api.Services.Billing;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Enums;

namespace PetSpa.Tests.Foundation;

public sealed class BillingSettlementTests
{
    private static readonly DateTimeOffset Now = BookingDepositProjectionTests.Now;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancelledTarget_UsesActualNightsBeforeOrAfterStateMutation_AndFrozenOtherPetPrice(bool alreadyCancelled)
    {
        var (booking, pet, combo) = PartialBooking();
        pet.TrangThaiChamSoc = alreadyCancelled ? PetCareState.CANCELLED : PetCareState.IN_PROGRESS;
        booking.PhuPhiDonDatLichItems.Add(new PhuPhi { SoTien = 50000m });
        booking.PhuPhiDonDatLichItems.Add(new PhuPhi { SoTien = 90000m, HuyAt = Now });
        booking.ChiTietDonDonDatLichItems.Add(new ChiTietDon
        {
            Id = 2, DichVuTrongDonChiTietDonItems = [new DichVuTrongDon
            { Id = 2, DichVuId = 2, BangGiaSizeSnapshot = "{\"s\":150000,\"m\":200000}" }]
        });
        booking.ChinhSachSnapshot = "{\"thuCung\":[{\"maChiTietDon\":2,\"sizeCamKet\":\"M\"}]}";
        new BillingSettlementHelper().SettleCancelledPetInMemory(booking, pet, new HashSet<int> { 1 }, 1, Now);
        Assert.Equal(650000m, booking.TongTamTinh);
        Assert.Equal(150000m, combo.GiaChot);
        Assert.Equal(OrderServiceState.HOAN_TAT, combo.TrangThai);
        Assert.Equal(OrderServiceState.HUY, combo.ThanhPhanDichVuTrongDonDichVuTrongDonItems.Last().TrangThai);
        Assert.Equal(1, pet.SoDemDaO);
        Assert.Equal(alreadyCancelled ? PetCareState.CANCELLED : PetCareState.IN_PROGRESS, pet.TrangThaiChamSoc);
        Assert.Empty(booking.TienCocDonDatLich!.HoanCocTienCocItems);
    }

    [Theory]
    [InlineData(0, 0, OrderServiceState.HUY)]
    [InlineData(1, 150000, OrderServiceState.HOAN_TAT)]
    [InlineData(2, 350000, OrderServiceState.HOAN_TAT)]
    public void Combo_NotStartedPartialAndComplete_UseCorrectPrice(int complete, int price, OrderServiceState expected)
    {
        var (booking, pet, combo) = PartialBooking();
        var parts = combo.ThanhPhanDichVuTrongDonDichVuTrongDonItems.ToArray();
        for (var i = 0; i < parts.Length; i++) parts[i].TrangThai = i < complete ? OrderServiceState.HOAN_TAT : OrderServiceState.CHO_THUC_HIEN;
        new BillingSettlementHelper().SettleCancelledPetInMemory(booking, pet, parts.Take(complete).Select(x => x.Id).ToHashSet(), 0, Now);
        Assert.Equal(price, combo.GiaChot);
        Assert.Equal(expected, combo.TrangThai);
        Assert.Equal(price, booking.TongTamTinh);
    }

    [Fact]
    public void SingleService_CompletedPriceKept_UnfinishedPriceZeroed()
    {
        var (booking, pet, _) = PartialBooking();
        pet.LoaiPhongId = null;
        pet.DichVuTrongDonChiTietDonItems = [
            new DichVuTrongDon { DichVuId = 1, TrangThai = OrderServiceState.HOAN_TAT, GiaChot = 120000m },
            new DichVuTrongDon { DichVuId = 2, GiaChot = 50000m }];
        new BillingSettlementHelper().SettleCancelledPetInMemory(booking, pet, new HashSet<int>(), 0, Now);
        Assert.Equal(120000m, booking.TongTamTinh);
        Assert.Equal(0m, pet.DichVuTrongDonChiTietDonItems.Last().GiaChot);
        Assert.Equal(OrderServiceState.HUY, pet.DichVuTrongDonChiTietDonItems.Last().TrangThai);
    }

    [Fact]
    public void MissingOtherPetPriceOrWrongCompletedIds_DoesNotPartiallyMutateTarget()
    {
        var (booking, pet, combo) = PartialBooking();
        var helper = new BillingSettlementHelper();
        Assert.Throws<ArgumentException>(() => helper.SettleCancelledPetInMemory(booking, pet, new HashSet<int>(), 1, Now));
        booking.ChiTietDonDonDatLichItems.Add(new ChiTietDon { Id = 2,
            DichVuTrongDonChiTietDonItems = [new DichVuTrongDon { DichVuId = 2, BangGiaSizeSnapshot = "{}" }] });
        Assert.Throws<InvalidOperationException>(() => helper.SettleCancelledPetInMemory(booking, pet, new HashSet<int> { 1 }, 1, Now));
        Assert.Equal(350000m, combo.GiaChot);
        Assert.Equal(OrderServiceState.CHO_THUC_HIEN, combo.ThanhPhanDichVuTrongDonDichVuTrongDonItems.Last().TrangThai);
        Assert.Null(pet.SoDemDaO);
        Assert.Equal(1100000m, booking.TongTamTinh);
    }

    [Fact]
    public void RefundBudget_DoesNotGrowWhenReservedRefundIsPaid_AndIgnoresRevokedInvoice()
    {
        var (booking, _, _) = PartialBooking();
        booking.TongTamTinh = 400000m;
        var deposit = booking.TienCocDonDatLich!;
        deposit.GiaoDichThuTienCocItems.Add(new GiaoDichThu { SoTien = 600000m });
        var refund = new HoanCoc { SoTien = 100000m };
        deposit.HoanCocTienCocItems.Add(refund);
        booking.HoaDonDonDatLichItems.Add(new HoaDon { TrangThai = InvoiceState.REVOKED, CocKhauTru = 500000m });
        var helper = new BillingSettlementHelper();
        Assert.Equal(400000m, helper.CalculateObligatedDepositAmount(booking));
        Assert.Equal(100000m, 600000m - helper.CalculateObligatedDepositAmount(booking) - 100000m);
        refund.TrangThai = RefundState.DA_HOAN;
        Assert.Equal(100000m, 600000m - helper.CalculateObligatedDepositAmount(booking) - 100000m);
        booking.HoaDonDonDatLichItems.Add(new HoaDon { TrangThai = InvoiceState.UNPAID, CocKhauTru = 250000m });
        Assert.Equal(250000m, helper.CalculateObligatedDepositAmount(booking));
    }

    [Fact]
    public void M2_04_RequiresPaidDoneAndReturnedCancelledPet_ButDoesNotWaitForRefund()
    {
        var booking = new DonDatLich { TrangThai = BookingState.IN_PROGRESS,
            ChiTietDonDonDatLichItems = [new ChiTietDon { TrangThaiChamSoc = PetCareState.DONE },
                new ChiTietDon { TrangThaiChamSoc = PetCareState.CANCELLED }],
            HoaDonDonDatLichItems = [new HoaDon { TrangThai = InvoiceState.PAID }] };
        var helper = new BillingSettlementHelper();
        helper.TryTransitionBookingTerminalState(booking, Now);
        Assert.Equal(BookingState.IN_PROGRESS, booking.TrangThai);
        booking.ChiTietDonDonDatLichItems.Last().TraLaiAt = Now;
        booking.HoaDonDonDatLichItems.Single().TrangThai = InvoiceState.UNPAID;
        helper.TryTransitionBookingTerminalState(booking, Now);
        Assert.Equal(BookingState.IN_PROGRESS, booking.TrangThai);
        booking.HoaDonDonDatLichItems.Single().TrangThai = InvoiceState.PAID;
        booking.ChiTietDonDonDatLichItems.First().TrangThaiChamSoc = PetCareState.READY;
        helper.TryTransitionBookingTerminalState(booking, Now);
        Assert.Equal(BookingState.IN_PROGRESS, booking.TrangThai);
        booking.ChiTietDonDonDatLichItems.First().TrangThaiChamSoc = PetCareState.DONE;
        booking.TienCocDonDatLich = new TienCoc { GiaoDichThuTienCocItems = [new GiaoDichThu { SoTien = 100m }],
            HoanCocTienCocItems = [new HoanCoc { SoTien = 100m, TrangThai = RefundState.CHO_HOAN }] };
        helper.TryTransitionBookingTerminalState(booking, Now);
        Assert.Equal(BookingState.COMPLETED, booking.TrangThai);
        helper.TryTransitionBookingTerminalState(booking, Now.AddHours(1));
        Assert.Equal(Now, booking.HoanTatAt);
    }

    [Theory]
    [InlineData(false, true, false, false, BookingState.IN_PROGRESS)]
    [InlineData(true, false, false, false, BookingState.IN_PROGRESS)]
    [InlineData(true, true, true, false, BookingState.IN_PROGRESS)]
    [InlineData(true, true, false, true, BookingState.IN_PROGRESS)]
    [InlineData(true, true, false, false, BookingState.CANCELLED)]
    public void M2_05_WaitsForPetPaymentRefundAndAllocation(bool returned, bool paid, bool pendingRefund, bool surplus, BookingState expected)
    {
        var booking = new DonDatLich { TrangThai = BookingState.IN_PROGRESS, TongTamTinh = 400000m,
            ChiTietDonDonDatLichItems = [new ChiTietDon { TrangThaiChamSoc = PetCareState.CANCELLED, CheckInAt = Now, TraLaiAt = returned ? Now : null }],
            HoaDonDonDatLichItems = [new HoaDon { TrangThai = paid ? InvoiceState.PAID : InvoiceState.UNPAID, CocKhauTru = 400000m }],
            TienCocDonDatLich = new TienCoc { GiaoDichThuTienCocItems = [new GiaoDichThu { SoTien = 600000m }],
                HoanCocTienCocItems = [new HoanCoc { SoTien = surplus ? 100000m : 200000m,
                    TrangThai = pendingRefund ? RefundState.CHO_HOAN : RefundState.DA_HOAN }] } };
        new BillingSettlementHelper().TryTransitionBookingTerminalState(booking, Now);
        Assert.Equal(expected, booking.TrangThai);
        Assert.Equal(PetCareState.CANCELLED, booking.ChiTietDonDonDatLichItems.Single().TrangThaiChamSoc);
    }

    [Fact]
    public void PrecheckinAllCancelled_WithPendingRefund_ClosesWithoutForfeiture()
    {
        var booking = BookingDepositProjectionTests.Booking();
        booking.TrangThai = BookingState.CONFIRMED;
        booking.TongTamTinh = 0m;
        booking.ChiTietDonDonDatLichItems.Add(new ChiTietDon { TrangThaiChamSoc = PetCareState.CANCELLED, TraLaiAt = Now });
        var helper = new BillingSettlementHelper();
        Assert.Equal(0m, helper.CalculateObligatedDepositAmount(booking));
        helper.TryTransitionBookingTerminalState(booking, Now);
        Assert.Equal(BookingState.CANCELLED, booking.TrangThai);
        Assert.Null(booking.LoaiHuy);
        Assert.Equal(0m, helper.CalculateObligatedDepositAmount(booking));
        helper.TryTransitionBookingTerminalState(booking, Now.AddHours(1));
        Assert.Equal(Now, booking.HuyAt);
    }

    [Fact]
    public void Terminal_EmptyPetsOrCheckedInPrecheckinState_DoesNotClose()
    {
        var helper = new BillingSettlementHelper();
        var empty = new DonDatLich { TrangThai = BookingState.IN_PROGRESS };
        helper.TryTransitionBookingTerminalState(empty, Now);
        Assert.Equal(BookingState.IN_PROGRESS, empty.TrangThai);
        empty.TrangThai = BookingState.CONFIRMED;
        empty.ChiTietDonDonDatLichItems.Add(new ChiTietDon { TrangThaiChamSoc = PetCareState.CANCELLED, CheckInAt = Now, TraLaiAt = Now });
        helper.TryTransitionBookingTerminalState(empty, Now);
        Assert.Equal(BookingState.CONFIRMED, empty.TrangThai);
    }

    [Theory]
    [InlineData(0, false, BookingState.CANCELLED)]
    [InlineData(1, false, BookingState.IN_PROGRESS)]
    [InlineData(0, true, BookingState.IN_PROGRESS)]
    public void AllCancelled_PayableZeroNeedsNoInvoice_ButUnpaidOrPositivePayableBlocks(int payable, bool unpaid, BookingState expected)
    {
        var booking = new DonDatLich { TrangThai = BookingState.IN_PROGRESS, TongTamTinh = payable,
            ChiTietDonDonDatLichItems = [new ChiTietDon { TrangThaiChamSoc = PetCareState.CANCELLED, CheckInAt = Now, TraLaiAt = Now }] };
        if (unpaid) booking.HoaDonDonDatLichItems.Add(new HoaDon { TrangThai = InvoiceState.UNPAID });
        new BillingSettlementHelper().TryTransitionBookingTerminalState(booking, Now);
        Assert.Equal(expected, booking.TrangThai);
    }

    private static (DonDatLich Booking, ChiTietDon Pet, DichVuTrongDon Combo) PartialBooking()
    {
        var combo = new DichVuTrongDon { Id = 1, ComboId = 1, GiaChot = 350000m,
            ThanhPhanDichVuTrongDonDichVuTrongDonItems = [
                new ThanhPhanDichVuTrongDon { Id = 1, TrangThai = OrderServiceState.HOAN_TAT, GiaLeSizeSnapshot = "{\"s\":150000}" },
                new ThanhPhanDichVuTrongDon { Id = 2, GiaLeSizeSnapshot = "{\"s\":250000}" }] };
        var pet = new ChiTietDon { Id = 1, LoaiPhongId = 1, GiaPhongCamKet = 250000m, SoDemChot = 3,
            CheckInAt = Now, SizeChot = SizeBand.S, DichVuTrongDonChiTietDonItems = [combo] };
        var booking = new DonDatLich { Id = 1, TrangThai = BookingState.IN_PROGRESS, TongTamTinh = 1100000m,
            ChiTietDonDonDatLichItems = [pet], TienCocDonDatLich = new TienCoc() };
        pet.DonDatLichId = booking.Id;
        return (booking, pet, combo);
    }
}
