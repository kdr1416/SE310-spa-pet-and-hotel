using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using PetSpa.Api.Data;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Availability;
using PetSpa.Core.DTOs.Booking;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Scheduling;
using PetSpa.Core.Enums;
using PetSpa.Tests.Infrastructure;
using Xunit;

namespace PetSpa.Tests.Booking;

public class Issue15BookingTests
{
    private static string CreateToken(int staffId, string username, params string[] permissions)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(AuthApplicationFactory.TestJwtSecret);
        var claims = new List<Claim>
        {
            new("id", staffId.ToString()),
            new("username", username),
            new("role", "RECEPTIONIST")
        };
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            Issuer = "PetSpaApi",
            Audience = "PetSpaClient",
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    private static void SeedIssue15Data(PetSpaDbContext db, DateOnly intakeDate)
    {
        // Staff & Role
        db.NhanVienSet.Add(new NhanVien { Id = 1, TenDangNhap = "booking-staff", VaiTro = Role.STAFF, IsActive = true });
        db.NhanVienQuyenSet.Add(new NhanVienQuyen { NhanVienId = 1, MaQuyen = "CARE_UPDATE", CapBoiId = 1 });

        // Shifts & Slots
        db.CaLamViecSet.Add(new CaLamViec
        {
            Id = 1,
            GioBatDau = new TimeOnly(8, 0),
            GioKetThuc = new TimeOnly(12, 0),
            SucChuaPet = 10,
            TranPhut = 300,
            IsActive = true
        });

        db.DangKyCaSet.Add(new DangKyCa
        {
            Ngay = intakeDate,
            CaLamViecId = 1,
            NhanVienId = 1,
            TrangThai = ShiftRegistrationState.APPROVED
        });

        db.KhungGioDatLichSet.Add(new KhungGioDatLich
        {
            Id = 1,
            Ngay = intakeDate,
            CaLamViecId = 1,
            TongSlot = 5,
            SlotDaDat = 0,
            TranPhut = 300,
            PhutDaGiu = 0,
            MoNhanKhach = true
        });

        // Room Types
        db.LoaiPhongSet.Add(new LoaiPhong
        {
            Id = 1,
            TenPhong = "Phòng Deluxe",
            GiaMoiDem = 200000m,
            SoLuongPhong = 3,
            IsActive = true
        });

        // Single Services
        db.DichVuSet.Add(new DichVu
        {
            Id = 1,
            TenDichVu = "Tắm sấy cơ bản",
            LoaiDichVu = "SPA",
            ThoiLuongPhut = 45,
            YeuCauTiemChung = false,
            IsActive = true
        });

        db.DichVuSet.Add(new DichVu
        {
            Id = 2,
            TenDichVu = "Cắt móng vệ sinh tai",
            LoaiDichVu = "SPA",
            ThoiLuongPhut = 30,
            YeuCauTiemChung = false,
            IsActive = true
        });

        // Combo
        db.ComboSet.Add(new Combo
        {
            Id = 1,
            TenCombo = "Combo Toàn Diện",
            IsActive = true
        });
        db.ChiTietComboSet.Add(new ChiTietCombo { ComboId = 1, DichVuId = 1 });
        db.ChiTietComboSet.Add(new ChiTietCombo { ComboId = 1, DichVuId = 2 });

        // Pet Sizes & Pricing (S, M, L, XL)
        (decimal From, decimal? To)[] weights = [(0, 5), (5, 15), (15, 25), (25, null)];
        for (var index = 0; index < weights.Length; index++)
        {
            var (from, to) = weights[index];
            var sizeId = index + 1;
            db.HeSoCanNangSet.Add(new HeSoCanNang
            {
                Id = sizeId,
                MaSize = (SizeBand)index,
                CanNangTu = from,
                CanNangDen = to
            });
            db.BangGiaSizeSet.Add(new BangGiaSize { DichVuId = 1, SizeId = sizeId, DonGia = 150000m + index * 50000m });
            db.BangGiaSizeSet.Add(new BangGiaSize { DichVuId = 2, SizeId = sizeId, DonGia = 100000m + index * 20000m });
            db.BangGiaSizeSet.Add(new BangGiaSize { ComboId = 1, SizeId = sizeId, DonGia = 220000m + index * 50000m });
        }

        // Extension settings: OTP not required
        db.CauHinhHeThongSet.Add(new CauHinhHeThong
        {
            Khoa = "EXTENSION_SETTINGS",
            GiaTri = "{\"otpEmail\":false,\"cocHotel\":true,\"giuCho\":true}"
        });

        // Customer & Pet
        db.KhachHangSet.Add(new KhachHang
        {
            Id = 10,
            HoTen = "Nguyễn Văn A",
            SoDienThoai = "0901234567",
            Email = "vana@example.com"
        });

        db.ThuCungSet.Add(new ThuCung
        {
            Id = 20,
            KhachHangId = 10,
            TenThuCung = "Miu Con",
            LoaiPet = PetType.MEO,
            CanNang = 3.5m
        });

        db.SaveChanges();
    }

