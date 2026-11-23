using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PetSpa.Api.Data;
using PetSpa.Api.Services.Identity;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Billing;
using PetSpa.Core.DTOs.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Scheduling;
using PetSpa.Core.Enums;
using PetSpa.Tests.Infrastructure;
using Xunit;

namespace PetSpa.Tests.Billing;

public sealed class ExtraChargesEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // =========================================================================
    // E20: Thêm dịch vụ đơn & combo phát sinh, giữ phút cùng ca, replay & guards
    // =========================================================================
    [Fact]
    public async Task AddExtraService_SingleAndComboInSameSlot_AddsMinutesWithoutExtraPetSeatAndSupportsReplay()
    {
        var seed = new SeedRefs();
        using var factory = AuthApplicationFactory.Create(db => SeedExtraChargeScenario(db, seed, PetCareState.IN_PROGRESS));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(seed.Staff!));

        // 1. Thêm dịch vụ lẻ phát sinh trong cùng ca (SlotDaDat ban đầu = 1, PhutDaGiu = 60, TranPhut = 240)
        var req1 = new
        {
            requestId = Guid.NewGuid(),
            maDichVu = seed.ExtraSingleServiceId,
            maKhungGio = seed.SlotId,
            lyDo = "Khách yêu cầu vệ sinh tai thêm"
        };
        using var resp1 = await client.PostAsJsonAsync($"/api/internal/booking-pets/{seed.BookingPetId}/extra-services", req1);
        resp1.StatusCode.Should().Be(HttpStatusCode.Created);
        var body1 = await resp1.Content.ReadFromJsonAsync<ApiResponse<OrderService>>(JsonOptions);
        body1!.Data!.SizeChot.Should().Be("M");
        body1.Data.GiaChot.Should().Be(80_000m);

        // Replay cùng requestId -> 200 OK, không nhân đôi dòng
        using var replay1 = await client.PostAsJsonAsync($"/api/internal/booking-pets/{seed.BookingPetId}/extra-services", req1);
        replay1.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Thêm Combo phát sinh (90 phút, 350.000đ) -> sinh kèm 2 thành phần con
        var reqCombo = new
        {
            requestId = Guid.NewGuid(),
            maCombo = seed.ComboId,
            maKhungGio = seed.SlotId,
            lyDo = "Nâng cấp thêm combo chăm sóc"
        };
        using var respCombo = await client.PostAsJsonAsync($"/api/internal/booking-pets/{seed.BookingPetId}/extra-services", reqCombo);
        respCombo.StatusCode.Should().Be(HttpStatusCode.Created);
        var bodyCombo = await respCombo.Content.ReadFromJsonAsync<ApiResponse<OrderService>>(JsonOptions);
        bodyCombo!.Data!.GiaChot.Should().Be(350_000m);
        bodyCombo.Data.ThanhPhan.Should().HaveCount(2);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
            var slot = await db.KhungGioDatLichSet.FindAsync(seed.SlotId);
            slot!.SlotDaDat.Should().Be(1); // Cùng khung giờ nên không tăng đầu pet
            slot.PhutDaGiu.Should().Be(60 + 30 + 90); // 180 phút

            var booking = await db.DonDatLichSet.FindAsync(seed.BookingId);
            booking!.TongTamTinh.Should().Be(200_000m + 80_000m + 350_000m);
        }

        // 3. Thêm tiếp dịch vụ 90 phút -> vượt trần 240 phút (180 + 90 = 270 > 240) -> 409 CAPACITY_EXCEEDED
        using var overCapResp = await client.PostAsJsonAsync($"/api/internal/booking-pets/{seed.BookingPetId}/extra-services", new
        {
            requestId = Guid.NewGuid(),
            maCombo = seed.ComboId,
            maKhungGio = seed.SlotId,
            lyDo = "Vượt phút"
        });
        overCapResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await overCapResp.Content.ReadFromJsonAsync<ApiResponse<OrderService>>(JsonOptions))!.ErrorCode.Should().Be("CAPACITY_EXCEEDED");
    }

    [Fact]
    public async Task AddExtraService_WhenPetReadyOrInvoiceExists_Returns409InvalidTransition()
    {
        var seed = new SeedRefs();
        using var factory = AuthApplicationFactory.Create(db => SeedExtraChargeScenario(db, seed, PetCareState.READY));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(seed.Staff!));

        using var resp = await client.PostAsJsonAsync($"/api/internal/booking-pets/{seed.BookingPetId}/extra-services", new
        {
            requestId = Guid.NewGuid(),
            maDichVu = seed.ExtraSingleServiceId,
            maKhungGio = seed.SlotId,
            lyDo = "Thêm trễ"
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await resp.Content.ReadFromJsonAsync<ApiResponse<OrderService>>(JsonOptions))!.ErrorCode.Should().Be("INVALID_TRANSITION");
    }

    // =========================================================================
    // E22a, E22b, E22c: Phụ phí gỡ rối lông (guard ROI_NANG / ảnh), phí chung đơn, sửa & hủy
    // =========================================================================
    [Fact]
    public async Task Fees_DetanglingGuardGeneralFeeEditAndCancel_UpdatesBookingTotalAndEnforcesRules()
    {
        var seed = new SeedRefs();
        using var factory = AuthApplicationFactory.Create(db => SeedExtraChargeScenario(db, seed, PetCareState.IN_PROGRESS, furJson: "[\"BINH_THUONG\"]"));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(seed.Staff!));

        // 1. Tạo phụ phí gỡ rối khi lông BINH_THUONG và chưa có ảnh care log -> 409 INVALID_TRANSITION
        using var blockedFeeResp = await client.PostAsJsonAsync($"/api/internal/bookings/{seed.BookingId}/fees", new
        {
            requestId = Guid.NewGuid(),
            tenPhuPhi = "Phụ phí gỡ rối lông",
            soTien = 100_000,
            lyDo = "Lông rối",
            maChiTietDon = seed.BookingPetId
        });
        blockedFeeResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await blockedFeeResp.Content.ReadFromJsonAsync<ApiResponse<FeeDto>>(JsonOptions))!.ErrorCode.Should().Be("INVALID_TRANSITION");

        // Bổ sung ảnh nhật ký chăm sóc chứng minh lông rối -> cho phép tạo phụ phí gỡ rối
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
            db.NhatKyChamSocSet.Add(new NhatKyChamSoc
            {
                LoaiNhatKy = "CARE_LOG",
                GhiChu = "Lông bết rối vùng bụng",
                ObjectPath = "care/proof-fur.jpg",
                GhiNhanAt = DateTimeOffset.UtcNow,
                ChiTietDonId = seed.BookingPetId,
                NhanVienId = seed.Staff!.Id
            });
            await db.SaveChangesAsync();
        }

        // 2. Tạo phụ phí gỡ rối hợp lệ -> 201 Created
        using var createDetangleResp = await client.PostAsJsonAsync($"/api/internal/bookings/{seed.BookingId}/fees", new
        {
            requestId = Guid.NewGuid(),
            tenPhuPhi = "Phụ phí gỡ rối lông",
            soTien = 100_000,
            lyDo = "Lông bết rối nhiều vùng bụng",
            maChiTietDon = seed.BookingPetId
        });
        createDetangleResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var detangleFee = (await createDetangleResp.Content.ReadFromJsonAsync<ApiResponse<FeeDto>>(JsonOptions))!.Data!;

        // 3. Tạo phụ phí chung toàn đơn (maChiTietDon không truyền) -> 201 Created
        using var createGeneralResp = await client.PostAsJsonAsync($"/api/internal/bookings/{seed.BookingId}/fees", new
        {
            requestId = Guid.NewGuid(),
            tenPhuPhi = "Phí đón trễ ngoài giờ",
            soTien = 50_000,
            lyDo = "Khách đón trễ 45 phút"
        });
        createGeneralResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var generalFee = (await createGeneralResp.Content.ReadFromJsonAsync<ApiResponse<FeeDto>>(JsonOptions))!.Data!;
        generalFee.MaChiTietDon.Should().BeNull();

        // 4. E22b: Sửa số tiền phụ phí gỡ rối từ 100.000đ lên 130.000đ -> 200 OK
        using var editResp = await client.PatchAsJsonAsync($"/api/internal/fees/{detangleFee.MaPhuPhi}", new
        {
            requestId = Guid.NewGuid(),
            soTien = 130_000,
            lyDo = "Điều chỉnh mức độ gỡ rối nặng hơn"
        });
        editResp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await editResp.Content.ReadFromJsonAsync<ApiResponse<FeeDto>>(JsonOptions))!.Data!.SoTien.Should().Be(130_000m);

        // 5. E22c: Hủy phụ phí chung 50.000đ -> 200 OK, HuyAt != null
        using var cancelResp = await client.PostAsJsonAsync($"/api/internal/fees/{generalFee.MaPhuPhi}/cancel", new
        {
            requestId = Guid.NewGuid(),
            lyDo = "Miễn phí đón trễ cho khách thân thiết"
        });
        cancelResp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await cancelResp.Content.ReadFromJsonAsync<ApiResponse<FeeDto>>(JsonOptions))!.Data!.HuyAt.Should().NotBeNull();

        // Kiểm tra TongTamTinh = 200.000 (gốc) + 130.000 (gỡ rối đã sửa) = 330.000đ
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
            var booking = await db.DonDatLichSet.FindAsync(seed.BookingId);
            booking!.TongTamTinh.Should().Be(330_000m);
        }
    }

    // =========================================================================
    // FR01: Thu hồi quyền INVOICE_CREATE -> 403 FORBIDDEN trên cả E20, E22a, E22b, E22c
    // =========================================================================
    [Fact]
    public async Task ExtraCharges_WhenPermissionRevoked_Returns403Forbidden()
    {
        var seed = new SeedRefs();
        using var factory = AuthApplicationFactory.Create(db => SeedExtraChargeScenario(db, seed, PetCareState.IN_PROGRESS, grantPermission: false));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(seed.Staff!));

        using var r1 = await client.PostAsJsonAsync($"/api/internal/booking-pets/{seed.BookingPetId}/extra-services", new
        {
            requestId = Guid.NewGuid(),
            maDichVu = seed.ExtraSingleServiceId,
            maKhungGio = seed.SlotId,
            lyDo = "Test"
        });
        using var r2 = await client.PostAsJsonAsync($"/api/internal/bookings/{seed.BookingId}/fees", new
        {
            requestId = Guid.NewGuid(),
            tenPhuPhi = "Phí test",
            soTien = 50_000,
            lyDo = "Test"
        });
        using var r3 = await client.PatchAsJsonAsync("/api/internal/fees/1", new
        {
            requestId = Guid.NewGuid(),
            soTien = 60_000,
            lyDo = "Test"
        });
        using var r4 = await client.PostAsJsonAsync("/api/internal/fees/1/cancel", new
        {
            requestId = Guid.NewGuid(),
            lyDo = "Test"
        });

        foreach (var resp in new[] { r1, r2, r3, r4 })
        {
            resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            var body = await resp.Content.ReadFromJsonAsync<ApiResponse<object>>(JsonOptions);
            body!.ErrorCode.Should().Be("FORBIDDEN");
        }
    }

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

    private static void SeedExtraChargeScenario(
        PetSpaDbContext db,
        SeedRefs refs,
        PetCareState petCareState,
        string furJson = "[\"ROI_NANG\"]",
        bool grantPermission = true)
    {
        var now = new DateTimeOffset(2026, 11, 25, 1, 0, 0, TimeSpan.Zero); // 08:00 GMT+7

        db.QuyenSet.AddRange(
            new Quyen { MaQuyen = "INVOICE_CREATE", TenQuyen = "Lập hóa đơn & Phát sinh" },
            new Quyen { MaQuyen = "CARE_UPDATE", TenQuyen = "Cập nhật chăm sóc" });

        var staff = new NhanVien
        {
            TenDangNhap = "staff_extra",
            MatKhauHash = "hash",
            TenNhanVien = "Lễ tân Phát sinh",
            VaiTro = Role.STAFF,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        var groomer = new NhanVien
        {
            TenDangNhap = "groomer_extra",
            MatKhauHash = "hash",
            TenNhanVien = "KTV Chăm sóc",
            VaiTro = Role.STAFF,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.NhanVienSet.AddRange(staff, groomer);
        db.SaveChanges();

        if (grantPermission)
        {
            db.NhanVienQuyenSet.Add(new NhanVienQuyen
            {
                NhanVienId = staff.Id,
                MaQuyen = "INVOICE_CREATE",
                CapBoiId = staff.Id,
                CapAt = now
            });
        }
        db.NhanVienQuyenSet.Add(new NhanVienQuyen
        {
            NhanVienId = groomer.Id,
            MaQuyen = "CARE_UPDATE",
            CapBoiId = staff.Id,
            CapAt = now
        });

        var shift = new CaLamViec
        {
            TenCa = "Ca Sáng",
            GioBatDau = new TimeOnly(8, 0),
            GioKetThuc = new TimeOnly(12, 0),
            SucChuaPet = 10,
            TranPhut = 240,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.CaLamViecSet.Add(shift);
        db.SaveChanges();

        var serviceDay = new DateOnly(2026, 11, 25);
        db.DangKyCaSet.Add(new DangKyCa
        {
            Ngay = serviceDay,
            TrangThai = ShiftRegistrationState.APPROVED,
            NhanVienId = groomer.Id,
            CaLamViecId = shift.Id,
            DuyetBoiId = staff.Id,
            DuyetAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });

        var slot = new KhungGioDatLich
        {
            Ngay = serviceDay,
            CaLamViecId = shift.Id,
            MoNhanKhach = true,
            TongSlot = 10,
            SlotDaDat = 1,
            TranPhut = 240,
            PhutDaGiu = 60,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.KhungGioDatLichSet.Add(slot);

        var mainService = new DichVu
        {
            TenDichVu = "Tắm sấy vệ sinh",
            LoaiDichVu = "SPA",
            ThoiLuongPhut = 60,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        var extraService = new DichVu
        {
            TenDichVu = "Vệ sinh tai chuyên sâu",
            LoaiDichVu = "SPA",
            ThoiLuongPhut = 30,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.DichVuSet.AddRange(mainService, extraService);
        db.SaveChanges();

        var combo = new Combo
        {
            TenCombo = "Combo Chăm Sóc Toàn Diện",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
            ChiTietComboComboItems =
            [
                new ChiTietCombo { DichVuId = mainService.Id },
                new ChiTietCombo { DichVuId = extraService.Id }
            ]
        };
        db.ComboSet.Add(combo);
        db.SaveChanges();

        db.BangGiaSizeSet.AddRange(
            new BangGiaSize { DichVuId = mainService.Id, SizeId = (int)SizeBand.M, DonGia = 200_000m, CreatedAt = now, UpdatedAt = now },
            new BangGiaSize { DichVuId = extraService.Id, SizeId = (int)SizeBand.M, DonGia = 80_000m, CreatedAt = now, UpdatedAt = now },
            new BangGiaSize { ComboId = combo.Id, SizeId = (int)SizeBand.M, DonGia = 350_000m, CreatedAt = now, UpdatedAt = now });
        var customer = new KhachHang
        {
            HoTen = "Lê Văn C",
            SoDienThoai = "0908111222",
            Email = "khachc@example.com",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.KhachHangSet.Add(customer);
        db.SaveChanges();

        var pet = new ThuCung
        {
            TenThuCung = "Coca",
            LoaiPet = PetType.CHO,
            CanNang = 9m,
            KhachHangId = customer.Id,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.ThuCungSet.Add(pet);

        var booking = new DonDatLich
        {
            TrackingCode = "TRK-EXT1",
            KhungGioId = slot.Id,
            RequestId = Guid.NewGuid(),
            BatDauDuKien = now,
            KetThucDuKien = now.AddHours(3),
            TrangThai = BookingState.IN_PROGRESS,
            TenNguoiDat = customer.HoTen,
            SoDienThoaiSnapshot = customer.SoDienThoai,
            EmailSnapshot = customer.Email,
            TongTamTinh = 200_000m,
            KhachHangId = customer.Id,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.DonDatLichSet.Add(booking);
        db.SaveChanges();

        var bookingPet = new ChiTietDon
        {
            TenPetSnapshot = pet.TenThuCung,
            CanNangKhaiBao = 9m,
            CanNangChot = 9m,
            SizeThucTe = SizeBand.M,
            SizeChot = SizeBand.M,
            TinhTrangLong = furJson,
            TrangThaiChamSoc = petCareState,
            CheckInAt = now,
            DonDatLichId = booking.Id,
            ThuCungId = pet.Id,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.ChiTietDonSet.Add(bookingPet);
        db.SaveChanges();

        db.DichVuTrongDonSet.Add(new DichVuTrongDon
        {
            TenSnapshot = mainService.TenDichVu,
            RequestId = Guid.NewGuid(),
            ThoiLuongSnapshot = 60,
            BangGiaSizeSnapshot = "{\"m\":200000}",
            TrangThai = OrderServiceState.CHO_THUC_HIEN,
            SizeChot = SizeBand.M,
            GiaChot = 200_000m,
            ChiTietDonId = bookingPet.Id,
            DichVuId = mainService.Id,
            KhungGioId = slot.Id,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.SaveChanges();

        refs.Staff = staff;
        refs.BookingId = booking.Id;
        refs.BookingPetId = bookingPet.Id;
        refs.SlotId = slot.Id;
        refs.ExtraSingleServiceId = extraService.Id;
        refs.ComboId = combo.Id;
    }

    private sealed class SeedRefs
    {
        public NhanVien? Staff { get; set; }
        public int BookingId { get; set; }
        public int BookingPetId { get; set; }
        public int SlotId { get; set; }
        public int ExtraSingleServiceId { get; set; }
        public int ComboId { get; set; }
    }
}