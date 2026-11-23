using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PetSpa.Api.Data;
using PetSpa.Api.Services.Identity;
using PetSpa.Api.Services.Storage;
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
        bool assigneeHasApprovedShift = true,
        bool grantCarePermission = true)
    {
        var today = new DateOnly(2026, 11, 20);
        var now = CareTestSupport.Now;

        var permission = new Quyen { MaQuyen = "CHECKIN_PERFORM", TenQuyen = "CHECKIN_PERFORM" };
        db.QuyenSet.AddRange(permission, new Quyen { MaQuyen = "CARE_UPDATE", TenQuyen = "CARE_UPDATE" });

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

        if (grantCarePermission)
        {
            db.NhanVienQuyenSet.Add(new NhanVienQuyen
            {
                NhanVienId = careStaff.Id,
                MaQuyen = "CARE_UPDATE",
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

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 5)]
    public async Task CheckIn_RequiresActiveCareGrantAndCurrentShift(bool grantCarePermission, int utcHour)
    {
        using var factory = CareTestSupport.Create(db => SeedCheckinScenario(db, grantCarePermission: grantCarePermission),
            clock: new CareTestSupport.Clock(new DateTimeOffset(2026, 11, 20, utcHour, 0, 0, TimeSpan.Zero)));
        using var client = CreateAuthorizedClient(factory);
        using var form = BuildCheckinForm(CreateValidPayload(), ValidPngBytes);

        var response = await client.PostAsync("/api/internal/booking-pets/10/check-in", form);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body!.ErrorCode.Should().Be("ASSIGNMENT_UNAVAILABLE");
    }

    [Fact]
    public async Task CheckIn_ThuGonKeepsCommittedSizeAndPrice()
    {
        using var factory = CareTestSupport.Create(db =>
        {
            SeedCheckinScenario(db);
            var service = db.DichVuTrongDonSet.Single();
            service.SizeChot = null;
            service.GiaChot = null;
            db.BangGiaSizeSet.Single(price => price.SizeId == 1).DonGia = 999000m;
            db.HeSoCanNangSet.Single(band => band.MaSize == SizeBand.S).CanNangDen = 4m;
            db.HeSoCanNangSet.Single(band => band.MaSize == SizeBand.M).CanNangTu = 4m;
            db.DonDatLichSet.Single().ChinhSachSnapshot = """
                {"thuCung":[{"maChiTietDon":10,"sizeCamKet":"S","yeuCauTiemChung":false}]}
                """;
            db.SaveChanges();
        });
        using var client = CreateAuthorizedClient(factory);
        var request = CreateValidPayload();
        request.QuyetDinhGia = PriceDecision.THU_GON;
        request.NoiDungThuGon = "Giữ gói giá đã xác nhận";
        using var form = BuildCheckinForm(request, ValidPngBytes);

        var response = await client.PostAsync("/api/internal/booking-pets/10/check-in", form);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body!.Data!.SizeThucTe.Should().Be("M");
        body.Data.SizeChot.Should().Be("S");
        body.Data.DichVu.Should().ContainSingle().Which.GiaChot.Should().Be(150000m);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
        (await db.DonDatLichSet.SingleAsync()).TongTamTinh.Should().Be(150000m);
    }

    [Theory]
    [InlineData(true, false, HttpStatusCode.BadRequest)]
    [InlineData(false, true, HttpStatusCode.OK)]
    public async Task CheckIn_PerPetVaccinationSnapshotWinsOverChangedCatalog(
        bool requiredAtBooking, bool requiredNow, HttpStatusCode expectedStatus)
    {
        using var factory = CareTestSupport.Create(db =>
        {
            SeedCheckinScenario(db);
            db.DichVuSet.Single().YeuCauTiemChung = requiredNow;
            db.DonDatLichSet.Single().ChinhSachSnapshot = JsonSerializer.Serialize(new
            {
                thuCung = new[] { new { maChiTietDon = 10, yeuCauTiemChung = requiredAtBooking,
                    vaccinationObjectPath = "unverified-document.png" } }
            });
            db.SaveChanges();
        });
        using var client = CreateAuthorizedClient(factory);
        using var form = BuildCheckinForm(CreateValidPayload(), ValidPngBytes);

        var response = await client.PostAsync("/api/internal/booking-pets/10/check-in", form);

        response.StatusCode.Should().Be(expectedStatus);
        using var scope = factory.Services.CreateScope();
        var pet = await scope.ServiceProvider.GetRequiredService<PetSpaDbContext>().ChiTietDonSet.SingleAsync();
        pet.XacMinhTiemAt.Should().BeNull();
        pet.HoSoTiemChungId.Should().BeNull();
    }

    [Fact]
    public async Task CheckIn_ReplayAfterFurDeactivationReturnsSavedCodesAndSignsAfterCommit()
    {
        var storage = new CareTestSupport.Storage();
        using var factory = CareTestSupport.Create(db => SeedCheckinScenario(db), storage);
        using var client = CreateAuthorizedClient(factory);
        var request = CreateValidPayload();
        storage.BeforeRead = async (path, ct) =>
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
            (await db.NhatKyChamSocSet.SingleAsync(ct)).ObjectPath.Should().Be(path);
            (await db.AuditLogSet.SingleAsync(ct)).KetQua.Should().NotContain(path).And.NotContain("https://");
        };
        using var form = BuildCheckinForm(request, ValidPngBytes);
        var first = await client.PostAsync("/api/internal/booking-pets/10/check-in", form);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
            var config = await db.CauHinhHeThongSet.SingleAsync();
            config.GiaTri = "[]";
            await db.SaveChangesAsync();
        }

        using var retryForm = BuildCheckinForm(request, ValidPngBytes);
        var retry = await client.PostAsync("/api/internal/booking-pets/10/check-in", retryForm);

        retry.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await retry.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body!.Data!.TinhTrangLong.Should().Equal("BINH_THUONG", "ROI_NHE");
        body.Data.NhatKy.Single().AnhUrl.Should().StartWith("https://photo.test/");
        storage.Uploads.Should().ContainSingle();
        storage.Reads.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CheckIn_FailedUploadOrRevokedGrantLeavesNoCommittedCareChanges(bool failUpload)
    {
        var storage = new CareTestSupport.Storage();
        using var factory = CareTestSupport.Create(db => SeedCheckinScenario(db), storage);
        using var client = CreateAuthorizedClient(factory);
        storage.BeforeUpload = async ct =>
        {
            if (failUpload)
                throw new FileStorageException("Ảnh không tải được.", "STORAGE_UNAVAILABLE");

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
            db.NhanVienQuyenSet.Remove(await db.NhanVienQuyenSet.SingleAsync(q => q.MaQuyen == "CHECKIN_PERFORM", ct));
            await db.SaveChangesAsync(ct);
        };
        using var form = BuildCheckinForm(CreateValidPayload(), ValidPngBytes);

        var response = await client.PostAsync("/api/internal/booking-pets/10/check-in", form);

        response.StatusCode.Should().Be(failUpload ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Forbidden);
        using var saved = factory.Services.CreateScope();
        var db = saved.ServiceProvider.GetRequiredService<PetSpaDbContext>();
        (await db.ChiTietDonSet.SingleAsync()).TrangThaiChamSoc.Should().Be(PetCareState.NOT_ARRIVED);
        (await db.AuditLogSet.CountAsync()).Should().Be(0);
        (await db.NhatKyChamSocSet.CountAsync()).Should().Be(0);
        storage.Deletes.Should().Equal(storage.Uploads);
    }

    [Fact]
    public async Task CheckIn_WhenCancellationWinsDuringUploadRollsBackPhotoAndState()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        SeedCheckinScenario(db);
        db.QuyenSet.Add(new Quyen { MaQuyen = "BOOKING_APPROVE", TenQuyen = "BOOKING_APPROVE" });
        db.NhanVienQuyenSet.Add(new NhanVienQuyen { NhanVienId = 201, MaQuyen = "BOOKING_APPROVE", CapBoiId = 201 });
        var booking = await db.DonDatLichSet.SingleAsync();
        booking.BatDauDuKien = CareTestSupport.Now.AddHours(3);
        booking.KetThucDuKien = booking.BatDauDuKien.AddHours(1);
        var slot = await db.KhungGioDatLichSet.SingleAsync();
        slot.SlotDaDat = 1;
        slot.PhutDaGiu = 60;
        await db.SaveChangesAsync();

        var storage = new CareTestSupport.Storage();
        var uploading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.BeforeUpload = async ct =>
        {
            uploading.TrySetResult();
            await resume.Task.WaitAsync(ct);
        };
        await using var postgres = new PostgresWebApplicationFactory(database.ConnectionString);
        await using var factory = CareTestSupport.Configure(postgres, storage);
        using var client = CreateAuthorizedClient(factory);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var form = BuildCheckinForm(CreateValidPayload(), ValidPngBytes);

        await using var blocker = new NpgsqlConnection(database.ConnectionString);
        await blocker.OpenAsync(timeout.Token);
        await using var transaction = await blocker.BeginTransactionAsync(timeout.Token);
        await using var hold = new NpgsqlCommand("SELECT id FROM don_dat_lich WHERE id = 1 FOR UPDATE", blocker, transaction);
        await hold.ExecuteScalarAsync(timeout.Token);

        var checkIn = client.PostAsync("/api/internal/booking-pets/10/check-in", form, timeout.Token);
        Task<HttpResponseMessage> cancel;
        try
        {
            await uploading.Task.WaitAsync(timeout.Token);
            cancel = client.PostAsJsonAsync("/api/internal/bookings/1/cancel",
                new { requestId = Guid.NewGuid(), loaiHuy = "STAFF_CANCELLED", lyDo = "Khách yêu cầu hủy" }, timeout.Token);
            await CareTestSupport.WaitForLockWaitersAsync(
                database.ConnectionString, "%don_dat_lich%FOR UPDATE%", 1, timeout.Token);
            resume.TrySetResult();
            await CareTestSupport.WaitForLockWaitersAsync(
                database.ConnectionString, "%don_dat_lich%FOR UPDATE%", 2, timeout.Token);
        }
        finally
        {
            resume.TrySetResult();
            await transaction.CommitAsync();
        }

        using var cancellation = await cancel;
        cancellation.StatusCode.Should().Be(HttpStatusCode.OK);
        using var response = await checkIn;
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await db.DonDatLichSet.AsNoTracking().SingleAsync()).TrangThai.Should().Be(BookingState.CANCELLED);
        (await db.ChiTietDonSet.AsNoTracking().SingleAsync()).CheckInAt.Should().BeNull();
        (await db.NhatKyChamSocSet.CountAsync()).Should().Be(0);
        storage.Deletes.Should().ContainSingle().Which.Should().Be(storage.Uploads.Single());
    }

    [Fact]
    public async Task CheckIn_ConcurrentCommitDuringPreflightReturnsCommittedReplay()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        SeedCheckinScenario(db);
        var storage = new CareTestSupport.Storage();
        var gate = new PreflightGate();
        await using var postgres = new PostgresWebApplicationFactory(database.ConnectionString);
        await using var host = CareTestSupport.Configure(postgres, storage);
        await using var factory = host.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddDbContext<PetSpaDbContext>((_, options) => options.AddInterceptors(gate))));
        using var client = CreateAuthorizedClient(factory);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var payload = CreateValidPayload();
        using var pendingForm = BuildCheckinForm(payload, ValidPngBytes);
        var pending = client.PostAsync("/api/internal/booking-pets/10/check-in", pendingForm, timeout.Token);
        try
        {
            // The pending request has already missed the first audit lookup.
            await gate.BeforeRead.Task.WaitAsync(timeout.Token);
            using var winningForm = BuildCheckinForm(payload, ValidPngBytes);
            using var winner = await client.PostAsync("/api/internal/booking-pets/10/check-in", winningForm, timeout.Token);
            winner.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            gate.Resume.TrySetResult();
        }

        using var replay = await pending;
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayBody = await replay.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>(timeout.Token);
        replayBody!.Data!.TrangThai.Should().Be("WAITING");
        storage.Uploads.Should().ContainSingle();
        (await db.AuditLogSet.CountAsync(timeout.Token)).Should().Be(1);
    }

    private sealed class PreflightGate : DbCommandInterceptor
    {
        private int _armed = 1;
        public TaskCompletionSource BeforeRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM cau_hinh_he_thong", StringComparison.Ordinal) &&
                Interlocked.Exchange(ref _armed, 0) == 1)
            {
                BeforeRead.TrySetResult();
                await Resume.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    [Fact]
    public async Task CheckIn_ConcurrentRequestKeyAcrossBookingsRollsBackLosingPetAndPhoto()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        SeedCheckinScenario(db);
        db.DonDatLichSet.Add(new DonDatLich
        {
            Id = 2,
            RequestId = Guid.NewGuid(),
            Fingerprint = "second",
            TrackingCode = "TRK-8X2B",
            TrangThai = BookingState.CONFIRMED,
            TenNguoiDat = "Nguyễn Văn A",
            SoDienThoaiSnapshot = "0912345678",
            EmailSnapshot = "khach@example.com",
            BatDauDuKien = CareTestSupport.Now,
            KetThucDuKien = CareTestSupport.Now.AddHours(1),
            MocCuoiCa = CareTestSupport.Now.AddHours(4),
            ChinhSachSnapshot = "{}",
            KhachHangId = 1,
            KhungGioId = 1
        });
        db.ThuCungSet.Add(new ThuCung { Id = 2, KhachHangId = 1, TenThuCung = "Bông", LoaiPet = PetType.MEO, CanNang = 4m });
        db.ChiTietDonSet.Add(new ChiTietDon
        {
            Id = 20,
            DonDatLichId = 2,
            ThuCungId = 2,
            TenPetSnapshot = "Bông",
            CanNangKhaiBao = 4m,
            TrangThaiChamSoc = PetCareState.NOT_ARRIVED
        });
        await db.SaveChangesAsync();

        var storage = new CareTestSupport.Storage();
        await using var postgres = new PostgresWebApplicationFactory(database.ConnectionString);
        await using var factory = CareTestSupport.Configure(postgres, storage);
        using var client = CreateAuthorizedClient(factory);
        await using var blocker = new NpgsqlConnection(database.ConnectionString);
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await using var hold = new NpgsqlCommand("LOCK TABLE audit_log IN SHARE MODE", blocker, transaction);
        await hold.ExecuteNonQueryAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var request = CreateValidPayload();
        using var form1 = BuildCheckinForm(request, ValidPngBytes);
        using var form2 = BuildCheckinForm(request, ValidPngBytes);
        var first = client.PostAsync("/api/internal/booking-pets/10/check-in", form1, timeout.Token);
        var second = client.PostAsync("/api/internal/booking-pets/20/check-in", form2, timeout.Token);
        try
        {
            await CareTestSupport.WaitForLockWaitersAsync(database.ConnectionString, "%INSERT INTO audit_log%", 2, timeout.Token);
        }
        finally
        {
            await transaction.CommitAsync();
        }

        using var one = await first;
        using var two = await second;
        new[] { one.StatusCode, two.StatusCode }.Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
        var loser = one.StatusCode == HttpStatusCode.Conflict ? one : two;
        var failed = await loser.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        failed!.ErrorCode.Should().Be("IDEMPOTENCY_CONFLICT");
        var saved = await db.ChiTietDonSet.AsNoTracking().ToListAsync();
        saved.Should().ContainSingle(pet => pet.TrangThaiChamSoc == PetCareState.WAITING);
        saved.Should().ContainSingle(pet => pet.TrangThaiChamSoc == PetCareState.NOT_ARRIVED);
        (await db.AuditLogSet.CountAsync()).Should().Be(1);
        storage.Uploads.Should().HaveCount(2);
        storage.Deletes.Should().ContainSingle();
        (await db.NhatKyChamSocSet.SingleAsync()).ObjectPath.Should().Be(storage.Uploads.Except(storage.Deletes).Single());
    }

    private static HttpClient CreateAuthorizedClient(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var token = GenerateJwtFor(new NhanVien { Id = 201, TenDangNhap = "reception1", VaiTro = Role.STAFF });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task CheckIn_ValidRequest_TransitionsPetToWaitingAndBookingToInProgress()
    {
        using var factory = CareTestSupport.Create(db => SeedCheckinScenario(db));
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
        using var factory = CareTestSupport.Create(db => SeedCheckinScenario(db, bookingState: BookingState.PENDING));
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
        using var factory = CareTestSupport.Create(db => SeedCheckinScenario(db));
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
        using var factory = CareTestSupport.Create(db => SeedCheckinScenario(db));
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
        using var factory = CareTestSupport.Create(db => SeedCheckinScenario(db));
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
        using var factory = CareTestSupport.Create(db => SeedCheckinScenario(db, assigneeHasApprovedShift: false));
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
        using var factory = CareTestSupport.Create(db => SeedCheckinScenario(db));
        using var client = factory.CreateClient();

        using var form = BuildCheckinForm(CreateValidPayload(), ValidPngBytes);
        var response = await client.PostAsync("/api/internal/booking-pets/10/check-in", form);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CheckIn_WithoutPermission_Returns403Forbidden()
    {
        using var factory = CareTestSupport.Create(db => SeedCheckinScenario(db, grantCheckinPermission: false));
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
        using var factory = CareTestSupport.Create(db => SeedCheckinScenario(db));
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
        using var factory = CareTestSupport.Create(db => SeedCheckinScenario(db));
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

    [Fact]
    public async Task CheckIn_ComboSizeChange_UsesComboPricingFallbackAndUpdatesTongTamTinh()
    {
        using var factory = CareTestSupport.Create(db =>
        {
            SeedCheckinScenario(db);
            var oldLine = db.DichVuTrongDonSet.Single();
            db.DichVuTrongDonSet.Remove(oldLine);

            var partService = new DichVu
            {
                Id = 2,
                TenDichVu = "Cắt tỉa lông",
                LoaiDichVu = "SPA",
                ThoiLuongPhut = 45,
                IsActive = true,
                YeuCauTiemChung = false
            };
            db.DichVuSet.Add(partService);

            var combo = new Combo
            {
                Id = 1,
                TenCombo = "Combo Tắm Cắt Toàn Diện",
                IsActive = true
            };
            db.ComboSet.Add(combo);
            db.BangGiaSizeSet.AddRange(
                new BangGiaSize { ComboId = combo.Id, SizeId = 1, DonGia = 250000m },
                new BangGiaSize { ComboId = combo.Id, SizeId = 2, DonGia = 350000m });

            var comboLine = new DichVuTrongDon
            {
                Id = 101,
                RequestId = Guid.NewGuid(),
                ChiTietDonId = 10,
                ComboId = combo.Id,
                KhungGioId = 1,
                TenSnapshot = combo.TenCombo,
                BangGiaSizeSnapshot = "{}",
                ThoiLuongSnapshot = 105,
                TrangThai = OrderServiceState.CHO_THUC_HIEN,
                SizeChot = SizeBand.S,
                GiaChot = 250000m
            };
            db.DichVuTrongDonSet.Add(comboLine);
            db.ThanhPhanDichVuTrongDonSet.AddRange(
                new ThanhPhanDichVuTrongDon
                {
                    Id = 1001,
                    DichVuTrongDonId = comboLine.Id,
                    DichVuId = 1,
                    TenSnapshot = "Tắm sấy vệ sinh",
                    GiaLeSizeSnapshot = "{\"S\":150000,\"M\":200000}",
                    ThoiLuongSnapshot = 60,
                    TrangThai = OrderServiceState.CHO_THUC_HIEN
                },
                new ThanhPhanDichVuTrongDon
                {
                    Id = 1002,
                    DichVuTrongDonId = comboLine.Id,
                    DichVuId = partService.Id,
                    TenSnapshot = partService.TenDichVu,
                    GiaLeSizeSnapshot = "{\"S\":150000,\"M\":200000}",
                    ThoiLuongSnapshot = 45,
                    TrangThai = OrderServiceState.CHO_THUC_HIEN
                });

            db.DonDatLichSet.Single().TongTamTinh = 250000m;
            db.SaveChanges();
        });
        using var client = CreateAuthorizedClient(factory);
        using var form = BuildCheckinForm(CreateValidPayload(), ValidPngBytes);

        var response = await client.PostAsync("/api/internal/booking-pets/10/check-in", form);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body!.Data!.SizeChot.Should().Be("M");
        var line = body.Data.DichVu.Should().ContainSingle().Subject;
        line.MaCombo.Should().Be(1);
        line.SizeChot.Should().Be("M");
        line.GiaChot.Should().Be(350000m);
        line.ThanhPhan.Should().HaveCount(2).And.OnlyContain(c => c.TrangThai == "CHO_THUC_HIEN");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
        (await db.DonDatLichSet.SingleAsync()).TongTamTinh.Should().Be(350000m);
    }

    [Fact]
    public async Task CheckIn_ComboComponentRequiresVaccination_WithoutProofReturns400ValidationFailed()
    {
        using var factory = CareTestSupport.Create(db =>
        {
            SeedCheckinScenario(db);
            var oldLine = db.DichVuTrongDonSet.Single();
            db.DichVuTrongDonSet.Remove(oldLine);

            var vaccineService = new DichVu
            {
                Id = 2,
                TenDichVu = "Cắt tỉa yêu cầu tiêm",
                LoaiDichVu = "SPA",
                ThoiLuongPhut = 45,
                IsActive = true,
                YeuCauTiemChung = true
            };
            db.DichVuSet.Add(vaccineService);

            var combo = new Combo
            {
                Id = 1,
                TenCombo = "Combo Bảo Vệ",
                IsActive = true
            };
            db.ComboSet.Add(combo);

            var comboLine = new DichVuTrongDon
            {
                Id = 101,
                RequestId = Guid.NewGuid(),
                ChiTietDonId = 10,
                ComboId = combo.Id,
                KhungGioId = 1,
                TenSnapshot = combo.TenCombo,
                BangGiaSizeSnapshot = "{\"S\":250000,\"M\":350000}",
                ThoiLuongSnapshot = 60,
                TrangThai = OrderServiceState.CHO_THUC_HIEN,
                SizeChot = SizeBand.S,
                GiaChot = 250000m
            };
            db.DichVuTrongDonSet.Add(comboLine);
            db.ThanhPhanDichVuTrongDonSet.Add(new ThanhPhanDichVuTrongDon
            {
                Id = 1001,
                DichVuTrongDonId = comboLine.Id,
                DichVuId = vaccineService.Id,
                TenSnapshot = vaccineService.TenDichVu,
                GiaLeSizeSnapshot = "{\"S\":150000,\"M\":200000}",
                ThoiLuongSnapshot = 45,
                TrangThai = OrderServiceState.CHO_THUC_HIEN
            });
            db.SaveChanges();
        });
        using var client = CreateAuthorizedClient(factory);
        using var form = BuildCheckinForm(CreateValidPayload(), ValidPngBytes);

        var response = await client.PostAsync("/api/internal/booking-pets/10/check-in", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body!.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task CheckIn_HotelOnly_SetsCommittedRoomRateAndHcmNightsWithoutDoubleCounting()
    {
        using var factory = CareTestSupport.Create(db =>
        {
            SeedCheckinScenario(db);
            db.DichVuTrongDonSet.Remove(db.DichVuTrongDonSet.Single());

            var roomType = new LoaiPhong
            {
                Id = 1,
                TenPhong = "Phòng VIP Chó",
                GiaMoiDem = 300000m,
                SoLuongPhong = 5,
                IsActive = true
            };
            db.LoaiPhongSet.Add(roomType);

            var booking = db.DonDatLichSet.Single();
            // 2026-11-20 18:00 UTC = 2026-11-21 01:00 HCM -> 2026-11-22 18:00 UTC = 2026-11-23 01:00 HCM (2 nights in HCM)
            // If UTC .Date were wrongly used (Nov 20 to Nov 22 -> also 2), test 16:00 UTC vs 18:00 UTC:
            // Start: 2026-11-20 18:00 UTC (Nov 21 HCM), End: 2026-11-22 15:00 UTC (Nov 22 HCM) => 1 night in HCM vs 2 nights in UTC!
            booking.BatDauDuKien = new DateTimeOffset(2026, 11, 20, 18, 0, 0, TimeSpan.Zero);
            booking.KetThucDuKien = new DateTimeOffset(2026, 11, 22, 15, 0, 0, TimeSpan.Zero);
            booking.TongTamTinh = 280000m;

            var pet = db.ChiTietDonSet.Single();
            pet.LoaiPhongId = roomType.Id;
            pet.GiaPhongCamKet = 280000m;
            db.SaveChanges();
        });
        using var client = CreateAuthorizedClient(factory);
        using var form = BuildCheckinForm(CreateValidPayload(), ValidPngBytes);

        var response = await client.PostAsync("/api/internal/booking-pets/10/check-in", form);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body!.Data!.MaLoaiPhong.Should().Be(1);
        body.Data.DichVu.Should().BeEmpty();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
        var savedPet = await db.ChiTietDonSet.SingleAsync();
        savedPet.GiaPhongChot.Should().Be(280000m);
        savedPet.SoDemChot.Should().Be(1);
        (await db.DonDatLichSet.SingleAsync()).TongTamTinh.Should().Be(280000m);
    }

    [Fact]
    public async Task CheckIn_HotelAndSpa_UpdatesSpaDeltaWithoutDoublingRoomRate()
    {
        using var factory = CareTestSupport.Create(db =>
        {
            SeedCheckinScenario(db);
            var roomType = new LoaiPhong
            {
                Id = 1,
                TenPhong = "Phòng Tiêu Chuẩn",
                GiaMoiDem = 200000m,
                SoLuongPhong = 5,
                IsActive = true
            };
            db.LoaiPhongSet.Add(roomType);

            var booking = db.DonDatLichSet.Single();
            booking.BatDauDuKien = new DateTimeOffset(2026, 11, 20, 2, 0, 0, TimeSpan.Zero);
            booking.KetThucDuKien = new DateTimeOffset(2026, 11, 22, 2, 0, 0, TimeSpan.Zero);
            // Spa S (150k) + Hotel 2 nights (2 * 200k = 400k) = 550k
            booking.TongTamTinh = 550000m;

            var pet = db.ChiTietDonSet.Single();
            pet.LoaiPhongId = roomType.Id;
            pet.GiaPhongCamKet = 200000m;
            pet.SoDemChot = 2;
            db.SaveChanges();
        });
        using var client = CreateAuthorizedClient(factory);
        using var form = BuildCheckinForm(CreateValidPayload(), ValidPngBytes);

        var response = await client.PostAsync("/api/internal/booking-pets/10/check-in", form);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
        var savedPet = await db.ChiTietDonSet.SingleAsync();
        savedPet.GiaPhongChot.Should().Be(200000m);
        savedPet.SoDemChot.Should().Be(2);
        // Spa M (200k) - Spa S (150k) = +50k => 600k
        (await db.DonDatLichSet.SingleAsync()).TongTamTinh.Should().Be(600000m);
    }
}
