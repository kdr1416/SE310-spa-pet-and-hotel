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
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Scheduling;
using PetSpa.Core.Enums;
using PetSpa.Tests.Infrastructure;
using Xunit;

namespace PetSpa.Tests.Care;

public class CareActionsEndpointTests
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

    private static MultipartFormDataContent BuildCareActionForm(
        CareAction payload,
        byte[]? photoBytes = null,
        string fileName = "result.png",
        string contentType = "image/png")
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json"), "payload");

        if (photoBytes is not null)
        {
            var fileContent = new ByteArrayContent(photoBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(fileContent, "anhThanhPham", fileName);
        }

        return form;
    }

    private static void SeedCareScenario(
        PetSpaDbContext db,
        PetCareState petState = PetCareState.WAITING,
        BookingState bookingState = BookingState.IN_PROGRESS,
        bool grantCarePermission = true,
        bool assigneeHasApprovedShift = true,
        bool seedComboIncomplete = false,
        bool seedPhotoException = false)
    {
        var today = new DateOnly(2026, 11, 20);
        var now = CareTestSupport.Now;

        var careUpdatePerm = new Quyen { MaQuyen = "CARE_UPDATE", TenQuyen = "CARE_UPDATE" };
        db.QuyenSet.Add(careUpdatePerm);

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
            MaQuyen = "CARE_UPDATE",
            CapBoiId = admin.Id,
            CapAt = now
        });

        if (grantCarePermission)
        {
            db.NhanVienQuyenSet.AddRange(
                new NhanVienQuyen
                {
                    NhanVienId = careStaff.Id,
                    MaQuyen = "CARE_UPDATE",
                    CapBoiId = admin.Id,
                    CapAt = now
                },
                new NhanVienQuyen
                {
                    NhanVienId = otherStaff.Id,
                    MaQuyen = "CARE_UPDATE",
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
            TrangThai = bookingState,
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
            TinhTrangLong = "[\"BINH_THUONG\"]",
            GhiChuSucKhoe = "Bé khỏe mạnh",
            NhanVienId = careStaff.Id,
            CheckInAt = now.AddMinutes(-15),
            CheckInBoiId = admin.Id,
            TrangThaiChamSoc = petState
        };
        db.ChiTietDonSet.Add(chiTietDon);

        db.NhatKyChamSocSet.Add(new NhatKyChamSoc
        {
            Id = 1,
            ChiTietDonId = chiTietDon.Id,
            NhanVienId = admin.Id,
            LoaiNhatKy = "CHECK_IN",
            GhiNhanAt = now.AddMinutes(-15),
            ObjectPath = "check-in/seed.png",
            GhiChu = "Ảnh tiếp nhận",
            CanNangGhiNhan = 7.50m
        });

        if (seedComboIncomplete)
        {
            var combo = new Combo
            {
                Id = 1,
                TenCombo = "Combo Tắm Cắt",
                IsActive = true
            };
            db.ComboSet.Add(combo);

            var orderCombo = new DichVuTrongDon
            {
                Id = 101,
                RequestId = Guid.NewGuid(),
                ChiTietDonId = chiTietDon.Id,
                ComboId = combo.Id,
                KhungGioId = slot.Id,
                TenSnapshot = combo.TenCombo,
                BangGiaSizeSnapshot = "{}",
                ThoiLuongSnapshot = 90,
                TrangThai = OrderServiceState.CHO_THUC_HIEN,
                SizeChot = SizeBand.M,
                GiaChot = 350000m
            };
            db.DichVuTrongDonSet.Add(orderCombo);

            db.ThanhPhanDichVuTrongDonSet.Add(new ThanhPhanDichVuTrongDon
            {
                Id = 201,
                DichVuTrongDonId = orderCombo.Id,
                DichVuId = dichVu.Id,
                TenSnapshot = dichVu.TenDichVu,
                GiaLeSizeSnapshot = "{}",
                ThoiLuongSnapshot = 60,
                TrangThai = OrderServiceState.CHO_THUC_HIEN
            });
        }
        else
        {
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
                SizeChot = SizeBand.M,
                GiaChot = 200000m
            });
        }

        if (seedPhotoException)
        {
            db.AuditLogSet.Add(new AuditLog
            {
                Id = 501,
                ActorLoai = Role.ADMIN.ToString(),
                ActorId = admin.Id,
                HanhDong = "PHOTO_EXCEPTION_APPROVE",
                LoaiDoiTuong = "ChiTietDon",
                DoiTuongId = chiTietDon.Id,
                LyDo = "Bé hoảng sợ khi chụp ảnh",
                TraceId = "seed-trace",
                CreatedAt = now
            });
        }

        db.SaveChanges();
    }

    [Fact]
    public async Task CareActions_ReadyReplayRedactsPhotoAfterShiftEnds()
    {
        var clock = new CareTestSupport.Clock(CareTestSupport.Now);
        var storage = new CareTestSupport.Storage();
        using var factory = CareTestSupport.Create(db => SeedCareScenario(db, petState: PetCareState.IN_PROGRESS), storage, clock);
        using var client = factory.CreateClient();
        var token = GenerateJwtFor(new NhanVien { Id = 202, TenDangNhap = "care1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var request = new CareAction { RequestId = Guid.NewGuid(), HanhDong = CareActionType.READY };
        using var form = BuildCareActionForm(request, ValidPngBytes);
        using var first = await client.PostAsync("/api/internal/booking-pets/10/care-actions", form);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var committed = (await first.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>())!.Data!;
        committed.NhatKy.Single(log => log.LoaiNhatKy == "CARE_READY").AnhUrl.Should().StartWith("https://photo.test/");

        clock.UtcNow = new DateTimeOffset(2026, 11, 20, 5, 0, 0, TimeSpan.Zero);
        using var retryForm = BuildCareActionForm(request, ValidPngBytes);
        using var retry = await client.PostAsync("/api/internal/booking-pets/10/care-actions", retryForm);

        retry.StatusCode.Should().Be(HttpStatusCode.OK);
        var replay = (await retry.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>())!.Data!;
        replay.TrangThai.Should().Be("READY");
        replay.NhatKy.Should().OnlyContain(log => log.AnhUrl == null);
        storage.Reads.Should().ContainSingle();
        storage.Uploads.Should().ContainSingle();
    }

    [Fact]
    public async Task CareActions_ApprovedReservationShiftDoesNotAuthorizeOutsideActualShiftHours()
    {
        using var factory = CareTestSupport.Create(db => SeedCareScenario(db),
            clock: new CareTestSupport.Clock(new DateTimeOffset(2026, 11, 20, 5, 0, 0, TimeSpan.Zero)));
        using var client = factory.CreateClient();
        var token = GenerateJwtFor(new NhanVien { Id = 202, TenDangNhap = "care1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var form = BuildCareActionForm(new CareAction { RequestId = Guid.NewGuid(), HanhDong = CareActionType.START });

        using var response = await client.PostAsync("/api/internal/booking-pets/10/care-actions", form);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CareActions_SequentialStartThenReadyWithPhoto_SucceedsAndKeepsBookingInProgress()
    {
        using var factory = CareTestSupport.Create(db => SeedCareScenario(db, petState: PetCareState.WAITING));
        using var client = factory.CreateClient();

        var token = GenerateJwtFor(new NhanVien { Id = 202, TenDangNhap = "care1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var startPayload = new CareAction
        {
            RequestId = Guid.NewGuid(),
            HanhDong = CareActionType.START
        };
        using var startForm = BuildCareActionForm(startPayload);
        var startResponse = await client.PostAsync("/api/internal/booking-pets/10/care-actions", startForm);

        startResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var startBody = await startResponse.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        startBody!.Success.Should().BeTrue();
        startBody.Data!.TrangThai.Should().Be("IN_PROGRESS");

        var readyPayload = new CareAction
        {
            RequestId = Guid.NewGuid(),
            HanhDong = CareActionType.READY,
            GhiChu = "Đã tắm sấy thơm tho"
        };
        using var readyForm = BuildCareActionForm(readyPayload, ValidPngBytes, "done.png");
        var readyResponse = await client.PostAsync("/api/internal/booking-pets/10/care-actions", readyForm);

        readyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var readyBody = await readyResponse.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        readyBody!.Success.Should().BeTrue();
        readyBody.Data!.TrangThai.Should().Be("READY");
        readyBody.Data.DichVu.Should().ContainSingle(d => d.TrangThai == "HOAN_TAT");
        readyBody.Data.NhatKy.Should().Contain(nk => nk.LoaiNhatKy == "CARE_READY" && nk.AnhUrl != null && nk.AnhUrl.StartsWith("https://photo.test/"));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
        var savedPet = await db.ChiTietDonSet
            .Include(c => c.DonDatLich)
            .Include(c => c.DichVuTrongDonChiTietDonItems)
            .FirstAsync(c => c.Id == 10);

        savedPet.TrangThaiChamSoc.Should().Be(PetCareState.READY);
        savedPet.ReadyAt.Should().NotBeNull();
        savedPet.DonDatLich.TrangThai.Should().Be(BookingState.IN_PROGRESS);
        savedPet.DichVuTrongDonChiTietDonItems.Should().OnlyContain(d => d.TrangThai == OrderServiceState.HOAN_TAT && d.HoanTatAt != null);
    }

    [Fact]
    public async Task CareActions_PauseAndResume_RequiresReasonOnPause_AndTransitionsProperly()
    {
        using var factory = CareTestSupport.Create(db => SeedCareScenario(db, petState: PetCareState.IN_PROGRESS));
        using var client = factory.CreateClient();

        var token = GenerateJwtFor(new NhanVien { Id = 202, TenDangNhap = "care1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var pauseWithoutReason = new CareAction
        {
            RequestId = Guid.NewGuid(),
            HanhDong = CareActionType.PAUSE
        };
        using var badPauseForm = BuildCareActionForm(pauseWithoutReason);
        var badPauseResponse = await client.PostAsync("/api/internal/booking-pets/10/care-actions", badPauseForm);
        badPauseResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var badPauseBody = await badPauseResponse.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        badPauseBody!.ErrorCode.Should().Be("VALIDATION_FAILED");

        var validPause = new CareAction
        {
            RequestId = Guid.NewGuid(),
            HanhDong = CareActionType.PAUSE,
            GhiChu = "Chờ khách xác nhận phụ phí gỡ rối"
        };
        using var pauseForm = BuildCareActionForm(validPause);
        var pauseResponse = await client.PostAsync("/api/internal/booking-pets/10/care-actions", pauseForm);
        pauseResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var pauseBody = await pauseResponse.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        pauseBody!.Data!.TrangThai.Should().Be("PAUSED");
        pauseBody.Data.NhatKy.Should().Contain(nk => nk.LoaiNhatKy == "CARE_PAUSE" && nk.GhiChu == "Chờ khách xác nhận phụ phí gỡ rối");

        var resumePayload = new CareAction
        {
            RequestId = Guid.NewGuid(),
            HanhDong = CareActionType.RESUME
        };
        using var resumeForm = BuildCareActionForm(resumePayload);
        var resumeResponse = await client.PostAsync("/api/internal/booking-pets/10/care-actions", resumeForm);
        resumeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var resumeBody = await resumeResponse.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        resumeBody!.Data!.TrangThai.Should().Be("IN_PROGRESS");
    }

    [Fact]
    public async Task CareActions_PausedDirectlyToReady_Returns409InvalidTransition()
    {
        using var factory = CareTestSupport.Create(db => SeedCareScenario(db, petState: PetCareState.PAUSED));
        using var client = factory.CreateClient();

        var token = GenerateJwtFor(new NhanVien { Id = 202, TenDangNhap = "care1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new CareAction
        {
            RequestId = Guid.NewGuid(),
            HanhDong = CareActionType.READY
        };
        using var form = BuildCareActionForm(payload, ValidPngBytes);
        var response = await client.PostAsync("/api/internal/booking-pets/10/care-actions", form);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body!.Success.Should().BeFalse();
        body.ErrorCode.Should().Be("INVALID_TRANSITION");
    }

    [Fact]
    public async Task CareActions_ReadyWithoutPhotoOrException_Returns409InvalidTransition()
    {
        using var factory = CareTestSupport.Create(db => SeedCareScenario(db, petState: PetCareState.IN_PROGRESS));
        using var client = factory.CreateClient();

        var token = GenerateJwtFor(new NhanVien { Id = 202, TenDangNhap = "care1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new CareAction
        {
            RequestId = Guid.NewGuid(),
            HanhDong = CareActionType.READY
        };
        using var form = BuildCareActionForm(payload);
        var response = await client.PostAsync("/api/internal/booking-pets/10/care-actions", form);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body!.ErrorCode.Should().Be("INVALID_TRANSITION");
    }

    [Fact]
    public async Task CareActions_ReadyWithValidPhotoException_SucceedsWithoutPhoto()
    {
        using var factory = CareTestSupport.Create(db => SeedCareScenario(
            db,
            petState: PetCareState.IN_PROGRESS,
            seedPhotoException: true));
        using var client = factory.CreateClient();

        var token = GenerateJwtFor(new NhanVien { Id = 202, TenDangNhap = "care1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new CareAction
        {
            RequestId = Guid.NewGuid(),
            HanhDong = CareActionType.READY,
            MaNgoaiLeAnh = 501
        };
        using var form = BuildCareActionForm(payload);
        var response = await client.PostAsync("/api/internal/booking-pets/10/care-actions", form);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body!.Data!.TrangThai.Should().Be("READY");
        body.Data.NhatKy.Should().Contain(nk => nk.LoaiNhatKy == "CARE_READY" && nk.AnhUrl == null && nk.GhiChu == "Bé hoảng sợ khi chụp ảnh");
    }

    [Fact]
    public async Task CareActions_ReadyWithInvalidPhotoException_Returns409InvalidTransition()
    {
        using var factory = CareTestSupport.Create(db => SeedCareScenario(db, petState: PetCareState.IN_PROGRESS));
        using var client = factory.CreateClient();

        var token = GenerateJwtFor(new NhanVien { Id = 202, TenDangNhap = "care1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new CareAction
        {
            RequestId = Guid.NewGuid(),
            HanhDong = CareActionType.READY,
            MaNgoaiLeAnh = 999
        };
        using var form = BuildCareActionForm(payload);
        var response = await client.PostAsync("/api/internal/booking-pets/10/care-actions", form);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body!.ErrorCode.Should().Be("INVALID_TRANSITION");
    }

    [Fact]
    public async Task CareActions_ReadyWithIncompleteComboComponents_Returns409InvalidTransition()
    {
        using var factory = CareTestSupport.Create(db => SeedCareScenario(
            db,
            petState: PetCareState.IN_PROGRESS,
            seedComboIncomplete: true));
        using var client = factory.CreateClient();

        var token = GenerateJwtFor(new NhanVien { Id = 202, TenDangNhap = "care1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new CareAction
        {
            RequestId = Guid.NewGuid(),
            HanhDong = CareActionType.READY
        };
        using var form = BuildCareActionForm(payload, ValidPngBytes);
        var response = await client.PostAsync("/api/internal/booking-pets/10/care-actions", form);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body!.ErrorCode.Should().Be("INVALID_TRANSITION");
    }

    [Fact]
    public async Task CareActions_WrongActorOrMissingShiftOrMissingPermission_Returns403Forbidden()
    {
        using var factory1 = CareTestSupport.Create(db => SeedCareScenario(db));
        using var client1 = factory1.CreateClient();
        var wrongActorToken = GenerateJwtFor(new NhanVien { Id = 203, TenDangNhap = "care2", VaiTro = Role.STAFF });
        client1.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", wrongActorToken);
        using var form1 = BuildCareActionForm(new CareAction { RequestId = Guid.NewGuid(), HanhDong = CareActionType.START });
        var res1 = await client1.PostAsync("/api/internal/booking-pets/10/care-actions", form1);
        res1.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var factory2 = CareTestSupport.Create(db => SeedCareScenario(db, assigneeHasApprovedShift: false));
        using var client2 = factory2.CreateClient();
        var staffToken = GenerateJwtFor(new NhanVien { Id = 202, TenDangNhap = "care1", VaiTro = Role.STAFF });
        client2.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        using var form2 = BuildCareActionForm(new CareAction { RequestId = Guid.NewGuid(), HanhDong = CareActionType.START });
        var res2 = await client2.PostAsync("/api/internal/booking-pets/10/care-actions", form2);
        res2.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var factory3 = CareTestSupport.Create(db => SeedCareScenario(db, grantCarePermission: false));
        using var client3 = factory3.CreateClient();
        client3.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        using var form3 = BuildCareActionForm(new CareAction { RequestId = Guid.NewGuid(), HanhDong = CareActionType.START });
        var res3 = await client3.PostAsync("/api/internal/booking-pets/10/care-actions", form3);
        res3.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CareActions_AdminOverrideActor_Succeeds()
    {
        using var factory = CareTestSupport.Create(db => SeedCareScenario(db, petState: PetCareState.WAITING));
        using var client = factory.CreateClient();

        var adminToken = GenerateJwtFor(new NhanVien { Id = 200, TenDangNhap = "admin", VaiTro = Role.ADMIN });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var payload = new CareAction
        {
            RequestId = Guid.NewGuid(),
            HanhDong = CareActionType.START,
            GhiChu = "Admin xử lý thay ca"
        };
        using var form = BuildCareActionForm(payload);
        var response = await client.PostAsync("/api/internal/booking-pets/10/care-actions", form);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body!.Data!.TrangThai.Should().Be("IN_PROGRESS");
    }

    [Fact]
    public async Task CareActions_MissingJwt_Returns401Unauthorized()
    {
        using var factory = CareTestSupport.Create(db => SeedCareScenario(db));
        using var client = factory.CreateClient();

        using var form = BuildCareActionForm(new CareAction { RequestId = Guid.NewGuid(), HanhDong = CareActionType.START });
        var response = await client.PostAsync("/api/internal/booking-pets/10/care-actions", form);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CareActions_PetNotFound_Returns404NotFound()
    {
        using var factory = CareTestSupport.Create(db => SeedCareScenario(db));
        using var client = factory.CreateClient();

        var token = GenerateJwtFor(new NhanVien { Id = 202, TenDangNhap = "care1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var form = BuildCareActionForm(new CareAction { RequestId = Guid.NewGuid(), HanhDong = CareActionType.START });
        var response = await client.PostAsync("/api/internal/booking-pets/9999/care-actions", form);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CareActions_DuplicateRequestId_SamePayloadReturns200_DifferentPayloadReturns409()
    {
        using var factory = CareTestSupport.Create(db => SeedCareScenario(db, petState: PetCareState.WAITING));
        using var client = factory.CreateClient();

        var token = GenerateJwtFor(new NhanVien { Id = 202, TenDangNhap = "care1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new CareAction
        {
            RequestId = Guid.NewGuid(),
            HanhDong = CareActionType.START
        };

        using var form1 = BuildCareActionForm(payload);
        var res1 = await client.PostAsync("/api/internal/booking-pets/10/care-actions", form1);
        res1.StatusCode.Should().Be(HttpStatusCode.OK);

        using var form2 = BuildCareActionForm(payload);
        var res2 = await client.PostAsync("/api/internal/booking-pets/10/care-actions", form2);
        res2.StatusCode.Should().Be(HttpStatusCode.OK);
        var body2 = await res2.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body2!.Data!.TrangThai.Should().Be("IN_PROGRESS");

        payload.HanhDong = CareActionType.PAUSE;
        payload.GhiChu = "Đổi hành động cùng requestId";
        using var form3 = BuildCareActionForm(payload);
        var res3 = await client.PostAsync("/api/internal/booking-pets/10/care-actions", form3);
        res3.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body3 = await res3.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body3!.ErrorCode.Should().Be("IDEMPOTENCY_CONFLICT");
    }

    [Fact]
    public async Task CareActions_UnsupportedFileSignature_Returns415UnsupportedFile()
    {
        using var factory = CareTestSupport.Create(db => SeedCareScenario(db, petState: PetCareState.IN_PROGRESS));
        using var client = factory.CreateClient();

        var token = GenerateJwtFor(new NhanVien { Id = 202, TenDangNhap = "care1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new CareAction
        {
            RequestId = Guid.NewGuid(),
            HanhDong = CareActionType.READY
        };
        var invalidBytes = Encoding.UTF8.GetBytes("not-an-image");
        using var form = BuildCareActionForm(payload, invalidBytes, "fake.txt", "text/plain");
        var response = await client.PostAsync("/api/internal/booking-pets/10/care-actions", form);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body!.ErrorCode.Should().Be("UNSUPPORTED_FILE");
    }
}
