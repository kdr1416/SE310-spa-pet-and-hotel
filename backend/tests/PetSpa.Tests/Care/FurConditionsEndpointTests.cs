using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PetSpa.Tests.Foundation;

namespace PetSpa.Tests.Care;

public class FurConditionsEndpointTests
{
    [Fact]
    public async Task OnlyActiveCatalogItemsAreReturnedWithoutWrites()
    {
        await using var f = await FoundationTestSupport.CreateAsync(DateTimeOffset.Parse("2035-01-01T01:00:00Z"));
        await using var db = f.Database.CreateContext();
        var row = await db.CauHinhHeThongSet.SingleAsync(x => x.Khoa == "FUR_CONDITIONS");
        row.GiaTri = """[{"ma":"NORMAL","ten":"Bình thường","dangHoatDong":true,"choPhepPhuPhiGoRoi":false},{"ma":"OLD","ten":"Cũ","dangHoatDong":false,"choPhepPhuPhiGoRoi":false}]""";
        await db.SaveChangesAsync();
        var version = row.PhienBan;
        using var client = await f.AdminClientAsync();
        var response = await client.GetAsync("/api/internal/fur-conditions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = json.RootElement.GetProperty("data").EnumerateArray().ToArray();
        items.Should().HaveCount(1);
        items[0].GetProperty("ma").GetString().Should().Be("NORMAL");
        await db.Entry(row).ReloadAsync();
        row.PhienBan.Should().Be(version);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrMalformedConfigReturns503RatherThanDefaults(bool malformed)
    {
        await using var f = await FoundationTestSupport.CreateAsync(DateTimeOffset.Parse("2035-01-01T01:00:00Z"));
        await using var db = f.Database.CreateContext();
        var row = await db.CauHinhHeThongSet.SingleAsync(x => x.Khoa == "FUR_CONDITIONS");
        if (malformed) row.GiaTri = "{}"; else db.Remove(row);
        await db.SaveChangesAsync();
        using var client = await f.AdminClientAsync();
        var response = await client.GetAsync("/api/internal/fur-conditions");
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).Should().Contain("TEMPORARY_FAILURE").And.NotContain("JsonException");
    }

    [Fact]
    public async Task AnonymousAndRevokedPermissionCannotReadConfig()
    {
        await using var f = await FoundationTestSupport.CreateAsync(DateTimeOffset.Parse("2035-01-01T01:00:00Z"));
        using var anonymous = f.Host.CreateClient();
        (await anonymous.GetAsync("/api/internal/fur-conditions")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var client = await f.AdminClientAsync();
        await using var db = f.Database.CreateContext();
        var admin = await db.NhanVienSet.SingleAsync(x => x.TenDangNhap == "admin");
        await db.NhanVienQuyenSet.Where(x => x.NhanVienId == admin.Id && x.MaQuyen == "CHECKIN_PERFORM").ExecuteDeleteAsync();
        (await client.GetAsync("/api/internal/fur-conditions")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
