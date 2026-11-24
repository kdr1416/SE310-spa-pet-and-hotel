using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Npgsql;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PetSpa.Api.Data;
using PetSpa.Api.Services.Identity;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Scheduling;
using PetSpa.Core.Enums;
using PetSpa.Tests.Infrastructure;
using Xunit;

namespace PetSpa.Tests.Care;

public class HandoverEndpointTests
{
    private static string GenerateJwtFor(NhanVien nhanVien)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SecretKey"] = AuthApplicationFactory.TestJwtSecret,
                ["Jwt:Issuer"] = "PetSpaApi",
                ["Jwt:Audience"] = "PetSpaClient",
                ["Jwt:ExpiryMinutes"] = "480"
            })
            .Build();

        return new TokenService(config).GenerateToken(nhanVien).Token;
    }

    private static void SeedHandoverScenario(
        PetSpaDbContext db,
        PetCareState firstPetState = PetCareState.READY,
        PetCareState? secondPetState = null,
        BookingState bookingState = BookingState.IN_PROGRESS,
        InvoiceState? invoiceState = InvoiceState.PAID,
        bool grantHandoverPermission = true)
    {
        var today = new DateOnly(2026, 11, 21);
        var now = CareTestSupport.Now;

        var handoverPerm = new Quyen { MaQuyen = "PET_HANDOVER", TenQuyen = "PET_HANDOVER" };
        db.QuyenSet.Add(handoverPerm);

        var admin = new NhanVien
        {
            Id = 300,
            TenDangNhap = "admin",
            TenNhanVien = "Quản Trị Viên",
            VaiTro = Role.ADMIN,
            IsActive = true
        };
        admin.MatKhauHash = new PasswordHasher<NhanVien>().HashPassword(admin, "Password123@");

        var receptionist = new NhanVien
        {
            Id = 301,
            TenDangNhap = "staff1",
            TenNhanVien = "Lễ Tân",
            VaiTro = Role.STAFF,
            IsActive = true
        };
        receptionist.MatKhauHash = new PasswordHasher<NhanVien>().HashPassword(receptionist, "Password123@");

        var careStaff = new NhanVien
        {
            Id = 302,
            TenDangNhap = "staff2",
            TenNhanVien = "KTV Spa",
            VaiTro = Role.STAFF,
            IsActive = true
        };
        careStaff.MatKhauHash = new PasswordHasher<NhanVien>().HashPassword(careStaff, "Password123@");

        db.NhanVienSet.AddRange(admin, receptionist, careStaff);

        db.NhanVienQuyenSet.Add(new NhanVienQuyen
        {
            NhanVienId = admin.Id,
            MaQuyen = "PET_HANDOVER",
            CapBoiId = admin.Id,
            CapAt = now
        });

        if (grantHandoverPermission)
        {
            db.NhanVienQuyenSet.Add(new NhanVienQuyen
            {
                NhanVienId = receptionist.Id,
                MaQuyen = "PET_HANDOVER",
                CapBoiId = admin.Id,
                CapAt = now
            });
        }

        var shift = new CaLamViec
        {
            Id = 1,
            TenCa = "Ca Sáng",
            GioBatDau = new TimeOnly(8, 0),
            GioKetThuc = new TimeOnly(12, 0),
            SucChuaPet = 10,
            TranPhut = 240,
            IsActive = true
        };
        db.CaLamViecSet.Add(shift);

        var slot = new KhungGioDatLich
        {
            Id = 1,
            Ngay = today,
            CaLamViecId = shift.Id,
            MoNhanKhach = true,
            TongSlot = 10,
            TranPhut = 240
        };
        db.KhungGioDatLichSet.Add(slot);

        var dichVu = new DichVu
        {
            Id = 1,
            TenDichVu = "Tắm sấy vệ sinh",
            LoaiDichVu = "SPA",
            ThoiLuongPhut = 60,
            IsActive = true,
            YeuCauTiemChung = false
        };
        db.DichVuSet.Add(dichVu);

        var customer = new KhachHang
        {
            Id = 1,
            HoTen = "Nguyễn Văn A",
            SoDienThoai = "0912345678",
            Email = "khach@example.com"
        };
        db.KhachHangSet.Add(customer);

        var pet1 = new ThuCung
        {
            Id = 1,
            KhachHangId = customer.Id,
            TenThuCung = "Mochi",
            LoaiPet = PetType.CHO,
            CanNang = 7.50m
        };
        db.ThuCungSet.Add(pet1);

        var booking = new DonDatLich
        {
            Id = 1,
            RequestId = Guid.NewGuid(),
            Fingerprint = "seed",
            TrackingCode = "TRK-8X2A",
            TrangThai = bookingState,
            TenNguoiDat = customer.HoTen,
            SoDienThoaiSnapshot = customer.SoDienThoai,
            EmailSnapshot = customer.Email,
            BatDauDuKien = now.AddHours(-2),
            KetThucDuKien = now.AddHours(-1),
            MocCuoiCa = now.AddHours(2),
            TongTamTinh = 200000m,
            ChinhSachSnapshot = "{}",
            KhachHangId = customer.Id,
            KhungGioId = slot.Id
        };
        db.DonDatLichSet.Add(booking);

        var chiTiet1 = new ChiTietDon
        {
            Id = 10,
            DonDatLichId = booking.Id,
            ThuCungId = pet1.Id,
            TenPetSnapshot = pet1.TenThuCung,
            CanNangKhaiBao = 7.00m,
            CanNangChot = 7.50m,
            SizeThucTe = SizeBand.M,
            SizeChot = SizeBand.M,
            NhanVienId = careStaff.Id,
            CheckInAt = now.AddHours(-2),
            CheckInBoiId = receptionist.Id,
            ReadyAt = firstPetState == PetCareState.READY ? now.AddMinutes(-20) : null,
            TrangThaiChamSoc = firstPetState
        };
        db.ChiTietDonSet.Add(chiTiet1);

        db.DichVuTrongDonSet.Add(new DichVuTrongDon
        {
            Id = 100,
            RequestId = Guid.NewGuid(),
            ChiTietDonId = chiTiet1.Id,
            DichVuId = dichVu.Id,
            KhungGioId = slot.Id,
            TenSnapshot = dichVu.TenDichVu,
            BangGiaSizeSnapshot = "{\"S\":150000,\"M\":200000,\"L\":250000,\"XL\":300000}",
            ThoiLuongSnapshot = 60,
            TrangThai = OrderServiceState.HOAN_TAT,
            SizeChot = SizeBand.M,
            GiaChot = 200000m,
            HoanTatAt = now.AddMinutes(-20)
        });

        if (secondPetState.HasValue)
        {
            var pet2 = new ThuCung
            {
                Id = 2,
                KhachHangId = customer.Id,
                TenThuCung = "Bông",
                LoaiPet = PetType.MEO,
                CanNang = 4.00m
            };
            db.ThuCungSet.Add(pet2);

            var chiTiet2 = new ChiTietDon
            {
                Id = 11,
                DonDatLichId = booking.Id,
                ThuCungId = pet2.Id,
                TenPetSnapshot = pet2.TenThuCung,
                CanNangKhaiBao = 4.00m,
                CanNangChot = 4.00m,
                SizeThucTe = SizeBand.S,
                SizeChot = SizeBand.S,
                NhanVienId = careStaff.Id,
                CheckInAt = now.AddHours(-2),
                CheckInBoiId = receptionist.Id,
                ReadyAt = secondPetState.Value == PetCareState.READY ? now.AddMinutes(-15) : null,
                TraLaiAt = secondPetState.Value == PetCareState.CANCELLED ? now.AddMinutes(-10) : null,
                TrangThaiChamSoc = secondPetState.Value
            };
            db.ChiTietDonSet.Add(chiTiet2);

            db.DichVuTrongDonSet.Add(new DichVuTrongDon
            {
                Id = 101,
                RequestId = Guid.NewGuid(),
                ChiTietDonId = chiTiet2.Id,
                DichVuId = dichVu.Id,
                KhungGioId = slot.Id,
                TenSnapshot = dichVu.TenDichVu,
                BangGiaSizeSnapshot = "{\"S\":150000,\"M\":200000,\"L\":250000,\"XL\":300000}",
                ThoiLuongSnapshot = 60,
                TrangThai = secondPetState.Value == PetCareState.CANCELLED ? OrderServiceState.HUY : OrderServiceState.HOAN_TAT,
                SizeChot = SizeBand.S,
                GiaChot = 150000m
            });
        }

        if (invoiceState.HasValue)
        {
            db.HoaDonSet.Add(new HoaDon
            {
                Id = 1,
                MaHoaDon = "HD-20261121-0001",
                DonDatLichId = booking.Id,
                TrangThai = invoiceState.Value,
                TongTien = 200000m,
                CocKhauTru = 0m,
                ConPhaiTra = 200000m,
                KhachSnapshot = "{\"hoTen\":\"Nguyễn Văn A\",\"soDienThoai\":\"0912345678\",\"email\":\"khach@example.com\"}",
                PaidAt = invoiceState.Value == InvoiceState.PAID ? now.AddMinutes(-5) : null,
                TaoBoiId = receptionist.Id,
                ThuBoiId = invoiceState.Value == InvoiceState.PAID ? receptionist.Id : null
            });
        }

        db.SaveChanges();
    }

    [Fact]
    public async Task Handover_ConcurrentFinalTwoPetsCompleteBooking()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        SeedHandoverScenario(db, secondPetState: PetCareState.READY);
        await using var postgres = new PostgresWebApplicationFactory(database.ConnectionString);
        await using var factory = CareTestSupport.Configure(postgres);
        using var client = factory.CreateClient();
        var token = GenerateJwtFor(new NhanVien { Id = 301, TenDangNhap = "staff1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await using var blocker = new NpgsqlConnection(database.ConnectionString);
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await using var hold = new NpgsqlCommand("SELECT id FROM don_dat_lich WHERE id = 1 FOR UPDATE", blocker, transaction);
        await hold.ExecuteScalarAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        Task<HttpResponseMessage> HandoverAsync(int petId) =>
            client.PostAsJsonAsync($"/api/internal/booking-pets/{petId}/handover",
                new Handover { RequestId = Guid.NewGuid(), XacNhanDaNhan = true }, timeout.Token);
        var first = HandoverAsync(10);
        var second = HandoverAsync(11);
        try
        {
            await CareTestSupport.WaitForLockWaitersAsync(
                database.ConnectionString, "%don_dat_lich%FOR UPDATE%", 2, timeout.Token);
        }
        finally
        {
            await transaction.CommitAsync();
        }

        using var result1 = await first;
        using var result2 = await second;
        result1.StatusCode.Should().Be(HttpStatusCode.OK);
        result2.StatusCode.Should().Be(HttpStatusCode.OK);
        (await db.DonDatLichSet.AsNoTracking().SingleAsync()).TrangThai.Should().Be(BookingState.COMPLETED);
        (await db.ChiTietDonSet.AsNoTracking().ToListAsync()).Should().OnlyContain(p => p.TrangThaiChamSoc == PetCareState.DONE);
        (await db.AuditLogSet.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Handover_AllPetsCancelledDoesNotCompleteBooking()
    {
        using var factory = CareTestSupport.Create(db => SeedHandoverScenario(db,
            bookingState: BookingState.CANCELLED, firstPetState: PetCareState.CANCELLED, secondPetState: PetCareState.CANCELLED));
        using var client = factory.CreateClient();
        var token = GenerateJwtFor(new NhanVien { Id = 301, TenDangNhap = "staff1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.PostAsJsonAsync("/api/internal/booking-pets/10/handover",
            new Handover { RequestId = Guid.NewGuid(), XacNhanDaNhan = true });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
        var booking = await db.DonDatLichSet.SingleAsync();
        booking.TrangThai.Should().Be(BookingState.CANCELLED);
        booking.HoanTatAt.Should().BeNull();
    }

    [Fact]
    public async Task Handover_SinglePetWhenInvoicePaid_TransitionsPetToDoneAndBookingToCompleted()
    {
        using var factory = CareTestSupport.Create(db => SeedHandoverScenario(db));
        using var client = factory.CreateClient();

        var token = GenerateJwtFor(new NhanVien { Id = 301, TenDangNhap = "staff1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync(
            "/api/internal/booking-pets/10/handover",
            new Handover { RequestId = Guid.NewGuid(), XacNhanDaNhan = true });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body!.Success.Should().BeTrue();
        body.Data!.TrangThai.Should().Be("DONE");
        body.Data.NhanSom.Should().BeFalse();
        body.Data.BanGiaoAt.Should().NotBeNull();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
        var savedPet = await db.ChiTietDonSet
            .Include(c => c.DonDatLich)
            .FirstAsync(c => c.Id == 10);

        savedPet.TrangThaiChamSoc.Should().Be(PetCareState.DONE);
        savedPet.BanGiaoAt.Should().NotBeNull();
        savedPet.BanGiaoBoiId.Should().Be(301);
        savedPet.NhanSom.Should().BeFalse();
        savedPet.DonDatLich.TrangThai.Should().Be(BookingState.COMPLETED);
        savedPet.DonDatLich.HoanTatAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handover_MultiPet_FirstPetKeepsBookingInProgress_LastPetCompletesBooking()
    {
        using var factory = CareTestSupport.Create(db =>
            SeedHandoverScenario(db, firstPetState: PetCareState.READY, secondPetState: PetCareState.READY));
        using var client = factory.CreateClient();

        var token = GenerateJwtFor(new NhanVien { Id = 301, TenDangNhap = "staff1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var firstResponse = await client.PostAsJsonAsync(
            "/api/internal/booking-pets/10/handover",
            new Handover { RequestId = Guid.NewGuid(), XacNhanDaNhan = true });

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await firstResponse.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        firstBody!.Data!.TrangThai.Should().Be("DONE");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
            var bookingAfterFirst = await db.DonDatLichSet.FirstAsync(d => d.Id == 1);
            bookingAfterFirst.TrangThai.Should().Be(BookingState.IN_PROGRESS);
            bookingAfterFirst.HoanTatAt.Should().BeNull();
        }

        var secondResponse = await client.PostAsJsonAsync(
            "/api/internal/booking-pets/11/handover",
            new Handover { RequestId = Guid.NewGuid(), XacNhanDaNhan = true });

        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondBody = await secondResponse.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        secondBody!.Data!.TrangThai.Should().Be("DONE");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
            var bookingAfterSecond = await db.DonDatLichSet.FirstAsync(d => d.Id == 1);
            bookingAfterSecond.TrangThai.Should().Be(BookingState.COMPLETED);
            bookingAfterSecond.HoanTatAt.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task Handover_MultiPetWithCancelledPet_CompletesBookingWhenNonCancelledPetsDone()
    {
        using var factory = CareTestSupport.Create(db =>
            SeedHandoverScenario(db, firstPetState: PetCareState.READY, secondPetState: PetCareState.CANCELLED));
        using var client = factory.CreateClient();

        var token = GenerateJwtFor(new NhanVien { Id = 301, TenDangNhap = "staff1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync(
            "/api/internal/booking-pets/10/handover",
            new Handover { RequestId = Guid.NewGuid(), XacNhanDaNhan = true });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
        var booking = await db.DonDatLichSet
            .Include(d => d.ChiTietDonDonDatLichItems)
            .FirstAsync(d => d.Id == 1);

        booking.TrangThai.Should().Be(BookingState.COMPLETED);
        booking.ChiTietDonDonDatLichItems.Should().Contain(p => p.Id == 10 && p.TrangThaiChamSoc == PetCareState.DONE);
        booking.ChiTietDonDonDatLichItems.Should().Contain(p => p.Id == 11 && p.TrangThaiChamSoc == PetCareState.CANCELLED);
    }

    [Theory]
    [InlineData(InvoiceState.UNPAID)]
    [InlineData(InvoiceState.REVOKED)]
    [InlineData(null)]
    public async Task Handover_WhenInvoiceNotPaid_Returns409InvalidTransition(InvoiceState? invoiceState)
    {
        using var factory = CareTestSupport.Create(db =>
            SeedHandoverScenario(db, invoiceState: invoiceState));
        using var client = factory.CreateClient();

        var token = GenerateJwtFor(new NhanVien { Id = 301, TenDangNhap = "staff1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync(
            "/api/internal/booking-pets/10/handover",
            new Handover { RequestId = Guid.NewGuid(), XacNhanDaNhan = true });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body!.Success.Should().BeFalse();
        body.ErrorCode.Should().Be("INVALID_TRANSITION");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
        var pet = await db.ChiTietDonSet.FirstAsync(c => c.Id == 10);
        pet.TrangThaiChamSoc.Should().Be(PetCareState.READY);
        pet.BanGiaoAt.Should().BeNull();
    }

    [Theory]
    [InlineData(PetCareState.WAITING)]
    [InlineData(PetCareState.IN_PROGRESS)]
    [InlineData(PetCareState.PAUSED)]
    [InlineData(PetCareState.DONE)]
    [InlineData(PetCareState.CANCELLED)]
    public async Task Handover_WhenPetNotReady_Returns409InvalidTransition(PetCareState petState)
    {
        using var factory = CareTestSupport.Create(db =>
            SeedHandoverScenario(db, firstPetState: petState));
        using var client = factory.CreateClient();

        var token = GenerateJwtFor(new NhanVien { Id = 301, TenDangNhap = "staff1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync(
            "/api/internal/booking-pets/10/handover",
            new Handover { RequestId = Guid.NewGuid(), XacNhanDaNhan = true });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body!.ErrorCode.Should().Be("INVALID_TRANSITION");
    }

    [Fact]
    public async Task Handover_IdempotentRetrySameRequestId_Returns200CachedWithoutDuplicateAudit()
    {
        var clock = new CareTestSupport.Clock(CareTestSupport.Now);
        using var factory = CareTestSupport.Create(db => SeedHandoverScenario(db), clock: clock);
        using var client = factory.CreateClient();

        var token = GenerateJwtFor(new NhanVien { Id = 301, TenDangNhap = "staff1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var requestId = Guid.NewGuid();
        var payload = new Handover { RequestId = requestId, XacNhanDaNhan = true };

        var firstResponse = await client.PostAsJsonAsync("/api/internal/booking-pets/10/handover", payload);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await firstResponse.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();

        clock.UtcNow = clock.UtcNow.AddMinutes(20);
        var retryResponse = await client.PostAsJsonAsync("/api/internal/booking-pets/10/handover", payload);
        retryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var retryBody = await retryResponse.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();

        retryBody!.Data!.BanGiaoAt.Should().Be(firstBody!.Data!.BanGiaoAt);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
        var auditCount = await db.AuditLogSet.CountAsync(a => a.HanhDong == "PET_HANDOVER" && a.RequestId == requestId);
        auditCount.Should().Be(1);
        (await db.ChiTietDonSet.SingleAsync()).BanGiaoAt.Should().Be(CareTestSupport.Now);
    }

    [Fact]
    public async Task Handover_WithoutPermissionOrUnauthenticated_Returns403Or401()
    {
        using var factory = CareTestSupport.Create(db => SeedHandoverScenario(db));
        using var client = factory.CreateClient();

        var unauthResponse = await client.PostAsJsonAsync(
            "/api/internal/booking-pets/10/handover",
            new Handover { RequestId = Guid.NewGuid(), XacNhanDaNhan = true });
        unauthResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var careStaffToken = GenerateJwtFor(new NhanVien { Id = 302, TenDangNhap = "staff2", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", careStaffToken);

        var forbiddenResponse = await client.PostAsJsonAsync(
            "/api/internal/booking-pets/10/handover",
            new Handover { RequestId = Guid.NewGuid(), XacNhanDaNhan = true });
        forbiddenResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await forbiddenResponse.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body!.ErrorCode.Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task Handover_InvalidPayloadOrNotFound_Returns400Or404()
    {
        using var factory = CareTestSupport.Create(db => SeedHandoverScenario(db));
        using var client = factory.CreateClient();

        var token = GenerateJwtFor(new NhanVien { Id = 301, TenDangNhap = "staff1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var badResponse = await client.PostAsJsonAsync(
            "/api/internal/booking-pets/10/handover",
            new Handover { RequestId = Guid.NewGuid(), XacNhanDaNhan = false });
        badResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var badBody = await badResponse.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        badBody!.ErrorCode.Should().Be("VALIDATION_FAILED");

        var notFoundResponse = await client.PostAsJsonAsync(
            "/api/internal/booking-pets/999/handover",
            new Handover { RequestId = Guid.NewGuid(), XacNhanDaNhan = true });
        notFoundResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var notFoundBody = await notFoundResponse.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        notFoundBody!.ErrorCode.Should().Be("NOT_FOUND");
    }
}
