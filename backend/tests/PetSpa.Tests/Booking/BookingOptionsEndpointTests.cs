using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PetSpa.Core.Entities.Scheduling;
using PetSpa.Tests.Foundation;

namespace PetSpa.Tests.Booking;

public class BookingOptionsEndpointTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2034-12-31T18:30:00Z");

    [Fact]
    public async Task Options_ReturnRealSlotsAndActiveShiftsWithoutWritingOrExposingCapacity()
    {
        await using var f = await FoundationTestSupport.CreateAsync(Now);
        await using var db = f.Database.CreateContext();
        var shifts = await db.CaLamViecSet.OrderBy(x => x.GioBatDau).ToListAsync();
        var slot = await db.KhungGioDatLichSet.FirstAsync(x => x.Ngay == new DateOnly(2035, 1, 1));
        slot.Id.Should().BePositive();
        var before = await db.KhungGioDatLichSet.CountAsync();
        using var client = f.Host.CreateClient();
        var response = await client.GetAsync("/api/booking-options?ngay=2035-01-01");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var rows = json.RootElement.GetProperty("data").GetProperty("ca").EnumerateArray().ToArray();
        rows.Select(x => x.GetProperty("maCa").GetInt32()).Should().Equal(shifts.Where(x => x.IsActive).Select(x => x.Id));
        rows.Should().Contain(x => x.GetProperty("maKhungGio").GetInt32() == slot.Id);
        var text = json.RootElement.ToString();
        text.Should().NotContain("slotDaDat").And.NotContain("phutDaGiu").And.NotContain("tenDangNhap");
        (await db.KhungGioDatLichSet.CountAsync()).Should().Be(before);
        var tomorrow = await client.GetAsync("/api/booking-options?ngay=2035-01-02");
        tomorrow.StatusCode.Should().Be(HttpStatusCode.OK);
        using var next = JsonDocument.Parse(await tomorrow.Content.ReadAsStringAsync());
        next.RootElement.GetProperty("data").GetProperty("ca").EnumerateArray()
            .Should().OnlyContain(x => x.GetProperty("maKhungGio").ValueKind == JsonValueKind.Null && !x.GetProperty("moNhanKhach").GetBoolean());
    }

    [Fact]
    public async Task ClosedSlotsAndHolidayAreNotBookableAndInactiveShiftsAreAbsent()
    {
        await using var f = await FoundationTestSupport.CreateAsync(Now);
        await using var db = f.Database.CreateContext();
        var shift = await db.CaLamViecSet.OrderBy(x => x.Id).FirstAsync();
        shift.IsActive = false;
        var slot = await db.KhungGioDatLichSet.FirstAsync();
        slot.MoNhanKhach = false;
        var actor = await db.NhanVienSet.FirstAsync(x => x.TenDangNhap == "admin");
        db.NgayNghiSet.Add(new NgayNghi { Ngay = new DateOnly(2035, 1, 1), LyDo = "Nghỉ test", TaoBoiId = actor.Id });
        await db.SaveChangesAsync();
        using var client = f.Host.CreateClient();
        var response = await client.GetAsync("/api/booking-options?ngay=2035-01-01");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var rows = json.RootElement.GetProperty("data").GetProperty("ca").EnumerateArray().ToArray();
        rows.Should().NotContain(x => x.GetProperty("maCa").GetInt32() == shift.Id);
        rows.Should().OnlyContain(x => !x.GetProperty("moNhanKhach").GetBoolean());
    }

    [Theory]
    [InlineData("")]
    [InlineData("?ngay=invalid")]
    [InlineData("?ngay=2034-12-31")]
    [InlineData("?ngay=2035-04-02")]
    public async Task MissingInvalidPastOrBeyondNinetyDaysReturnsFieldError(string query)
    {
        await using var f = await FoundationTestSupport.CreateAsync(Now);
        using var client = f.Host.CreateClient();
        var response = await client.GetAsync("/api/booking-options" + query);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task NinetyDayBoundaryIsIncluded()
    {
        await using var f = await FoundationTestSupport.CreateAsync(Now);
        using var client = f.Host.CreateClient();
        (await client.GetAsync("/api/booking-options?ngay=2035-04-01")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