    [Fact]
    public async Task Availability_WithMultiNightHotel_ReturnsRemainingCapacity()
    {
        var intake = new DateOnly(2026, 11, 25);
        using var factory = AuthApplicationFactory.Create(db => SeedIssue15Data(db, intake));
        using var client = factory.CreateClient();

        var query = new AvailabilityQuery
        {
            MaKhungGio = 1,
            NgayTra = intake.AddDays(2),
            ThuCung =
            [
                new PetDemand
                {
                    MaLoaiPhong = 1,
                    DichVu =
                    [
                        new ServiceSelection { MaKhungGio = 1, MaCombo = 1 }
                    ]
                }
            ]
        };

        var response = await client.PostAsJsonAsync("/api/availability", query);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<AvailabilityResponse>>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Data!.CoTheDat.Should().BeTrue();
        result.Data.Phong.Should().HaveCount(2); // 2 nights
        result.Data.Phong.All(p => p.SuatConLai == 3).Should().BeTrue();
    }

    [Fact]
    public async Task Availability_HotelOnlyPet_ConsumesZeroSpaSlots()
    {
        var intake = new DateOnly(2026, 11, 25);
        using var factory = AuthApplicationFactory.Create(db => SeedIssue15Data(db, intake));
        using var client = factory.CreateClient();

        var query = new AvailabilityQuery
        {
            MaKhungGio = 1,
            NgayTra = intake.AddDays(1),
            ThuCung =
            [
                new PetDemand
                {
                    MaLoaiPhong = 1,
                    DichVu = [] // Hotel only, no spa services
                }
            ]
        };

        var response = await client.PostAsJsonAsync("/api/availability", query);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<AvailabilityResponse>>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Data!.CoTheDat.Should().BeTrue();
        result.Data.SlotConLai.Should().Be(5); // unchanged
        result.Data.PhutConLai.Should().Be(240); // 4 hours shift duration
    }

    [Fact]
    public async Task Availability_IntakeOnHoliday_ReturnsCapacityExceeded()
    {
        var intake = new DateOnly(2026, 11, 25);
        using var factory = AuthApplicationFactory.Create(db =>
        {
            SeedIssue15Data(db, intake);
            db.NgayNghiSet.Add(new NgayNghi { Ngay = intake });
            db.SaveChanges();
        });
        using var client = factory.CreateClient();

        var query = new AvailabilityQuery
        {
            MaKhungGio = 1,
            NgayTra = intake.AddDays(2),
            ThuCung =
            [
                new PetDemand
                {
                    MaLoaiPhong = 1,
                    DichVu = []
                }
            ]
        };

        var response = await client.PostAsJsonAsync("/api/availability", query);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<AvailabilityResponse>>();
        result!.ErrorCode.Should().Be("CAPACITY_EXCEEDED");
    }

    private static readonly JsonSerializerOptions JsonOptionsWithoutNulls = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static Task<HttpResponseMessage> PostInternalBookingAsync(HttpClient client, BookingCreate request)
    {
        var json = JsonSerializer.Serialize(request, JsonOptionsWithoutNulls);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return client.PostAsync("/api/internal/bookings", content);
    }

