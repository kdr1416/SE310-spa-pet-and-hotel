using System.Reflection;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetSpa.Api.Services.Booking;
using PetSpa.Core.DTOs.Billing;
using PetSpa.Core.DTOs.Booking;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Enums;

namespace PetSpa.Tests.Foundation;

public sealed class BookingDepositProjectionTests
{
    internal static readonly DateTimeOffset Now = new(2035, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Projection_SumsReceiptsAndBothRefundStates_ExcludesRevokedInvoice()
    {
        var booking = Booking();
        booking.HoaDonDonDatLichItems.Add(new HoaDon { TrangThai = InvoiceState.UNPAID, CocKhauTru = 300000m });
        booking.HoaDonDonDatLichItems.Add(new HoaDon { TrangThai = InvoiceState.REVOKED, CocKhauTru = 500000m });
        var deposit = Deposit(BookingProjection.Project(booking));
        Assert.Equal(600000m, deposit.ThucNhan);
        Assert.Equal(300000m, deposit.KhauTru);
        Assert.Equal(150000m, deposit.DanhHoan);
        Assert.Equal(50000m, deposit.DaHoan);
    }

    [Theory]
    [InlineData(7200, CancellationType.CUSTOMER_CANCELLED, DepositRequestState.DA_DAP_UNG, false, 0)]
    [InlineData(7199, CancellationType.CUSTOMER_CANCELLED, DepositRequestState.DA_DAP_UNG, false, 450000)]
    [InlineData(0, CancellationType.NO_SHOW, DepositRequestState.HET_HAN, false, 0)]
    [InlineData(0, CancellationType.STAFF_REJECTED, DepositRequestState.DA_DAP_UNG, false, 0)]
    [InlineData(0, null, DepositRequestState.DA_DAP_UNG, false, 0)]
    [InlineData(0, CancellationType.NO_SHOW, DepositRequestState.DA_DAP_UNG, true, 0)]
    public void Penalty_RequiresWholeBookingCancellation_AndNeverAppliesToExpiry(int seconds,
        CancellationType? type, DepositRequestState state, bool checkedIn, int expected)
    {
        var booking = Booking();
        booking.TrangThai = BookingState.CANCELLED;
        booking.LoaiHuy = type;
        booking.HuyAt = Now;
        booking.BatDauDuKien = Now.AddSeconds(seconds);
        booking.TienCocDonDatLich!.TrangThaiYeuCau = state;
        booking.ChiTietDonDonDatLichItems.Add(new ChiTietDon { CheckInAt = checkedIn ? Now : null });
        Assert.Equal(expected, Deposit(BookingProjection.Project(booking)).GiuLai);
    }

    [Theory]
    [InlineData("BANK-12345678", "***5678")]
    [InlineData("1234", "***")]
    [InlineData("long-ghi chú", "***")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void InternalThenPublicProjection_DoesNotMutateRawReference(string? raw, string? expected)
    {
        var booking = Booking();
        var refund = booking.TienCocDonDatLich!.HoanCocTienCocItems.First();
        refund.ThamChieu = raw;
        var method = typeof(BookingProjection).GetMethods().SingleOrDefault(m => m.Name == "Project" && m.GetParameters().Length == 3);
        Assert.NotNull(method);
        var internalView = (BookingResponse)method.Invoke(null, [booking, null, true])!;
        var publicView = BookingProjection.Project(booking);
        Assert.Equal(expected, Deposit(internalView).HoanCoc.First().ThamChieu);
        Assert.Null(Deposit(publicView).HoanCoc.First().ThamChieu);
        Assert.Equal(raw, refund.ThamChieu);
        Assert.DoesNotContain("bangChungPath", JsonSerializer.Serialize(publicView, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Fact]
    public void MultipleActiveInvoices_FailsAsInvariantViolation()
    {
        var booking = Booking();
        booking.HoaDonDonDatLichItems.Add(new HoaDon { TrangThai = InvoiceState.UNPAID });
        booking.HoaDonDonDatLichItems.Add(new HoaDon { TrangThai = InvoiceState.PAID });
        Assert.Throws<InvalidOperationException>(() => BookingProjection.Project(booking));
    }

    [Fact]
    public void BookingWithoutDeposit_StillReturnsNull() => Assert.Null(BookingProjection.Project(new DonDatLich()).TienCoc);

    [Fact]
    public void Sprint1CreationSnapshot_DeserializesNullDeposit()
    {
        var response = BookingProjection.Project(new DonDatLich());
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Null(JsonSerializer.Deserialize<BookingResponse>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.TienCoc);
    }

    [Fact]
    public async Task HttpTrackingAndInternalList_LoadDepositGraphWithoutExposingEvidence()
    {
        await using var fixture = await FoundationTestSupport.CreateAsync(Now);
        await using var db = fixture.Database.CreateContext();
        var booking = await fixture.AddBookingAsync(db);
        var actor = await db.NhanVienSet.Select(x => x.Id).FirstAsync();
        booking.TienCocDonDatLich!.GiaoDichThuTienCocItems.Add(new GiaoDichThu
        {
            RequestId = Guid.NewGuid(), SoTien = 40000m, XacNhanBoiId = actor, GiaoDichAt = Now, XacNhanAt = Now
        });
        booking.TienCocDonDatLich.HoanCocTienCocItems.Add(new HoanCoc
        {
            RequestId = Guid.NewGuid(), SoTien = 10000m, TaoBoiId = actor, LyDo = "Hoàn test",
            ThamChieu = "BANK-12345678", BangChungPath = "private/evidence"
        });
        await db.SaveChangesAsync();
        using var guest = fixture.Host.CreateClient();
        using var response = await guest.PostAsJsonAsync("/api/tracking/lookup", new { soDienThoai = booking.SoDienThoaiSnapshot, trackingCode = booking.TrackingCode });
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var deposit = json.RootElement.GetProperty("data").GetProperty("tienCoc");
        Assert.Equal(40000m, deposit.GetProperty("thucNhan").GetDecimal());
        Assert.Equal(10000m, deposit.GetProperty("danhHoan").GetDecimal());
        Assert.Equal(JsonValueKind.Null, deposit.GetProperty("hoanCoc")[0].GetProperty("thamChieu").ValueKind);
        Assert.DoesNotContain("private/evidence", json.RootElement.GetRawText());
        using var admin = await fixture.AdminClientAsync();
        using var list = await admin.GetAsync("/api/internal/bookings");
        list.EnsureSuccessStatusCode();
        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Equal(40000m, listJson.RootElement.GetProperty("data").GetProperty("items")[0].GetProperty("tienCoc").GetProperty("thucNhan").GetDecimal());
    }

    internal static DonDatLich Booking()
    {
        var booking = new DonDatLich { Id = 1, BatDauDuKien = Now.AddHours(8) };
        booking.TienCocDonDatLich = new TienCoc
        {
            Id = 1, DonDatLichId = 1, CoSoTinhSnapshot = 1000000m, TyLe = .30m,
            SoTienYeuCau = 300000m, HetHanAt = Now.AddMinutes(30), TrangThaiYeuCau = DepositRequestState.CHO_THU,
            GiaoDichThuTienCocItems = [new GiaoDichThu { SoTien = 400000m }, new GiaoDichThu { SoTien = 200000m }],
            HoanCocTienCocItems = [new HoanCoc { Id = 1, SoTien = 100000m, TrangThai = RefundState.CHO_HOAN },
                new HoanCoc { Id = 2, SoTien = 50000m, TrangThai = RefundState.DA_HOAN }]
        };
        return booking;
    }

    private static DepositDto Deposit(BookingResponse response) => Assert.IsType<DepositDto>(response.TienCoc);
}
