using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PetSpa.Api.Data;
using PetSpa.Api.Services.Identity;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Enums;
using PetSpa.Tests.Infrastructure;

namespace PetSpa.Tests.Foundation;

internal static class FoundationTestSupport
{
    public static async Task<FoundationFixture> CreateAsync(DateTimeOffset now)
    {
        var database = await PostgresTestDatabase.CreateAsync();
        var clock = new Clock(now);
        var password = SeedApplicationFactory.NewPassword();
        var host = new PostgresWebApplicationFactory(database.ConnectionString).WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SeedData:Enabled"] = "true", ["SeedData:Date"] = "2035-01-01",
                ["SeedData:AdminPassword"] = password, ["SeedData:Staff1Password"] = password,
                ["SeedData:Staff2Password"] = password
            }));
            builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock)));
        });
        try
        {
            using var client = host.CreateClient();
            return new FoundationFixture(database, host, clock);
        }
        catch
        {
            await host.DisposeAsync();
            await database.DisposeAsync();
            throw;
        }
    }

    public sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}

internal sealed class FoundationFixture(PostgresTestDatabase database, WebApplicationFactory<Program> host,
    FoundationTestSupport.Clock clock) : IAsyncDisposable
{
    public PostgresTestDatabase Database { get; } = database;
    public WebApplicationFactory<Program> Host { get; } = host;
    public FoundationTestSupport.Clock Clock { get; } = clock;

    public async Task<HttpClient> AdminClientAsync()
    {
        await using var db = Database.CreateContext();
        var admin = await db.NhanVienSet.SingleAsync(x => x.TenDangNhap == "admin");
        using var scope = Host.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateToken(admin).Token;
        var client = Host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task<DonDatLich> AddBookingAsync(PetSpaDbContext db, bool spa = true, int? roomId = null, bool deposit = true)
    {
        var slot = await db.KhungGioDatLichSet.OrderBy(x => x.Id).FirstAsync();
        var customer = new KhachHang { HoTen = "Khách test", SoDienThoai = "0" + Random.Shared.Next(100000000, 999999999), Email = "guest@example.test" };
        var pet = new ChiTietDon
        {
            TenPetSnapshot = "Milo", CanNangKhaiBao = 4m,
            ThuCung = new ThuCung { TenThuCung = "Milo", CanNang = 4m, KhachHang = customer },
            LoaiPhongId = roomId, GiaPhongCamKet = roomId.HasValue ? 250000m : null,
            SoDemChot = roomId.HasValue ? 3 : null
        };
        var booking = new DonDatLich
        {
            RequestId = Guid.NewGuid(), Fingerprint = "test", TrackingCode = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            KhachHang = customer, TenNguoiDat = customer.HoTen, SoDienThoaiSnapshot = customer.SoDienThoai,
            EmailSnapshot = customer.Email, KhungGioId = slot.Id, BatDauDuKien = new DateTimeOffset(2035, 1, 1, 1, 0, 0, TimeSpan.Zero),
            KetThucDuKien = new DateTimeOffset(2035, 1, roomId.HasValue ? 4 : 1, 5, 0, 0, TimeSpan.Zero),
            MocCuoiCa = new DateTimeOffset(2035, 1, 1, 5, 0, 0, TimeSpan.Zero), TongTamTinh = 150000m,
            ChiTietDonDonDatLichItems = [pet]
        };
        if (spa)
        {
            var service = await db.DichVuSet.SingleAsync(x => x.TenDichVu == "Tắm sấy vệ sinh");
            pet.DichVuTrongDonChiTietDonItems.Add(new DichVuTrongDon
            {
                RequestId = Guid.NewGuid(), DichVuId = service.Id, KhungGioId = slot.Id,
                TenSnapshot = service.TenDichVu, ThoiLuongSnapshot = 60,
                BangGiaSizeSnapshot = "{\"s\":150000,\"m\":200000,\"l\":250000,\"xl\":300000}"
            });
            slot.SlotDaDat++;
            slot.PhutDaGiu += 60;
        }
        if (deposit) booking.TienCocDonDatLich = new TienCoc
        {
            CoSoTinhSnapshot = 150000m, SoTienYeuCau = 45000m, TyLe = .30m,
            HetHanAt = Clock.GetUtcNow(), TrangThaiYeuCau = DepositRequestState.CHO_THU
        };
        db.DonDatLichSet.Add(booking);
        await db.SaveChangesAsync();
        return booking;
    }

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        await Database.DisposeAsync();
    }
}
