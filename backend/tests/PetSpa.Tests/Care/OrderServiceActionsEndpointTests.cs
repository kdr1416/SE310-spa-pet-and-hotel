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
using PetSpa.Core.DTOs.Billing;
using PetSpa.Core.DTOs.Booking;
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

public class OrderServiceActionsEndpointTests
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

    private static MultipartFormDataContent BuildCareActionForm(CareAction payload, byte[]? photoBytes = null)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json"), "payload");

        if (photoBytes is not null)
        {
            var fileContent = new ByteArrayContent(photoBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(fileContent, "anhThanhPham", "result.png");
        }

        return form;
    }

    private static void SeedScenario(
        PetSpaDbContext db,
        bool grantAdminCancelPermission = true,
        bool seedActiveInvoice = false,
        bool extraInSeparateSlot = false,
        bool extraHasCompletedComponent = false,
        OrderServiceState extraInitialState = OrderServiceState.CHO_THUC_HIEN)
    {
        var today = new DateOnly(2026, 11, 20);
        var now = CareTestSupport.Now;

        db.QuyenSet.AddRange(
            new Quyen { MaQuyen = "CARE_UPDATE", TenQuyen = "CARE_UPDATE" },
            new Quyen { MaQuyen = "PET_CANCEL_LINE", TenQuyen = "PET_CANCEL_LINE" });

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

        db.NhanVienQuyenSet.AddRange(
            new NhanVienQuyen { NhanVienId = admin.Id, MaQuyen = "CARE_UPDATE", CapBoiId = admin.Id, CapAt = now },
            new NhanVienQuyen { NhanVienId = careStaff.Id, MaQuyen = "CARE_UPDATE", CapBoiId = admin.Id, CapAt = now },
            new NhanVienQuyen { NhanVienId = otherStaff.Id, MaQuyen = "CARE_UPDATE", CapBoiId = admin.Id, CapAt = now });

        if (grantAdminCancelPermission)
        {
            db.NhanVienQuyenSet.Add(new NhanVienQuyen
            {
                NhanVienId = admin.Id,
                MaQuyen = "PET_CANCEL_LINE",
                CapBoiId = admin.Id,
                CapAt = now
            });
        }

        var shift1 = new CaLamViec
        {
            Id = 1,
            TenCa = "Ca Sáng",
            GioBatDau = new TimeOnly(8, 0),
            GioKetThuc = new TimeOnly(12, 0),
            SucChuaPet = 10,
            TranPhut = 240,
            IsActive = true
        };
        var shift2 = new CaLamViec
        {
            Id = 2,
            TenCa = "Ca Chiều",
            GioBatDau = new TimeOnly(13, 0),
            GioKetThuc = new TimeOnly(17, 0),
            SucChuaPet = 10,
            TranPhut = 240,
            IsActive = true
        };
        db.CaLamViecSet.AddRange(shift1, shift2);

        var slot1 = new KhungGioDatLich
        {
            Id = 1,
            Ngay = today,
            CaLamViecId = shift1.Id,
            MoNhanKhach = true,
            TongSlot = 10,
            SlotDaDat = 1,
            TranPhut = 240,
            PhutDaGiu = extraInSeparateSlot ? 90 : 120
        };
        var slot2 = new KhungGioDatLich
        {
            Id = 2,
            Ngay = today,
            CaLamViecId = shift2.Id,
            MoNhanKhach = true,
            TongSlot = 10,
            SlotDaDat = extraInSeparateSlot ? 1 : 0,
            TranPhut = 240,
            PhutDaGiu = extraInSeparateSlot ? 30 : 0
        };
        db.KhungGioDatLichSet.AddRange(slot1, slot2);

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

        db.DangKyCaSet.Add(new DangKyCa
        {
            Id = 1,
            Ngay = today,
            CaLamViecId = shift1.Id,
            DotId = window.Id,
            NhanVienId = careStaff.Id,
            TrangThai = ShiftRegistrationState.APPROVED,
            DuyetBoiId = admin.Id,
            DuyetAt = now
        });

        var svc1 = new DichVu { Id = 1, TenDichVu = "Tắm vệ sinh", LoaiDichVu = "SPA", ThoiLuongPhut = 45, IsActive = true };
        var svc2 = new DichVu { Id = 2, TenDichVu = "Cắt tỉa lông", LoaiDichVu = "SPA", ThoiLuongPhut = 45, IsActive = true };
        var svcExtra = new DichVu { Id = 3, TenDichVu = "Gỡ rối lông", LoaiDichVu = "SPA", ThoiLuongPhut = 30, IsActive = true };
        db.DichVuSet.AddRange(svc1, svc2, svcExtra);

        var combo = new Combo { Id = 1, TenCombo = "Combo Tắm Cắt", IsActive = true };
        db.ComboSet.Add(combo);

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
            KetThucDuKien = now.AddHours(2),
            MocCuoiCa = now.AddHours(4),
            TongTamTinh = 500000m,
            ChinhSachSnapshot = "{}",
            KhachHangId = customer.Id,
            KhungGioId = slot1.Id
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
            TrangThaiChamSoc = PetCareState.IN_PROGRESS
        };
        db.ChiTietDonSet.Add(chiTietDon);

        var comboLine = new DichVuTrongDon
        {
            Id = 100,
            RequestId = Guid.NewGuid(),
            ChiTietDonId = chiTietDon.Id,
            ComboId = combo.Id,
            KhungGioId = slot1.Id,
            LaPhatSinh = false,
            TenSnapshot = combo.TenCombo,
            BangGiaSizeSnapshot = "{}",
            ThoiLuongSnapshot = 90,
            TrangThai = OrderServiceState.CHO_THUC_HIEN,
            SizeChot = SizeBand.M,
            GiaChot = 350000m
        };

        var extraLine = new DichVuTrongDon
        {
            Id = 101,
            RequestId = Guid.NewGuid(),
            ChiTietDonId = chiTietDon.Id,
            DichVuId = extraHasCompletedComponent ? null : svcExtra.Id,
            ComboId = extraHasCompletedComponent ? combo.Id : null,
            KhungGioId = extraInSeparateSlot ? slot2.Id : slot1.Id,
            LaPhatSinh = true,
            TenSnapshot = extraHasCompletedComponent ? "Combo Phát Sinh" : svcExtra.TenDichVu,
            BangGiaSizeSnapshot = "{}",
            ThoiLuongSnapshot = 30,
            TrangThai = extraInitialState,
            SizeChot = SizeBand.M,
            GiaChot = 150000m,
            LyDo = "Khách yêu cầu thêm"
        };

        db.DichVuTrongDonSet.AddRange(comboLine, extraLine);

        db.ThanhPhanDichVuTrongDonSet.AddRange(
            new ThanhPhanDichVuTrongDon
            {
                Id = 1001,
                DichVuTrongDonId = comboLine.Id,
                DichVuId = svc1.Id,
                TenSnapshot = svc1.TenDichVu,
                GiaLeSizeSnapshot = "{}",
                ThoiLuongSnapshot = 45,
                TrangThai = OrderServiceState.CHO_THUC_HIEN
            },
            new ThanhPhanDichVuTrongDon
            {
                Id = 1002,
                DichVuTrongDonId = comboLine.Id,
                DichVuId = svc2.Id,
                TenSnapshot = svc2.TenDichVu,
                GiaLeSizeSnapshot = "{}",
                ThoiLuongSnapshot = 45,
                TrangThai = OrderServiceState.CHO_THUC_HIEN
            });

        if (extraHasCompletedComponent)
        {
            db.ThanhPhanDichVuTrongDonSet.Add(new ThanhPhanDichVuTrongDon
            {
                Id = 1003,
                DichVuTrongDonId = extraLine.Id,
                DichVuId = svc1.Id,
                TenSnapshot = svc1.TenDichVu,
                GiaLeSizeSnapshot = "{}",
                ThoiLuongSnapshot = 30,
                TrangThai = OrderServiceState.HOAN_TAT,
                HoanTatAt = now.AddMinutes(-5)
            });
        }

        if (seedActiveInvoice)
        {
            db.HoaDonSet.Add(new HoaDon
            {
                Id = 50,
                MaHoaDon = "INV-00050",
                DonDatLichId = booking.Id,
                TaoBoiId = admin.Id,
                TrangThai = InvoiceState.UNPAID,
                TongTien = 500000m,
                CocKhauTru = 0m,
                ConPhaiTra = 500000m
            });
        }

        db.SaveChanges();
    }

    [Fact]
    public async Task E61_CompleteComboComponents_ProgressesAndUnlocksReadyOnlyWhenAllComplete()
    {
        await using var factory = CareTestSupport.Create(db => SeedScenario(db));
        using var client = factory.CreateClient();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
            var careStaff = await db.NhanVienSet.SingleAsync(x => x.Id == 202);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(careStaff));
        }

        // Complete extra service first so only combo blocks READY
        var completeExtra = await client.PostAsJsonAsync(
            "/api/internal/extra-services/101/actions",
            new { requestId = Guid.NewGuid(), hanhDong = "COMPLETE" });
        completeExtra.StatusCode.Should().Be(HttpStatusCode.OK);

        // Complete 1/2 combo component
        var res1 = await client.PostAsJsonAsync(
            "/api/internal/order-services/100/components/1001/complete",
            new { requestId = Guid.NewGuid() });
        res1.StatusCode.Should().Be(HttpStatusCode.OK);
        var body1 = await res1.Content.ReadFromJsonAsync<ApiResponse<OrderService>>(JsonOptions);
        body1!.Data!.TrangThai.Should().Be("CHO_THUC_HIEN");
        body1.Data.ThanhPhan.Single(c => c.MaThanhPhan == 1001).TrangThai.Should().Be("HOAN_TAT");
        body1.Data.ThanhPhan.Single(c => c.MaThanhPhan == 1001).HoanTatAt.Should().NotBeNull();
        body1.Data.ThanhPhan.Single(c => c.MaThanhPhan == 1002).TrangThai.Should().Be("CHO_THUC_HIEN");

        // E17 READY should be blocked (409) while component 2 is still CHO_THUC_HIEN
        var readyBlocked = await client.PostAsync(
            "/api/internal/booking-pets/10/care-actions",
            BuildCareActionForm(new CareAction { RequestId = Guid.NewGuid(), HanhDong = CareActionType.READY }, ValidPngBytes));
        readyBlocked.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Complete 2/2 combo component -> parent combo becomes HOAN_TAT
        var res2 = await client.PostAsJsonAsync(
            "/api/internal/order-services/100/components/1002/complete",
            new { requestId = Guid.NewGuid() });
        res2.StatusCode.Should().Be(HttpStatusCode.OK);
        var body2 = await res2.Content.ReadFromJsonAsync<ApiResponse<OrderService>>(JsonOptions);
        body2!.Data!.TrangThai.Should().Be("HOAN_TAT");
        body2.Data.ThanhPhan.Should().OnlyContain(c => c.TrangThai == "HOAN_TAT" && c.HoanTatAt != null);

        // Now E17 READY succeeds
        var readyOk = await client.PostAsync(
            "/api/internal/booking-pets/10/care-actions",
            BuildCareActionForm(new CareAction { RequestId = Guid.NewGuid(), HanhDong = CareActionType.READY }, ValidPngBytes));
        readyOk.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task E61_AuthorizationAdminOverrideAndIdempotency_EnforcedCorrectly()
    {
        await using var factory = CareTestSupport.Create(db => SeedScenario(db));
        using var client = factory.CreateClient();

        NhanVien otherStaff;
        NhanVien admin;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
            otherStaff = await db.NhanVienSet.SingleAsync(x => x.Id == 203);
            admin = await db.NhanVienSet.SingleAsync(x => x.Id == 200);
        }

        // Wrong KTV -> 403
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(otherStaff));
        var forbidden = await client.PostAsJsonAsync(
            "/api/internal/order-services/100/components/1001/complete",
            new { requestId = Guid.NewGuid() });
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Admin override -> 200 + idempotency replay & conflict
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(admin));
        var reqId = Guid.NewGuid();
        var ok = await client.PostAsJsonAsync(
            "/api/internal/order-services/100/components/1001/complete",
            new { requestId = reqId });
        ok.StatusCode.Should().Be(HttpStatusCode.OK);

        var replay = await client.PostAsJsonAsync(
            "/api/internal/order-services/100/components/1001/complete",
            new { requestId = reqId });
        replay.StatusCode.Should().Be(HttpStatusCode.OK);

        var conflict = await client.PostAsJsonAsync(
            "/api/internal/order-services/100/components/1002/complete",
            new { requestId = reqId });
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var conflictBody = await conflict.Content.ReadFromJsonAsync<ApiResponse<OrderService>>(JsonOptions);
        conflictBody!.ErrorCode.Should().Be("IDEMPOTENCY_CONFLICT");

        // Validation 400 & NotFound 404
        var badGuid = await client.PostAsJsonAsync(
            "/api/internal/order-services/100/components/1002/complete",
            new { requestId = Guid.Empty });
        badGuid.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var notFound = await client.PostAsJsonAsync(
            "/api/internal/order-services/100/components/9999/complete",
            new { requestId = Guid.NewGuid() });
        notFound.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task E21_CompleteExtraService_RespectsAssignmentAndUnblocksReady()
    {
        await using var factory = CareTestSupport.Create(db => SeedScenario(db));
        using var client = factory.CreateClient();

        NhanVien careStaff;
        NhanVien otherStaff;
        NhanVien admin;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
            careStaff = await db.NhanVienSet.SingleAsync(x => x.Id == 202);
            otherStaff = await db.NhanVienSet.SingleAsync(x => x.Id == 203);
            admin = await db.NhanVienSet.SingleAsync(x => x.Id == 200);
        }

        // Wrong KTV -> 403
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(otherStaff));
        var forbidden = await client.PostAsJsonAsync(
            "/api/internal/extra-services/101/actions",
            new { requestId = Guid.NewGuid(), hanhDong = "COMPLETE" });
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Complete combo components first as admin override
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(admin));
        (await client.PostAsJsonAsync("/api/internal/order-services/100/components/1001/complete", new { requestId = Guid.NewGuid() }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/api/internal/order-services/100/components/1002/complete", new { requestId = Guid.NewGuid() }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // While extra service is still CHO_THUC_HIEN, E17 READY is blocked 409
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(careStaff));
        var readyBlocked = await client.PostAsync(
            "/api/internal/booking-pets/10/care-actions",
            BuildCareActionForm(new CareAction { RequestId = Guid.NewGuid(), HanhDong = CareActionType.READY }, ValidPngBytes));
        readyBlocked.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Assigned KTV completes extra service -> 200
        var completeOk = await client.PostAsJsonAsync(
            "/api/internal/extra-services/101/actions",
            new { requestId = Guid.NewGuid(), hanhDong = "COMPLETE" });
        completeOk.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await completeOk.Content.ReadFromJsonAsync<ApiResponse<OrderService>>(JsonOptions);
        body!.Data!.TrangThai.Should().Be("HOAN_TAT");

        // Now E17 READY succeeds
        var readyOk = await client.PostAsync(
            "/api/internal/booking-pets/10/care-actions",
            BuildCareActionForm(new CareAction { RequestId = Guid.NewGuid(), HanhDong = CareActionType.READY }, ValidPngBytes));
        readyOk.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task E21_CancelExtraService_SameSlot_KeepsSlotDaDatAndRefundsMinutesAndTotal()
    {
        await using var factory = CareTestSupport.Create(db => SeedScenario(db, extraInSeparateSlot: false));
        using var client = factory.CreateClient();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
            var admin = await db.NhanVienSet.SingleAsync(x => x.Id == 200);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(admin));
        }

        var response = await client.PostAsJsonAsync(
            "/api/internal/extra-services/101/actions",
            new { requestId = Guid.NewGuid(), hanhDong = "CANCEL", lyDo = "Khách đổi ý" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<OrderService>>(JsonOptions);
        body!.Data!.TrangThai.Should().Be("HUY");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
            var slot1 = await db.KhungGioDatLichSet.SingleAsync(s => s.Id == 1);
            var booking = await db.DonDatLichSet.SingleAsync(b => b.Id == 1);
            slot1.PhutDaGiu.Should().Be(90);
            slot1.SlotDaDat.Should().Be(1); // Combo line still active in slot 1
            booking.TongTamTinh.Should().Be(350000m);
        }
    }

    [Fact]
    public async Task E21_CancelExtraService_SeparateSlot_DecrementsSlotDaDat()
    {
        await using var factory = CareTestSupport.Create(db => SeedScenario(db, extraInSeparateSlot: true));
        using var client = factory.CreateClient();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
            var admin = await db.NhanVienSet.SingleAsync(x => x.Id == 200);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(admin));
        }

        var response = await client.PostAsJsonAsync(
            "/api/internal/extra-services/101/actions",
            new { requestId = Guid.NewGuid(), hanhDong = "CANCEL", lyDo = "Hủy dịch vụ ca chiều" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
            var slot2 = await db.KhungGioDatLichSet.SingleAsync(s => s.Id == 2);
            slot2.PhutDaGiu.Should().Be(0);
            slot2.SlotDaDat.Should().Be(0);
        }
    }

    [Theory]
    [InlineData(OrderServiceState.HOAN_TAT, false, false)]
    [InlineData(OrderServiceState.CHO_THUC_HIEN, true, false)]
    [InlineData(OrderServiceState.CHO_THUC_HIEN, false, true)]
    public async Task E21_CancelExtraService_Blocked409_WhenCompletedOrHasCompletedComponentOrActiveInvoice(
        OrderServiceState initialState, bool hasCompletedComponent, bool hasActiveInvoice)
    {
        await using var factory = CareTestSupport.Create(db => SeedScenario(
            db,
            seedActiveInvoice: hasActiveInvoice,
            extraHasCompletedComponent: hasCompletedComponent,
            extraInitialState: initialState));
        using var client = factory.CreateClient();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
            var admin = await db.NhanVienSet.SingleAsync(x => x.Id == 200);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(admin));
        }

        var response = await client.PostAsJsonAsync(
            "/api/internal/extra-services/101/actions",
            new { requestId = Guid.NewGuid(), hanhDong = "CANCEL", lyDo = "Thử hủy" });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<OrderService>>(JsonOptions);
        body!.ErrorCode.Should().Be("INVALID_TRANSITION");
    }

    [Fact]
    public async Task E21_CancelExtraService_WithoutPetCancelLinePermission_Returns403()
    {
        await using var factory = CareTestSupport.Create(db => SeedScenario(db, grantAdminCancelPermission: false));
        using var client = factory.CreateClient();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
            var admin = await db.NhanVienSet.SingleAsync(x => x.Id == 200);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(admin));
        }

        var response = await client.PostAsJsonAsync(
            "/api/internal/extra-services/101/actions",
            new { requestId = Guid.NewGuid(), hanhDong = "CANCEL", lyDo = "Không có quyền" });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Probing non-existent order service without permission also returns 403, not 404
        var probeNotFound = await client.PostAsJsonAsync(
            "/api/internal/extra-services/99999/actions",
            new { requestId = Guid.NewGuid(), hanhDong = "CANCEL", lyDo = "Probe" });
        probeNotFound.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task E21_CancelExtraService_MissingReason_Returns400AndCancelledExtraDoesNotBlockReady()
    {
        await using var factory = CareTestSupport.Create(db => SeedScenario(db));
        using var client = factory.CreateClient();

        NhanVien admin;
        NhanVien careStaff;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
            admin = await db.NhanVienSet.SingleAsync(x => x.Id == 200);
            careStaff = await db.NhanVienSet.SingleAsync(x => x.Id == 202);
        }

        // Missing lyDo on CANCEL -> 400 VALIDATION_FAILED
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(admin));
        var badCancel = await client.PostAsJsonAsync(
            "/api/internal/extra-services/101/actions",
            new { requestId = Guid.NewGuid(), hanhDong = "CANCEL" });
        badCancel.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Complete combo components and cancel extra service
        (await client.PostAsJsonAsync("/api/internal/order-services/100/components/1001/complete", new { requestId = Guid.NewGuid() }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/api/internal/order-services/100/components/1002/complete", new { requestId = Guid.NewGuid() }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/api/internal/extra-services/101/actions",
            new { requestId = Guid.NewGuid(), hanhDong = "CANCEL", lyDo = "Khách hủy" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Cancelled extra service does not block E17 READY
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(careStaff));
        var readyOk = await client.PostAsync(
            "/api/internal/booking-pets/10/care-actions",
            BuildCareActionForm(new CareAction { RequestId = Guid.NewGuid(), HanhDong = CareActionType.READY }, ValidPngBytes));
        readyOk.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task E21_CompleteExtraCombo_MarksAllPendingComponentsCompleted_AndEnforcesIdempotency()
    {
        await using var factory = CareTestSupport.Create(db =>
        {
            SeedScenario(db);
            var svc2 = db.DichVuSet.Single(s => s.Id == 2);
            var extraLine = db.DichVuTrongDonSet.Single(l => l.Id == 101);
            extraLine.DichVuId = null;
            extraLine.ComboId = 1;
            extraLine.TenSnapshot = "Combo Phát Sinh";
            db.ThanhPhanDichVuTrongDonSet.Add(new ThanhPhanDichVuTrongDon
            {
                Id = 1004,
                DichVuTrongDonId = extraLine.Id,
                DichVuId = svc2.Id,
                TenSnapshot = svc2.TenDichVu,
                GiaLeSizeSnapshot = "{}",
                ThoiLuongSnapshot = 30,
                TrangThai = OrderServiceState.CHO_THUC_HIEN
            });
            db.SaveChanges();
        });
        using var client = factory.CreateClient();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
            var admin = await db.NhanVienSet.SingleAsync(x => x.Id == 200);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(admin));
        }

        var reqId = Guid.NewGuid();
        var complete = await client.PostAsJsonAsync(
            "/api/internal/extra-services/101/actions",
            new { requestId = reqId, hanhDong = "COMPLETE" });
        complete.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await complete.Content.ReadFromJsonAsync<ApiResponse<OrderService>>(JsonOptions);
        body!.Data!.TrangThai.Should().Be("HOAN_TAT");
        body.Data.ThanhPhan.Should().ContainSingle(c => c.MaThanhPhan == 1004 && c.TrangThai == "HOAN_TAT" && c.HoanTatAt != null);

        // Replay with same requestId -> 200 OK
        var replay = await client.PostAsJsonAsync(
            "/api/internal/extra-services/101/actions",
            new { requestId = reqId, hanhDong = "COMPLETE" });
        replay.StatusCode.Should().Be(HttpStatusCode.OK);

        // Reuse same requestId with different action -> 409 IDEMPOTENCY_CONFLICT
        var conflict = await client.PostAsJsonAsync(
            "/api/internal/extra-services/101/actions",
            new { requestId = reqId, hanhDong = "CANCEL", lyDo = "Đổi hành động" });
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var conflictBody = await conflict.Content.ReadFromJsonAsync<ApiResponse<OrderService>>(JsonOptions);
        conflictBody!.ErrorCode.Should().Be("IDEMPOTENCY_CONFLICT");
    }

    [Fact]
    public async Task E61_Blocked409_WhenActiveInvoiceExists()
    {
        await using var factory = CareTestSupport.Create(db => SeedScenario(db, seedActiveInvoice: true));
        using var client = factory.CreateClient();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
            var careStaff = await db.NhanVienSet.SingleAsync(x => x.Id == 202);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(careStaff));
        }

        var res = await client.PostAsJsonAsync(
            "/api/internal/order-services/100/components/1001/complete",
            new { requestId = Guid.NewGuid() });
        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await res.Content.ReadFromJsonAsync<ApiResponse<OrderService>>(JsonOptions);
        body!.ErrorCode.Should().Be("INVALID_TRANSITION");
    }
}
