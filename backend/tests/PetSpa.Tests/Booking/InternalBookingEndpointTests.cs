using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PetSpa.Api.Data;
using PetSpa.Api.Services.Identity;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Booking;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Scheduling;
using PetSpa.Core.Enums;
using PetSpa.Tests.Infrastructure;
using Xunit;

namespace PetSpa.Tests.Booking;

public class InternalBookingEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

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

    private static (NhanVien Staff, DonDatLich PendingBooking, DonDatLich ConfirmedBooking) SeedInternalScenario(PetSpaDbContext db)
    {
        var staff = new NhanVien
        {
            Id = 50,
            TenNhanVien = "Lễ Tân 1",
            TenDangNhap = "letan1",
            MatKhauHash = "hash",
            VaiTro = Role.STAFF,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.NhanVienSet.Add(staff);

        var ca = new CaLamViec
        {
            Id = 1,
            TenCa = "Ca Sáng",
            GioBatDau = new TimeOnly(8, 0),
            GioKetThuc = new TimeOnly(12, 0),
            SucChuaPet = 10,
            TranPhut = 300,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.CaLamViecSet.Add(ca);

        var khungGio = new KhungGioDatLich
        {
            Id = 1,
            Ngay = new DateOnly(2026, 11, 26),
            MoNhanKhach = true,
            TongSlot = 10,
            SlotDaDat = 2,
            TranPhut = 300,
            PhutDaGiu = 120,
            CaLamViecId = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.KhungGioDatLichSet.Add(khungGio);

        var khach = new KhachHang
        {
            Id = 1,
            HoTen = "Nguyễn Văn Test",
            SoDienThoai = "0988776655",
            Email = "test@gmail.com",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.KhachHangSet.Add(khach);

        var pet1 = new ThuCung
        {
            Id = 1,
            TenThuCung = "Bé 1",
            LoaiPet = PetType.CHO,
            CanNang = 5m,
            KhachHangId = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var pet2 = new ThuCung
        {
            Id = 2,
            TenThuCung = "Bé 2",
            LoaiPet = PetType.MEO,
            CanNang = 3m,
            KhachHangId = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.ThuCungSet.AddRange(pet1, pet2);

        var pending = new DonDatLich
        {
            Id = 101,
            RequestId = Guid.NewGuid(),
            TrackingCode = "TRK11111",
            TrangThai = BookingState.PENDING,
            TenNguoiDat = khach.HoTen,
            SoDienThoaiSnapshot = khach.SoDienThoai,
            EmailSnapshot = khach.Email,
            KhachHangId = khach.Id,
            KhungGioId = khungGio.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var ct1 = new ChiTietDon
        {
            Id = 201,
            TenPetSnapshot = pet1.TenThuCung,
            TrangThaiChamSoc = PetCareState.NOT_ARRIVED,
            CanNangKhaiBao = 5m,
            ThuCungId = pet1.Id,
            DonDatLichId = pending.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var dv1 = new DichVuTrongDon
        {
            Id = 301,
            RequestId = Guid.NewGuid(),
            KhungGioId = khungGio.Id,
            TenSnapshot = "Tắm spa",
            ThoiLuongSnapshot = 60,
            TrangThai = OrderServiceState.CHO_THUC_HIEN,
            ChiTietDonId = ct1.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        ct1.DichVuTrongDonChiTietDonItems.Add(dv1);
        pending.ChiTietDonDonDatLichItems.Add(ct1);

        var confirmed = new DonDatLich
        {
            Id = 102,
            RequestId = Guid.NewGuid(),
            TrackingCode = "TRK22222",
            TrangThai = BookingState.CONFIRMED,
            TenNguoiDat = khach.HoTen,
            SoDienThoaiSnapshot = khach.SoDienThoai,
            EmailSnapshot = khach.Email,
            KhachHangId = khach.Id,
            KhungGioId = khungGio.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var ct2 = new ChiTietDon
        {
            Id = 202,
            TenPetSnapshot = pet2.TenThuCung,
            TrangThaiChamSoc = PetCareState.NOT_ARRIVED,
            CanNangKhaiBao = 3m,
            ThuCungId = pet2.Id,
            DonDatLichId = confirmed.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var dv2 = new DichVuTrongDon
        {
            Id = 302,
            RequestId = Guid.NewGuid(),
            KhungGioId = khungGio.Id,
            TenSnapshot = "Cắt tỉa lông",
            ThoiLuongSnapshot = 60,
            TrangThai = OrderServiceState.CHO_THUC_HIEN,
            ChiTietDonId = ct2.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        ct2.DichVuTrongDonChiTietDonItems.Add(dv2);
        confirmed.ChiTietDonDonDatLichItems.Add(ct2);

        db.DonDatLichSet.AddRange(pending, confirmed);
        db.SaveChanges();

        return (staff, pending, confirmed);
    }

    [Fact]
    public async Task GetBookings_WithPendingFilter_ReturnsOnlyPendingBookings()
    {
        // Arrange
        NhanVien? staff = null;
        using var factory = AuthApplicationFactory.Create(db =>
        {
            var res = SeedInternalScenario(db);
            staff = res.Staff;
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(staff!));

        // Act
        var res = await client.GetAsync("/api/internal/bookings?trangThai=PENDING");

        // Assert
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await res.Content.ReadFromJsonAsync<ApiResponse<PageBooking>>();
        content.Should().NotBeNull();
        content!.Success.Should().BeTrue();
        content.Data!.Items.Should().HaveCount(1);
        content.Data.Items[0].TrangThai.Should().Be("PENDING");
        content.Data.Items[0].MaDon.Should().Be(101);
    }

    [Fact]
    public async Task ConfirmBooking_WhenPending_TransitionsToConfirmed()
    {
        // Arrange
        NhanVien? staff = null;
        using var factory = AuthApplicationFactory.Create(db =>
        {
            var res = SeedInternalScenario(db);
            staff = res.Staff;
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(staff!));

        var command = new ConfirmBookingCommand { RequestId = Guid.NewGuid() };

        // Act
        var res = await client.PostAsJsonAsync("/api/internal/bookings/101/confirm", command);

        // Assert
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await res.Content.ReadFromJsonAsync<ApiResponse<BookingResponse>>();
        content.Should().NotBeNull();
        content!.Success.Should().BeTrue();
        content.Data!.TrangThai.Should().Be("CONFIRMED");
    }

    [Fact]
    public async Task ConfirmBooking_WhenNotPending_Returns409InvalidTransition()
    {
        // Arrange
        NhanVien? staff = null;
        using var factory = AuthApplicationFactory.Create(db =>
        {
            var res = SeedInternalScenario(db);
            staff = res.Staff;
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(staff!));

        var command = new ConfirmBookingCommand { RequestId = Guid.NewGuid() };

        // Act: Booking 102 is already CONFIRMED
        var res = await client.PostAsJsonAsync("/api/internal/bookings/102/confirm", command);

        // Assert
        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var content = await res.Content.ReadFromJsonAsync<ApiResponse<BookingResponse>>();
        content.Should().NotBeNull();
        content!.Success.Should().BeFalse();
        content.ErrorCode.Should().Be("INVALID_TRANSITION");
    }

    [Fact]
    public async Task CancelBooking_WhenPending_TransitionsToCancelled_AndReleasesCapacity()
    {
        // Arrange
        NhanVien? staff = null;
        using var factory = AuthApplicationFactory.Create(db =>
        {
            var res = SeedInternalScenario(db);
            staff = res.Staff;
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(staff!));

        var command = new CancelBookingCommand
        {
            RequestId = Guid.NewGuid(),
            LoaiHuy = CancellationType.STAFF_REJECTED,
            LyDo = "Khung giờ bận đột xuất"
        };

        // Act
        var res = await client.PostAsJsonAsync("/api/internal/bookings/101/cancel", command);

        // Assert
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await res.Content.ReadFromJsonAsync<ApiResponse<BookingResponse>>();
        content.Should().NotBeNull();
        content!.Success.Should().BeTrue();
        content.Data!.TrangThai.Should().Be("CANCELLED");

        // Verify capacity release in DB
        using var scope = factory.Services.CreateScope();
        var dbCheck = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
        var kg = await dbCheck.KhungGioDatLichSet.FindAsync(1);
        kg.Should().NotBeNull();
        // SlotDaDat was 2, decremented by 1 (1 pet) -> 1
        kg!.SlotDaDat.Should().Be(1);
        // PhutDaGiu was 120, decremented by 60 -> 60
        kg.PhutDaGiu.Should().Be(60);
    }

    [Fact]
    public async Task CancelBooking_WhenAlreadyCancelled_Returns409InvalidTransition()
    {
        // Arrange
        NhanVien? staff = null;
        using var factory = AuthApplicationFactory.Create(db =>
        {
            var res = SeedInternalScenario(db);
            staff = res.Staff;
            var b = db.DonDatLichSet.Find(101);
            b!.TrangThai = BookingState.CANCELLED;
            db.SaveChanges();
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(staff!));

        var command = new CancelBookingCommand
        {
            RequestId = Guid.NewGuid(),
            LoaiHuy = CancellationType.STAFF_REJECTED,
            LyDo = "Hủy lại"
        };

        // Act
        var res = await client.PostAsJsonAsync("/api/internal/bookings/101/cancel", command);

        // Assert
        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var content = await res.Content.ReadFromJsonAsync<ApiResponse<BookingResponse>>();
        content.Should().NotBeNull();
        content!.Success.Should().BeFalse();
        content.ErrorCode.Should().Be("INVALID_TRANSITION");
    }
}
