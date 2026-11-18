using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
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
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Scheduling;
using PetSpa.Core.Enums;
using PetSpa.Tests.Infrastructure;
using Xunit;

namespace PetSpa.Tests.Care;

public class CheckinEndpointTests
{
    private static readonly byte[] ValidPngBytes =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52
    ];

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

    private static MultipartFormDataContent BuildCheckinForm(
        Checkin payload,
        byte[]? photoBytes = null,
        string fileName = "checkin.png",
        string contentType = "image/png")
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json"), "payload");

        if (photoBytes is not null)
        {
            var fileContent = new ByteArrayContent(photoBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(fileContent, "anhTiepNhan", fileName);
        }

        return form;
    }

    private static Checkin CreateValidPayload(int assigneeId = 202) => new()
    {
        RequestId = Guid.NewGuid(),
        CanNangThucTe = 7.50m,
        TinhTrangLong = ["BINH_THUONG", "ROI_NHE"],
        MaNhanVien = assigneeId,
        GhiChuSucKhoe = "Bé khỏe mạnh, hơi rối nhẹ phần tai.",
        QuyetDinhGia = PriceDecision.SIZE_THUC_TE,
        XacNhanKhach = true
    };

    private static void SeedCheckinScenario(
        PetSpaDbContext db,
        BookingState bookingState = BookingState.CONFIRMED,
        PetCareState petState = PetCareState.NOT_ARRIVED,
        bool grantCheckinPermission = true,
        bool assigneeHasApprovedShift = true)
    {
        var today = new DateOnly(2026, 11, 20);
        var now = DateTimeOffset.UtcNow;

        var permission = new Quyen { MaQuyen = "CHECKIN_PERFORM", TenQuyen = "CHECKIN_PERFORM" };
        db.QuyenSet.Add(permission);

        var reception = new NhanVien
        {
            Id = 201,
            TenDangNhap = "reception1",
            TenNhanVien = "Lễ Tân Quầy",
            VaiTro = Role.STAFF,
            IsActive = true
        };
        reception.MatKhauHash = new PasswordHasher<NhanVien>().HashPassword(reception, "Password123@");

        var careStaff = new NhanVien
        {
            Id = 202,
            TenDangNhap = "care1",
            TenNhanVien = "KTV Spa",
            VaiTro = Role.STAFF,
            IsActive = true
        };
        careStaff.MatKhauHash = new PasswordHasher<NhanVien>().HashPassword(careStaff, "Password123@");

        db.NhanVienSet.AddRange(reception, careStaff);

        if (grantCheckinPermission)
        {
            db.NhanVienQuyenSet.Add(new NhanVienQuyen
            {
                NhanVienId = reception.Id,
                MaQuyen = "CHECKIN_PERFORM",
                CapBoiId = reception.Id,
                CapAt = now
            });
        }

        db.CauHinhHeThongSet.Add(new CauHinhHeThong
        {
            Khoa = "FUR_CONDITIONS",
            PhienBan = 1,
            SuaBoiId = reception.Id,
            GiaTri = "[{\"ma\":\"BINH_THUONG\",\"ten\":\"Bình thường\",\"dangHoatDong\":true,\"choPhepPhuPhiGoRoi\":false},{\"ma\":\"ROI_NHE\",\"ten\":\"Rối nhẹ\",\"dangHoatDong\":true,\"choPhepPhuPhiGoRoi\":false},{\"ma\":\"ROI_NANG\",\"ten\":\"Rối nặng\",\"dangHoatDong\":true,\"choPhepPhuPhiGoRoi\":true},{\"ma\":\"OLD_CODE\",\"ten\":\"Ngừng dùng\",\"dangHoatDong\":false,\"choPhepPhuPhiGoRoi\":false}]"
        });

        var sizeS = new HeSoCanNang { Id = 1, MaSize = SizeBand.S, CanNangTu = 0, CanNangDen = 5 };
        var sizeM = new HeSoCanNang { Id = 2, MaSize = SizeBand.M, CanNangTu = 5, CanNangDen = 15 };
        var sizeL = new HeSoCanNang { Id = 3, MaSize = SizeBand.L, CanNangTu = 15, CanNangDen = 25 };
        var sizeXL = new HeSoCanNang { Id = 4, MaSize = SizeBand.XL, CanNangTu = 25, CanNangDen = null };
        db.HeSoCanNangSet.AddRange(sizeS, sizeM, sizeL, sizeXL);

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

        var window = new DotDangKyCa
        {
            Id = 1,
            TuNgay = today,
            DenNgay = today,
            MoAt = now.AddDays(-7),
            DongAt = now.AddDays(-1),
            TaoBoiId = reception.Id
        };
        db.DotDangKyCaSet.Add(window);

        if (assigneeHasApprovedShift)
        {
            db.DangKyCaSet.Add(new DangKyCa
            {
                Id = 1,
                Ngay = today,
                CaLamViecId = shift.Id,
                DotId = window.Id,
                NhanVienId = careStaff.Id,
                TrangThai = ShiftRegistrationState.APPROVED,
                DuyetBoiId = reception.Id,
                DuyetAt = now
            });
        }

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
        db.BangGiaSizeSet.AddRange(
            new BangGiaSize { DichVuId = 1, SizeId = 1, DonGia = 150000m },
            new BangGiaSize { DichVuId = 1, SizeId = 2, DonGia = 200000m });

        var customer = new KhachHang
        {
            Id = 1,
            HoTen = "Nguyễn Văn A",
            SoDienThoai = "0912345678",
            Email = "khach@example.com"
        };
        db.KhachHangSet.Add(customer);

        var pet = new ThuCung
        {
            Id = 1,
            KhachHangId = customer.Id,
            TenThuCung = "Mochi",
            LoaiPet = PetType.CHO,
            CanNang = 4.5m
        };
        db.ThuCungSet.Add(pet);

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
            BatDauDuKien = now,
            KetThucDuKien = now.AddHours(1),
            MocCuoiCa = now.AddHours(4),
            TongTamTinh = 150000m,
            ChinhSachSnapshot = "{}",
            KhachHangId = customer.Id,
            KhungGioId = slot.Id
        };
        db.DonDatLichSet.Add(booking);

        var chiTietDon = new ChiTietDon
        {
            Id = 10,
            DonDatLichId = booking.Id,
            ThuCungId = pet.Id,
            TenPetSnapshot = pet.TenThuCung,
            CanNangKhaiBao = 4.5m,
            TrangThaiChamSoc = petState
        };
        db.ChiTietDonSet.Add(chiTietDon);

        db.DichVuTrongDonSet.Add(new DichVuTrongDon
        {
            Id = 100,
            RequestId = Guid.NewGuid(),
            ChiTietDonId = chiTietDon.Id,
            DichVuId = dichVu.Id,
            KhungGioId = slot.Id,
            TenSnapshot = dichVu.TenDichVu,
            BangGiaSizeSnapshot = "{\"S\":150000,\"M\":200000,\"L\":250000,\"XL\":300000}",
            ThoiLuongSnapshot = 60,
            TrangThai = OrderServiceState.CHO_THUC_HIEN,
            SizeChot = SizeBand.S,
            GiaChot = 150000m
        });

        db.SaveChanges();
    }

    [Fact]
    public async Task CheckIn_ValidRequest_TransitionsPetToWaitingAndBookingToInProgress()
    {
        using var factory = AuthApplicationFactory.Create(db => SeedCheckinScenario(db));
        using var client = factory.CreateClient();

        var token = GenerateJwtFor(new NhanVien { Id = 201, TenDangNhap = "reception1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = CreateValidPayload();
        using var form = BuildCheckinForm(payload, ValidPngBytes);

        var response = await client.PostAsync("/api/internal/booking-pets/10/check-in", form);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body.Should().NotBeNull();
        body!.Success.Should().BeTrue();
        body.Data.Should().NotBeNull();
        body.Data!.MaChiTietDon.Should().Be(10);
        body.Data.TrangThai.Should().Be("WAITING");
        body.Data.CanNangChot.Should().Be(7.50m);
        body.Data.SizeThucTe.Should().Be("M");
        body.Data.SizeChot.Should().Be("M");
        body.Data.TinhTrangLong.Should().BeEquivalentTo(["BINH_THUONG", "ROI_NHE"]);
        body.Data.TenNhanVienPhuTrach.Should().Be("KTV Spa");
        body.Data.DichVu.Should().ContainSingle(d => d.SizeChot == "M" && d.GiaChot == 200000m);
        body.Data.NhatKy.Should().ContainSingle(nk => nk.LoaiNhatKy == "CHECK_IN" && !string.IsNullOrWhiteSpace(nk.AnhUrl));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
        var savedChiTiet = await db.ChiTietDonSet.Include(c => c.DonDatLich).Include(c => c.ThuCung).FirstAsync(c => c.Id == 10);
        savedChiTiet.TrangThaiChamSoc.Should().Be(PetCareState.WAITING);
        savedChiTiet.TinhTrangLong.Should().Be("[\"BINH_THUONG\",\"ROI_NHE\"]");
        savedChiTiet.DonDatLich.TrangThai.Should().Be(BookingState.IN_PROGRESS);
        savedChiTiet.DonDatLich.TongTamTinh.Should().Be(200000m);
        savedChiTiet.ThuCung.CanNang.Should().Be(7.50m);
    }

    [Fact]
    public async Task CheckIn_BookingPending_Returns409InvalidTransition()
    {
        using var factory = AuthApplicationFactory.Create(db => SeedCheckinScenario(db, bookingState: BookingState.PENDING));
        using var client = factory.CreateClient();

        var token = GenerateJwtFor(new NhanVien { Id = 201, TenDangNhap = "reception1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var form = BuildCheckinForm(CreateValidPayload(), ValidPngBytes);
        var response = await client.PostAsync("/api/internal/booking-pets/10/check-in", form);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body!.Success.Should().BeFalse();
        body.ErrorCode.Should().Be("INVALID_TRANSITION");
    }

    [Fact]
    public async Task CheckIn_PetNotFound_Returns404NotFound()
    {
        using var factory = AuthApplicationFactory.Create(db => SeedCheckinScenario(db));
        using var client = factory.CreateClient();

        var token = GenerateJwtFor(new NhanVien { Id = 201, TenDangNhap = "reception1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var form = BuildCheckinForm(CreateValidPayload(), ValidPngBytes);
        var response = await client.PostAsync("/api/internal/booking-pets/9999/check-in", form);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body!.Success.Should().BeFalse();
        body.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task CheckIn_InvalidFurConditionCode_Returns400ValidationFailed()
    {
        using var factory = AuthApplicationFactory.Create(db => SeedCheckinScenario(db));
        using var client = factory.CreateClient();

        var token = GenerateJwtFor(new NhanVien { Id = 201, TenDangNhap = "reception1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = CreateValidPayload();
        payload.TinhTrangLong = ["BINH_THUONG", "OLD_CODE"];

        using var form = BuildCheckinForm(payload, ValidPngBytes);
        var response = await client.PostAsync("/api/internal/booking-pets/10/check-in", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body!.Success.Should().BeFalse();
        body.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task CheckIn_ThuGonWithoutNoiDung_Returns400ValidationFailed()
    {
        using var factory = AuthApplicationFactory.Create(db => SeedCheckinScenario(db));
        using var client = factory.CreateClient();

        var token = GenerateJwtFor(new NhanVien { Id = 201, TenDangNhap = "reception1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = CreateValidPayload();
        payload.QuyetDinhGia = PriceDecision.THU_GON;
        payload.NoiDungThuGon = null;

        using var form = BuildCheckinForm(payload, ValidPngBytes);
        var response = await client.PostAsync("/api/internal/booking-pets/10/check-in", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body!.Success.Should().BeFalse();
        body.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task CheckIn_AssigneeWithoutApprovedShift_Returns409AssignmentUnavailable()
    {
        using var factory = AuthApplicationFactory.Create(db => SeedCheckinScenario(db, assigneeHasApprovedShift: false));
        using var client = factory.CreateClient();

        var token = GenerateJwtFor(new NhanVien { Id = 201, TenDangNhap = "reception1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var form = BuildCheckinForm(CreateValidPayload(), ValidPngBytes);
        var response = await client.PostAsync("/api/internal/booking-pets/10/check-in", form);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body!.Success.Should().BeFalse();
        body.ErrorCode.Should().Be("ASSIGNMENT_UNAVAILABLE");
    }

    [Fact]
    public async Task CheckIn_MissingJwt_Returns401Unauthorized()
    {
        using var factory = AuthApplicationFactory.Create(db => SeedCheckinScenario(db));
        using var client = factory.CreateClient();

        using var form = BuildCheckinForm(CreateValidPayload(), ValidPngBytes);
        var response = await client.PostAsync("/api/internal/booking-pets/10/check-in", form);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CheckIn_WithoutPermission_Returns403Forbidden()
    {
        using var factory = AuthApplicationFactory.Create(db => SeedCheckinScenario(db, grantCheckinPermission: false));
        using var client = factory.CreateClient();

        var token = GenerateJwtFor(new NhanVien { Id = 201, TenDangNhap = "reception1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var form = BuildCheckinForm(CreateValidPayload(), ValidPngBytes);
        var response = await client.PostAsync("/api/internal/booking-pets/10/check-in", form);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body!.ErrorCode.Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task CheckIn_DuplicateRequestId_SamePayloadReturns200_DifferentPayloadReturns409()
    {
        using var factory = AuthApplicationFactory.Create(db => SeedCheckinScenario(db));
        using var client = factory.CreateClient();

        var token = GenerateJwtFor(new NhanVien { Id = 201, TenDangNhap = "reception1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = CreateValidPayload();

        using var form1 = BuildCheckinForm(payload, ValidPngBytes);
        var res1 = await client.PostAsync("/api/internal/booking-pets/10/check-in", form1);
        res1.StatusCode.Should().Be(HttpStatusCode.OK);

        using var form2 = BuildCheckinForm(payload, ValidPngBytes);
        var res2 = await client.PostAsync("/api/internal/booking-pets/10/check-in", form2);
        res2.StatusCode.Should().Be(HttpStatusCode.OK);
        var body2 = await res2.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body2!.Data!.TrangThai.Should().Be("WAITING");

        payload.CanNangThucTe = 8.0m;
        using var form3 = BuildCheckinForm(payload, ValidPngBytes);
        var res3 = await client.PostAsync("/api/internal/booking-pets/10/check-in", form3);
        res3.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body3 = await res3.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body3!.ErrorCode.Should().Be("IDEMPOTENCY_CONFLICT");
    }

    [Fact]
    public async Task CheckIn_UnsupportedFileSignature_Returns415UnsupportedFile()
    {
        using var factory = AuthApplicationFactory.Create(db => SeedCheckinScenario(db));
        using var client = factory.CreateClient();

        var token = GenerateJwtFor(new NhanVien { Id = 201, TenDangNhap = "reception1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var invalidBytes = Encoding.UTF8.GetBytes("not-an-image");
        using var form = BuildCheckinForm(CreateValidPayload(), invalidBytes, "fake.txt", "text/plain");
        var response = await client.PostAsync("/api/internal/booking-pets/10/check-in", form);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body!.ErrorCode.Should().Be("UNSUPPORTED_FILE");
    }
}
