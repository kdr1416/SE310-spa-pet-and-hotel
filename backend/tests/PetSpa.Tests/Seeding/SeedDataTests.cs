using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PetSpa.Api.Data;
using PetSpa.Api.Data.Seeding;
using Microsoft.Extensions.Hosting;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Enums;
using PetSpa.Tests.Infrastructure;

namespace PetSpa.Tests.Seeding;

public class SeedDataTests
{
    [Fact]
    public async Task ConcurrentSeeds_CommitOneSetOfData_WithoutUniqueKeyConflicts()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        using var factory = SeedApplicationFactory.Create(database.ConnectionString,
            SeedApplicationFactory.NewPassword(), enabled: false);
        var environment = factory.Services.GetRequiredService<IHostEnvironment>();
        var password = SeedApplicationFactory.NewPassword();
        var options = new SeedDataOptions
        {
            Date = new DateOnly(2026, 11, 4), AdminPassword = password,
            Staff1Password = password, Staff2Password = password
        };
        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(async () =>
        {
            await using var context = database.CreateContext();
            await DbInitializer.InitializeAsync(context, options, environment);
        })));

        await using var verify = database.CreateContext();
        Assert.Equal(3, await verify.NhanVienSet.CountAsync());
        Assert.Equal(4, await verify.HeSoCanNangSet.CountAsync());
        Assert.Equal(3, await verify.DichVuSet.CountAsync());
        Assert.Equal(12, await verify.BangGiaSizeSet.CountAsync());
        Assert.Equal(24, await verify.QuyenSet.CountAsync());
        Assert.Equal(39, await verify.NhanVienQuyenSet.CountAsync());
        Assert.Equal(2, await verify.KhungGioDatLichSet.CountAsync());
        Assert.Equal(2, await verify.DangKyCaSet.CountAsync());
        Assert.Equal(1, await verify.CauHinhHeThongSet.CountAsync());
        Assert.Equal(1, await verify.DotDangKyCaSet.CountAsync());
    }

    [Fact]
    public async Task NewSeedDate_AddsNewSchedule_AndPreservesPreviousReservations()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var password = SeedApplicationFactory.NewPassword();
        using (var first = SeedApplicationFactory.Create(database.ConnectionString, password))
            _ = first.Services;
        await using (var context = database.CreateContext())
        {
            var slot = await context.KhungGioDatLichSet.OrderBy(s => s.Id).FirstAsync();
            slot.SlotDaDat = 1;
            slot.PhutDaGiu = 90;
            await context.SaveChangesAsync();
        }
        using (var next = SeedApplicationFactory.Create(database.ConnectionString, password,
                   environment: "Development", date: "2026-11-05"))
            _ = next.Services;
        await using var verify = database.CreateContext();
        Assert.Equal(3, await verify.NhanVienSet.CountAsync());
        Assert.Equal(2, await verify.CaLamViecSet.CountAsync());
        Assert.Equal(4, await verify.KhungGioDatLichSet.CountAsync());
        Assert.Equal(4, await verify.DangKyCaSet.CountAsync());
        Assert.Equal(2, await verify.DotDangKyCaSet.CountAsync());
        var oldSlot = await verify.KhungGioDatLichSet.OrderBy(s => s.Id).FirstAsync();
        Assert.Equal(new DateOnly(2026, 11, 4), oldSlot.Ngay);
        Assert.Equal(1, oldSlot.SlotDaDat);
        Assert.Equal(90, oldSlot.PhutDaGiu);
        var newSlots = await verify.KhungGioDatLichSet.Where(s => s.Ngay == new DateOnly(2026, 11, 5)).ToArrayAsync();
        Assert.Equal(2, newSlots.Length);
        Assert.All(newSlots, s => { Assert.Equal(0, s.SlotDaDat); Assert.Equal(0, s.PhutDaGiu); });
    }

    [Fact]
    public async Task MissingSeedDate_UsesCurrentVietnamDate_InDevelopment()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var earliest = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);
        using var factory = SeedApplicationFactory.Create(database.ConnectionString,
            SeedApplicationFactory.NewPassword(), environment: "Development", date: null);
        _ = factory.Services;
        var latest = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);
        await using var context = database.CreateContext();
        var slots = await context.KhungGioDatLichSet.ToArrayAsync();
        Assert.Equal(2, slots.Length);
        Assert.All(slots, s => Assert.InRange(s.Ngay, earliest, latest));
    }

    [Fact]
    public async Task RepeatedSeed_RepairsMissingRows_WithoutResettingExistingData()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        using (var first = SeedApplicationFactory.Create(database.ConnectionString, SeedApplicationFactory.NewPassword()))
            _ = first.Services;
        string hash;
        int adminId;
        await using (var context = database.CreateContext())
        {
            var admin = await context.NhanVienSet.SingleAsync(e => e.TenDangNhap == "admin");
            hash = admin.MatKhauHash;
            adminId = admin.Id;
            var price = await context.BangGiaSizeSet.OrderBy(p => p.Id).FirstAsync();
            price.DonGia = 123000;
            var slot = await context.KhungGioDatLichSet.OrderBy(s => s.Id).FirstAsync();
            slot.SlotDaDat = 2;
            slot.PhutDaGiu = 60;
            var setting = await context.CauHinhHeThongSet.SingleAsync();
            setting.GiaTri = "{\"otpEmail\":true,\"cocHotel\":false,\"giuCho\":false}";
            setting.PhienBan = 2;
            await context.SaveChangesAsync();
            context.Remove(await context.BangGiaSizeSet.OrderBy(p => p.Id).LastAsync());
            context.Remove(await context.DangKyCaSet.OrderBy(r => r.Id).LastAsync());
            await context.SaveChangesAsync();
        }
        using (var second = SeedApplicationFactory.Create(database.ConnectionString, SeedApplicationFactory.NewPassword()))
            _ = second.Services;
        using (var third = SeedApplicationFactory.Create(database.ConnectionString, SeedApplicationFactory.NewPassword()))
            _ = third.Services;
        await using var verify = database.CreateContext();
        Assert.Equal(4, await verify.HeSoCanNangSet.CountAsync());
        Assert.Equal(3, await verify.NhanVienSet.CountAsync());
        Assert.Equal(3, await verify.DichVuSet.CountAsync());
        Assert.Equal(12, await verify.BangGiaSizeSet.CountAsync());
        Assert.Equal(2, await verify.CaLamViecSet.CountAsync());
        Assert.Equal(2, await verify.KhungGioDatLichSet.CountAsync());
        Assert.Equal(2, await verify.DangKyCaSet.CountAsync());
        Assert.Equal(1, await verify.DotDangKyCaSet.CountAsync());
        Assert.Equal(24, await verify.QuyenSet.CountAsync());
        Assert.Equal(39, await verify.NhanVienQuyenSet.CountAsync());
        Assert.Equal(hash, (await verify.NhanVienSet.SingleAsync(e => e.Id == adminId)).MatKhauHash);
        Assert.Equal(123000, (await verify.BangGiaSizeSet.OrderBy(p => p.Id).FirstAsync()).DonGia);
        var preservedSlot = await verify.KhungGioDatLichSet.OrderBy(s => s.Id).FirstAsync();
        Assert.Equal(2, preservedSlot.SlotDaDat);
        Assert.Equal(60, preservedSlot.PhutDaGiu);
        var preservedSetting = await verify.CauHinhHeThongSet.SingleAsync();
        Assert.Equal(2, preservedSetting.PhienBan);
        using var json = JsonDocument.Parse(preservedSetting.GiaTri);
        Assert.True(json.RootElement.GetProperty("otpEmail").GetBoolean());
    }

    [Fact]
    public async Task MissingPassword_RejectsStartup_WithoutPartialSeed()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        using var factory = SeedApplicationFactory.Create(database.ConnectionString,
            SeedApplicationFactory.NewPassword(), missingStaffPassword: true);
        var error = Assert.Throws<InvalidOperationException>(() => factory.Services);
        Assert.Contains("SeedData:Staff2Password", error.Message);
        await using var context = database.CreateContext();
        Assert.Equal(0, await context.NhanVienSet.CountAsync());
        Assert.Equal(0, await context.HeSoCanNangSet.CountAsync());
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void EnabledSeed_RejectsNonDevelopmentEnvironments_BeforeDatabaseAccess(string environment)
    {
        using var factory = SeedApplicationFactory.Create("Host=127.0.0.1;Port=1;Database=unused",
            SeedApplicationFactory.NewPassword(), environment);
        var error = Assert.Throws<InvalidOperationException>(() => factory.Services);
        Assert.Contains("Development", error.Message);
    }

    [Fact]
    public async Task ConflictingExistingAccount_RollsBackEarlierInserts_AndPreservesTheAccount()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using (var context = database.CreateContext())
        {
            var existing = new NhanVien { TenDangNhap = "staff2", TenNhanVien = "Existing inactive account",
                VaiTro = Role.STAFF, IsActive = false };
            existing.MatKhauHash = new PasswordHasher<NhanVien>().HashPassword(existing, SeedApplicationFactory.NewPassword());
            context.NhanVienSet.Add(existing);
            await context.SaveChangesAsync();
        }
        using var factory = SeedApplicationFactory.Create(database.ConnectionString, SeedApplicationFactory.NewPassword());
        var error = Assert.Throws<InvalidOperationException>(() => factory.Services);
        Assert.Contains("conflicts", error.Message);
        await using var verify = database.CreateContext();
        var remaining = await verify.NhanVienSet.SingleAsync();
        Assert.Equal("staff2", remaining.TenDangNhap);
        Assert.False(remaining.IsActive);
        Assert.Equal("Existing inactive account", remaining.TenNhanVien);
        Assert.Equal(0, await verify.HeSoCanNangSet.CountAsync());
        Assert.Equal(0, await verify.NhanVienQuyenSet.CountAsync());
    }

    [Fact]
    public async Task DisabledSeed_DoesNotConnectToDatabase()
    {
        using var factory = SeedApplicationFactory.Create("Host=127.0.0.1;Port=1;Database=unused",
            string.Empty, enabled: false);
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var response = await client.GetAsync("/api/health");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Startup_SeedsCatalogAccountsPermissionsAndApprovedShifts()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var password = SeedApplicationFactory.NewPassword();
        using var factory = SeedApplicationFactory.Create(database.ConnectionString, password);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();

        var sizes = await context.HeSoCanNangSet.OrderBy(s => s.CanNangTu).ToArrayAsync();
        Assert.Equal(new[] { SizeBand.S, SizeBand.M, SizeBand.L, SizeBand.XL }, sizes.Select(s => s.MaSize));
        Assert.Equal(new decimal[] { 0, 5, 15, 25 }, sizes.Select(s => s.CanNangTu));
        Assert.Equal(new decimal?[] { 5, 15, 25, null }, sizes.Select(s => s.CanNangDen));
        Assert.Equal(3, await context.DichVuSet.CountAsync());
        Assert.Equal(12, await context.BangGiaSizeSet.CountAsync());
        var bath = await context.DichVuSet.SingleAsync(s => s.TenDichVu == "Tắm sấy vệ sinh");
        Assert.Equal(new decimal[] { 150000, 200000, 250000, 300000 },
            await context.BangGiaSizeSet.Where(p => p.DichVuId == bath.Id)
                .OrderBy(p => p.KhoangSize.CanNangTu).Select(p => p.DonGia).ToArrayAsync());

        var accounts = await context.NhanVienSet.OrderBy(e => e.TenDangNhap).ToArrayAsync();
        Assert.Equal(new[] { "admin", "staff1", "staff2" }, accounts.Select(e => e.TenDangNhap));
        Assert.Equal(Role.ADMIN, accounts[0].VaiTro);
        Assert.All(accounts.Skip(1), e => Assert.Equal(Role.STAFF, e.VaiTro));
        foreach (var account in accounts)
        {
            Assert.True(account.IsActive);
            Assert.NotEqual(password, account.MatKhauHash);
            Assert.NotEqual(PasswordVerificationResult.Failed,
                new PasswordHasher<NhanVien>().VerifyHashedPassword(account, account.MatKhauHash, password));
        }
        Assert.Equal(24, await context.QuyenSet.CountAsync());
        var reception = await context.NhanVienQuyenSet.Where(p => p.NhanVienId == accounts[1].Id)
            .Select(p => p.MaQuyen).ToArrayAsync();
        Assert.Contains("CHECKIN_PERFORM", reception);
        Assert.Contains("INVOICE_COLLECT", reception);
        Assert.DoesNotContain("CARE_UPDATE", reception);
        string[] expectedReception = ["CATALOG_VIEW", "BOOKING_CREATE", "TRACKING_VIEW", "CUSTOMER_LOOKUP",
            "BOOKING_APPROVE", "CHECKIN_PERFORM", "PET_HANDOVER", "INVOICE_CREATE", "INVOICE_COLLECT",
            "SCHEDULE_REGISTER", "DEPOSIT_CONFIRM"];
        Assert.Equal(expectedReception.Order(), reception.Order());
        var care = await context.NhanVienQuyenSet.Where(p => p.NhanVienId == accounts[2].Id)
            .Select(p => p.MaQuyen).ToArrayAsync();
        Assert.Contains("CARE_UPDATE", care);
        Assert.Contains("PHOTO_UPLOAD", care);
        Assert.DoesNotContain("PERMISSION_MANAGE", care);
        Assert.Equal(new[] { "CARE_UPDATE", "CATALOG_VIEW", "PHOTO_UPLOAD", "SCHEDULE_REGISTER" }, care.Order());
        var adminPermissions = await context.NhanVienQuyenSet.Where(p => p.NhanVienId == accounts[0].Id)
            .Select(p => p.MaQuyen).ToArrayAsync();
        var allPermissions = await context.QuyenSet.Select(p => p.MaQuyen).ToArrayAsync();
        Assert.Equal(allPermissions.Order(), adminPermissions.Order());

        var shifts = await context.CaLamViecSet.OrderBy(s => s.GioBatDau).ToArrayAsync();
        Assert.Equal(2, shifts.Length);
        Assert.Equal(new TimeOnly(8, 0), shifts[0].GioBatDau);
        Assert.Equal(new TimeOnly(13, 30), shifts[1].GioBatDau);
        Assert.All(shifts, s => { Assert.Equal(10, s.SucChuaPet); Assert.Equal(240, s.TranPhut); });
        var slots = await context.KhungGioDatLichSet.ToArrayAsync();
        Assert.Equal(2, slots.Length);
        Assert.All(slots, s =>
        {
            Assert.Equal(new DateOnly(2026, 11, 4), s.Ngay);
            Assert.True(s.MoNhanKhach);
            Assert.Equal(10, s.TongSlot);
            Assert.Equal(240, s.TranPhut);
        });
        var registrations = await context.DangKyCaSet.ToArrayAsync();
        Assert.Equal(2, registrations.Length);
        Assert.All(registrations, r =>
        {
            Assert.Equal(ShiftRegistrationState.APPROVED, r.TrangThai);
            Assert.Equal(accounts[2].Id, r.NhanVienId);
            Assert.Equal(accounts[0].Id, r.DuyetBoiId);
            Assert.NotNull(r.DuyetAt);
        });
        var settings = await context.CauHinhHeThongSet.SingleAsync(s => s.Khoa == "EXTENSION_SETTINGS");
        using var json = JsonDocument.Parse(settings.GiaTri);
        Assert.False(json.RootElement.GetProperty("otpEmail").GetBoolean());
        Assert.False(json.RootElement.GetProperty("cocHotel").GetBoolean());
        Assert.False(json.RootElement.GetProperty("giuCho").GetBoolean());
    }
}
