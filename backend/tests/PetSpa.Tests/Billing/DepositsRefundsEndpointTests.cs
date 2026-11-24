using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Data.Common;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PetSpa.Api.Data;
using PetSpa.Api.Services.Booking;
using PetSpa.Api.Services.Identity;
using PetSpa.Api.Services.Storage;
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

public sealed class DepositsRefundsEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ConfirmReceipt_ConcurrentBankReferenceAcrossBookings_ReturnsConflictWithoutDoubleCredit()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var clock = new MutableClock(new DateTimeOffset(2026, 11, 23, 8, 0, 0, TimeSpan.Zero));
        var seed = new SeedRefs();
        int secondDepositId;
        await using (var db = database.CreateContext())
        {
            SeedDepositScenario(db, seed, clock.GetUtcNow(), 300_000m);
            var first = await db.DonDatLichSet.FindAsync(seed.BookingId);
            var firstSlot = await db.KhungGioDatLichSet.FindAsync(seed.SlotId);
            var secondSlot = new KhungGioDatLich
            {
                Ngay = firstSlot!.Ngay.AddDays(1), CaLamViecId = firstSlot.CaLamViecId,
                MoNhanKhach = true, TongSlot = 10, TranPhut = 240
            };
            db.KhungGioDatLichSet.Add(secondSlot);
            await db.SaveChangesAsync();
            var second = new DonDatLich
            {
                TrackingCode = "TRK-DEP2", RequestId = Guid.NewGuid(), KhungGioId = secondSlot.Id,
                KhachHangId = first!.KhachHangId, TenNguoiDat = first.TenNguoiDat,
                SoDienThoaiSnapshot = first.SoDienThoaiSnapshot, EmailSnapshot = first.EmailSnapshot,
                BatDauDuKien = first.BatDauDuKien.AddDays(1), KetThucDuKien = first.KetThucDuKien.AddDays(1),
                TrangThai = BookingState.PENDING, TongTamTinh = 1_000_000m
            };
            db.DonDatLichSet.Add(second);
            await db.SaveChangesAsync();
            var deposit = new TienCoc
            {
                DonDatLichId = second.Id, CoSoTinhSnapshot = 1_000_000m, TyLe = 0.30m,
                SoTienYeuCau = 300_000m, HetHanAt = clock.GetUtcNow().AddMinutes(30),
                TrangThaiYeuCau = DepositRequestState.CHO_THU
            };
            db.TienCocSet.Add(deposit);
            await db.SaveChangesAsync();
            secondDepositId = deposit.Id;
        }
        var barrier = new BankReferenceReadBarrier();
        using var factory = new PostgresWebApplicationFactory(database.ConnectionString).WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
                services.AddDbContext<PetSpaDbContext>(options => options.AddInterceptors(barrier));
            }));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(seed.Staff!));
        Task<HttpResponseMessage> Receipt(int id) => client.PostAsJsonAsync($"/api/internal/deposits/{id}/receipts", new
        {
            requestId = Guid.NewGuid(), soTien = 100_000, phuongThuc = "BANK_TRANSFER",
            giaoDichAt = clock.GetUtcNow(), nganHang = "VCB", maGiaoDich = "SAME-BANK-REFERENCE"
        });
        var responses = await Task.WhenAll(Receipt(seed.DepositId), Receipt(secondDepositId));
        try
        {
            barrier.Arrivals.Should().Be(2);
            responses.Select(response => response.StatusCode).Should().BeEquivalentTo(
                new[] { HttpStatusCode.Created, HttpStatusCode.Conflict });
            var conflict = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
            (await conflict.Content.ReadFromJsonAsync<ApiResponse<DepositDto>>(JsonOptions))!.ErrorCode.Should().Be("INVALID_TRANSITION");
            await using var verify = database.CreateContext();
            (await verify.GiaoDichThuSet.CountAsync()).Should().Be(1);
            (await verify.GiaoDichThuSet.SumAsync(receipt => receipt.SoTien)).Should().Be(100_000m);
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Theory]
    [InlineData("create")]
    [InlineData("confirm")]
    [InlineData("list")]
    public async Task Refunds_StaffWithRefundPermissionCannotUseAdminOperations(string operation)
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 11, 23, 8, 0, 0, TimeSpan.Zero));
        var seed = new SeedRefs();
        using var factory = CreateFactory(clock, db =>
        {
            SeedDepositScenario(db, seed, clock.GetUtcNow(), 300_000m);
            db.NhanVienQuyenSet.Add(new NhanVienQuyen
            {
                NhanVienId = seed.Staff!.Id, MaQuyen = "DEPOSIT_REFUND", CapBoiId = seed.Admin!.Id, CapAt = clock.GetUtcNow()
            });
            var booking = db.DonDatLichSet.Find(seed.BookingId)!;
            booking.TrangThai = BookingState.IN_PROGRESS;
            booking.TongTamTinh = 180_000m;
            db.GiaoDichThuSet.Add(new GiaoDichThu
            {
                RequestId = Guid.NewGuid(), SoTien = 300_000m, PhuongThuc = PaymentMethod.CASH,
                GiaoDichAt = clock.GetUtcNow(), XacNhanAt = clock.GetUtcNow(),
                TienCocId = seed.DepositId, XacNhanBoiId = seed.Staff.Id
            });
            db.SaveChanges();
        }, new FakeFileStorageService(), new FakeEmailSender());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(seed.Admin!));
        using var created = await client.PostAsJsonAsync($"/api/admin/deposits/{seed.DepositId}/refunds",
            new { requestId = Guid.NewGuid(), soTien = 50_000, lyDo = "Hoàn cọc dư" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var refund = (await created.Content.ReadFromJsonAsync<ApiResponse<RefundDto>>(JsonOptions))!.Data!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(seed.Staff!));
        using var response = operation switch
        {
            "create" => await client.PostAsJsonAsync($"/api/admin/deposits/{seed.DepositId}/refunds",
                new { requestId = Guid.NewGuid(), soTien = 50_000, lyDo = "Hoàn thêm cọc dư" }),
            "confirm" => await client.PostAsJsonAsync($"/api/admin/refunds/{refund.MaHoanCoc}/confirm",
                new { requestId = Guid.NewGuid(), phuongThuc = "CASH", hoanAt = clock.GetUtcNow(), thamChieu = "counter-proof" }),
            _ => await client.GetAsync("/api/admin/refunds")
        };
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadFromJsonAsync<ApiResponse<object>>(JsonOptions))!.ErrorCode.Should().Be("FORBIDDEN");
        using var scope = factory.Services.CreateScope();
        var verify = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
        (await verify.HoanCocSet.SingleAsync()).TrangThai.Should().Be(RefundState.CHO_HOAN);
    }

    // =========================================================================
    // E42: Thu cọc 30% (đủ, thiếu, cộng dồn, trùng mã GD, replay idempotent)
    // =========================================================================
    [Fact]
    public async Task ConfirmReceipt_PartialAndFullWithinDeadline_AccumulatesAndCompletesDeposit()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 11, 23, 8, 0, 0, TimeSpan.Zero));
        var seed = new SeedRefs();
        using var factory = CreateFactory(clock, db => SeedDepositScenario(db, seed, clock.GetUtcNow(), requiredDeposit: 300_000m));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(seed.Staff!));

        // Lần 1: Nộp thiếu 100.000đ -> giữ trạng thái CHO_THU, không đổi HetHanAt
        var req1 = new
        {
            requestId = Guid.NewGuid(),
            soTien = 100_000,
            phuongThuc = "BANK_TRANSFER",
            giaoDichAt = clock.GetUtcNow().AddMinutes(5),
            nganHang = "VCB",
            maGiaoDich = "TX-001"
        };
        using var resp1 = await client.PostAsJsonAsync($"/api/internal/deposits/{seed.DepositId}/receipts", req1);
        resp1.StatusCode.Should().Be(HttpStatusCode.Created);
        var body1 = await resp1.Content.ReadFromJsonAsync<ApiResponse<DepositDto>>(JsonOptions);
        body1!.Data!.TrangThaiYeuCau.Should().Be("CHO_THU");
        body1.Data.ThucNhan.Should().Be(100_000m);
        body1.Data.HetHanAt.Should().Be(clock.GetUtcNow().AddMinutes(30));

        // Trùng mã giao dịch (VCB, TX-001) với requestId mới -> 409 INVALID_TRANSITION (M2-L05)
        var dupTxReq = new
        {
            requestId = Guid.NewGuid(),
            soTien = 200_000,
            phuongThuc = "BANK_TRANSFER",
            giaoDichAt = clock.GetUtcNow().AddMinutes(10),
            nganHang = "VCB",
            maGiaoDich = "TX-001"
        };
        using var dupResp = await client.PostAsJsonAsync($"/api/internal/deposits/{seed.DepositId}/receipts", dupTxReq);
        dupResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await dupResp.Content.ReadFromJsonAsync<ApiResponse<DepositDto>>(JsonOptions))!.ErrorCode.Should().Be("INVALID_TRANSITION");

        // Lần 2: Nộp nốt 200.000đ trước hạn -> đủ 300.000đ -> DA_DAP_UNG
        var req2 = new
        {
            requestId = Guid.NewGuid(),
            soTien = 200_000,
            phuongThuc = "BANK_TRANSFER",
            giaoDichAt = clock.GetUtcNow().AddMinutes(15),
            nganHang = "VCB",
            maGiaoDich = "TX-002"
        };
        using var resp2 = await client.PostAsJsonAsync($"/api/internal/deposits/{seed.DepositId}/receipts", req2);
        resp2.StatusCode.Should().Be(HttpStatusCode.Created);
        var body2 = await resp2.Content.ReadFromJsonAsync<ApiResponse<DepositDto>>(JsonOptions);
        body2!.Data!.TrangThaiYeuCau.Should().Be("DA_DAP_UNG");
        body2.Data.ThucNhan.Should().Be(300_000m);

        // Replay cùng requestId của lần 2 -> 200 OK, không nhân đôi GiaoDichThu
        using var replayResp = await client.PostAsJsonAsync($"/api/internal/deposits/{seed.DepositId}/receipts", req2);
        replayResp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
        (await db.GiaoDichThuSet.CountAsync(x => x.TienCocId == seed.DepositId)).Should().Be(2);
    }

    // =========================================================================
    // E42 quá hạn 30 phút (M2-L06): Chạy trên PostgresTestDatabase để dùng Transaction + FOR UPDATE
    // =========================================================================
    [Fact]
    public async Task ConfirmReceipt_WhenOverdueWithPriorPartialPayment_CancelsBookingReleasesCapacityAndCreatesFullRefund()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 11, 23, 8, 0, 0, TimeSpan.Zero));
        var seed = new SeedRefs();
        using var factory = CreateFactory(clock, db => SeedDepositScenario(db, seed, clock.GetUtcNow(), requiredDeposit: 300_000m));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(seed.Staff!));

        // 1. Nộp thiếu 100.000đ lúc +10 phút (trong hạn)
        using var partialResp = await client.PostAsJsonAsync($"/api/internal/deposits/{seed.DepositId}/receipts", new
        {
            requestId = Guid.NewGuid(),
            soTien = 100_000,
            phuongThuc = "BANK_TRANSFER",
            giaoDichAt = clock.GetUtcNow().AddMinutes(10),
            nganHang = "ACB",
            maGiaoDich = "ACB-100"
        });
        partialResp.StatusCode.Should().Be(HttpStatusCode.Created);

        // 2. Tua đồng hồ qua mốc 30 phút (+31 phút) rồi biên lai 200.000đ mới về
        clock.Advance(TimeSpan.FromMinutes(31));
        using var lateResp = await client.PostAsJsonAsync($"/api/internal/deposits/{seed.DepositId}/receipts", new
        {
            requestId = Guid.NewGuid(),
            soTien = 200_000,
            phuongThuc = "BANK_TRANSFER",
            giaoDichAt = clock.GetUtcNow(),
            nganHang = "ACB",
            maGiaoDich = "ACB-200"
        });

        lateResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await lateResp.Content.ReadFromJsonAsync<ApiResponse<DepositDto>>(JsonOptions);
        body!.Data!.TrangThaiYeuCau.Should().Be("HET_HAN");
        body.Data.ThucNhan.Should().Be(300_000m);
        body.Data.GiuLai.Should().Be(0m);
        body.Data.DanhHoan.Should().Be(300_000m);
        body.Data.HoanCoc.Should().ContainSingle(r => r.TrangThai == "CHO_HOAN" && r.SoTien == 300_000m);

        using var scope = factory.Services.CreateScope();
        var verifyDb = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
        var booking = await verifyDb.DonDatLichSet.FindAsync(seed.BookingId);
        booking!.TrangThai.Should().Be(BookingState.CANCELLED);
        var slot = await verifyDb.KhungGioDatLichSet.FindAsync(seed.SlotId);
        slot!.SlotDaDat.Should().Be(0);
        slot.PhutDaGiu.Should().Be(0);
    }

    // =========================================================================
    // E44, E45, E46: Tạo hoàn cọc (chặn vượt hạn mức), xác nhận (JSON + Multipart) & phân trang
    // =========================================================================
    [Fact]
    public async Task Refunds_CreateConfirmWithMultipartAndPagination_EnforcesConservationAndCompletes()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 11, 23, 8, 0, 0, TimeSpan.Zero));
        var seed = new SeedRefs();
        var storage = new FakeFileStorageService();
        var email = new FakeEmailSender();

        using var factory = CreateFactory(clock, db =>
        {
            SeedDepositScenario(db, seed, clock.GetUtcNow(), requiredDeposit: 300_000m);
            var booking = db.DonDatLichSet.Find(seed.BookingId)!;
            booking.TrangThai = BookingState.IN_PROGRESS;
            booking.TongTamTinh = 180_000m;

            var deposit = db.TienCocSet.Find(seed.DepositId)!;
            deposit.TrangThaiYeuCau = DepositRequestState.DA_DAP_UNG;
            deposit.KetThucAt = clock.GetUtcNow();
            db.GiaoDichThuSet.Add(new GiaoDichThu
            {
                RequestId = Guid.NewGuid(),
                SoTien = 300_000m,
                PhuongThuc = PaymentMethod.BANK_TRANSFER,
                NganHang = "VCB",
                MaGiaoDich = "VCB-300",
                GiaoDichAt = clock.GetUtcNow(),
                XacNhanAt = clock.GetUtcNow(),
                TienCocId = deposit.Id,
                XacNhanBoiId = seed.Staff!.Id,
                CreatedAt = clock.GetUtcNow(),
                UpdatedAt = clock.GetUtcNow()
            });
            db.SaveChanges();
        }, storage, email);

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(seed.Admin!));

        // 1. E44: Tạo hoàn vượt quá phần cọc dư 120.000đ -> 409 AMOUNT_MISMATCH
        using var overResp = await client.PostAsJsonAsync($"/api/admin/deposits/{seed.DepositId}/refunds", new
        {
            requestId = Guid.NewGuid(),
            soTien = 150_000,
            lyDo = "Hoàn vượt mức"
        });
        overResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await overResp.Content.ReadFromJsonAsync<ApiResponse<RefundDto>>(JsonOptions))!.ErrorCode.Should().Be("AMOUNT_MISMATCH");

        // 2. E44: Tạo hoàn hợp lệ 120.000đ -> 201 Created
        var createRefundReq = new
        {
            requestId = Guid.NewGuid(),
            soTien = 120_000,
            lyDo = "Hoàn phần cọc dư do giảm dịch vụ"
        };
        using var createResp = await client.PostAsJsonAsync($"/api/admin/deposits/{seed.DepositId}/refunds", createRefundReq);
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdRefund = (await createResp.Content.ReadFromJsonAsync<ApiResponse<RefundDto>>(JsonOptions))!.Data!;
        createdRefund.TrangThai.Should().Be("CHO_HOAN");
        createdRefund.SoTien.Should().Be(120_000m);

        // 3. E45: Xác nhận hoàn cọc kèm file ảnh bằng chứng multipart/form-data -> 200 OK (DA_HOAN)
        using var multipart = new MultipartFormDataContent();
        var confirmPayload = JsonSerializer.Serialize(new
        {
            requestId = Guid.NewGuid(),
            phuongThuc = "BANK_TRANSFER",
            hoanAt = clock.GetUtcNow().AddMinutes(5),
            thamChieu = "FT2632799998888"
        }, JsonOptions);
        multipart.Add(new StringContent(confirmPayload, Encoding.UTF8, "application/json"), "payload");
        var imgContent = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47]);
        imgContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        multipart.Add(imgContent, "bangChung", "receipt.png");

        using var confirmResp = await client.PostAsync($"/api/admin/refunds/{createdRefund.MaHoanCoc}/confirm", multipart);
        confirmResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var confirmedRefund = (await confirmResp.Content.ReadFromJsonAsync<ApiResponse<RefundDto>>(JsonOptions))!.Data!;
        confirmedRefund.TrangThai.Should().Be("DA_HOAN");
        confirmedRefund.ThamChieu.Should().Be("***8888");
        storage.UploadedCount.Should().Be(1);
        email.RefundEmailsSent.Should().Be(1);

        // 4. E43 & E46: Kiểm tra phân trang danh sách cọc và hoàn cọc
        using var pageDepResp = await client.GetAsync("/api/internal/deposits?trang=1&kichThuocTrang=10&trangThai=DA_DAP_UNG");
        pageDepResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var pageDep = (await pageDepResp.Content.ReadFromJsonAsync<ApiResponse<PageDeposit>>(JsonOptions))!.Data!;
        pageDep.Items.Should().ContainSingle(d => d.MaTienCoc == seed.DepositId && d.DaHoan == 120_000m);

        using var pageRefResp = await client.GetAsync("/api/admin/refunds?trang=1&kichThuocTrang=10&trangThai=DA_HOAN");
        pageRefResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var pageRef = (await pageRefResp.Content.ReadFromJsonAsync<ApiResponse<PageRefund>>(JsonOptions))!.Data!;
        pageRef.Items.Should().ContainSingle(r => r.MaHoanCoc == createdRefund.MaHoanCoc);
    }

    // =========================================================================
    // FR01: Thu hồi quyền DEPOSIT_CONFIRM / DEPOSIT_REFUND -> 403 FORBIDDEN
    // =========================================================================
    [Fact]
    public async Task DepositsAndRefunds_WhenPermissionsRevoked_Returns403Forbidden()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 11, 23, 8, 0, 0, TimeSpan.Zero));
        var seed = new SeedRefs();
        using var factory = CreateFactory(clock, db => SeedDepositScenario(db, seed, clock.GetUtcNow(), requiredDeposit: 300_000m, grantPermissions: false));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateJwtFor(seed.Staff!));

        using var r1 = await client.GetAsync("/api/internal/deposits");
        using var r2 = await client.PostAsJsonAsync($"/api/internal/deposits/{seed.DepositId}/receipts", new
        {
            requestId = Guid.NewGuid(),
            soTien = 100_000,
            phuongThuc = "CASH",
            giaoDichAt = clock.GetUtcNow()
        });
        using var r3 = await client.PostAsJsonAsync($"/api/admin/deposits/{seed.DepositId}/refunds", new
        {
            requestId = Guid.NewGuid(),
            soTien = 50_000,
            lyDo = "Test"
        });
        using var r4 = await client.GetAsync("/api/admin/refunds");

        foreach (var resp in new[] { r1, r2, r3, r4 })
        {
            resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            var body = await resp.Content.ReadFromJsonAsync<ApiResponse<object>>(JsonOptions);
            body!.ErrorCode.Should().Be("FORBIDDEN");
        }
    }

    private static Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> CreateFactory(
        TimeProvider clock,
        Action<PetSpaDbContext> seedAction,
        IFileStorageService? storage = null,
        IEmailSender? email = null)
    {
        return AuthApplicationFactory.Create(seedAction).WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton(clock));
                if (storage is not null)
                    services.Replace(ServiceDescriptor.Singleton(storage));
                if (email is not null)
                    services.Replace(ServiceDescriptor.Singleton(email));
            });
        });
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

    private static void SeedDepositScenario(
        PetSpaDbContext db,
        SeedRefs refs,
        DateTimeOffset now,
        decimal requiredDeposit,
        bool grantPermissions = true)
    {
        db.QuyenSet.AddRange(
            new Quyen { MaQuyen = "DEPOSIT_CONFIRM", TenQuyen = "Xác nhận thu cọc" },
            new Quyen { MaQuyen = "DEPOSIT_REFUND", TenQuyen = "Hoàn tiền cọc" });

        var staff = new NhanVien
        {
            TenDangNhap = "staff_dep",
            MatKhauHash = "hash",
            TenNhanVien = "Lễ tân Cọc",
            VaiTro = Role.STAFF,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        var admin = new NhanVien
        {
            TenDangNhap = "admin_dep",
            MatKhauHash = "hash",
            TenNhanVien = "Quản trị viên",
            VaiTro = Role.ADMIN,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.NhanVienSet.AddRange(staff, admin);
        db.SaveChanges();

        if (grantPermissions)
        {
            db.NhanVienQuyenSet.AddRange(
                new NhanVienQuyen { NhanVienId = staff.Id, MaQuyen = "DEPOSIT_CONFIRM", CapBoiId = admin.Id, CapAt = now },
                new NhanVienQuyen { NhanVienId = admin.Id, MaQuyen = "DEPOSIT_CONFIRM", CapBoiId = admin.Id, CapAt = now },
                new NhanVienQuyen { NhanVienId = admin.Id, MaQuyen = "DEPOSIT_REFUND", CapBoiId = admin.Id, CapAt = now });
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
            Ngay = new DateOnly(2026, 11, 25),
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

        var service = new DichVu
        {
            TenDichVu = "Tắm sấy",
            LoaiDichVu = "SPA",
            ThoiLuongPhut = 60,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.DichVuSet.Add(service);

        var customer = new KhachHang
        {
            HoTen = "Trần Thị B",
            SoDienThoai = "0909888777",
            Email = "khachb@example.com",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.KhachHangSet.Add(customer);
        db.SaveChanges();

        var pet = new ThuCung
        {
            TenThuCung = "Bông",
            LoaiPet = PetType.CHO,
            CanNang = 10m,
            KhachHangId = customer.Id,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.ThuCungSet.Add(pet);

        var booking = new DonDatLich
        {
            TrackingCode = "TRK-DEP1",
            KhungGioId = slot.Id,
            RequestId = Guid.NewGuid(),
            BatDauDuKien = now.AddDays(2),
            KetThucDuKien = now.AddDays(2).AddHours(1),
            TrangThai = BookingState.PENDING,
            TenNguoiDat = customer.HoTen,
            SoDienThoaiSnapshot = customer.SoDienThoai,
            EmailSnapshot = customer.Email,
            TongTamTinh = 1_000_000m,
            KhachHangId = customer.Id,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.DonDatLichSet.Add(booking);
        db.SaveChanges();

        var bookingPet = new ChiTietDon
        {
            TenPetSnapshot = pet.TenThuCung,
            CanNangKhaiBao = 10m,
            TrangThaiChamSoc = PetCareState.NOT_ARRIVED,
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
            RequestId = Guid.NewGuid(),
            ThoiLuongSnapshot = 60,
            BangGiaSizeSnapshot = "{\"m\":200000}",
            TrangThai = OrderServiceState.CHO_THUC_HIEN,
            ChiTietDonId = bookingPet.Id,
            DichVuId = service.Id,
            KhungGioId = slot.Id,
            CreatedAt = now,
            UpdatedAt = now
        });

        var deposit = new TienCoc
        {
            CoSoTinhSnapshot = 1_000_000m,
            TyLe = 0.30m,
            SoTienYeuCau = requiredDeposit,
            HetHanAt = now.AddMinutes(30),
            TrangThaiYeuCau = DepositRequestState.CHO_THU,
            DonDatLichId = booking.Id,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.TienCocSet.Add(deposit);
        db.SaveChanges();

        refs.Staff = staff;
        refs.Admin = admin;
        refs.BookingId = booking.Id;
        refs.SlotId = slot.Id;
        refs.DepositId = deposit.Id;
    }

    private sealed class SeedRefs
    {
        public NhanVien? Staff { get; set; }
        public NhanVien? Admin { get; set; }
        public int BookingId { get; set; }
        public int SlotId { get; set; }
        public int DepositId { get; set; }
    }

    private sealed class BankReferenceReadBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _bothRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;
        public int Arrivals => _arrivals;
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("SELECT EXISTS", StringComparison.Ordinal)
                && command.CommandText.Contains("giao_dich_thu", StringComparison.Ordinal)
                && command.CommandText.Contains("ngan_hang", StringComparison.Ordinal)
                && command.CommandText.Contains("ma_giao_dich", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref _arrivals) == 2) _bothRead.TrySetResult();
                await _bothRead.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            return result;
        }
    }

    private sealed class MutableClock(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset _now = initial;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now += delta;
    }

    private sealed class FakeFileStorageService : IFileStorageService
    {
        public int UploadedCount { get; private set; }
        public Task<string> UploadPhotoAsync(byte[] content, CancellationToken cancellationToken = default)
        {
            UploadedCount++;
            return Task.FromResult($"refunds/evidence-{UploadedCount}.png");
        }
        public Task DeleteAsync(string objectPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Uri> CreateReadUrlAsync(string objectPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new Uri($"https://storage.local/{objectPath}"));
    }

    private sealed class FakeEmailSender : IEmailSender
    {
        public int RefundEmailsSent { get; private set; }
        public Task SendTrackingCodeAsync(string recipient, string trackingCode, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SendBookingConfirmedAsync(string recipient, string trackingCode, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SendTrackingResendAsync(string recipient, string trackingCode, CancellationToken ct) => Task.CompletedTask;
        public Task SendRefundConfirmedAsync(string recipient, string trackingCode, int refundId, decimal amount, DateTimeOffset refundedAt, CancellationToken ct)
        {
            RefundEmailsSent++;
            return Task.CompletedTask;
        }
    }
}
