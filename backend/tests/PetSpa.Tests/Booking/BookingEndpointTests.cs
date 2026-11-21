using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PetSpa.Api.Data;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Availability;
using PetSpa.Core.DTOs.Booking;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Scheduling;
using PetSpa.Core.Enums;
using PetSpa.Tests.Infrastructure;
using Xunit;

namespace PetSpa.Tests.Booking;

public class BookingEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static void SeedBookingDependencies(PetSpaDbContext db)
    {
        var ca = new CaLamViec
        {
            Id = 1,
            TenCa = "Ca Sáng",
            GioBatDau = new TimeOnly(8, 0),
            GioKetThuc = new TimeOnly(12, 0),
            SucChuaPet = 5,
            TranPhut = 240,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.CaLamViecSet.Add(ca);

        var staff = new NhanVien
        {
            Id = 10,
            TenNhanVien = "Kỹ thuật viên 1",
            TenDangNhap = "ktv1",
            MatKhauHash = "hash",
            VaiTro = Role.ADMIN,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.NhanVienSet.Add(staff);

        var khungGio = new KhungGioDatLich
        {
            Id = 1,
            Ngay = new DateOnly(2026, 11, 25),
            MoNhanKhach = true,
            TongSlot = 5,
            SlotDaDat = 0,
            TranPhut = 240,
            PhutDaGiu = 0,
            CaLamViecId = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.KhungGioDatLichSet.Add(khungGio);

        var dangKyCa = new DangKyCa
        {
            Id = 1,
            Ngay = new DateOnly(2026, 11, 25),
            CaLamViecId = 1,
            NhanVienId = 10,
            TrangThai = ShiftRegistrationState.APPROVED,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.DangKyCaSet.Add(dangKyCa);

        var heSoS = new HeSoCanNang
        {
            Id = 1,
            MaSize = SizeBand.S,
            CanNangTu = 0m,
            CanNangDen = 5m,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var heSoM = new HeSoCanNang
        {
            Id = 2,
            MaSize = SizeBand.M,
            CanNangTu = 5m,
            CanNangDen = 15m,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.HeSoCanNangSet.AddRange(heSoS, heSoM);

        var dichVu = new DichVu
        {
            Id = 1,
            TenDichVu = "Tắm vệ sinh thú cưng",
            LoaiDichVu = "SPA",
            ThoiLuongPhut = 60,
            YeuCauTiemChung = false,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.DichVuSet.Add(dichVu);

        var bangGia = new BangGiaSize
        {
            Id = 1,
            DichVuId = 1,
            SizeId = 1,
            DonGia = 150000m,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.BangGiaSizeSet.Add(bangGia);

        db.SaveChanges();
    }

    [Fact]
    public async Task CreateBooking_WhenValidPayload_Returns201Created_WithTrackingCode()
    {
        // Arrange
        using var factory = AuthApplicationFactory.Create(SeedBookingDependencies);
        var client = factory.CreateClient();

        var bookingPayload = new BookingCreate
        {
            RequestId = Guid.NewGuid(),
            LienHe = new Contact
            {
                HoTen = "Nguyễn Văn A",
                SoDienThoai = "0987654321",
                Email = "nguyenvana@gmail.com"
            },
            MaKhungGio = 1,
            NgayTra = new DateOnly(2026, 11, 25),
            ThuCung =
            [
                new PetSelection
                {
                    TenThuCung = "Miu",
                    LoaiPet = "MEO",
                    CanNangKhaiBao = 3.5m,
                    DichVu = [new ServiceSelection { MaDichVu = 1, MaKhungGio = 1 }]
                }
            ]
        };

        var form = new MultipartFormDataContent();
        form.Add(new StringContent(JsonSerializer.Serialize(bookingPayload, JsonOptions), Encoding.UTF8, "application/json"), "payload");

        // Act
        var response = await client.PostAsync("/api/bookings", form);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var content = await response.Content.ReadFromJsonAsync<ApiResponse<BookingResponse>>();
        content.Should().NotBeNull();
        content!.Success.Should().BeTrue();
        content.Data.Should().NotBeNull();
        content.Data!.TrackingCode.Should().StartWith("TRK").And.HaveLength(8);
        content.Data.TrangThai.Should().Be("PENDING");
        content.Data.TongTamTinh.Should().Be(150000m);
        content.Data.ThuCung.Should().HaveCount(1);
        content.Data.ThuCung[0].TenThuCung.Should().Be("Miu");
    }

    [Fact]
    public async Task CreateBooking_IdempotentReplay_Returns200Ok_WithSameBooking()
    {
        // Arrange
        using var factory = AuthApplicationFactory.Create(SeedBookingDependencies);
        var client = factory.CreateClient();

        var requestId = Guid.NewGuid();
        var bookingPayload = new BookingCreate
        {
            RequestId = requestId,
            LienHe = new Contact
            {
                HoTen = "Trần Thị B",
                SoDienThoai = "0912345678",
                Email = "tranthib@gmail.com"
            },
            MaKhungGio = 1,
            NgayTra = new DateOnly(2026, 11, 25),
            ThuCung =
            [
                new PetSelection
                {
                    TenThuCung = "Cún",
                    LoaiPet = "CHO",
                    CanNangKhaiBao = 4.0m,
                    DichVu = [new ServiceSelection { MaDichVu = 1, MaKhungGio = 1 }]
                }
            ]
        };

        var json = JsonSerializer.Serialize(bookingPayload, JsonOptions);

        var form1 = new MultipartFormDataContent();
        form1.Add(new StringContent(json, Encoding.UTF8, "application/json"), "payload");

        // First attempt -> 201 Created
        var response1 = await client.PostAsync("/api/bookings", form1);
        response1.StatusCode.Should().Be(HttpStatusCode.Created);
        var result1 = await response1.Content.ReadFromJsonAsync<ApiResponse<BookingResponse>>();

        // Act: Second identical attempt with same RequestId -> 200 OK replay
        var form2 = new MultipartFormDataContent();
        form2.Add(new StringContent(json, Encoding.UTF8, "application/json"), "payload");
        var response2 = await client.PostAsync("/api/bookings", form2);

        // Assert
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
        var result2 = await response2.Content.ReadFromJsonAsync<ApiResponse<BookingResponse>>();
        result2.Should().NotBeNull();
        result2!.Success.Should().BeTrue();
        result2.Data!.MaDon.Should().Be(result1!.Data!.MaDon);
        result2.Data.TrackingCode.Should().Be(result1.Data.TrackingCode);
    }

    [Fact]
    public async Task CreateBooking_IdempotencyConflict_Returns409Conflict()
    {
        // Arrange
        using var factory = AuthApplicationFactory.Create(SeedBookingDependencies);
        var client = factory.CreateClient();

        var requestId = Guid.NewGuid();
        var payload1 = new BookingCreate
        {
            RequestId = requestId,
            LienHe = new Contact { HoTen = "Lê Văn C", SoDienThoai = "0933333333", Email = "c1@gmail.com" },
            MaKhungGio = 1,
            NgayTra = new DateOnly(2026, 11, 25),
            ThuCung = [new PetSelection { TenThuCung = "Bé 1", CanNangKhaiBao = 2.0m, DichVu = [new ServiceSelection { MaDichVu = 1, MaKhungGio = 1 }] }]
        };

        var form1 = new MultipartFormDataContent();
        form1.Add(new StringContent(JsonSerializer.Serialize(payload1, JsonOptions), Encoding.UTF8, "application/json"), "payload");
        var res1 = await client.PostAsync("/api/bookings", form1);
        res1.StatusCode.Should().Be(HttpStatusCode.Created);

        // Act: Same RequestId but modified details
        var payload2 = new BookingCreate
        {
            RequestId = requestId,
            LienHe = new Contact { HoTen = "Lê Văn C Đã Đổi", SoDienThoai = "0944444444", Email = "c2@gmail.com" },
            MaKhungGio = 1,
            NgayTra = new DateOnly(2026, 11, 25),
            ThuCung = [new PetSelection { TenThuCung = "Bé Khác", CanNangKhaiBao = 5.0m, DichVu = [new ServiceSelection { MaDichVu = 1, MaKhungGio = 1 }] }]
        };

        var form2 = new MultipartFormDataContent();
        form2.Add(new StringContent(JsonSerializer.Serialize(payload2, JsonOptions), Encoding.UTF8, "application/json"), "payload");
        var res2 = await client.PostAsync("/api/bookings", form2);

        // Assert
        res2.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var content = await res2.Content.ReadFromJsonAsync<ApiResponse<BookingResponse>>();
        content.Should().NotBeNull();
        content!.Success.Should().BeFalse();
        content.ErrorCode.Should().Be("IDEMPOTENCY_CONFLICT");
    }

    [Fact]
    public async Task CreateBooking_WhenCapacityExceeded_Returns409Conflict_WithCapacityExceededCode()
    {
        // Arrange
        using var factory = AuthApplicationFactory.Create(db =>
        {
            SeedBookingDependencies(db);
            // Làm đầy khung giờ: SlotDaDat = SucChuaPet
            var kg = db.KhungGioDatLichSet.First();
            kg.SlotDaDat = 5;
            db.SaveChanges();
        });
        var client = factory.CreateClient();

        var bookingPayload = new BookingCreate
        {
            RequestId = Guid.NewGuid(),
            LienHe = new Contact { HoTen = "Phạm Thị D", SoDienThoai = "0955555555", Email = "d@gmail.com" },
            MaKhungGio = 1,
            NgayTra = new DateOnly(2026, 11, 25),
            ThuCung = [new PetSelection { TenThuCung = "Miu Miu", CanNangKhaiBao = 3.0m, DichVu = [new ServiceSelection { MaDichVu = 1, MaKhungGio = 1 }] }]
        };

        var form = new MultipartFormDataContent();
        form.Add(new StringContent(JsonSerializer.Serialize(bookingPayload, JsonOptions), Encoding.UTF8, "application/json"), "payload");

        // Act
        var res = await client.PostAsync("/api/bookings", form);

        // Assert
        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var content = await res.Content.ReadFromJsonAsync<ApiResponse<BookingResponse>>();
        content.Should().NotBeNull();
        content!.Success.Should().BeFalse();
        content.ErrorCode.Should().Be("CAPACITY_EXCEEDED");
    }

    [Fact]
    public async Task CreateBooking_WhenNoPayload_Returns400BadRequest()
    {
        // Arrange
        using var factory = AuthApplicationFactory.Create();
        var client = factory.CreateClient();

        var emptyForm = new MultipartFormDataContent();

        // Act
        var res = await client.PostAsync("/api/bookings", emptyForm);

        // Assert
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await res.Content.ReadFromJsonAsync<ApiResponse<BookingResponse>>();
        content.Should().NotBeNull();
        content!.Success.Should().BeFalse();
        content.ErrorCode.Should().Be("VALIDATION_FAILED");
    }
}
