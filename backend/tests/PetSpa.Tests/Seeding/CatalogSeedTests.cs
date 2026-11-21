using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Scheduling;
using PetSpa.Core.Enums;
using PetSpa.Tests.Infrastructure;

namespace PetSpa.Tests.Seeding;

public class CatalogSeedTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StagingCatalogSeed_OnlyAddsMissingFurCodes(bool existingRow)
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var admin = new NhanVien { TenDangNhap = "catalog-admin", TenNhanVien = "Admin", VaiTro = Role.ADMIN, IsActive = true };
        context.NhanVienSet.Add(admin);
        await context.SaveChangesAsync();
        if (existingRow)
        {
            context.CauHinhHeThongSet.Add(new CauHinhHeThong
            {
                Khoa = "FUR_CONDITIONS", PhienBan = 1, SuaBoiId = admin.Id,
                GiaTri = """
                [{"ma":"ROI_NANG","ten":"Tên đã sửa","dangHoatDong":false,"choPhepPhuPhiGoRoi":false},
                 {"ma":"CUSTOM","ten":"Mã riêng","dangHoatDong":true,"extra":true}]
                """
            });
            await context.SaveChangesAsync();
        }

        using var first = Factory();
        using var second = Factory();
        await Task.WhenAll(Task.Run(() => { using var client = first.CreateClient(); }),
            Task.Run(() => { using var client = second.CreateClient(); }));

        var row = await context.CauHinhHeThongSet.AsNoTracking().SingleAsync();
        Assert.Equal("FUR_CONDITIONS", row.Khoa);
        Assert.Equal(1, await context.NhanVienSet.CountAsync());
        Assert.Equal(existingRow ? 2 : 1, row.PhienBan);
        using var json = JsonDocument.Parse(row.GiaTri);
        var values = json.RootElement.EnumerateArray().ToDictionary(item => item.GetProperty("ma").GetString()!);
        Assert.Equal(existingRow ? 7 : 6, values.Count);
        Assert.True(values.ContainsKey("LONG_DAI"));
        if (existingRow)
        {
            Assert.Equal("Tên đã sửa", values["ROI_NANG"].GetProperty("ten").GetString());
            Assert.False(values["ROI_NANG"].GetProperty("dangHoatDong").GetBoolean());
            Assert.False(values["ROI_NANG"].GetProperty("choPhepPhuPhiGoRoi").GetBoolean());
            Assert.True(values["CUSTOM"].GetProperty("extra").GetBoolean());
        }

        WebApplicationFactory<Program> Factory() => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Staging");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = database.ConnectionString,
                ["SeedData:Enabled"] = "false",
                ["CatalogSeed:Enabled"] = "true",
                ["CatalogSeed:AdminId"] = admin.Id.ToString()
            }));
        });
    }
}
