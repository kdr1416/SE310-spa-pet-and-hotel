using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PetSpa.Api.Data;
using PetSpa.Api.Services.Identity;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Booking;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Scheduling;
using PetSpa.Core.Enums;
using PetSpa.Tests.Infrastructure;
using Xunit;

namespace PetSpa.Tests.Booking;

public sealed class CustomerPetEndpointTests
{
    private const string LookupUrl = "/api/internal/customers/lookup";
    private const string PetsUrl = "/api/internal/pets";

    [Fact]
    public async Task Lookup_ValidPhone_Returns200WithCustomerAndPets()
    {
        using var fixture = TestFixture.Create();
        var response = await fixture.Staff.GetAsync($"{LookupUrl}?soDienThoai=0901234567");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<IReadOnlyList<CustomerDto>>>();
        body.Should().NotBeNull();
        body!.Success.Should().BeTrue();
        body.Data.Should().NotBeNull();
        body.Data!.Count.Should().Be(1);

        var customer = body.Data[0];
        customer.MaKhachHang.Should().Be(1);
        customer.HoTen.Should().Be("Nguyễn Văn A");
        customer.SoDienThoai.Should().Be("0901234567");
        customer.Email.Should().Be("vana@example.com");

        customer.ThuCung.Should().HaveCount(2);
        customer.ThuCung[0].MaThuCung.Should().Be(1);
        customer.ThuCung[0].TenThuCung.Should().Be("Ki");
        customer.ThuCung[0].LoaiPet.Should().Be("CHO");
        customer.ThuCung[0].CanNang.Should().Be(5.2m);
        customer.ThuCung[0].Giong.Should().Be("Poodle");

        customer.ThuCung[1].MaThuCung.Should().Be(2);
        customer.ThuCung[1].TenThuCung.Should().Be("Mimi");
        customer.ThuCung[1].LoaiPet.Should().Be("MEO");
    }

