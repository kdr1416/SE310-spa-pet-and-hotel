using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PetSpa.Api.Data;
using PetSpa.Api.Services.Identity;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Billing;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Scheduling;
using PetSpa.Core.Enums;
using PetSpa.Tests.Infrastructure;
using Xunit;

namespace PetSpa.Tests.Billing;

public class InvoicesEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task CreateInvoice_WhenPetsAreReady_Returns201UnpaidWithSnapshotLines()
    {
        var seed = new SeedRefs();
        using var factory = AuthApplicationFactory.Create(db => SeedBillingScenario(db, seed, PetCareState.READY));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(seed.Cashier!));

        var response = await client.PostAsync($"/api/internal/bookings/{seed.BookingId}/invoices", null);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<InvoiceResponseDto>>(JsonOptions);
        body.Should().NotBeNull();
        body!.Success.Should().BeTrue();
        body.Data.Should().NotBeNull();
        body.Data!.TrangThai.Should().Be("UNPAID");
        body.Data.TongTien.Should().Be(200_000m);
        body.Data.ConPhaiTra.Should().Be(200_000m);
        body.Data.ChiTiet.Should().ContainSingle();
        body.Data.ChiTiet[0].SizeSnapshot.Should().Be("M");
        body.Data.ChiTiet[0].DonGiaSnapshot.Should().Be(200_000m);
    }

    [Fact]
    public async Task CreateInvoice_WhenPetNotReady_Returns409InvalidTransition()
    {
        var seed = new SeedRefs();
        using var factory = AuthApplicationFactory.Create(db => SeedBillingScenario(db, seed, PetCareState.IN_PROGRESS));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(seed.Cashier!));

        var response = await client.PostAsync($"/api/internal/bookings/{seed.BookingId}/invoices", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<InvoiceResponseDto>>(JsonOptions);
        body!.ErrorCode.Should().Be("INVALID_TRANSITION");
    }

    [Fact]
    public async Task GetInvoice_WhenExists_Returns200WithLineItems()
    {
        var seed = new SeedRefs();
        using var factory = AuthApplicationFactory.Create(db => SeedBillingScenario(db, seed, PetCareState.READY));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(seed.Cashier!));

        var createResp = await client.PostAsync($"/api/internal/bookings/{seed.BookingId}/invoices", null);
        var created = await createResp.Content.ReadFromJsonAsync<ApiResponse<InvoiceResponseDto>>(JsonOptions);

        var getResp = await client.GetAsync($"/api/internal/invoices/{created!.Data!.Id}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await getResp.Content.ReadFromJsonAsync<ApiResponse<InvoiceResponseDto>>(JsonOptions);
        body!.Data!.Id.Should().Be(created.Data.Id);
        body.Data.ChiTiet.Should().ContainSingle();
    }

    [Fact]
    public async Task CheckoutInvoice_WhenExactAmount_Returns200Paid_AndSupportsIdempotentRetry()
    {
        var seed = new SeedRefs();
        using var factory = AuthApplicationFactory.Create(db => SeedBillingScenario(db, seed, PetCareState.READY));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(seed.Cashier!));

        var createResp = await client.PostAsync($"/api/internal/bookings/{seed.BookingId}/invoices", null);
        var created = await createResp.Content.ReadFromJsonAsync<ApiResponse<InvoiceResponseDto>>(JsonOptions);
        var invoiceId = created!.Data!.Id;

        var requestId = Guid.NewGuid();
        var checkoutReq = new CheckoutRequestDto
        {
            RequestId = requestId,
            SoTienThucNhan = 250_000m,
            SoTienDaTraLai = 50_000m,
            PhuongThuc = PaymentMethod.CASH,
            GhiChu = "Thu đủ tiền mặt"
        };

        var checkoutResp = await client.PostAsJsonAsync($"/api/internal/invoices/{invoiceId}/checkout", checkoutReq, JsonOptions);
        checkoutResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var paidBody = await checkoutResp.Content.ReadFromJsonAsync<ApiResponse<InvoiceResponseDto>>(JsonOptions);
        paidBody!.Data!.TrangThai.Should().Be("PAID");
        paidBody.Data.PaidAt.Should().NotBeNull();

        // Idempotent retry with same requestId & payload -> 200 OK, no duplicate GiaoDichThu
        var retryResp = await client.PostAsJsonAsync($"/api/internal/invoices/{invoiceId}/checkout", checkoutReq, JsonOptions);
        retryResp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
        var txCount = await db.GiaoDichThuSet.CountAsync(x => x.HoaDonId == invoiceId);
        txCount.Should().Be(1);

        // Same requestId with different payload -> 409 IDEMPOTENCY_CONFLICT
        var conflictReq = new CheckoutRequestDto
        {
            RequestId = requestId,
            SoTienThucNhan = 200_000m,
            SoTienDaTraLai = 0m,
            PhuongThuc = PaymentMethod.BANK_TRANSFER
        };
        var conflictResp = await client.PostAsJsonAsync($"/api/internal/invoices/{invoiceId}/checkout", conflictReq, JsonOptions);
        conflictResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var conflictBody = await conflictResp.Content.ReadFromJsonAsync<ApiResponse<InvoiceResponseDto>>(JsonOptions);
        conflictBody!.ErrorCode.Should().Be("IDEMPOTENCY_CONFLICT");

        // New requestId on already PAID invoice -> 409 INVALID_TRANSITION
        var secondReq = new CheckoutRequestDto
        {
            RequestId = Guid.NewGuid(),
            SoTienThucNhan = 200_000m,
            SoTienDaTraLai = 0m,
            PhuongThuc = PaymentMethod.CASH
        };
        var secondResp = await client.PostAsJsonAsync($"/api/internal/invoices/{invoiceId}/checkout", secondReq, JsonOptions);
        secondResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var secondBody = await secondResp.Content.ReadFromJsonAsync<ApiResponse<InvoiceResponseDto>>(JsonOptions);
        secondBody!.ErrorCode.Should().Be("INVALID_TRANSITION");
    }

    [Fact]
    public async Task CheckoutInvoice_WhenUnderpaid_Returns409AmountMismatch_AndKeepsUnpaid()
    {
        var seed = new SeedRefs();
        using var factory = AuthApplicationFactory.Create(db => SeedBillingScenario(db, seed, PetCareState.READY));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(seed.Cashier!));

        var createResp = await client.PostAsync($"/api/internal/bookings/{seed.BookingId}/invoices", null);
        var created = await createResp.Content.ReadFromJsonAsync<ApiResponse<InvoiceResponseDto>>(JsonOptions);
        var invoiceId = created!.Data!.Id;

        var underpayReq = new CheckoutRequestDto
        {
            RequestId = Guid.NewGuid(),
            SoTienThucNhan = 150_000m,
            SoTienDaTraLai = 0m,
            PhuongThuc = PaymentMethod.CASH
        };

        var resp = await client.PostAsJsonAsync($"/api/internal/invoices/{invoiceId}/checkout", underpayReq, JsonOptions);
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<InvoiceResponseDto>>(JsonOptions);
        body!.ErrorCode.Should().Be("AMOUNT_MISMATCH");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
        var invoice = await db.HoaDonSet.FindAsync(invoiceId);
        invoice!.TrangThai.Should().Be(InvoiceState.UNPAID);
    }

    [Fact]
    public async Task CreateInvoice_WithoutPermission_Returns403Forbidden()
    {
        var seed = new SeedRefs();
        using var factory = AuthApplicationFactory.Create(db => SeedBillingScenario(db, seed, PetCareState.READY, grantPermissions: false));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(seed.Cashier!));

        var response = await client.PostAsync($"/api/internal/bookings/{seed.BookingId}/invoices", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<InvoiceResponseDto>>(JsonOptions);
        body!.ErrorCode.Should().Be("FORBIDDEN");
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

    private static void SeedBillingScenario(
        PetSpaDbContext db,
        SeedRefs refs,
        PetCareState petCareState,
        bool grantPermissions = true)
    {
        var now = DateTimeOffset.UtcNow;

        var permCreate = new Quyen { MaQuyen = "INVOICE_CREATE", TenQuyen = "Lập hóa đơn" };
        var permCollect = new Quyen { MaQuyen = "INVOICE_COLLECT", TenQuyen = "Thu tiền hóa đơn" };
        db.QuyenSet.AddRange(permCreate, permCollect);

        var cashier = new NhanVien
        {
            TenDangNhap = "cashier1",
            MatKhauHash = "hash",
            TenNhanVien = "Thu ngân",
            VaiTro = Role.STAFF,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.NhanVienSet.Add(cashier);
        db.SaveChanges();

        if (grantPermissions)
        {
            db.NhanVienQuyenSet.AddRange(
                new NhanVienQuyen { NhanVienId = cashier.Id, MaQuyen = "INVOICE_CREATE", CapBoiId = cashier.Id, CapAt = now },
                new NhanVienQuyen { NhanVienId = cashier.Id, MaQuyen = "INVOICE_COLLECT", CapBoiId = cashier.Id, CapAt = now }
            );
        }

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

        var slot = new KhungGioDatLich
        {
            Ngay = new DateOnly(2026, 11, 20),
            CaLamViecId = shift.Id,
            MoNhanKhach = true,
            TongSlot = 10,
            TranPhut = 240,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.KhungGioDatLichSet.Add(slot);

        var service = new DichVu
        {
            TenDichVu = "Tắm sấy vệ sinh",
            LoaiDichVu = "SPA",
            ThoiLuongPhut = 60,
            YeuCauTiemChung = false,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.DichVuSet.Add(service);

        var customer = new KhachHang
        {
            HoTen = "Nguyễn Văn A",
            SoDienThoai = "0901234567",
            Email = "khach@example.com",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.KhachHangSet.Add(customer);
        db.SaveChanges();

        var pet = new ThuCung
        {
            TenThuCung = "Milo",
            LoaiPet = PetType.CHO,
            CanNang = 8.5m,
            KhachHangId = customer.Id,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.ThuCungSet.Add(pet);

        var booking = new DonDatLich
        {
            TrackingCode = "TRK-INV1",
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
            CanNangChot = 8.5m,
            SizeThucTe = SizeBand.M,
            SizeChot = SizeBand.M,
            TrangThaiChamSoc = petCareState,
            DonDatLichId = booking.Id,
            ThuCungId = pet.Id,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.ChiTietDonSet.Add(bookingPet);
        db.SaveChanges();

        db.DichVuTrongDonSet.Add(new DichVuTrongDon
        {
            TenSnapshot = service.TenDichVu,
            TrangThai = OrderServiceState.HOAN_TAT,
            SizeChot = SizeBand.M,
            GiaChot = 200_000m,
            ChiTietDonId = bookingPet.Id,
            DichVuId = service.Id,
            KhungGioId = slot.Id,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.SaveChanges();

        refs.Cashier = cashier;
        refs.BookingId = booking.Id;
    }

    private sealed class SeedRefs
    {
        public NhanVien? Cashier { get; set; }
        public int BookingId { get; set; }
    }
}
