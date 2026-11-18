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

public class CareLogsEndpointTests
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

    private static MultipartFormDataContent BuildCareLogForm(
        CareLogCreate payload,
        byte[]? photoBytes = null,
        string fileName = "log.png",
        string contentType = "image/png")
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json"), "payload");

        if (photoBytes is not null)
        {
            var fileContent = new ByteArrayContent(photoBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(fileContent, "anh", fileName);
        }

        return form;
    }

    private static void SeedCareLogScenario(
        PetSpaDbContext db,
        PetCareState petState = PetCareState.IN_PROGRESS,
        bool grantPhotoPermission = true,
        bool assigneeHasApprovedShift = true)
    {
        var today = new DateOnly(2026, 11, 20);
        var now = DateTimeOffset.UtcNow;

        var photoPerm = new Quyen { MaQuyen = "PHOTO_UPLOAD", TenQuyen = "PHOTO_UPLOAD" };
        db.QuyenSet.Add(photoPerm);

        var admin = new NhanVien
        {
            Id = 200,
            TenDangNhap = "admin",
            TenNhanVien = "Quản Trị Viên",
            VaiTro = Role.ADMIN,
            IsActive = true
        };
        admin.MatKhauHash = new PasswordHasher<NhanVien>().HashPassword(admin, "Password123@");

        var careStaff = new NhanVien
        {
            Id = 202,
            TenDangNhap = "care1",
            TenNhanVien = "KTV Spa",
            VaiTro = Role.STAFF,
            IsActive = true
        };
        careStaff.MatKhauHash = new PasswordHasher<NhanVien>().HashPassword(careStaff, "Password123@");

        var otherStaff = new NhanVien
        {
            Id = 203,
            TenDangNhap = "care2",
            TenNhanVien = "KTV Khác",
            VaiTro = Role.STAFF,
            IsActive = true
        };
        otherStaff.MatKhauHash = new PasswordHasher<NhanVien>().HashPassword(otherStaff, "Password123@");

        db.NhanVienSet.AddRange(admin, careStaff, otherStaff);

        db.NhanVienQuyenSet.Add(new NhanVienQuyen
        {
            NhanVienId = admin.Id,
            MaQuyen = "PHOTO_UPLOAD",
            CapBoiId = admin.Id,
            CapAt = now
        });

        if (grantPhotoPermission)
        {
            db.NhanVienQuyenSet.AddRange(
                new NhanVienQuyen
                {
                    NhanVienId = careStaff.Id,
                    MaQuyen = "PHOTO_UPLOAD",
                    CapBoiId = admin.Id,
                    CapAt = now
                },
                new NhanVienQuyen
                {
                    NhanVienId = otherStaff.Id,
                    MaQuyen = "PHOTO_UPLOAD",
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

        var window = new DotDangKyCa
        {
            Id = 1,
            TuNgay = today,
            DenNgay = today,
            MoAt = now.AddDays(-7),
            DongAt = now.AddDays(-1),
            TaoBoiId = admin.Id
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
                DuyetBoiId = admin.Id,
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
            CanNang = 7.50m
        };
        db.ThuCungSet.Add(pet);

        var booking = new DonDatLich
        {
            Id = 1,
            RequestId = Guid.NewGuid(),
            Fingerprint = "seed",
            TrackingCode = "TRK-8X2A",
            TrangThai = BookingState.IN_PROGRESS,
            TenNguoiDat = customer.HoTen,
            SoDienThoaiSnapshot = customer.SoDienThoai,
            EmailSnapshot = customer.Email,
            BatDauDuKien = now,
            KetThucDuKien = now.AddHours(1),
            MocCuoiCa = now.AddHours(4),
            TongTamTinh = 200000m,
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
            CanNangKhaiBao = 4.50m,
            CanNangChot = 7.50m,
            SizeThucTe = SizeBand.M,
            SizeChot = SizeBand.M,
            NhanVienId = careStaff.Id,
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
            BangGiaSizeSnapshot = "{}",
            ThoiLuongSnapshot = 60,
            TrangThai = OrderServiceState.CHO_THUC_HIEN,
            SizeChot = SizeBand.M,
            GiaChot = 200000m
        });

        db.SaveChanges();
    }

    [Fact]
    public async Task CareLogs_WithPhotoNoteAndWeight_Returns201_AndDoesNotOverwriteFinalWeight()
    {
        using var factory = AuthApplicationFactory.Create(db => SeedCareLogScenario(db));
        using var client = factory.CreateClient();

        var token = GenerateJwtFor(new NhanVien { Id = 202, TenDangNhap = "care1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new CareLogCreate
        {
            RequestId = Guid.NewGuid(),
            GhiChu = "Bé vừa tắm xong, đang sấy khô",
            CanNangTheoDoi = 7.80m
        };
        using var form = BuildCareLogForm(payload, ValidPngBytes, "drying.png");
        var response = await client.PostAsync("/api/internal/booking-pets/10/care-logs", form);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<CareLog>>();
        body!.Success.Should().BeTrue();
        body.Data.Should().NotBeNull();
        body.Data!.MaNhatKy.Should().BeGreaterThan(0);
        body.Data.LoaiNhatKy.Should().Be("CARE_LOG");
        body.Data.GhiChu.Should().Be("Bé vừa tắm xong, đang sấy khô");
        body.Data.AnhUrl.Should().StartWith("/storage/care-logs/");
        body.Data.CanNangTheoDoi.Should().Be(7.80m);
        body.Data.TenTacGia.Should().Be("KTV Spa");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
        var savedLog = await db.NhatKyChamSocSet.FirstOrDefaultAsync(nk => nk.Id == body.Data.MaNhatKy);
        savedLog.Should().NotBeNull();
        savedLog!.NhanVienId.Should().Be(202);
        savedLog.CanNangGhiNhan.Should().Be(7.80m);

        var savedChiTiet = await db.ChiTietDonSet.Include(c => c.ThuCung).FirstAsync(c => c.Id == 10);
        savedChiTiet.CanNangChot.Should().Be(7.50m);
        savedChiTiet.ThuCung.CanNang.Should().Be(7.50m);
    }

    [Fact]
    public async Task CareLogs_NoteOnlyWithoutPhoto_Returns201Created()
    {
        using var factory = AuthApplicationFactory.Create(db => SeedCareLogScenario(db));
        using var client = factory.CreateClient();

        var token = GenerateJwtFor(new NhanVien { Id = 202, TenDangNhap = "care1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new CareLogCreate
        {
            RequestId = Guid.NewGuid(),
            GhiChu = "Bé ăn uống bình thường"
        };
        using var form = BuildCareLogForm(payload);
        var response = await client.PostAsync("/api/internal/booking-pets/10/care-logs", form);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<CareLog>>();
        body!.Data!.AnhUrl.Should().BeNull();
        body.Data.GhiChu.Should().Be("Bé ăn uống bình thường");
    }

    [Fact]
    public async Task CareLogs_PhotoOnlyWithoutNote_Returns201Created()
    {
        using var factory = AuthApplicationFactory.Create(db => SeedCareLogScenario(db));
        using var client = factory.CreateClient();

        var token = GenerateJwtFor(new NhanVien { Id = 202, TenDangNhap = "care1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new CareLogCreate
        {
            RequestId = Guid.NewGuid()
        };
        using var form = BuildCareLogForm(payload, ValidPngBytes);
        var response = await client.PostAsync("/api/internal/booking-pets/10/care-logs", form);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<CareLog>>();
        body!.Data!.AnhUrl.Should().StartWith("/storage/care-logs/");
        body.Data.GhiChu.Should().BeNull();
    }

    [Fact]
    public async Task CareLogs_WithoutNoteAndWithoutPhoto_Returns400ValidationFailed()
    {
        using var factory = AuthApplicationFactory.Create(db => SeedCareLogScenario(db));
        using var client = factory.CreateClient();

        var token = GenerateJwtFor(new NhanVien { Id = 202, TenDangNhap = "care1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new CareLogCreate
        {
            RequestId = Guid.NewGuid()
        };
        using var form = BuildCareLogForm(payload);
        var response = await client.PostAsync("/api/internal/booking-pets/10/care-logs", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<CareLog>>();
        body!.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Theory]
    [InlineData(PetCareState.NOT_ARRIVED)]
    [InlineData(PetCareState.DONE)]
    [InlineData(PetCareState.CANCELLED)]
    public async Task CareLogs_InvalidPetState_Returns409InvalidTransition(PetCareState invalidState)
    {
        using var factory = AuthApplicationFactory.Create(db => SeedCareLogScenario(db, petState: invalidState));
        using var client = factory.CreateClient();

        var token = GenerateJwtFor(new NhanVien { Id = 202, TenDangNhap = "care1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new CareLogCreate
        {
            RequestId = Guid.NewGuid(),
            GhiChu = "Cập nhật nhật ký"
        };
        using var form = BuildCareLogForm(payload);
        var response = await client.PostAsync("/api/internal/booking-pets/10/care-logs", form);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<CareLog>>();
        body!.ErrorCode.Should().Be("INVALID_TRANSITION");
    }

    [Fact]
    public async Task CareLogs_WrongActorOrWithoutPermission_Returns403Forbidden()
    {
        using var factory1 = AuthApplicationFactory.Create(db => SeedCareLogScenario(db));
        using var client1 = factory1.CreateClient();
        var wrongActorToken = GenerateJwtFor(new NhanVien { Id = 203, TenDangNhap = "care2", VaiTro = Role.STAFF });
        client1.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", wrongActorToken);
        using var form1 = BuildCareLogForm(new CareLogCreate { RequestId = Guid.NewGuid(), GhiChu = "Log" });
        var res1 = await client1.PostAsync("/api/internal/booking-pets/10/care-logs", form1);
        res1.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var factory2 = AuthApplicationFactory.Create(db => SeedCareLogScenario(db, grantPhotoPermission: false));
        using var client2 = factory2.CreateClient();
        var staffToken = GenerateJwtFor(new NhanVien { Id = 202, TenDangNhap = "care1", VaiTro = Role.STAFF });
        client2.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        using var form2 = BuildCareLogForm(new CareLogCreate { RequestId = Guid.NewGuid(), GhiChu = "Log" });
        var res2 = await client2.PostAsync("/api/internal/booking-pets/10/care-logs", form2);
        res2.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CareLogs_AdminOverrideActor_Succeeds()
    {
        using var factory = AuthApplicationFactory.Create(db => SeedCareLogScenario(db));
        using var client = factory.CreateClient();

        var adminToken = GenerateJwtFor(new NhanVien { Id = 200, TenDangNhap = "admin", VaiTro = Role.ADMIN });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var payload = new CareLogCreate
        {
            RequestId = Guid.NewGuid(),
            GhiChu = "Admin bổ sung ảnh nhật ký"
        };
        using var form = BuildCareLogForm(payload, ValidPngBytes);
        var response = await client.PostAsync("/api/internal/booking-pets/10/care-logs", form);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<CareLog>>();
        body!.Data!.TenTacGia.Should().Be("Quản Trị Viên");
    }

    [Fact]
    public async Task CareLogs_DuplicateRequestId_SamePayloadReturns200_DifferentPayloadReturns409()
    {
        using var factory = AuthApplicationFactory.Create(db => SeedCareLogScenario(db));
        using var client = factory.CreateClient();

        var token = GenerateJwtFor(new NhanVien { Id = 202, TenDangNhap = "care1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new CareLogCreate
        {
            RequestId = Guid.NewGuid(),
            GhiChu = "Đang chải lông"
        };

        using var form1 = BuildCareLogForm(payload, ValidPngBytes);
        var res1 = await client.PostAsync("/api/internal/booking-pets/10/care-logs", form1);
        res1.StatusCode.Should().Be(HttpStatusCode.Created);
        var body1 = await res1.Content.ReadFromJsonAsync<ApiResponse<CareLog>>();

        using var form2 = BuildCareLogForm(payload, ValidPngBytes);
        var res2 = await client.PostAsync("/api/internal/booking-pets/10/care-logs", form2);
        res2.StatusCode.Should().Be(HttpStatusCode.OK);
        var body2 = await res2.Content.ReadFromJsonAsync<ApiResponse<CareLog>>();
        body2!.Data!.MaNhatKy.Should().Be(body1!.Data!.MaNhatKy);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
        (await db.NhatKyChamSocSet.CountAsync(nk => nk.ChiTietDonId == 10)).Should().Be(1);

        payload.GhiChu = "Nội dung khác cùng requestId";
        using var form3 = BuildCareLogForm(payload, ValidPngBytes);
        var res3 = await client.PostAsync("/api/internal/booking-pets/10/care-logs", form3);
        res3.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body3 = await res3.Content.ReadFromJsonAsync<ApiResponse<CareLog>>();
        body3!.ErrorCode.Should().Be("IDEMPOTENCY_CONFLICT");
    }

    [Fact]
    public async Task CareLogs_MissingJwt_Returns401Unauthorized()
    {
        using var factory = AuthApplicationFactory.Create(db => SeedCareLogScenario(db));
        using var client = factory.CreateClient();

        using var form = BuildCareLogForm(new CareLogCreate { RequestId = Guid.NewGuid(), GhiChu = "Test" });
        var response = await client.PostAsync("/api/internal/booking-pets/10/care-logs", form);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CareLogs_PetNotFound_Returns404NotFound()
    {
        using var factory = AuthApplicationFactory.Create(db => SeedCareLogScenario(db));
        using var client = factory.CreateClient();

        var token = GenerateJwtFor(new NhanVien { Id = 202, TenDangNhap = "care1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var form = BuildCareLogForm(new CareLogCreate { RequestId = Guid.NewGuid(), GhiChu = "Test" });
        var response = await client.PostAsync("/api/internal/booking-pets/9999/care-logs", form);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CareLogs_UnsupportedFileSignature_Returns415UnsupportedFile()
    {
        using var factory = AuthApplicationFactory.Create(db => SeedCareLogScenario(db));
        using var client = factory.CreateClient();

        var token = GenerateJwtFor(new NhanVien { Id = 202, TenDangNhap = "care1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var invalidBytes = Encoding.UTF8.GetBytes("not-an-image");
        using var form = BuildCareLogForm(new CareLogCreate { RequestId = Guid.NewGuid() }, invalidBytes, "fake.txt", "text/plain");
        var response = await client.PostAsync("/api/internal/booking-pets/10/care-logs", form);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<CareLog>>();
        body!.ErrorCode.Should().Be("UNSUPPORTED_FILE");
    }
}