    [Fact]
    public async Task CounterBooking_WithoutToken_Returns401()
    {
        var intake = new DateOnly(2026, 11, 25);
        using var factory = AuthApplicationFactory.Create(db => SeedIssue15Data(db, intake));
        using var client = factory.CreateClient();

        var request = new BookingCreate(
            RequestId: Guid.NewGuid(),
            LienHe: new Contact("Khách Lẻ", "0988888888", "khach@example.com"),
            MaKhungGio: 1,
            NgayTra: intake,
            ThuCung:
            [
                new PetSelection(
                    CanNangKhaiBao: 3.5m,
                    DichVu: [new ServiceSelection { MaDichVu = 1, MaKhungGio = 1 }],
                    TenThuCung: "Cún",
                    LoaiPet: "CHO")
            ]
        );

        var response = await PostInternalBookingAsync(client, request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CounterBooking_WithoutBookingCreatePermission_Returns403()
    {
        var intake = new DateOnly(2026, 11, 25);
        using var factory = AuthApplicationFactory.Create(db => SeedIssue15Data(db, intake));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CreateToken(1, "booking-staff"));

        var request = new BookingCreate(
            RequestId: Guid.NewGuid(),
            LienHe: new Contact("Khách Lẻ", "0988888888", "khach@example.com"),
            MaKhungGio: 1,
            NgayTra: intake,
            ThuCung:
            [
                new PetSelection(
                    CanNangKhaiBao: 3.5m,
                    DichVu: [new ServiceSelection { MaDichVu = 1, MaKhungGio = 1 }],
                    TenThuCung: "Cún",
                    LoaiPet: "CHO")
            ]
        );

        var response = await PostInternalBookingAsync(client, request);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CounterBooking_WithHotelAndCombo_Sets30PercentDepositAndAssignsActor()
    {
        var intake = new DateOnly(2026, 11, 25);
        using var factory = AuthApplicationFactory.Create(db =>
        {
            SeedIssue15Data(db, intake);
            db.NhanVienQuyenSet.Add(new NhanVienQuyen { NhanVienId = 1, MaQuyen = "BOOKING_CREATE", CapBoiId = 1 });
            db.SaveChanges();
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CreateToken(1, "booking-staff"));

        var request = new BookingCreate(
            RequestId: Guid.NewGuid(),
            LienHe: new Contact("Nguyễn Văn A", "0901234567", "vana@example.com"),
            MaKhungGio: 1,
            NgayTra: intake.AddDays(2), // 2 nights * 200,000 = 400,000
            MaKhachHang: 10,
            ThuCung:
            [
                new PetSelection(
                    CanNangKhaiBao: 3.5m,
                    DichVu: [new ServiceSelection { MaCombo = 1, MaKhungGio = 1 }], // Combo: 220,000
                    MaThuCung: 20,
                    TenThuCung: "Miu Con",
                    LoaiPet: "MEO",
                    MaLoaiPhong: 1 // 400,000
                )
            ]
        );

        // Total: 400,000 + 220,000 = 620,000
        // 30% Deposit: 620,000 * 0.30 = 186,000
        var response = await PostInternalBookingAsync(client, request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<BookingResponse>>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Data!.MaDon.Should().BeGreaterThan(0);
        result.Data.TongTamTinh.Should().Be(620000m);
        result.Data.TienCoc.Should().NotBeNull();
        result.Data.TienCoc!.SoTienYeuCau.Should().Be(186000m);

        // Verify Database state
        using var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(factory.Services);
        var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
        var booking = await db.DonDatLichSet
            .Include(b => b.TienCocDonDatLich)
            .Include(b => b.ChiTietDonDonDatLichItems)
                .ThenInclude(ct => ct.DichVuTrongDonChiTietDonItems)
            .FirstAsync(b => b.Id == result.Data.MaDon);

        booking.TaoBoiId.Should().Be(1); // Set to staff actor id
        booking.TienCocDonDatLich.Should().NotBeNull();
        booking.TienCocDonDatLich!.SoTienYeuCau.Should().Be(186000m);
        booking.TienCocDonDatLich.TrangThaiYeuCau.Should().Be(DepositRequestState.CHO_THU);

        // Verify hotel details
        var petOrder = booking.ChiTietDonDonDatLichItems.Should().ContainSingle().Subject;
        petOrder.LoaiPhongId.Should().Be(1);
        petOrder.SoDemChot.Should().Be(2);
        petOrder.GiaPhongCamKet.Should().Be(200000m);

        // Verify combo services expanded into order services
        var comboOrder = petOrder.DichVuTrongDonChiTietDonItems.Should().ContainSingle().Subject;
        comboOrder.ComboId.Should().Be(1);

        var components = await db.ThanhPhanDichVuTrongDonSet
            .Where(tp => tp.DichVuTrongDonId == comboOrder.Id)
            .ToListAsync();
        components.Should().HaveCount(2);
    }
}
