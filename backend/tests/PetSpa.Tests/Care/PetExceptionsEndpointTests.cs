using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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

public class PetExceptionsEndpointTests
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

    private static readonly NhanVien AdminUser = new()
    {
        Id = 300,
        TenDangNhap = "admin",
        TenNhanVien = "Quản Trị Viên",
        VaiTro = Role.ADMIN,
        IsActive = true
    };

    private static readonly NhanVien StaffWithPermsUser = new()
    {
        Id = 301,
        TenDangNhap = "staff1",
        TenNhanVien = "Lễ Tân",
        VaiTro = Role.STAFF,
        IsActive = true
    };

    private static readonly NhanVien StaffNoPermsUser = new()
    {
        Id = 302,
        TenDangNhap = "staff2",
        TenNhanVien = "KTV Spa",
        VaiTro = Role.STAFF,
        IsActive = true
    };

    private static void SeedBaseUsers(PetSpaDbContext db, DateTimeOffset now, bool grantStaffAdminPerms = false)
    {
        var perms = new[]
        {
            new Quyen { MaQuyen = "PET_CANCEL_LINE", TenQuyen = "PET_CANCEL_LINE" },
            new Quyen { MaQuyen = "EARLY_HANDOVER_APPROVE", TenQuyen = "EARLY_HANDOVER_APPROVE" },
            new Quyen { MaQuyen = "PET_HANDOVER", TenQuyen = "PET_HANDOVER" },
            new Quyen { MaQuyen = "CARE_UPDATE", TenQuyen = "CARE_UPDATE" }
        };
        db.QuyenSet.AddRange(perms);

        var hasher = new PasswordHasher<NhanVien>();
        var admin = new NhanVien
        {
            Id = AdminUser.Id,
            TenDangNhap = AdminUser.TenDangNhap,
            TenNhanVien = AdminUser.TenNhanVien,
            VaiTro = Role.ADMIN,
            IsActive = true
        };
        admin.MatKhauHash = hasher.HashPassword(admin, "Password123@");

        var staff1 = new NhanVien
        {
            Id = StaffWithPermsUser.Id,
            TenDangNhap = StaffWithPermsUser.TenDangNhap,
            TenNhanVien = StaffWithPermsUser.TenNhanVien,
            VaiTro = Role.STAFF,
            IsActive = true
        };
        staff1.MatKhauHash = hasher.HashPassword(staff1, "Password123@");

        var staff2 = new NhanVien
        {
            Id = StaffNoPermsUser.Id,
            TenDangNhap = StaffNoPermsUser.TenDangNhap,
            TenNhanVien = StaffNoPermsUser.TenNhanVien,
            VaiTro = Role.STAFF,
            IsActive = true
        };
        staff2.MatKhauHash = hasher.HashPassword(staff2, "Password123@");

        db.NhanVienSet.AddRange(admin, staff1, staff2);

        foreach (var p in perms)
        {
            db.NhanVienQuyenSet.Add(new NhanVienQuyen
            {
                NhanVienId = admin.Id,
                MaQuyen = p.MaQuyen,
                CapBoiId = admin.Id,
                CapAt = now
            });
        }

        db.NhanVienQuyenSet.Add(new NhanVienQuyen
        {
            NhanVienId = staff1.Id,
            MaQuyen = "PET_HANDOVER",
            CapBoiId = admin.Id,
            CapAt = now
        });

        if (grantStaffAdminPerms)
        {
            db.NhanVienQuyenSet.AddRange(
                new NhanVienQuyen { NhanVienId = staff1.Id, MaQuyen = "PET_CANCEL_LINE", CapBoiId = admin.Id, CapAt = now },
                new NhanVienQuyen { NhanVienId = staff1.Id, MaQuyen = "EARLY_HANDOVER_APPROVE", CapBoiId = admin.Id, CapAt = now }
            );
        }
    }

    private static void SeedComboScenario(PetSpaDbContext db, bool firstComponentCompleted = true)
    {
        var now = CareTestSupport.Now;
        SeedBaseUsers(db, now);

        var shift = new CaLamViec
        {
            Id = 1, TenCa = "Ca Sáng", GioBatDau = new TimeOnly(8, 0), GioKetThuc = new TimeOnly(12, 0),
            SucChuaPet = 10, TranPhut = 240, IsActive = true
        };
        db.CaLamViecSet.Add(shift);

        var slot = new KhungGioDatLich
        {
            Id = 1, Ngay = new DateOnly(2026, 11, 21), CaLamViecId = shift.Id,
            MoNhanKhach = true, TongSlot = 10, SlotDaDat = 1, PhutDaGiu = 90, TranPhut = 240
        };
        db.KhungGioDatLichSet.Add(slot);

        var dv1 = new DichVu { Id = 1, TenDichVu = "Tắm gội", LoaiDichVu = "SPA", ThoiLuongPhut = 45, IsActive = true };
        var dv2 = new DichVu { Id = 2, TenDichVu = "Cắt tỉa", LoaiDichVu = "SPA", ThoiLuongPhut = 45, IsActive = true };
        db.DichVuSet.AddRange(dv1, dv2);

        var combo = new Combo { Id = 1, TenCombo = "Combo Tắm Cắt", IsActive = true };
        db.ComboSet.Add(combo);

        var customer = new KhachHang { Id = 1, HoTen = "Nguyễn Văn A", SoDienThoai = "0912345678", Email = "khach@example.com" };
        db.KhachHangSet.Add(customer);

        var pet1 = new ThuCung { Id = 1, KhachHangId = customer.Id, TenThuCung = "Mochi", LoaiPet = PetType.CHO, CanNang = 7.5m };
        db.ThuCungSet.Add(pet1);

        var booking = new DonDatLich
        {
            Id = 1, RequestId = Guid.NewGuid(), Fingerprint = "seed", TrackingCode = "TRK-8X2A",
            TrangThai = BookingState.IN_PROGRESS, TenNguoiDat = customer.HoTen,
            SoDienThoaiSnapshot = customer.SoDienThoai, EmailSnapshot = customer.Email,
            BatDauDuKien = now.AddHours(-1), KetThucDuKien = now.AddHours(1), MocCuoiCa = now.AddHours(3),
            TongTamTinh = 320000m, ChinhSachSnapshot = "{}", KhachHangId = customer.Id, KhungGioId = slot.Id
        };
        db.DonDatLichSet.Add(booking);

        var chiTiet1 = new ChiTietDon
        {
            Id = 10, DonDatLichId = booking.Id, ThuCungId = pet1.Id, TenPetSnapshot = pet1.TenThuCung,
            CanNangKhaiBao = 7.5m, CanNangChot = 7.5m, SizeThucTe = SizeBand.M, SizeChot = SizeBand.M,
            NhanVienId = StaffNoPermsUser.Id, CheckInAt = now.AddHours(-1), CheckInBoiId = StaffWithPermsUser.Id,
            TrangThaiChamSoc = PetCareState.IN_PROGRESS
        };
        db.ChiTietDonSet.Add(chiTiet1);

        var line = new DichVuTrongDon
        {
            Id = 100, RequestId = Guid.NewGuid(), ChiTietDonId = chiTiet1.Id, ComboId = combo.Id, KhungGioId = slot.Id,
            TenSnapshot = combo.TenCombo, BangGiaSizeSnapshot = "{\"S\":250000,\"M\":320000,\"L\":400000,\"XL\":500000}",
            ThoiLuongSnapshot = 90, TrangThai = OrderServiceState.CHO_THUC_HIEN, SizeChot = SizeBand.M, GiaChot = 320000m
        };
        db.DichVuTrongDonSet.Add(line);

        db.ThanhPhanDichVuTrongDonSet.AddRange(
            new ThanhPhanDichVuTrongDon
            {
                Id = 1001, DichVuTrongDonId = line.Id, DichVuId = dv1.Id,
                GiaLeSizeSnapshot = "{\"S\":120000,\"M\":150000,\"L\":200000,\"XL\":250000}",
                TrangThai = firstComponentCompleted ? OrderServiceState.HOAN_TAT : OrderServiceState.CHO_THUC_HIEN,
                HoanTatAt = firstComponentCompleted ? now.AddMinutes(-15) : null
            },
            new ThanhPhanDichVuTrongDon
            {
                Id = 1002, DichVuTrongDonId = line.Id, DichVuId = dv2.Id,
                GiaLeSizeSnapshot = "{\"S\":150000,\"M\":200000,\"L\":250000,\"XL\":300000}",
                TrangThai = OrderServiceState.CHO_THUC_HIEN
            }
        );

        db.SaveChanges();
    }

    private static void SeedTwoPetScenario(
        PetSpaDbContext db,
        PetCareState pet1State = PetCareState.READY,
        PetCareState pet2State = PetCareState.IN_PROGRESS,
        BookingState bookingState = BookingState.IN_PROGRESS,
        InvoiceState? invoiceState = null,
        bool pet1CheckIn = true,
        bool pet2CheckIn = true,
        bool pet1NhanSom = false,
        DateTimeOffset? pet2TraLaiAt = null,
        decimal tongTamTinh = 350000m,
        bool withHotel = false,
        bool grantStaffAdminPerms = false)
    {
        var now = CareTestSupport.Now;
        SeedBaseUsers(db, now, grantStaffAdminPerms);

        var shift = new CaLamViec
        {
            Id = 1, TenCa = "Ca Sáng", GioBatDau = new TimeOnly(8, 0), GioKetThuc = new TimeOnly(12, 0),
            SucChuaPet = 10, TranPhut = 240, IsActive = true
        };
        db.CaLamViecSet.Add(shift);

        var slot = new KhungGioDatLich
        {
            Id = 1, Ngay = new DateOnly(2026, 11, 21), CaLamViecId = shift.Id,
            MoNhanKhach = true, TongSlot = 10, SlotDaDat = 2, PhutDaGiu = 120, TranPhut = 240
        };
        db.KhungGioDatLichSet.Add(slot);

        var dv = new DichVu { Id = 1, TenDichVu = "Tắm sấy", LoaiDichVu = "SPA", ThoiLuongPhut = 60, IsActive = true };
        db.DichVuSet.Add(dv);

        if (withHotel)
        {
            db.LoaiPhongSet.Add(new LoaiPhong
            {
                Id = 1, TenPhong = "Phòng VIP", GiaMoiDem = 200000m, SoLuongPhong = 5, IsActive = true
            });
        }

        var customer = new KhachHang { Id = 1, HoTen = "Nguyễn Văn A", SoDienThoai = "0912345678", Email = "khach@example.com" };
        db.KhachHangSet.Add(customer);

        var pet1 = new ThuCung { Id = 1, KhachHangId = customer.Id, TenThuCung = "Mochi", LoaiPet = PetType.CHO, CanNang = 7.5m };
        var pet2 = new ThuCung { Id = 2, KhachHangId = customer.Id, TenThuCung = "Bông", LoaiPet = PetType.MEO, CanNang = 4.0m };
        db.ThuCungSet.AddRange(pet1, pet2);

        var booking = new DonDatLich
        {
            Id = 1, RequestId = Guid.NewGuid(), Fingerprint = "seed", TrackingCode = "TRK-8X2A",
            TrangThai = bookingState, TenNguoiDat = customer.HoTen,
            SoDienThoaiSnapshot = customer.SoDienThoai, EmailSnapshot = customer.Email,
            BatDauDuKien = now.AddHours(-2), KetThucDuKien = now.AddDays(2), MocCuoiCa = now.AddHours(2),
            TongTamTinh = tongTamTinh, ChinhSachSnapshot = "{}", KhachHangId = customer.Id, KhungGioId = slot.Id
        };
        db.DonDatLichSet.Add(booking);

        var chiTiet1 = new ChiTietDon
        {
            Id = 10, DonDatLichId = booking.Id, ThuCungId = pet1.Id, TenPetSnapshot = pet1.TenThuCung,
            CanNangKhaiBao = 7.5m, CanNangChot = pet1CheckIn ? 7.5m : null,
            SizeThucTe = pet1CheckIn ? SizeBand.M : null, SizeChot = pet1CheckIn ? SizeBand.M : null,
            NhanVienId = pet1CheckIn ? StaffNoPermsUser.Id : null,
            CheckInAt = pet1CheckIn ? now.AddHours(-2) : null,
            CheckInBoiId = pet1CheckIn ? StaffWithPermsUser.Id : null,
            ReadyAt = pet1State == PetCareState.READY ? now.AddMinutes(-20) : null,
            NhanSom = pet1NhanSom,
            NhanSomBoiId = pet1NhanSom ? AdminUser.Id : null,
            NhanSomLyDo = pet1NhanSom ? "Khách đón trước" : null,
            BanGiaoAt = pet1State == PetCareState.DONE ? now.AddMinutes(-10) : null,
            BanGiaoBoiId = pet1State == PetCareState.DONE ? AdminUser.Id : null,
            HuyAt = pet1State == PetCareState.CANCELLED ? now.AddMinutes(-15) : null,
            HuyBoiId = pet1State == PetCareState.CANCELLED ? AdminUser.Id : null,
            LyDoHuy = pet1State == PetCareState.CANCELLED ? "Hủy pet 1" : null,
            TrangThaiChamSoc = pet1State
        };

        var chiTiet2 = new ChiTietDon
        {
            Id = 11, DonDatLichId = booking.Id, ThuCungId = pet2.Id, TenPetSnapshot = pet2.TenThuCung,
            CanNangKhaiBao = 4.0m, CanNangChot = pet2CheckIn ? 4.0m : null,
            SizeThucTe = pet2CheckIn ? SizeBand.S : null, SizeChot = pet2CheckIn ? SizeBand.S : null,
            NhanVienId = pet2CheckIn ? StaffNoPermsUser.Id : null,
            CheckInAt = pet2CheckIn ? now.AddHours(-2) : null,
            CheckInBoiId = pet2CheckIn ? StaffWithPermsUser.Id : null,
            ReadyAt = pet2State == PetCareState.READY ? now.AddMinutes(-15) : null,
            HuyAt = pet2State == PetCareState.CANCELLED ? now.AddMinutes(-15) : null,
            HuyBoiId = pet2State == PetCareState.CANCELLED ? AdminUser.Id : null,
            LyDoHuy = pet2State == PetCareState.CANCELLED ? "Hủy pet 2" : null,
            TraLaiAt = pet2TraLaiAt,
            LoaiPhongId = withHotel ? 1 : null,
            GiaPhongCamKet = withHotel ? 200000m : null,
            GiaPhongChot = withHotel && pet2CheckIn ? 200000m : null,
            SoDemChot = withHotel && pet2CheckIn ? 2 : null,
            TrangThaiChamSoc = pet2State
        };
        db.ChiTietDonSet.AddRange(chiTiet1, chiTiet2);

        db.DichVuTrongDonSet.AddRange(
            new DichVuTrongDon
            {
                Id = 100, RequestId = Guid.NewGuid(), ChiTietDonId = chiTiet1.Id, DichVuId = dv.Id, KhungGioId = slot.Id,
                TenSnapshot = dv.TenDichVu, BangGiaSizeSnapshot = "{\"S\":150000,\"M\":200000,\"L\":250000,\"XL\":300000}",
                ThoiLuongSnapshot = 60,
                TrangThai = pet1State == PetCareState.CANCELLED ? OrderServiceState.HUY :
                    pet1State is PetCareState.READY or PetCareState.DONE ? OrderServiceState.HOAN_TAT : OrderServiceState.CHO_THUC_HIEN,
                SizeChot = pet1CheckIn ? SizeBand.M : null, GiaChot = pet1CheckIn ? 200000m : null
            },
            new DichVuTrongDon
            {
                Id = 101, RequestId = Guid.NewGuid(), ChiTietDonId = chiTiet2.Id, DichVuId = dv.Id, KhungGioId = slot.Id,
                TenSnapshot = dv.TenDichVu, BangGiaSizeSnapshot = "{\"S\":150000,\"M\":200000,\"L\":250000,\"XL\":300000}",
                ThoiLuongSnapshot = 60,
                TrangThai = pet2State == PetCareState.CANCELLED ? OrderServiceState.HUY :
                    pet2State is PetCareState.READY or PetCareState.DONE ? OrderServiceState.HOAN_TAT : OrderServiceState.CHO_THUC_HIEN,
                SizeChot = pet2CheckIn ? SizeBand.S : null, GiaChot = pet2CheckIn ? 150000m : null
            }
        );

        if (invoiceState.HasValue)
        {
            db.HoaDonSet.Add(new HoaDon
            {
                Id = 1, MaHoaDon = "HD-20261121-0001", DonDatLichId = booking.Id, TrangThai = invoiceState.Value,
                TongTien = tongTamTinh, CocKhauTru = 0m, ConPhaiTra = tongTamTinh,
                KhachSnapshot = "{\"hoTen\":\"Nguyễn Văn A\",\"soDienThoai\":\"0912345678\",\"email\":\"khach@example.com\"}",
                PaidAt = invoiceState.Value == InvoiceState.PAID ? now.AddMinutes(-5) : null,
                TaoBoiId = StaffWithPermsUser.Id,
                ThuBoiId = invoiceState.Value == InvoiceState.PAID ? StaffWithPermsUser.Id : null
            });
        }

        db.SaveChanges();
    }

    // ==================== E47 TESTS ====================

    [Fact]
    public async Task E47_CancelPetWithPartialCombo_SettlesCompletedComponentAndReleasesUnusedMinutesOnly()
    {
        using var factory = CareTestSupport.Create(db => SeedComboScenario(db, firstComponentCompleted: true));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(AdminUser));

        var response = await client.PostAsJsonAsync("/api/admin/booking-pets/10/cancel", new PetCancelRequest
        {
            RequestId = Guid.NewGuid(),
            LyDo = "Khách đón gấp giữa chừng",
            SoDemDaO = 0,
            MaThanhPhanDaLam = [1001]
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body!.Success.Should().BeTrue();
        body.Data!.TrangThai.Should().Be("CANCELLED");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();

        var pet = await db.ChiTietDonSet.Include(p => p.DonDatLich).SingleAsync(p => p.Id == 10);
        pet.TrangThaiChamSoc.Should().Be(PetCareState.CANCELLED);
        pet.HuyBoiId.Should().Be(AdminUser.Id);
        pet.HuyAt.Should().NotBeNull();
        pet.LyDoHuy.Should().Be("Khách đón gấp giữa chừng");
        pet.TraLaiAt.Should().BeNull();
        pet.DonDatLich.TongTamTinh.Should().Be(150000m);

        var line = await db.DichVuTrongDonSet.Include(l => l.ThanhPhanDichVuTrongDonDichVuTrongDonItems)
            .SingleAsync(l => l.Id == 100);
        line.TrangThai.Should().Be(OrderServiceState.HOAN_TAT);
        line.GiaChot.Should().Be(150000m);
        line.ThanhPhanDichVuTrongDonDichVuTrongDonItems.Single(c => c.Id == 1002).TrangThai.Should().Be(OrderServiceState.HUY);

        var slot = await db.KhungGioDatLichSet.SingleAsync(s => s.Id == 1);
        slot.PhutDaGiu.Should().Be(45);
        slot.SlotDaDat.Should().Be(1);
    }

    [Fact]
    public async Task E47_MismatchedCompletedComponents_Returns409InvalidTransition()
    {
        using var factory = CareTestSupport.Create(db => SeedComboScenario(db, firstComponentCompleted: true));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(AdminUser));

        var response = await client.PostAsJsonAsync("/api/admin/booking-pets/10/cancel", new PetCancelRequest
        {
            RequestId = Guid.NewGuid(),
            LyDo = "Gửi thiếu thành phần đã làm",
            SoDemDaO = 0,
            MaThanhPhanDaLam = []
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body!.ErrorCode.Should().Be("INVALID_TRANSITION");
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 3)]
    public async Task E47_InvalidNightsStayed_Returns409InvalidTransition(bool withHotel, int nightsStayed)
    {
        using var factory = CareTestSupport.Create(db =>
            SeedTwoPetScenario(db, withHotel: withHotel, pet2State: PetCareState.IN_PROGRESS));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(AdminUser));

        var response = await client.PostAsJsonAsync("/api/admin/booking-pets/11/cancel", new PetCancelRequest
        {
            RequestId = Guid.NewGuid(),
            LyDo = "Số đêm không hợp lệ",
            SoDemDaO = nightsStayed,
            MaThanhPhanDaLam = []
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body!.ErrorCode.Should().Be("INVALID_TRANSITION");
    }

    [Fact]
    public async Task E47_NotArrivedPet_CancelSetsTraLaiAtAndTransitionsBookingIfAllCancelled()
    {
        using var factory = CareTestSupport.Create(db =>
            SeedTwoPetScenario(db,
                pet1State: PetCareState.NOT_ARRIVED,
                pet2State: PetCareState.CANCELLED,
                bookingState: BookingState.CONFIRMED,
                pet1CheckIn: false,
                pet2CheckIn: false,
                pet2TraLaiAt: CareTestSupport.Now.AddMinutes(-10),
                tongTamTinh: 0m));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(AdminUser));

        var response = await client.PostAsJsonAsync("/api/admin/booking-pets/10/cancel", new PetCancelRequest
        {
            RequestId = Guid.NewGuid(),
            LyDo = "Khách không mang bé tới",
            SoDemDaO = 0,
            MaThanhPhanDaLam = []
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body!.Data!.TrangThai.Should().Be("CANCELLED");
        body.Data.TraLaiAt.Should().NotBeNull();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
        var booking = await db.DonDatLichSet.SingleAsync(b => b.Id == 1);
        booking.TrangThai.Should().Be(BookingState.CANCELLED);
        booking.LoaiHuy.Should().BeNull();
    }

    [Fact]
    public async Task E47_NonAdminOrMissingPermission_Returns403Forbidden()
    {
        using var factory = CareTestSupport.Create(db =>
            SeedTwoPetScenario(db, grantStaffAdminPerms: true));
        using var client = factory.CreateClient();

        // STAFF even with PET_CANCEL_LINE grant must be rejected because E47 is Admin-only
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(StaffWithPermsUser));
        var staffRes = await client.PostAsJsonAsync("/api/admin/booking-pets/10/cancel", new PetCancelRequest
        {
            RequestId = Guid.NewGuid(),
            LyDo = "Staff thử hủy",
            SoDemDaO = 0,
            MaThanhPhanDaLam = []
        });
        staffRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await staffRes.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>())!.ErrorCode.Should().Be("FORBIDDEN");

        // STAFF without permission
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(StaffNoPermsUser));
        var noPermRes = await client.PostAsJsonAsync("/api/admin/booking-pets/10/cancel", new PetCancelRequest
        {
            RequestId = Guid.NewGuid(),
            LyDo = "Staff không quyền",
            SoDemDaO = 0,
            MaThanhPhanDaLam = []
        });
        noPermRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task E47_IdempotencyReplayAndConflict_BehavesCorrectly()
    {
        var clock = new CareTestSupport.Clock(CareTestSupport.Now);
        using var factory = CareTestSupport.Create(db => SeedComboScenario(db, firstComponentCompleted: true), clock: clock);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(AdminUser));

        var reqId = Guid.NewGuid();
        var payload = new PetCancelRequest
        {
            RequestId = reqId,
            LyDo = "Hủy trùng requestId",
            SoDemDaO = 0,
            MaThanhPhanDaLam = [1001]
        };

        var first = await client.PostAsJsonAsync("/api/admin/booking-pets/10/cancel", payload);
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        clock.UtcNow = clock.UtcNow.AddMinutes(5);
        var replay = await client.PostAsJsonAsync("/api/admin/booking-pets/10/cancel", payload);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);

        var conflict = await client.PostAsJsonAsync("/api/admin/booking-pets/10/cancel", new PetCancelRequest
        {
            RequestId = reqId,
            LyDo = "Lý do khác",
            SoDemDaO = 0,
            MaThanhPhanDaLam = [1001]
        });
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await conflict.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>())!.ErrorCode.Should().Be("IDEMPOTENCY_CONFLICT");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
        (await db.AuditLogSet.CountAsync(a => a.HanhDong == "PET_CANCEL_LINE" && a.RequestId == reqId)).Should().Be(1);
    }

    // ==================== E48 TESTS ====================

    [Fact]
    public async Task E48_EarlyHandoverOnePet_SucceedsAndKeepsBookingInProgress_ThenBlocksSecondPetE48AndE47()
    {
        using var factory = CareTestSupport.Create(db =>
            SeedTwoPetScenario(db, pet1State: PetCareState.READY, pet2State: PetCareState.READY));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(AdminUser));

        // 1. Early handover pet 1 succeeds
        var firstRes = await client.PostAsJsonAsync("/api/admin/booking-pets/10/early-handover", new ReasonCommand
        {
            RequestId = Guid.NewGuid(),
            LyDo = "Khách đón bé Mochi trước"
        });
        firstRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await firstRes.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        firstBody!.Data!.NhanSom.Should().BeTrue();
        firstBody.Data.TrangThai.Should().Be("DONE");
        firstBody.Data.BanGiaoAt.Should().NotBeNull();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
            var booking = await db.DonDatLichSet.SingleAsync(b => b.Id == 1);
            booking.TrangThai.Should().Be(BookingState.IN_PROGRESS);
        }

        // 2. Early handover pet 2 (last remaining pet in clinic, unpaid) is blocked -> 409
        var secondRes = await client.PostAsJsonAsync("/api/admin/booking-pets/11/early-handover", new ReasonCommand
        {
            RequestId = Guid.NewGuid(),
            LyDo = "Thử nhận nốt bé cuối"
        });
        secondRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await secondRes.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>())!.ErrorCode.Should().Be("INVALID_TRANSITION");

        // 3. E47 cancel pet 2 (last remaining pet in clinic after pet 1 early handover, unpaid) is blocked -> 409
        var cancelSecondRes = await client.PostAsJsonAsync("/api/admin/booking-pets/11/cancel", new PetCancelRequest
        {
            RequestId = Guid.NewGuid(),
            LyDo = "Thử hủy bé cuối sau khi bé đầu nhận sớm",
            SoDemDaO = 0,
            MaThanhPhanDaLam = []
        });
        cancelSecondRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await cancelSecondRes.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>())!.ErrorCode.Should().Be("INVALID_TRANSITION");
    }

    [Fact]
    public async Task E48_NonAdminOrMissingPermission_Returns403Forbidden()
    {
        using var factory = CareTestSupport.Create(db =>
            SeedTwoPetScenario(db, pet1State: PetCareState.READY, pet2State: PetCareState.READY, grantStaffAdminPerms: true));
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(StaffWithPermsUser));
        var staffRes = await client.PostAsJsonAsync("/api/admin/booking-pets/10/early-handover", new ReasonCommand
        {
            RequestId = Guid.NewGuid(),
            LyDo = "Staff thử nhận sớm"
        });
        staffRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await staffRes.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>())!.ErrorCode.Should().Be("FORBIDDEN");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(StaffNoPermsUser));
        var noPermRes = await client.PostAsJsonAsync("/api/admin/booking-pets/10/early-handover", new ReasonCommand
        {
            RequestId = Guid.NewGuid(),
            LyDo = "Staff không quyền"
        });
        noPermRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ==================== E49 & E19 TESTS ====================

    [Fact]
    public async Task E19_And_E49_MixedBookingM204_CompletesOnlyAfterBothHandoverAndReturnCancelled()
    {
        using var factory = CareTestSupport.Create(db =>
            SeedTwoPetScenario(db,
                pet1State: PetCareState.READY,
                pet2State: PetCareState.CANCELLED,
                bookingState: BookingState.IN_PROGRESS,
                invoiceState: InvoiceState.PAID,
                pet2TraLaiAt: null,
                tongTamTinh: 200000m));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(StaffWithPermsUser));

        // 1. E19 handover pet 1 while cancelled pet 2 has TraLaiAt == null -> booking remains IN_PROGRESS
        var handoverRes = await client.PostAsJsonAsync("/api/internal/booking-pets/10/handover", new Handover
        {
            RequestId = Guid.NewGuid(),
            XacNhanDaNhan = true
        });
        handoverRes.StatusCode.Should().Be(HttpStatusCode.OK);
        (await handoverRes.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>())!.Data!.TrangThai.Should().Be("DONE");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
            var booking = await db.DonDatLichSet.SingleAsync(b => b.Id == 1);
            booking.TrangThai.Should().Be(BookingState.IN_PROGRESS);
            booking.HoanTatAt.Should().BeNull();
        }

        // 2. E49 return cancelled pet 2 -> pet stays CANCELLED, TraLaiAt set, booking transitions to COMPLETED
        var returnRes = await client.PostAsJsonAsync("/api/internal/booking-pets/11/return-cancelled",
            new BookingCommand(Guid.NewGuid()));
        returnRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var returnBody = await returnRes.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        returnBody!.Data!.TrangThai.Should().Be("CANCELLED");
        returnBody.Data.TraLaiAt.Should().NotBeNull();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
            var booking = await db.DonDatLichSet.SingleAsync(b => b.Id == 1);
            booking.TrangThai.Should().Be(BookingState.COMPLETED);
            booking.HoanTatAt.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task E49_AllPetsCancelledM205_TransitionsBookingToCancelledAfterLastReturn()
    {
        using var factory = CareTestSupport.Create(db =>
            SeedTwoPetScenario(db,
                pet1State: PetCareState.CANCELLED,
                pet2State: PetCareState.CANCELLED,
                bookingState: BookingState.IN_PROGRESS,
                invoiceState: InvoiceState.PAID,
                pet2TraLaiAt: null,
                tongTamTinh: 150000m));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(StaffWithPermsUser));

        var firstReturn = await client.PostAsJsonAsync("/api/internal/booking-pets/10/return-cancelled",
            new BookingCommand(Guid.NewGuid()));
        firstReturn.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
            (await db.DonDatLichSet.SingleAsync(b => b.Id == 1)).TrangThai.Should().Be(BookingState.IN_PROGRESS);
        }

        var secondReturn = await client.PostAsJsonAsync("/api/internal/booking-pets/11/return-cancelled",
            new BookingCommand(Guid.NewGuid()));
        secondReturn.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
            var booking = await db.DonDatLichSet.SingleAsync(b => b.Id == 1);
            booking.TrangThai.Should().Be(BookingState.CANCELLED);
            booking.HuyAt.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task E49_UnpaidWhenTongTamTinhPositive_OrAlreadyReturned_Returns409InvalidTransition()
    {
        using var factory = CareTestSupport.Create(db =>
            SeedTwoPetScenario(db,
                pet1State: PetCareState.CANCELLED,
                pet2State: PetCareState.CANCELLED,
                bookingState: BookingState.IN_PROGRESS,
                invoiceState: InvoiceState.UNPAID,
                pet2TraLaiAt: CareTestSupport.Now.AddMinutes(-5),
                tongTamTinh: 150000m));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(StaffWithPermsUser));

        // Pet 10 has TraLaiAt == null, but invoice is UNPAID and TongTamTinh > 0 -> 409
        var unpaidRes = await client.PostAsJsonAsync("/api/internal/booking-pets/10/return-cancelled",
            new BookingCommand(Guid.NewGuid()));
        unpaidRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await unpaidRes.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>())!.ErrorCode.Should().Be("INVALID_TRANSITION");

        // Pet 11 already has TraLaiAt != null -> 409
        var alreadyReturnedRes = await client.PostAsJsonAsync("/api/internal/booking-pets/11/return-cancelled",
            new BookingCommand(Guid.NewGuid()));
        alreadyReturnedRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await alreadyReturnedRes.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>())!.ErrorCode.Should().Be("INVALID_TRANSITION");
    }
}