    [Fact]
    public async Task Lookup_NonExistentPhone_Returns200WithEmptyArray()
    {
        using var fixture = TestFixture.Create();
        var response = await fixture.Staff.GetAsync($"{LookupUrl}?soDienThoai=0987654321");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<IReadOnlyList<CustomerDto>>>();
        body.Should().NotBeNull();
        body!.Success.Should().BeTrue();
        body.Data.Should().NotBeNull();
        body.Data!.Should().BeEmpty();
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("09012345678")]
    [InlineData("090123456a")]
    [InlineData("")]
    public async Task Lookup_InvalidPhone_Returns400ValidationFailed(string phone)
    {
        using var fixture = TestFixture.Create();
        var response = await fixture.Staff.GetAsync($"{LookupUrl}?soDienThoai={phone}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<IReadOnlyList<CustomerDto>>>();
        body.Should().NotBeNull();
        body!.Success.Should().BeFalse();
        body.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task Lookup_WithoutPermission_Returns403Forbidden()
    {
        using var fixture = TestFixture.Create();
        var response = await fixture.Unprivileged.GetAsync($"{LookupUrl}?soDienThoai=0901234567");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<IReadOnlyList<CustomerDto>>>();
        body.Should().NotBeNull();
        body!.Success.Should().BeFalse();
        body.ErrorCode.Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task Lookup_Unauthenticated_Returns401Unauthorized()
    {
        using var fixture = TestFixture.Create();
        var response = await fixture.Public.GetAsync($"{LookupUrl}?soDienThoai=0901234567");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<IReadOnlyList<CustomerDto>>>();
        body.Should().NotBeNull();
        body!.Success.Should().BeFalse();
        body.ErrorCode.Should().Be("UNAUTHENTICATED");
    }

    [Fact]
    public async Task UpdatePet_ValidPayload_UpdatesMasterRecordAndAudits_PreservesBookingSnapshot()
    {
        using var fixture = TestFixture.Create();
        var requestId = Guid.NewGuid();
        var payload = new
        {
            requestId,
            tenThuCung = "Ki Bự",
            canNang = 6.5m,
            giong = "Poodle Lai"
        };

        var response = await fixture.Staff.PatchAsJsonAsync($"{PetsUrl}/1", payload);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<PetDto>>();
        body.Should().NotBeNull();
        body!.Success.Should().BeTrue();
        body.Data.Should().NotBeNull();
        body.Data!.MaThuCung.Should().Be(1);
        body.Data.TenThuCung.Should().Be("Ki Bự");
        body.Data.CanNang.Should().Be(6.5m);
        body.Data.Giong.Should().Be("Poodle Lai");

        // Verify database: Master ThuCung record is updated
        using var scope = fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
        var updatedPet = await db.ThuCungSet.SingleAsync(p => p.Id == 1);
        updatedPet.TenThuCung.Should().Be("Ki Bự");
        updatedPet.CanNang.Should().Be(6.5m);
        updatedPet.Giong.Should().Be("Poodle Lai");

        // Verify rule D10 / Q05: Historical ChiTietDon snapshot remains unchanged
        var bookingDetail = await db.ChiTietDonSet.SingleAsync(d => d.Id == 1);
        bookingDetail.TenPetSnapshot.Should().Be("Ki");
        bookingDetail.CanNangKhaiBao.Should().Be(5.2m);

        // Verify audit log
        var audit = await db.AuditLogSet.SingleAsync(a => a.HanhDong == "PET_EDIT" && a.RequestId == requestId);
        audit.ActorId.Should().Be(1);
        audit.DoiTuongId.Should().Be(1);
        audit.LoaiDoiTuong.Should().Be("ThuCung");
        audit.Truoc.Should().Contain("Ki");
        using var sauDoc = System.Text.Json.JsonDocument.Parse(audit.Sau!);
        sauDoc.RootElement.GetProperty("tenThuCung").GetString().Should().Be("Ki Bự");
        sauDoc.RootElement.GetProperty("giong").GetString().Should().Be("Poodle Lai");
    }

    [Fact]
    public async Task UpdatePet_IdempotentReplay_ReturnsSameResultWithoutDuplicatingAudit()
    {
        using var fixture = TestFixture.Create();
        var requestId = Guid.NewGuid();
        var payload = new
        {
            requestId,
            tenThuCung = "Ki Bự",
            canNang = 6.5m
        };

        var firstResponse = await fixture.Staff.PatchAsJsonAsync($"{PetsUrl}/1", payload);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Replay same request
        var replayResponse = await fixture.Staff.PatchAsJsonAsync($"{PetsUrl}/1", payload);
        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await replayResponse.Content.ReadFromJsonAsync<ApiResponse<PetDto>>();
        body.Should().NotBeNull();
        body!.Success.Should().BeTrue();
        body.Data!.TenThuCung.Should().Be("Ki Bự");

        using var scope = fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
        var auditCount = await db.AuditLogSet.CountAsync(a => a.HanhDong == "PET_EDIT" && a.RequestId == requestId);
        auditCount.Should().Be(1);
    }

    [Fact]
    public async Task UpdatePet_ConflictingReplay_Returns409IdempotencyConflict()
    {
        using var fixture = TestFixture.Create();
        var requestId = Guid.NewGuid();
        var initialPayload = new
        {
            requestId,
            tenThuCung = "Ki Bự"
        };
        var conflictPayload = new
        {
            requestId,
            tenThuCung = "Ki Nhỏ"
        };

        var firstResponse = await fixture.Staff.PatchAsJsonAsync($"{PetsUrl}/1", initialPayload);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var conflictResponse = await fixture.Staff.PatchAsJsonAsync($"{PetsUrl}/1", conflictPayload);
        conflictResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await conflictResponse.Content.ReadFromJsonAsync<ApiResponse<PetDto>>();
        body.Should().NotBeNull();
        body!.Success.Should().BeFalse();
        body.ErrorCode.Should().Be("IDEMPOTENCY_CONFLICT");
    }

    [Fact]
    public async Task UpdatePet_NonExistentPet_Returns404NotFound()
    {
        using var fixture = TestFixture.Create();
        var payload = new
        {
            requestId = Guid.NewGuid(),
            tenThuCung = "Unknown Pet"
        };

        var response = await fixture.Staff.PatchAsJsonAsync($"{PetsUrl}/99999", payload);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<PetDto>>();
        body.Should().NotBeNull();
        body!.Success.Should().BeFalse();
        body.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task UpdatePet_InvalidPetId_Returns400ValidationFailed(int petId)
    {
        using var fixture = TestFixture.Create();
        var payload = new
        {
            requestId = Guid.NewGuid(),
            tenThuCung = "Valid Name"
        };

        var response = await fixture.Staff.PatchAsJsonAsync($"{PetsUrl}/{petId}", payload);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<PetDto>>();
        body.Should().NotBeNull();
        body!.Success.Should().BeFalse();
        body.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task UpdatePet_EmptyFields_Returns400ValidationFailed()
    {
        using var fixture = TestFixture.Create();
        var payload = new
        {
            requestId = Guid.NewGuid()
        };

        var response = await fixture.Staff.PatchAsJsonAsync($"{PetsUrl}/1", payload);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<PetDto>>();
        body.Should().NotBeNull();
        body!.Success.Should().BeFalse();
        body.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task UpdatePet_WithoutPermission_Returns403Forbidden()
    {
        using var fixture = TestFixture.Create();
        var payload = new
        {
            requestId = Guid.NewGuid(),
            tenThuCung = "Ki Bự"
        };

        var response = await fixture.Unprivileged.PatchAsJsonAsync($"{PetsUrl}/1", payload);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<PetDto>>();
        body.Should().NotBeNull();
        body!.Success.Should().BeFalse();
        body.ErrorCode.Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task UpdatePet_Unauthenticated_Returns401Unauthorized()
    {
        using var fixture = TestFixture.Create();
        var payload = new
        {
            requestId = Guid.NewGuid(),
            tenThuCung = "Ki Bự"
        };

        var response = await fixture.Public.PatchAsJsonAsync($"{PetsUrl}/1", payload);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<PetDto>>();
        body.Should().NotBeNull();
        body!.Success.Should().BeFalse();
        body.ErrorCode.Should().Be("UNAUTHENTICATED");
    }

    private sealed class TestFixture : IDisposable
    {
        public WebApplicationFactory<Program> Host { get; }
        public HttpClient Staff { get; }
        public HttpClient Unprivileged { get; }
        public HttpClient Public { get; }

        private TestFixture(WebApplicationFactory<Program> host)
        {
            Host = host;
            Staff = CreateClientWithUser(1);
            Unprivileged = CreateClientWithUser(2);
            Public = host.CreateClient();
        }

        private HttpClient CreateClientWithUser(int userId)
        {
            var client = Host.CreateClient();
            using var scope = Host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
            var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
            var user = db.NhanVienSet.Single(u => u.Id == userId);
            var token = tokenService.GenerateToken(user).Token;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return client;
        }

        public static TestFixture Create()
        {
            var dbName = "PetSpa_CustomerPetTest_" + Guid.NewGuid().ToString("N");
            var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Jwt:SecretKey"] = AuthApplicationFactory.TestJwtSecret,
                        ["Jwt:Issuer"] = "PetSpaApi",
                        ["Jwt:Audience"] = "PetSpaClient",
                        ["Jwt:ExpiryMinutes"] = "480",
                        ["SeedData:Enabled"] = "false"
                    }));

                builder.ConfigureServices(services =>
                {
                    var descriptors = services.Where(d =>
                        d.ServiceType.Namespace?.StartsWith("Microsoft.EntityFrameworkCore") == true ||
                        d.ServiceType == typeof(DbContextOptions<PetSpaDbContext>) ||
                        d.ServiceType == typeof(DbContextOptions) ||
                        d.ServiceType == typeof(PetSpaDbContext)).ToList();

                    foreach (var descriptor in descriptors)
                        services.Remove(descriptor);

                    services.AddDbContext<PetSpaDbContext>(options =>
                    {
                        options.UseInMemoryDatabase(dbName);
                    });

                    var sp = services.BuildServiceProvider();
                    using var scope = sp.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
                    db.Database.EnsureCreated();
                    Seed(db);
                });
            });

            return new TestFixture(host);
        }

        private static void Seed(PetSpaDbContext db)
        {
            var staffLookup = new NhanVien
            {
                Id = 1,
                TenDangNhap = "staff_lookup",
                TenNhanVien = "Staff Nam",
                VaiTro = Role.STAFF,
                IsActive = true
            };
            var staffUnprivileged = new NhanVien
            {
                Id = 2,
                TenDangNhap = "staff_unprivileged",
                TenNhanVien = "Staff Unprivileged",
                VaiTro = Role.STAFF,
                IsActive = true
            };

            var perm = new Quyen { MaQuyen = "CUSTOMER_LOOKUP", TenQuyen = "Tra cứu khách hàng" };
            var staffPerm = new NhanVienQuyen { NhanVienId = 1, MaQuyen = "CUSTOMER_LOOKUP" };

            var customer = new KhachHang
            {
                Id = 1,
                HoTen = "Nguyễn Văn A",
                SoDienThoai = "0901234567",
                Email = "vana@example.com"
            };

            var pet1 = new ThuCung
            {
                Id = 1,
                KhachHangId = 1,
                TenThuCung = "Ki",
                LoaiPet = PetType.CHO,
                CanNang = 5.2m,
                Giong = "Poodle",
                LoaiLong = "Xoăn",
                GhiChuDiUng = "Dị ứng phấn hoa",
                NgaySinh = new DateOnly(2022, 1, 1)
            };

            var pet2 = new ThuCung
            {
                Id = 2,
                KhachHangId = 1,
                TenThuCung = "Mimi",
                LoaiPet = PetType.MEO,
                CanNang = 3.5m,
                Giong = "Anh lông ngắn",
                LoaiLong = "Ngắn"
            };

            var shift = new CaLamViec
            {
                Id = 1,
                TenCa = "Ca sáng",
                GioBatDau = new TimeOnly(8, 0),
                GioKetThuc = new TimeOnly(12, 0),
                SucChuaPet = 10,
                TranPhut = 180,
                IsActive = true
            };

            var slot = new KhungGioDatLich
            {
                Id = 1,
                Ngay = new DateOnly(2035, 1, 1),
                CaLamViecId = 1,
                MoNhanKhach = true,
                TongSlot = 10,
                TranPhut = 180
            };

            var booking = new DonDatLich
            {
                Id = 1,
                RequestId = Guid.NewGuid(),
                TrackingCode = "TRK-0001",
                KhachHangId = 1,
                TenNguoiDat = "Nguyễn Văn A",
                SoDienThoaiSnapshot = "0901234567",
                EmailSnapshot = "vana@example.com",
                KhungGioId = 1,
                TrangThai = BookingState.CONFIRMED,
                BatDauDuKien = DateTimeOffset.UtcNow,
                KetThucDuKien = DateTimeOffset.UtcNow.AddHours(2),
                MocCuoiCa = DateTimeOffset.UtcNow.AddHours(3),
                TongTamTinh = 150000m
            };

            var detail = new ChiTietDon
            {
                Id = 1,
                DonDatLichId = 1,
                ThuCungId = 1,
                TenPetSnapshot = "Ki",
                CanNangKhaiBao = 5.2m
            };

            db.AddRange(staffLookup, staffUnprivileged);
            db.Add(perm);
            db.Add(staffPerm);
            db.Add(customer);
            db.AddRange(pet1, pet2);
            db.Add(shift);
            db.Add(slot);
            db.Add(booking);
            db.Add(detail);
            db.SaveChanges();
        }

        public void Dispose()
        {
            Staff.Dispose();
            Unprivileged.Dispose();
            Public.Dispose();
            Host.Dispose();
        }
    }
}
