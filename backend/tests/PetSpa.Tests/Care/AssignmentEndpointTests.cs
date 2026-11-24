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
using PetSpa.Core.DTOs.Booking;
using PetSpa.Core.DTOs.Care;
using PetSpa.Core.DTOs.Identity;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Scheduling;
using PetSpa.Core.Enums;
using PetSpa.Tests.Infrastructure;
using Xunit;

namespace PetSpa.Tests.Care;

public class AssignmentEndpointTests
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

    private static HttpClient CreateAuthorizedClient(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory,
        int actorId = 201,
        string username = "reception1",
        Role role = Role.STAFF)
    {
        var client = factory.CreateClient();
        var token = GenerateJwtFor(new NhanVien { Id = actorId, TenDangNhap = username, VaiTro = role });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static void SeedAssignmentScenario(
        PetSpaDbContext db,
        PetCareState petState = PetCareState.WAITING,
        BookingState bookingState = BookingState.IN_PROGRESS,
        bool grantActorCheckin = true,
        bool newAssigneeHasCare = true,
        ShiftRegistrationState newAssigneeShiftState = ShiftRegistrationState.APPROVED,
        bool hasActiveInvoice = false)
    {
        var today = new DateOnly(2026, 11, 20);
        var now = CareTestSupport.Now;

        db.QuyenSet.AddRange(
            new Quyen { MaQuyen = "CHECKIN_PERFORM", TenQuyen = "CHECKIN_PERFORM" },
            new Quyen { MaQuyen = "CARE_UPDATE", TenQuyen = "CARE_UPDATE" });

        var hasher = new PasswordHasher<NhanVien>();
        var reception = new NhanVien
        {
            Id = 201,
            TenDangNhap = "reception1",
            TenNhanVien = "Lễ Tân Quầy",
            VaiTro = Role.STAFF,
            IsActive = true
        };
        reception.MatKhauHash = hasher.HashPassword(reception, "Password123@");

        var admin = new NhanVien
        {
            Id = 200,
            TenDangNhap = "admin1",
            TenNhanVien = "Quản Trị Viên",
            VaiTro = Role.ADMIN,
            IsActive = true
        };
        admin.MatKhauHash = hasher.HashPassword(admin, "Password123@");

        var care1 = new NhanVien
        {
            Id = 202,
            TenDangNhap = "care1",
            TenNhanVien = "KTV Spa 1",
            VaiTro = Role.STAFF,
            IsActive = true
        };
        care1.MatKhauHash = hasher.HashPassword(care1, "Password123@");

        var care2 = new NhanVien
        {
            Id = 203,
            TenDangNhap = "care2",
            TenNhanVien = "KTV Spa 2",
            VaiTro = Role.STAFF,
            IsActive = true
        };
        care2.MatKhauHash = hasher.HashPassword(care2, "Password123@");

        var carePending = new NhanVien
        {
            Id = 204,
            TenDangNhap = "care_pending",
            TenNhanVien = "KTV Chờ Duyệt",
            VaiTro = Role.STAFF,
            IsActive = true
        };
        carePending.MatKhauHash = hasher.HashPassword(carePending, "Password123@");

        var careInactive = new NhanVien
        {
            Id = 205,
            TenDangNhap = "care_inactive",
            TenNhanVien = "KTV Nghỉ Việc",
            VaiTro = Role.STAFF,
            IsActive = false
        };
        careInactive.MatKhauHash = hasher.HashPassword(careInactive, "Password123@");

        db.NhanVienSet.AddRange(admin, reception, care1, care2, carePending, careInactive);

        if (grantActorCheckin)
        {
            db.NhanVienQuyenSet.AddRange(
                new NhanVienQuyen { NhanVienId = reception.Id, MaQuyen = "CHECKIN_PERFORM", CapBoiId = admin.Id, CapAt = now },
                new NhanVienQuyen { NhanVienId = admin.Id, MaQuyen = "CHECKIN_PERFORM", CapBoiId = admin.Id, CapAt = now });
        }

        db.NhanVienQuyenSet.AddRange(
            new NhanVienQuyen { NhanVienId = care1.Id, MaQuyen = "CARE_UPDATE", CapBoiId = admin.Id, CapAt = now },
            new NhanVienQuyen { NhanVienId = carePending.Id, MaQuyen = "CARE_UPDATE", CapBoiId = admin.Id, CapAt = now },
            new NhanVienQuyen { NhanVienId = careInactive.Id, MaQuyen = "CARE_UPDATE", CapBoiId = admin.Id, CapAt = now });

        if (newAssigneeHasCare)
        {
            db.NhanVienQuyenSet.Add(new NhanVienQuyen
            {
                NhanVienId = care2.Id,
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

        db.DangKyCaSet.AddRange(
            new DangKyCa
            {
                Id = 1,
                Ngay = today,
                CaLamViecId = shift.Id,
                DotId = window.Id,
                NhanVienId = reception.Id,
                TrangThai = ShiftRegistrationState.APPROVED,
                DuyetBoiId = admin.Id,
                DuyetAt = now
            },
            new DangKyCa
            {
                Id = 2,
                Ngay = today,
                CaLamViecId = shift.Id,
                DotId = window.Id,
                NhanVienId = care1.Id,
                TrangThai = ShiftRegistrationState.APPROVED,
                DuyetBoiId = admin.Id,
                DuyetAt = now
            },
            new DangKyCa
            {
                Id = 3,
                Ngay = today,
                CaLamViecId = shift.Id,
                DotId = window.Id,
                NhanVienId = care2.Id,
                TrangThai = newAssigneeShiftState,
                DuyetBoiId = newAssigneeShiftState == ShiftRegistrationState.APPROVED ? admin.Id : null,
                DuyetAt = newAssigneeShiftState == ShiftRegistrationState.APPROVED ? now : null
            },
            new DangKyCa
            {
                Id = 4,
                Ngay = today,
                CaLamViecId = shift.Id,
                DotId = window.Id,
                NhanVienId = carePending.Id,
                TrangThai = ShiftRegistrationState.PENDING
            },
            new DangKyCa
            {
                Id = 5,
                Ngay = today,
                CaLamViecId = shift.Id,
                DotId = window.Id,
                NhanVienId = careInactive.Id,
                TrangThai = ShiftRegistrationState.APPROVED,
                DuyetBoiId = admin.Id,
                DuyetAt = now
            });

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
            CanNang = 4.5m
        };
        db.ThuCungSet.Add(pet);

        var booking = new DonDatLich
        {
            Id = 1,
            RequestId = Guid.NewGuid(),
            Fingerprint = "seed-assign",
            TrackingCode = "TRK-ASGN",
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
            CanNangChot = 4.5m,
            SizeThucTe = SizeBand.S,
            SizeChot = SizeBand.S,
            TrangThaiChamSoc = petState,
            NhanVienId = petState == PetCareState.NOT_ARRIVED ? null : care1.Id,
            CheckInAt = petState == PetCareState.NOT_ARRIVED ? null : now,
            CheckInBoiId = petState == PetCareState.NOT_ARRIVED ? null : reception.Id
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
            BangGiaSizeSnapshot = "{\"S\":150000,\"M\":200000}",
            ThoiLuongSnapshot = 60,
            TrangThai = OrderServiceState.CHO_THUC_HIEN,
            SizeChot = SizeBand.S,
            GiaChot = 150000m
        });

        if (petState != PetCareState.NOT_ARRIVED)
        {
            db.NhatKyChamSocSet.Add(new NhatKyChamSoc
            {
                Id = 500,
                ChiTietDonId = chiTietDon.Id,
                NhanVienId = care1.Id,
                LoaiNhatKy = "CARE_UPDATE",
                GhiNhanAt = now.AddMinutes(10),
                GhiChu = "Đã bắt đầu tắm cho bé",
                CreatedAt = now.AddMinutes(10),
                UpdatedAt = now.AddMinutes(10)
            });
        }

        if (hasActiveInvoice)
        {
            db.HoaDonSet.Add(new HoaDon
            {
                Id = 1,
                MaHoaDon = "HD-001",
                DonDatLichId = booking.Id,
                TrangThai = InvoiceState.UNPAID,
                TongTien = 150000m,
                ConPhaiTra = 150000m,
                TaoBoiId = reception.Id,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        db.SaveChanges();
    }

    [Fact]
    public async Task Candidates_ReturnsOnlyActiveCareStaffWithApprovedShift()
    {
        using var factory = CareTestSupport.Create(db => SeedAssignmentScenario(db));
        using var client = CreateAuthorizedClient(factory);

        var response = await client.GetAsync("/api/internal/assignment-candidates?ngay=2026-11-20&maCa=1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<List<Employee>>>();
        body!.Success.Should().BeTrue();
        body.Data!.Select(e => e.MaNhanVien).Should().Equal(202, 203);
    }

    [Theory]
    [InlineData("/api/internal/assignment-candidates?maCa=1")]
    [InlineData("/api/internal/assignment-candidates?ngay=2026-11-20&maCa=0")]
    public async Task Candidates_InvalidQuery_Returns400ValidationFailed(string url)
    {
        using var factory = CareTestSupport.Create(db => SeedAssignmentScenario(db));
        using var client = CreateAuthorizedClient(factory);

        var response = await client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<List<Employee>>>();
        body!.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task Candidates_ShiftNotFound_Returns404NotFound()
    {
        using var factory = CareTestSupport.Create(db => SeedAssignmentScenario(db));
        using var client = CreateAuthorizedClient(factory);

        var response = await client.GetAsync("/api/internal/assignment-candidates?ngay=2026-11-20&maCa=999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<List<Employee>>>();
        body!.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Candidates_RevokedPermission_Returns403Forbidden()
    {
        using var factory = CareTestSupport.Create(db => SeedAssignmentScenario(db, grantActorCheckin: false));
        using var client = CreateAuthorizedClient(factory);

        var response = await client.GetAsync("/api/internal/assignment-candidates?ngay=2026-11-20&maCa=1");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<List<Employee>>>();
        body!.ErrorCode.Should().Be("FORBIDDEN");
    }

    [Theory]
    [InlineData(PetCareState.WAITING)]
    [InlineData(PetCareState.IN_PROGRESS)]
    public async Task Reassign_ValidState_UpdatesAssigneePreservesOldLogsAndWritesAudit(PetCareState state)
    {
        using var factory = CareTestSupport.Create(db => SeedAssignmentScenario(db, petState: state));
        using var client = CreateAuthorizedClient(factory);
        var request = new AssignmentRequest
        {
            RequestId = Guid.NewGuid(),
            MaNhanVien = 203,
            LyDo = "Điều phối sang KTV trống tay hơn"
        };

        var response = await client.PutAsJsonAsync("/api/internal/booking-pets/10/assignment", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body!.Success.Should().BeTrue();
        body.Data!.TenNhanVienPhuTrach.Should().Be("KTV Spa 2");
        body.Data.NhatKy.Should().ContainSingle()
            .Which.TenTacGia.Should().Be("KTV Spa 1");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
        var savedPet = await db.ChiTietDonSet.SingleAsync(p => p.Id == 10);
        savedPet.NhanVienId.Should().Be(203);
        var oldLog = await db.NhatKyChamSocSet.SingleAsync(l => l.Id == 500);
        oldLog.NhanVienId.Should().Be(202);
        var audit = await db.AuditLogSet.SingleAsync();
        audit.LyDo.Should().Be("Điều phối sang KTV trống tay hơn");
    }

    [Theory]
    [InlineData(Role.STAFF, 201, "reception1", true, ShiftRegistrationState.PENDING)]
    [InlineData(Role.ADMIN, 200, "admin1", true, ShiftRegistrationState.PENDING)]
    [InlineData(Role.ADMIN, 200, "admin1", false, ShiftRegistrationState.APPROVED)]
    public async Task Reassign_AssigneeWithoutApprovedShiftOrCareGrant_Returns409EvenForAdmin(
        Role actorRole, int actorId, string username, bool hasCare, ShiftRegistrationState shiftState)
    {
        using var factory = CareTestSupport.Create(db => SeedAssignmentScenario(
            db,
            newAssigneeHasCare: hasCare,
            newAssigneeShiftState: shiftState));
        using var client = CreateAuthorizedClient(factory, actorId, username, actorRole);
        var request = new AssignmentRequest
        {
            RequestId = Guid.NewGuid(),
            MaNhanVien = 203,
            LyDo = "Thử phân công KTV không đủ điều kiện"
        };

        var response = await client.PutAsJsonAsync("/api/internal/booking-pets/10/assignment", request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body!.ErrorCode.Should().Be("ASSIGNMENT_UNAVAILABLE");
    }

    [Theory]
    [InlineData(PetCareState.NOT_ARRIVED, false)]
    [InlineData(PetCareState.DONE, false)]
    [InlineData(PetCareState.CANCELLED, false)]
    [InlineData(PetCareState.WAITING, true)]
    public async Task Reassign_InvalidPetStateOrActiveInvoice_Returns409InvalidTransition(
        PetCareState petState, bool hasInvoice)
    {
        using var factory = CareTestSupport.Create(db => SeedAssignmentScenario(
            db,
            petState: petState,
            hasActiveInvoice: hasInvoice));
        using var client = CreateAuthorizedClient(factory);
        var request = new AssignmentRequest
        {
            RequestId = Guid.NewGuid(),
            MaNhanVien = 203,
            LyDo = "Không hợp lệ"
        };

        var response = await client.PutAsJsonAsync("/api/internal/booking-pets/10/assignment", request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body!.ErrorCode.Should().Be("INVALID_TRANSITION");
    }

    [Fact]
    public async Task Reassign_PetNotFound_Returns404NotFound()
    {
        using var factory = CareTestSupport.Create(db => SeedAssignmentScenario(db));
        using var client = CreateAuthorizedClient(factory);
        var request = new AssignmentRequest
        {
            RequestId = Guid.NewGuid(),
            MaNhanVien = 203,
            LyDo = "Pet không tồn tại"
        };

        var response = await client.PutAsJsonAsync("/api/internal/booking-pets/9999/assignment", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body!.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Reassign_EmptyReason_Returns400ValidationFailed()
    {
        using var factory = CareTestSupport.Create(db => SeedAssignmentScenario(db));
        using var client = CreateAuthorizedClient(factory);
        var request = new AssignmentRequest
        {
            RequestId = Guid.NewGuid(),
            MaNhanVien = 203,
            LyDo = "   "
        };

        var response = await client.PutAsJsonAsync("/api/internal/booking-pets/10/assignment", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body!.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task Reassign_DuplicateRequestId_SamePayloadReturns200_DifferentPayloadReturns409()
    {
        using var factory = CareTestSupport.Create(db => SeedAssignmentScenario(db));
        using var client = CreateAuthorizedClient(factory);
        var requestId = Guid.NewGuid();
        var request = new AssignmentRequest
        {
            RequestId = requestId,
            MaNhanVien = 203,
            LyDo = "Chuyển ca chăm sóc"
        };

        var res1 = await client.PutAsJsonAsync("/api/internal/booking-pets/10/assignment", request);
        res1.StatusCode.Should().Be(HttpStatusCode.OK);

        var res2 = await client.PutAsJsonAsync("/api/internal/booking-pets/10/assignment", request);
        res2.StatusCode.Should().Be(HttpStatusCode.OK);
        var body2 = await res2.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body2!.Data!.TenNhanVienPhuTrach.Should().Be("KTV Spa 2");

        var conflict = new AssignmentRequest
        {
            RequestId = requestId,
            MaNhanVien = 202,
            LyDo = "Đổi lại KTV khác"
        };
        var res3 = await client.PutAsJsonAsync("/api/internal/booking-pets/10/assignment", conflict);
        res3.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body3 = await res3.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body3!.ErrorCode.Should().Be("IDEMPOTENCY_CONFLICT");
    }

    [Fact]
    public async Task Reassign_RevokedPermission_Returns403Forbidden()
    {
        using var factory = CareTestSupport.Create(db => SeedAssignmentScenario(db, grantActorCheckin: false));
        using var client = CreateAuthorizedClient(factory);
        var request = new AssignmentRequest
        {
            RequestId = Guid.NewGuid(),
            MaNhanVien = 203,
            LyDo = "Không có quyền"
        };

        var response = await client.PutAsJsonAsync("/api/internal/booking-pets/10/assignment", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BookingPet>>();
        body!.ErrorCode.Should().Be("FORBIDDEN");
    }
}
