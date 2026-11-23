using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using PetSpa.Api.Data;
using PetSpa.Api.Services.Identity;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Booking;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Scheduling;
using PetSpa.Core.Enums;
using PetSpa.Tests.Infrastructure;
using Xunit;

namespace PetSpa.Tests.Booking;

public sealed class Issue17EndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static void SeedData(PetSpaDbContext db)
    {
        // Permissions
        var qApprove = new Quyen { MaQuyen = "BOOKING_APPROVE", TenQuyen = "Duyệt lịch" };
        var qReschedule = new Quyen { MaQuyen = "BOOKING_RESCHEDULE", TenQuyen = "Dời lịch" };
        db.QuyenSet.AddRange(qApprove, qReschedule);

        // Employees
        var admin = new NhanVien
        {
            Id = 1,
            TenDangNhap = "admin",
            TenNhanVien = "Admin Quản trị",
            VaiTro = Role.ADMIN,
            IsActive = true
        };
        var staffWithPermissions = new NhanVien
        {
            Id = 2,
            TenDangNhap = "staff1",
            TenNhanVien = "Nhân viên Lễ tân",
            VaiTro = Role.STAFF,
            IsActive = true
        };
        var staffNoPermissions = new NhanVien
        {
            Id = 3,
            TenDangNhap = "staff2",
            TenNhanVien = "Nhân viên Chăm sóc",
            VaiTro = Role.STAFF,
            IsActive = true
        };
        db.NhanVienSet.AddRange(admin, staffWithPermissions, staffNoPermissions);

        db.NhanVienQuyenSet.AddRange(
            new NhanVienQuyen { NhanVienId = 2, MaQuyen = "BOOKING_APPROVE" },
            new NhanVienQuyen { NhanVienId = 2, MaQuyen = "BOOKING_RESCHEDULE" }
        );

        // Customer & Pet
        var customer = new KhachHang
        {
            Id = 1,
            HoTen = "Nguyễn Văn Test",
            SoDienThoai = "0901234567",
            Email = "customer@example.com"
        };
        var pet = new ThuCung
        {
            Id = 1,
            KhachHangId = 1,
            TenThuCung = "Miu Miu",
            LoaiPet = PetType.MEO,
            CanNang = 3.5m
        };
        db.KhachHangSet.Add(customer);
        db.ThuCungSet.Add(pet);

        // Shifts & Slots
        var shift = new CaLamViec
        {
            Id = 1,
            TenCa = "Ca sáng",
            GioBatDau = new TimeOnly(8, 0),
            GioKetThuc = new TimeOnly(12, 0),
            SucChuaPet = 10,
            TranPhut = 600,
            IsActive = true
        };
        db.CaLamViecSet.Add(shift);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var slotFuture1 = new KhungGioDatLich
        {
            Id = 1,
            CaLamViecId = 1,
            Ngay = today.AddDays(2),
            MoNhanKhach = true,
            TongSlot = 10,
            SlotDaDat = 2,
            TranPhut = 600,
            PhutDaGiu = 120
        };
        var slotFuture2 = new KhungGioDatLich
        {
            Id = 2,
            CaLamViecId = 1,
            Ngay = today.AddDays(3),
            MoNhanKhach = true,
            TongSlot = 10,
            SlotDaDat = 0,
            TranPhut = 600,
            PhutDaGiu = 0
        };
        var slotClosed = new KhungGioDatLich
        {
            Id = 3,
            CaLamViecId = 1,
            Ngay = today.AddDays(4),
            MoNhanKhach = false,
            TongSlot = 10,
            SlotDaDat = 10,
            TranPhut = 600,
            PhutDaGiu = 600
        };
        var slotToday = new KhungGioDatLich
        {
            Id = 4,
            CaLamViecId = 1,
            Ngay = today,
            MoNhanKhach = true,
            TongSlot = 10,
            SlotDaDat = 1,
            TranPhut = 600,
            PhutDaGiu = 60
        };
        db.KhungGioDatLichSet.AddRange(slotFuture1, slotFuture2, slotClosed, slotToday);

        // Services & Prices
        var service = new DichVu
        {
            Id = 1,
            TenDichVu = "Tắm Spa Chó Mèo",
            LoaiDichVu = "SPA",
            ThoiLuongPhut = 60,
            YeuCauTiemChung = false,
            IsActive = true
        };
        db.DichVuSet.Add(service);

        foreach (var band in new[] { SizeBand.S, SizeBand.M, SizeBand.L, SizeBand.XL })
        {
            var sizeId = (int)band + 1;
            db.BangGiaSizeSet.Add(new BangGiaSize
            {
                DichVuId = 1,
                SizeId = sizeId,
                DonGia = 150_000m + sizeId * 50_000m
            });
        }

        // Booking 1: PENDING, deposit CHO_THU
        var b1 = new DonDatLich
        {
            Id = 1,
            KhachHangId = 1,
            KhungGioId = 1,
            RequestId = Guid.NewGuid(),
            Fingerprint = "fp1",
            TrackingCode = "TRK-0001",
            TrangThai = BookingState.PENDING,
            TenNguoiDat = "Nguyễn Văn Test",
            SoDienThoaiSnapshot = "0901234567",
            EmailSnapshot = "customer@example.com",
            BatDauDuKien = DateTimeOffset.UtcNow.AddDays(2),
            KetThucDuKien = DateTimeOffset.UtcNow.AddDays(2).AddHours(2),
            MocCuoiCa = DateTimeOffset.UtcNow.AddDays(2).AddHours(4),
            TongTamTinh = 200_000m,
            ChinhSachSnapshot = "{}",
            EmailDelivery = "SENT"
        };
        var tc1 = new TienCoc
        {
            Id = 1,
            DonDatLichId = 1,
            CoSoTinhSnapshot = 200_000m,
            TyLe = 0.30m,
            SoTienYeuCau = 60_000m,
            TrangThaiYeuCau = DepositRequestState.CHO_THU,
            HetHanAt = DateTimeOffset.UtcNow.AddMinutes(30)
        };
        var petDetail1 = new ChiTietDon
        {
            Id = 1,
            DonDatLichId = 1,
            ThuCungId = 1,
            TenPetSnapshot = "Miu Miu",
            TrangThaiChamSoc = PetCareState.NOT_ARRIVED,
            CanNangKhaiBao = 3.5m
        };
        var serviceOrder1 = new DichVuTrongDon
        {
            Id = 1,
            ChiTietDonId = 1,
            DichVuId = 1,
            KhungGioId = 1,
            RequestId = Guid.NewGuid(),
            TenSnapshot = "Tắm Spa Chó Mèo",
            BangGiaSizeSnapshot = "{}",
            ThoiLuongSnapshot = 60,
            TrangThai = OrderServiceState.CHO_THUC_HIEN
        };
        b1.TienCocDonDatLich = tc1;
        b1.ChiTietDonDonDatLichItems.Add(petDetail1);
        petDetail1.DichVuTrongDonChiTietDonItems.Add(serviceOrder1);
        db.DonDatLichSet.Add(b1);

        // Booking 2: PENDING, deposit DA_DAP_UNG
        var b2 = new DonDatLich
        {
            Id = 2,
            KhachHangId = 1,
            KhungGioId = 1,
            RequestId = Guid.NewGuid(),
            Fingerprint = "fp2",
            TrackingCode = "TRK-0002",
            TrangThai = BookingState.PENDING,
            TenNguoiDat = "Nguyễn Văn Test",
            SoDienThoaiSnapshot = "0901234567",
            EmailSnapshot = "customer@example.com",
            BatDauDuKien = DateTimeOffset.UtcNow.AddDays(2),
            KetThucDuKien = DateTimeOffset.UtcNow.AddDays(2).AddHours(2),
            MocCuoiCa = DateTimeOffset.UtcNow.AddDays(2).AddHours(4),
            TongTamTinh = 200_000m,
            ChinhSachSnapshot = "{}",
            EmailDelivery = "SENT"
        };
        var tc2 = new TienCoc
        {
            Id = 2,
            DonDatLichId = 2,
            CoSoTinhSnapshot = 200_000m,
            TyLe = 0.30m,
            SoTienYeuCau = 60_000m,
            TrangThaiYeuCau = DepositRequestState.DA_DAP_UNG,
            HetHanAt = DateTimeOffset.UtcNow.AddMinutes(30)
        };
        tc2.GiaoDichThuTienCocItems.Add(new GiaoDichThu
        {
            Id = 2,
            TienCocId = 2,
            RequestId = Guid.NewGuid(),
            SoTien = 60_000m,
            PhuongThuc = PaymentMethod.BANK_TRANSFER,
            GiaoDichAt = DateTimeOffset.UtcNow,
            XacNhanAt = DateTimeOffset.UtcNow,
            XacNhanBoiId = 1
        });
        var petDetail2 = new ChiTietDon
        {
            Id = 2,
            DonDatLichId = 2,
            ThuCungId = 1,
            TenPetSnapshot = "Miu Miu",
            TrangThaiChamSoc = PetCareState.NOT_ARRIVED,
            CanNangKhaiBao = 3.5m
        };
        var serviceOrder2 = new DichVuTrongDon
        {
            Id = 2,
            ChiTietDonId = 2,
            DichVuId = 1,
            KhungGioId = 1,
            RequestId = Guid.NewGuid(),
            TenSnapshot = "Tắm Spa Chó Mèo",
            BangGiaSizeSnapshot = "{}",
            ThoiLuongSnapshot = 60,
            TrangThai = OrderServiceState.CHO_THUC_HIEN
        };
        b2.TienCocDonDatLich = tc2;
        b2.ChiTietDonDonDatLichItems.Add(petDetail2);
        petDetail2.DichVuTrongDonChiTietDonItems.Add(serviceOrder2);
        db.DonDatLichSet.Add(b2);

        // Booking 3: CONFIRMED, for reschedule test
        var b3 = new DonDatLich
        {
            Id = 3,
            KhachHangId = 1,
            KhungGioId = 1,
            RequestId = Guid.NewGuid(),
            Fingerprint = "fp3",
            TrackingCode = "TRK-0003",
            TrangThai = BookingState.CONFIRMED,
            TenNguoiDat = "Nguyễn Văn Test",
            SoDienThoaiSnapshot = "0901234567",
            EmailSnapshot = "customer@example.com",
            BatDauDuKien = DateTimeOffset.UtcNow.AddDays(2),
            KetThucDuKien = DateTimeOffset.UtcNow.AddDays(2).AddHours(2),
            MocCuoiCa = DateTimeOffset.UtcNow.AddDays(2).AddHours(4),
            TongTamTinh = 200_000m,
            ChinhSachSnapshot = "{}",
            EmailDelivery = "SENT"
        };
        var petDetail3 = new ChiTietDon
        {
            Id = 3,
            DonDatLichId = 3,
            ThuCungId = 1,
            TenPetSnapshot = "Miu Miu",
            TrangThaiChamSoc = PetCareState.NOT_ARRIVED,
            CanNangKhaiBao = 3.5m
        };
        var serviceOrder3 = new DichVuTrongDon
        {
            Id = 3,
            ChiTietDonId = 3,
            DichVuId = 1,
            KhungGioId = 1,
            RequestId = Guid.NewGuid(),
            TenSnapshot = "Tắm Spa Chó Mèo",
            BangGiaSizeSnapshot = "{}",
            ThoiLuongSnapshot = 60,
            TrangThai = OrderServiceState.CHO_THUC_HIEN
        };
        b3.ChiTietDonDonDatLichItems.Add(petDetail3);
        petDetail3.DichVuTrongDonChiTietDonItems.Add(serviceOrder3);
        db.DonDatLichSet.Add(b3);

        // Booking 4: CONFIRMED, starting in 30 minutes (late cancellation < 2h test)
        var b4 = new DonDatLich
        {
            Id = 4,
            KhachHangId = 1,
            KhungGioId = 4,
            RequestId = Guid.NewGuid(),
            Fingerprint = "fp4",
            TrackingCode = "TRK-0004",
            TrangThai = BookingState.CONFIRMED,
            TenNguoiDat = "Nguyễn Văn Test",
            SoDienThoaiSnapshot = "0901234567",
            EmailSnapshot = "customer@example.com",
            BatDauDuKien = DateTimeOffset.UtcNow.AddMinutes(30),
            KetThucDuKien = DateTimeOffset.UtcNow.AddHours(2),
            MocCuoiCa = DateTimeOffset.UtcNow.AddHours(3),
            TongTamTinh = 200_000m,
            ChinhSachSnapshot = "{}",
            EmailDelivery = "SENT"
        };
        var petDetail4 = new ChiTietDon
        {
            Id = 4,
            DonDatLichId = 4,
            ThuCungId = 1,
            TenPetSnapshot = "Miu Miu",
            TrangThaiChamSoc = PetCareState.NOT_ARRIVED,
            CanNangKhaiBao = 3.5m
        };
        var serviceOrder4 = new DichVuTrongDon
        {
            Id = 4,
            ChiTietDonId = 4,
            DichVuId = 1,
            KhungGioId = 4,
            RequestId = Guid.NewGuid(),
            TenSnapshot = "Tắm Spa Chó Mèo",
            BangGiaSizeSnapshot = "{}",
            ThoiLuongSnapshot = 60,
            TrangThai = OrderServiceState.CHO_THUC_HIEN
        };
        b4.ChiTietDonDonDatLichItems.Add(petDetail4);
        petDetail4.DichVuTrongDonChiTietDonItems.Add(serviceOrder4);
        db.DonDatLichSet.Add(b4);

        // Booking 5: CONFIRMED, shift ended 45 mins ago (no-show test)
        var b5 = new DonDatLich
        {
            Id = 5,
            KhachHangId = 1,
            KhungGioId = 4,
            RequestId = Guid.NewGuid(),
            Fingerprint = "fp5",
            TrackingCode = "TRK-0005",
            TrangThai = BookingState.CONFIRMED,
            TenNguoiDat = "Nguyễn Văn Test",
            SoDienThoaiSnapshot = "0901234567",
            EmailSnapshot = "customer@example.com",
            BatDauDuKien = DateTimeOffset.UtcNow.AddHours(-4),
            KetThucDuKien = DateTimeOffset.UtcNow.AddHours(-1),
            MocCuoiCa = DateTimeOffset.UtcNow.AddMinutes(-45),
            TongTamTinh = 200_000m,
            ChinhSachSnapshot = "{}",
            EmailDelivery = "SENT"
        };
        var tc5 = new TienCoc
        {
            Id = 5,
            DonDatLichId = 5,
            CoSoTinhSnapshot = 200_000m,
            TyLe = 0.30m,
            SoTienYeuCau = 60_000m,
            TrangThaiYeuCau = DepositRequestState.DA_DAP_UNG,
            HetHanAt = DateTimeOffset.UtcNow.AddHours(-3)
        };
        tc5.GiaoDichThuTienCocItems.Add(new GiaoDichThu
        {
            Id = 5,
            TienCocId = 5,
            RequestId = Guid.NewGuid(),
            SoTien = 60_000m,
            PhuongThuc = PaymentMethod.BANK_TRANSFER,
            GiaoDichAt = DateTimeOffset.UtcNow.AddHours(-3),
            XacNhanAt = DateTimeOffset.UtcNow.AddHours(-3),
            XacNhanBoiId = 1
        });
        var petDetail5 = new ChiTietDon
        {
            Id = 5,
            DonDatLichId = 5,
            ThuCungId = 1,
            TenPetSnapshot = "Miu Miu",
            TrangThaiChamSoc = PetCareState.NOT_ARRIVED,
            CanNangKhaiBao = 3.5m
        };
        var serviceOrder5 = new DichVuTrongDon
        {
            Id = 5,
            ChiTietDonId = 5,
            DichVuId = 1,
            KhungGioId = 4,
            RequestId = Guid.NewGuid(),
            TenSnapshot = "Tắm Spa Chó Mèo",
            BangGiaSizeSnapshot = "{}",
            ThoiLuongSnapshot = 60,
            TrangThai = OrderServiceState.CHO_THUC_HIEN
        };
        b5.TienCocDonDatLich = tc5;
        b5.ChiTietDonDonDatLichItems.Add(petDetail5);
        petDetail5.DichVuTrongDonChiTietDonItems.Add(serviceOrder5);
        db.DonDatLichSet.Add(b5);

        // Booking 6: CONFIRMED, 2 days in future, paid deposit (early cancel with refund test)
        var b6 = new DonDatLich
        {
            Id = 6,
            KhachHangId = 1,
            KhungGioId = 1,
            RequestId = Guid.NewGuid(),
            Fingerprint = "fp6",
            TrackingCode = "TRK-0006",
            TrangThai = BookingState.CONFIRMED,
            TenNguoiDat = "Nguyễn Văn Test",
            SoDienThoaiSnapshot = "0901234567",
            EmailSnapshot = "customer@example.com",
            BatDauDuKien = DateTimeOffset.UtcNow.AddDays(2),
            KetThucDuKien = DateTimeOffset.UtcNow.AddDays(2).AddHours(2),
            MocCuoiCa = DateTimeOffset.UtcNow.AddDays(2).AddHours(4),
            TongTamTinh = 200_000m,
            ChinhSachSnapshot = "{}",
            EmailDelivery = "SENT"
        };
        var tc6 = new TienCoc
        {
            Id = 6,
            DonDatLichId = 6,
            CoSoTinhSnapshot = 200_000m,
            TyLe = 0.30m,
            SoTienYeuCau = 60_000m,
            TrangThaiYeuCau = DepositRequestState.DA_DAP_UNG,
            HetHanAt = DateTimeOffset.UtcNow.AddMinutes(30)
        };
        tc6.GiaoDichThuTienCocItems.Add(new GiaoDichThu
        {
            Id = 6,
            TienCocId = 6,
            RequestId = Guid.NewGuid(),
            SoTien = 60_000m,
            PhuongThuc = PaymentMethod.BANK_TRANSFER,
            GiaoDichAt = DateTimeOffset.UtcNow,
            XacNhanAt = DateTimeOffset.UtcNow,
            XacNhanBoiId = 1
        });
        var petDetail6 = new ChiTietDon
        {
            Id = 6,
            DonDatLichId = 6,
            ThuCungId = 1,
            TenPetSnapshot = "Miu Miu",
            TrangThaiChamSoc = PetCareState.NOT_ARRIVED,
            CanNangKhaiBao = 3.5m
        };
        var serviceOrder6 = new DichVuTrongDon
        {
            Id = 6,
            ChiTietDonId = 6,
            DichVuId = 1,
            KhungGioId = 1,
            RequestId = Guid.NewGuid(),
            TenSnapshot = "Tắm Spa Chó Mèo",
            BangGiaSizeSnapshot = "{}",
            ThoiLuongSnapshot = 60,
            TrangThai = OrderServiceState.CHO_THUC_HIEN
        };
        b6.TienCocDonDatLich = tc6;
        b6.ChiTietDonDonDatLichItems.Add(petDetail6);
        petDetail6.DichVuTrongDonChiTietDonItems.Add(serviceOrder6);
        db.DonDatLichSet.Add(b6);

        db.SaveChanges();
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory, int employeeId, Role role, string[]? permissions = null)
    {
        var client = factory.CreateClient();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SecretKey"] = AuthApplicationFactory.TestJwtSecret,
            ["Jwt:Issuer"] = "PetSpaApi",
            ["Jwt:Audience"] = "PetSpaClient",
            ["Jwt:ExpiryMinutes"] = "480"
        }).Build();
        var tokenService = new TokenService(configuration);
        var emp = new NhanVien
        {
            Id = employeeId,
            TenDangNhap = $"user_{employeeId}",
            TenNhanVien = "Test",
            VaiTro = role,
            IsActive = true
        };
        if (permissions != null)
        {
            foreach (var p in permissions)
                emp.NhanVienQuyenNhanVienItems.Add(new NhanVienQuyen { NhanVienId = employeeId, MaQuyen = p });
        }
        var (token, _) = tokenService.GenerateToken(emp);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // ==========================================
    // E10b: GET /api/internal/bookings/{id}
    // ==========================================

    [Fact]
    public async Task GetById_WhenUnauthenticated_Returns401()
    {
        using var factory = AuthApplicationFactory.Create(SeedData);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/internal/bookings/1");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetById_WithoutApprovePermission_Returns403()
    {
        using var factory = AuthApplicationFactory.Create(SeedData);
        var client = CreateClient(factory, 3, Role.STAFF); // Staff without permissions

        var response = await client.GetAsync("/api/internal/bookings/1");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetById_WhenBookingNotFound_Returns404()
    {
        using var factory = AuthApplicationFactory.Create(SeedData);
        var client = CreateClient(factory, 1, Role.ADMIN);

        var response = await client.GetAsync("/api/internal/bookings/9999");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_WhenFound_Returns200WithFullDetails()
    {
        using var factory = AuthApplicationFactory.Create(SeedData);
        var client = CreateClient(factory, 2, Role.STAFF, ["BOOKING_APPROVE"]);

        var response = await client.GetAsync("/api/internal/bookings/1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<ApiResponse<BookingResponse>>(JsonOptions);
        json.Should().NotBeNull();
        json!.Success.Should().BeTrue();
        json.Data.Should().NotBeNull();
        json.Data!.MaDon.Should().Be(1);
        json.Data.TrackingCode.Should().Be("TRK-0001");
        json.Data.TienCoc.Should().NotBeNull();
        json.Data.TienCoc!.SoTienYeuCau.Should().Be(60_000m);
        json.Data.ThuCung.Should().HaveCount(1);
        json.Data.ThuCung[0].TenThuCung.Should().Be("Miu Miu");
    }

    // ==========================================
    // E11: POST /api/internal/bookings/{id}/confirm
    // ==========================================

    [Fact]
    public async Task Confirm_WhenDepositRequiredAndUnsatisfied_Returns409InvalidTransition()
    {
        using var factory = AuthApplicationFactory.Create(SeedData);
        var client = CreateClient(factory, 1, Role.ADMIN);

        var response = await client.PostAsJsonAsync("/api/internal/bookings/1/confirm", new BookingCommand(Guid.NewGuid()), JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("INVALID_TRANSITION");
    }

    [Fact]
    public async Task Confirm_WhenDepositSatisfied_TransitionsToConfirmed()
    {
        using var factory = AuthApplicationFactory.Create(SeedData);
        var client = CreateClient(factory, 1, Role.ADMIN);

        var response = await client.PostAsJsonAsync("/api/internal/bookings/2/confirm", new BookingCommand(Guid.NewGuid()), JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<ApiResponse<BookingResponse>>(JsonOptions);
        json!.Success.Should().BeTrue();
        json.Data!.TrangThai.Should().Be("CONFIRMED");
    }

    // ==========================================
    // E12: POST /api/internal/bookings/{id}/cancel
    // ==========================================

    [Fact]
    public async Task Cancel_LateCancellationByStaff_Returns403Forbidden()
    {
        using var factory = AuthApplicationFactory.Create(SeedData);
        var client = CreateClient(factory, 2, Role.STAFF, ["BOOKING_APPROVE"]);

        var cancelRequest = new CancelBooking(Guid.NewGuid(), "STAFF_CANCELLED", "Hủy gấp theo khách yêu cầu");
        var response = await client.PostAsJsonAsync("/api/internal/bookings/4/cancel", cancelRequest, JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Cancel_LateCancellationByAdmin_Succeeds()
    {
        using var factory = AuthApplicationFactory.Create(SeedData);
        var client = CreateClient(factory, 1, Role.ADMIN);

        var cancelRequest = new CancelBooking(Guid.NewGuid(), "STAFF_CANCELLED", "Quản trị viên hủy ngoại lệ");
        var response = await client.PostAsJsonAsync("/api/internal/bookings/4/cancel", cancelRequest, JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<ApiResponse<BookingResponse>>(JsonOptions);
        json!.Data!.TrangThai.Should().Be("CANCELLED");
    }

    [Fact]
    public async Task Cancel_NoShow_WhenGracePeriodNotMet_Returns409()
    {
        using var factory = AuthApplicationFactory.Create(SeedData);
        var client = CreateClient(factory, 1, Role.ADMIN);

        // Booking 3 is in future, not ended
        var cancelRequest = new CancelBooking(Guid.NewGuid(), "NO_SHOW", "Khách không liên hệ");
        var response = await client.PostAsJsonAsync("/api/internal/bookings/3/cancel", cancelRequest, JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Cancel_NoShow_WithoutExactAttestation_Returns409()
    {
        using var factory = AuthApplicationFactory.Create(SeedData);
        var client = CreateClient(factory, 1, Role.ADMIN);

        // Booking 5 ended > 30 mins ago, but reason missing attestation
        var cancelRequest = new CancelBooking(Guid.NewGuid(), "NO_SHOW", "Không thấy khách đến");
        var response = await client.PostAsJsonAsync("/api/internal/bookings/5/cancel", cancelRequest, JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Cancel_NoShow_Success_ForfeitsDeposit()
    {
        using var factory = AuthApplicationFactory.Create(SeedData);
        var client = CreateClient(factory, 1, Role.ADMIN);

        var cancelRequest = new CancelBooking(Guid.NewGuid(), "NO_SHOW", "Khách không liên hệ");
        var response = await client.PostAsJsonAsync("/api/internal/bookings/5/cancel", cancelRequest, JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<ApiResponse<BookingResponse>>(JsonOptions);
        json!.Data!.TrangThai.Should().Be("CANCELLED");
        // GiuLai should be 60,000 (100% retained)
        json.Data.TienCoc!.GiuLai.Should().Be(60_000m);
        json.Data.TienCoc.DanhHoan.Should().Be(0m);
    }

    [Fact]
    public async Task Cancel_EarlyCancellationWithPaidDeposit_CreatesChoHoanRefund()
    {
        using var factory = AuthApplicationFactory.Create(SeedData);
        var client = CreateClient(factory, 1, Role.ADMIN);

        var cancelRequest = new CancelBooking(Guid.NewGuid(), "CUSTOMER_CANCELLED", "Khách bận việc đột xuất");
        var response = await client.PostAsJsonAsync("/api/internal/bookings/6/cancel", cancelRequest, JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<ApiResponse<BookingResponse>>(JsonOptions);
        json!.Data!.TrangThai.Should().Be("CANCELLED");
        json.Data.TienCoc!.DanhHoan.Should().Be(60_000m);
        json.Data.TienCoc.GiuLai.Should().Be(0m);
        json.Data.TienCoc.HoanCoc.Should().HaveCount(1);
        json.Data.TienCoc.HoanCoc[0].TrangThai.Should().Be("CHO_HOAN");
        json.Data.TienCoc.HoanCoc[0].SoTien.Should().Be(60_000m);
    }

    // ==========================================
    // E13: POST /api/internal/bookings/{id}/reschedule
    // ==========================================

    [Fact]
    public async Task Reschedule_WithoutReschedulePermission_Returns403()
    {
        using var factory = AuthApplicationFactory.Create(SeedData);
        var client = CreateClient(factory, 3, Role.STAFF);

        var rescheduleRequest = new Reschedule
        {
            RequestId = Guid.NewGuid(),
            MaKhungGio = 2,
            NgayTra = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)),
            LyDo = "Khách đổi ngày"
        };
        var response = await client.PostAsJsonAsync("/api/internal/bookings/3/reschedule", rescheduleRequest, JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reschedule_WhenNewSlotClosed_Returns409CapacityExceeded()
    {
        using var factory = AuthApplicationFactory.Create(SeedData);
        var client = CreateClient(factory, 2, Role.STAFF, ["BOOKING_RESCHEDULE"]);

        var rescheduleRequest = new Reschedule
        {
            RequestId = Guid.NewGuid(),
            MaKhungGio = 3, // Closed slot
            NgayTra = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(4)),
            LyDo = "Khách đổi sang ngày đóng cửa"
        };
        var response = await client.PostAsJsonAsync("/api/internal/bookings/3/reschedule", rescheduleRequest, JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("CAPACITY_EXCEEDED");
    }

    [Fact]
    public async Task Reschedule_Success_MovesSlotAndUpdatesTime()
    {
        using var factory = AuthApplicationFactory.Create(SeedData);
        var client = CreateClient(factory, 2, Role.STAFF, ["BOOKING_RESCHEDULE"]);

        var reqId = Guid.NewGuid();
        var targetDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3));
        var rescheduleRequest = new Reschedule
        {
            RequestId = reqId,
            MaKhungGio = 2,
            NgayTra = targetDate,
            LyDo = "Khách dời sang ngày mốt"
        };
        var response = await client.PostAsJsonAsync("/api/internal/bookings/3/reschedule", rescheduleRequest, JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<ApiResponse<BookingResponse>>(JsonOptions);
        json!.Success.Should().BeTrue();
        json.Data!.TrangThai.Should().Be("CONFIRMED");

        // Idempotency replay with same data
        var replay = await client.PostAsJsonAsync("/api/internal/bookings/3/reschedule", rescheduleRequest, JsonOptions);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ==========================================
    // E09: POST /api/tracking/cancel
    // ==========================================

    [Fact]
    public async Task TrackingCancel_WithInvalidPair_Returns404NotFound()
    {
        using var factory = AuthApplicationFactory.Create(SeedData);
        var client = factory.CreateClient();

        var cancelRequest = new TrackingCancel
        {
            SoDienThoai = "0901234567",
            TrackingCode = "TRK-9999",
            RequestId = Guid.NewGuid(),
            LyDo = "Khách bận"
        };
        var response = await client.PostAsJsonAsync("/api/tracking/cancel", cancelRequest, JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TrackingCancel_LessThanTwoHours_Returns409InvalidTransition()
    {
        using var factory = AuthApplicationFactory.Create(SeedData);
        var client = factory.CreateClient();

        // Booking 4 is in 30 minutes (< 2 hours)
        var cancelRequest = new TrackingCancel
        {
            SoDienThoai = "0901234567",
            TrackingCode = "TRK-0004",
            RequestId = Guid.NewGuid(),
            LyDo = "Khách bận việc"
        };
        var response = await client.PostAsJsonAsync("/api/tracking/cancel", cancelRequest, JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("INVALID_TRANSITION");
    }

    [Fact]
    public async Task TrackingCancel_Success_CancelsAndCreatesRefund()
    {
        using var factory = AuthApplicationFactory.Create(SeedData);
        var client = factory.CreateClient();

        // Booking 6 is 2 days in future (>= 2h) with paid deposit
        var cancelRequest = new TrackingCancel
        {
            SoDienThoai = "0901234567",
            TrackingCode = "TRK-0006",
            RequestId = Guid.NewGuid(),
            LyDo = "Khách tự hủy trực tuyến"
        };
        var response = await client.PostAsJsonAsync("/api/tracking/cancel", cancelRequest, JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<ApiResponse<BookingResponse>>(JsonOptions);
        json!.Success.Should().BeTrue();
        json.Data!.TrangThai.Should().Be("CANCELLED");
        json.Data.TienCoc!.DanhHoan.Should().Be(60_000m);
        json.Data.TienCoc.HoanCoc.Should().HaveCount(1);
        json.Data.TienCoc.HoanCoc[0].TrangThai.Should().Be("CHO_HOAN");
    }

    // ==========================================
    // E50: POST /api/tracking/resend
    // ==========================================

    [Fact]
    public async Task TrackingResend_AlwaysReturns202Accepted()
    {
        using var factory = AuthApplicationFactory.Create(SeedData);
        var client = factory.CreateClient();

        var resendRequest = new TrackingResend
        {
            SoDienThoai = "0901234567",
            Email = "customer@example.com"
        };
        var response = await client.PostAsJsonAsync("/api/tracking/resend", resendRequest, JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var json = await response.Content.ReadFromJsonAsync<ApiResponse<Acknowledged>>(JsonOptions);
        json!.Success.Should().BeTrue();
        json.Data!.DaTiepNhan.Should().BeTrue();
    }
}
