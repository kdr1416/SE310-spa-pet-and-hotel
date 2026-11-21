using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using PetSpa.Api.Data;
using PetSpa.Api.Services.Booking;
using PetSpa.Api.Services.Identity;
using PetSpa.Api.Services.Storage;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Scheduling;
using PetSpa.Core.Enums;
using PetSpa.Tests.Infrastructure;
using Xunit;

namespace PetSpa.Tests.Booking;

public sealed class BookingManagementEndpointTests
{
    private const string Bookings = "/api/internal/bookings";
    private const string Tracking = "/api/tracking/lookup";
    private const string Phone = "0901234567";
    private const string TrackingCode = "TRK-1111";
    private static readonly DateTimeOffset Now = new(2035, 1, 1, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task List_DefaultsToPendingAndPaginatesByCreationTimeThenId()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await DataAsync(await fixture.Staff.GetAsync($"{Bookings}?kichThuocTrang=2"));
        Assert.Equal(3, (int?)first["tongSo"]);
        Assert.Equal(1, (int?)first["trang"]);
        Assert.Equal(2, (int?)first["kichThuocTrang"]);
        Assert.Equal([2, 1], BookingIds(first));

        var second = await DataAsync(await fixture.Staff.GetAsync($"{Bookings}?kichThuocTrang=2&trang=2"));
        Assert.Equal([4], BookingIds(second));
        var pastLastPage = await DataAsync(await fixture.Staff.GetAsync($"{Bookings}?trang=2147483647"));
        Assert.Empty(BookingIds(pastLastPage));
        var filtered = await DataAsync(await fixture.Staff.GetAsync(
            $"{Bookings}?trangThai=CONFIRMED&tuNgay=2035-01-01&denNgay=2035-01-01"));
        Assert.Equal([3], BookingIds(filtered));
        var otherDay = await DataAsync(await fixture.Staff.GetAsync($"{Bookings}?denNgay=2034-12-31"));
        Assert.Equal([4], BookingIds(otherDay));
    }

    [Theory]
    [InlineData("kichThuocTrang=101", "VALIDATION_FAILED")]
    [InlineData("trang=0", "VALIDATION_FAILED")]
    [InlineData("trangThai=REJECTED", "VALIDATION_FAILED")]
    [InlineData("trangThai=0", "VALIDATION_FAILED")]
    [InlineData("tuNgay=2035-02-01&denNgay=2035-01-01", "INVALID_DATE_RANGE")]
    [InlineData("tuNgay=2035-01-01&denNgay=2035-04-01", "INVALID_DATE_RANGE")]
    public async Task List_RejectsInvalidFilters(string query, string code)
    {
        await using var fixture = await Fixture.CreateAsync();
        await ErrorAsync(await fixture.Staff.GetAsync($"{Bookings}?{query}"), HttpStatusCode.BadRequest, code);
    }

    [Fact]
    public async Task InternalRoutes_RejectMissingAndRevokedApprovalPermissions()
    {
        await using var fixture = await Fixture.CreateAsync();
        await ErrorAsync(await fixture.Public.GetAsync(Bookings), HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        await ErrorAsync(await fixture.Unprivileged.GetAsync(Bookings), HttpStatusCode.Forbidden, "FORBIDDEN");
        await ErrorAsync(await fixture.Unprivileged.PostAsJsonAsync($"{Bookings}/1/confirm", Command()),
            HttpStatusCode.Forbidden, "FORBIDDEN");
        await ErrorAsync(await fixture.Unprivileged.PostAsJsonAsync($"{Bookings}/1/cancel", Cancel("STAFF_REJECTED")),
            HttpStatusCode.Forbidden, "FORBIDDEN");
        var command = Command();
        await DataAsync(await fixture.Staff.PostAsJsonAsync($"{Bookings}/1/confirm", command));
        await using var db = fixture.Database.CreateContext();
        await db.NhanVienQuyenSet.Where(permission => permission.NhanVienId == 1).ExecuteDeleteAsync();
        await ErrorAsync(await fixture.Staff.GetAsync(Bookings), HttpStatusCode.Forbidden, "FORBIDDEN");
        await ErrorAsync(await fixture.Staff.PostAsJsonAsync($"{Bookings}/1/confirm", command),
            HttpStatusCode.Forbidden, "FORBIDDEN");
        await ErrorAsync(await fixture.Staff.PostAsJsonAsync($"{Bookings}/2/cancel", Cancel("STAFF_REJECTED")),
            HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task Confirm_ReplayReturnsOriginalSnapshotAndDoesNotRepeatMutation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var command = Command();
        var confirmed = await DataAsync(await fixture.Staff.PostAsJsonAsync($"{Bookings}/1/confirm", command));
        Assert.Equal("CONFIRMED", (string?)confirmed["trangThai"]);
        await ErrorAsync(await fixture.Staff.PostAsJsonAsync($"{Bookings}/1/confirm", Command()),
            HttpStatusCode.Conflict, "INVALID_TRANSITION");
        await ErrorAsync(await fixture.Staff.PostAsJsonAsync($"{Bookings}/2/confirm", command),
            HttpStatusCode.Conflict, "IDEMPOTENCY_CONFLICT");
        await DataAsync(await fixture.Staff.PostAsJsonAsync($"{Bookings}/1/cancel", Cancel("STAFF_CANCELLED")));
        var replay = await DataAsync(await fixture.Staff.PostAsJsonAsync($"{Bookings}/1/confirm", command));
        Assert.True(JsonNode.DeepEquals(confirmed, replay));
        await using var db = fixture.Database.CreateContext();
        var booking = await db.DonDatLichSet.SingleAsync(booking => booking.Id == 1);
        Assert.Equal(1, booking.DuyetBoiId);
        Assert.Equal(Now, booking.DuyetAt);
        Assert.Single(await db.AuditLogSet.Where(audit => audit.DoiTuongId == 1 && audit.HanhDong == "BOOKING_CONFIRM")
            .ToListAsync());
        Assert.Equal(1, fixture.Email.ApprovalAttempts);
        Assert.True(fixture.Email.ConfirmedBeforeAttempt);
    }

    [Fact]
    public async Task Confirm_RemainsCommittedWhenApprovalEmailFails()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Email.Fail = true;
        var command = Command();
        Assert.Equal("CONFIRMED", (string?)(await DataAsync(
            await fixture.Staff.PostAsJsonAsync($"{Bookings}/1/confirm", command)))["trangThai"]);
        await DataAsync(await fixture.Staff.PostAsJsonAsync($"{Bookings}/1/confirm", command));
        Assert.Equal(1, fixture.Email.ApprovalAttempts);
        Assert.True(fixture.Email.ConfirmedBeforeAttempt);
    }

    [Fact]
    public async Task Confirm_RequiresARequestedDepositToBeSatisfied()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Database.CreateContext();
        var deposit = new TienCoc
        {
            DonDatLichId = 1, CoSoTinhSnapshot = 100_000m, SoTienYeuCau = 30_000m, TyLe = .30m,
            HetHanAt = Now.AddMinutes(10), TrangThaiYeuCau = DepositRequestState.CHO_THU
        };
        db.Add(deposit);
        await db.SaveChangesAsync();
        await ErrorAsync(await fixture.Staff.PostAsJsonAsync($"{Bookings}/1/confirm", Command()),
            HttpStatusCode.Conflict, "INVALID_TRANSITION");
        await ErrorAsync(await fixture.Staff.PostAsJsonAsync($"{Bookings}/1/cancel", Cancel("STAFF_REJECTED")),
            HttpStatusCode.Conflict, "INVALID_TRANSITION");
        deposit.TrangThaiYeuCau = DepositRequestState.DA_DAP_UNG;
        await db.SaveChangesAsync();
        await DataAsync(await fixture.Staff.PostAsJsonAsync($"{Bookings}/1/confirm", Command()));
    }

    [Fact]
    public async Task Tracking_ShowsOnlyTheMatchedBookingInvoiceAndCurrentPaymentState()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Database.CreateContext();
        var invoice = new HoaDon
        {
            MaHoaDon = "HD-1", DonDatLichId = 1, TaoBoiId = 1, TongTien = 100_000m, ConPhaiTra = 100_000m
        };
        db.AddRange(invoice, new HoaDon
        {
            MaHoaDon = "HD-PRIVATE", DonDatLichId = 3, TaoBoiId = 1, TongTien = 200_000m, ConPhaiTra = 200_000m
        });
        await db.SaveChangesAsync();
        db.Add(new CtHoaDon
        {
            HoaDonId = invoice.Id, LoaiKhoan = "SPA", TenKhoanMuc = "Spa", ChiTietDonId = 1,
            DichVuTrongDonId = await db.DichVuTrongDonSet.Where(service => service.ChiTietDonId == 1)
                .OrderBy(service => service.Id).Select(service => service.Id).FirstAsync(),
            SoLuong = 1, DonGiaSnapshot = 100_000m, ThanhTien = 100_000m, DienGiai = "internal-invoice-note"
        });
        await db.SaveChangesAsync();

        var unpaid = await DataAsync(await LookupAsync(fixture.Public, Phone, TrackingCode));
        Assert.Equal("HD-1", (string?)unpaid["hoaDon"]!["soHoaDon"]);
        Assert.Equal("UNPAID", (string?)unpaid["hoaDon"]!["trangThai"]);
        Assert.StartsWith("https://payments.example/", (string?)unpaid["hoaDon"]!["qr"]!["imageUrl"]);
        Assert.Null(unpaid["hoaDon"]!["dongTien"]![0]!["dienGiai"]);
        Assert.DoesNotContain("HD-PRIVATE", unpaid.ToJsonString(), StringComparison.Ordinal);
        invoice.TrangThai = InvoiceState.PAID;
        invoice.PaidAt = Now;
        invoice.ThuBoiId = 1;
        await db.SaveChangesAsync();
        var paid = await DataAsync(await LookupAsync(fixture.Public, Phone, TrackingCode));
        Assert.Equal("PAID", (string?)paid["hoaDon"]!["trangThai"]);
        Assert.Null(paid["hoaDon"]!["qr"]);
    }

    [Fact]
    public async Task Tracking_FinalHandoverDuringLookup_ReturnsOneBookingSnapshot()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (var db = fixture.Database.CreateContext())
        {
            var booking = await db.DonDatLichSet.SingleAsync(booking => booking.Id == 1);
            booking.TrangThai = BookingState.IN_PROGRESS;
            var pets = await db.ChiTietDonSet.Where(pet => pet.DonDatLichId == booking.Id)
                .OrderBy(pet => pet.Id).ToArrayAsync();
            pets[0].TrangThaiChamSoc = PetCareState.DONE;
            pets[0].BanGiaoAt = Now;
            pets[1].TrangThaiChamSoc = PetCareState.READY;
            db.Add(new Quyen { MaQuyen = "PET_HANDOVER", TenQuyen = "Bàn giao" });
            db.Add(new NhanVienQuyen { NhanVienId = 1, MaQuyen = "PET_HANDOVER", CapBoiId = 2 });
            db.Add(new HoaDon
            {
                MaHoaDon = "HD-BAN-GIAO", DonDatLichId = booking.Id, TaoBoiId = 1, ThuBoiId = 1,
                TongTien = 100_000m, ConPhaiTra = 100_000m, TrangThai = InvoiceState.PAID, PaidAt = Now
            });
            await db.SaveChangesAsync();
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        fixture.TrackingRead.Arm();
        var lookup = fixture.Public.PostAsJsonAsync(Tracking,
            new { soDienThoai = Phone, trackingCode = TrackingCode }, timeout.Token);
        try
        {
            await fixture.TrackingRead.BeforePets.Task.WaitAsync(timeout.Token);
            using var handover = await fixture.Staff.PostAsJsonAsync("/api/internal/booking-pets/2/handover",
                new { requestId = Guid.NewGuid(), xacNhanDaNhan = true }, timeout.Token);
            Assert.Equal("DONE", (string?)(await DataAsync(handover))["trangThai"]);
        }
        finally
        {
            fixture.TrackingRead.Resume.TrySetResult();
        }

        using var inFlightLookup = await lookup;
        var duringHandover = await DataAsync(inFlightLookup);
        Assert.Equal("IN_PROGRESS", (string?)duringHandover["trangThai"]);
        Assert.Equal("READY", (string?)duringHandover["thuCung"]![1]!["trangThai"]);

        using var freshLookup = await LookupAsync(fixture.Public, Phone, TrackingCode);
        var afterHandover = await DataAsync(freshLookup);
        Assert.Equal("COMPLETED", (string?)afterHandover["trangThai"]);
        Assert.All(afterHandover["thuCung"]!.AsArray(), pet => Assert.Equal("DONE", (string?)pet!["trangThai"]));
    }

    [Fact]
    public async Task Cancel_ConcurrentReplayReleasesOriginalSeatAndMinutesOncePerSlot()
    {
        await using var fixture = await Fixture.CreateAsync();
        var requestId = Guid.NewGuid();
        var request = new { requestId, loaiHuy = "STAFF_REJECTED", lyDo = "Không đáp ứng điều kiện" };
        var results = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => fixture.Staff.PostAsJsonAsync($"{Bookings}/1/cancel", request)));
        foreach (var result in results)
            Assert.Equal("CANCELLED", (string?)(await DataAsync(result))["trangThai"]);
        await ErrorAsync(await fixture.Staff.PostAsJsonAsync($"{Bookings}/1/cancel",
            new { requestId, loaiHuy = "STAFF_REJECTED", lyDo = "Khác" }), HttpStatusCode.Conflict, "IDEMPOTENCY_CONFLICT");
        await ErrorAsync(await fixture.Staff.PostAsJsonAsync($"{Bookings}/1/cancel", Cancel("STAFF_REJECTED")),
            HttpStatusCode.Conflict, "INVALID_TRANSITION");

        await using var db = fixture.Database.CreateContext();
        var booking = await db.DonDatLichSet.SingleAsync(booking => booking.Id == 1);
        Assert.Equal(CancellationType.STAFF_REJECTED, booking.LoaiHuy);
        Assert.Equal(request.lyDo, booking.LyDoHuy);
        Assert.Equal(1, booking.HuyBoiId);
        var slots = await db.KhungGioDatLichSet.OrderBy(slot => slot.Id).ToListAsync();
        Assert.Equal([(1, 15), (2, 50), (1, 30)], slots.Select(slot => (slot.SlotDaDat, slot.PhutDaGiu)));
        Assert.Single(await db.AuditLogSet.Where(audit => audit.DoiTuongId == 1).ToListAsync());
    }

    [Fact]
    public async Task Cancel_ConcurrentBookingsReleaseBothReservationsFromTheirSharedIntakeSlot()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (var db = fixture.Database.CreateContext())
        {
            await db.DonDatLichSet.Where(booking => booking.Id == 1 || booking.Id == 2)
                .ExecuteUpdateAsync(bookings => bookings.SetProperty(booking => booking.KhungGioId, 2));
            db.Add(new ChiTietDon
            {
                Id = 4, DonDatLichId = 2, ThuCungId = 1, TenPetSnapshot = "Pet 4", CanNangKhaiBao = 5
            });
            db.Add(new DichVuTrongDon
            {
                ChiTietDonId = 4, KhungGioId = 2, ThoiLuongSnapshot = 25, RequestId = Guid.NewGuid(),
                TenSnapshot = "Spa", DichVuId = 1
            });
            await db.SaveChangesAsync();
        }

        fixture.SlotLock.Arm();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var first = fixture.Staff.PostAsJsonAsync($"{Bookings}/1/cancel", Cancel("STAFF_REJECTED"), timeout.Token);
        try
        {
            await fixture.SlotLock.BeforeLock.Task.WaitAsync(timeout.Token);
            await DataAsync(await fixture.Staff.PostAsJsonAsync($"{Bookings}/2/cancel", Cancel("STAFF_REJECTED"), timeout.Token));
        }
        finally
        {
            fixture.SlotLock.Resume.TrySetResult();
        }
        await DataAsync(await first);

        await using var verification = fixture.Database.CreateContext();
        var sharedSlot = await verification.KhungGioDatLichSet.SingleAsync(slot => slot.Id == 2);
        Assert.Equal(1, sharedSlot.SlotDaDat);
        Assert.Equal(25, sharedSlot.PhutDaGiu);
    }

    [Theory]
    [InlineData("STAFF_CANCELLED", 119, false, false, HttpStatusCode.Forbidden, "FORBIDDEN")]
    [InlineData("STAFF_CANCELLED", 120, false, false, HttpStatusCode.OK, null)]
    [InlineData("STAFF_CANCELLED", 119, true, false, HttpStatusCode.OK, null)]
    [InlineData("CUSTOMER_CANCELLED", 119, false, false, HttpStatusCode.Forbidden, "FORBIDDEN")]
    [InlineData("CUSTOMER_CANCELLED", 120, false, false, HttpStatusCode.OK, null)]
    [InlineData("NO_SHOW", -29, false, false, HttpStatusCode.Conflict, "INVALID_TRANSITION")]
    [InlineData("NO_SHOW", -30, false, false, HttpStatusCode.OK, null)]
    [InlineData("NO_SHOW", -31, true, true, HttpStatusCode.Conflict, "INVALID_TRANSITION")]
    [InlineData("STAFF_CANCELLED", 120, true, true, HttpStatusCode.Conflict, "INVALID_TRANSITION")]
    [InlineData("STAFF_REJECTED", 180, false, false, HttpStatusCode.Conflict, "INVALID_TRANSITION")]
    public async Task Cancel_AppliesTypeTimeRoleAndCheckInGuards(
        string type, int minutes, bool admin, bool checkedIn, HttpStatusCode status, string? code)
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (var db = fixture.Database.CreateContext())
        {
            var booking = await db.DonDatLichSet.Include(booking => booking.ChiTietDonDonDatLichItems)
                .SingleAsync(booking => booking.Id == 3);
            booking.BatDauDuKien = type == "NO_SHOW" ? Now.AddHours(-4) : Now.AddMinutes(minutes);
            booking.MocCuoiCa = type == "NO_SHOW" ? Now.AddMinutes(minutes) : Now.AddHours(4);
            booking.KetThucDuKien = Now.AddHours(7);
            if (checkedIn)
                booking.ChiTietDonDonDatLichItems.Single().CheckInAt = Now.AddHours(-1);
            await db.SaveChangesAsync();
        }
        var response = await (admin ? fixture.Admin : fixture.Staff)
            .PostAsJsonAsync($"{Bookings}/3/cancel", Cancel(type));
        if (code is null)
            Assert.Equal("CANCELLED", (string?)(await DataAsync(response))["trangThai"]);
        else
            await ErrorAsync(response, status, code);
    }

    [Fact]
    public async Task Cancel_RejectsMissingReasonAndUnknownType()
    {
        await using var fixture = await Fixture.CreateAsync();
        foreach (var request in new[]
                 {
                     new { requestId = Guid.NewGuid(), loaiHuy = "STAFF_REJECTED", lyDo = " " },
                     new { requestId = Guid.NewGuid(), loaiHuy = "0", lyDo = "Có lý do" }
                 })
            await ErrorAsync(await fixture.Staff.PostAsJsonAsync($"{Bookings}/1/cancel", request),
                HttpStatusCode.BadRequest, "VALIDATION_FAILED");
    }

    [Theory]
    [InlineData("Khách đã gọi điện cho quầy", false)]
    [InlineData("Khách không liên hệ được", false)]
    [InlineData("Khách không liên hệ:", false)]
    [InlineData("Khách không liên hệ", true)]
    [InlineData("KHÁCH KHÔNG LIÊN HỆ: Quầy đã kiểm tra nhật ký cuộc gọi", true)]
    public async Task Cancel_NoShowRequiresExplicitNonContactAttestation(string reason, bool accepted)
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Database.CreateContext();
        await db.DonDatLichSet.Where(booking => booking.Id == 1).ExecuteUpdateAsync(bookings => bookings
            .SetProperty(booking => booking.BatDauDuKien, Now.AddHours(-4))
            .SetProperty(booking => booking.MocCuoiCa, Now.AddMinutes(-30)));
        var requestId = Guid.NewGuid();
        var response = await fixture.Staff.PostAsJsonAsync($"{Bookings}/1/cancel",
            new { requestId, loaiHuy = "NO_SHOW", lyDo = reason });

        if (accepted)
        {
            Assert.Equal("CANCELLED", (string?)(await DataAsync(response))["trangThai"]);
            Assert.Equal(reason, (await db.AuditLogSet.SingleAsync(audit => audit.RequestId == requestId)).LyDo);
        }
        else
        {
            await ErrorAsync(response, HttpStatusCode.Conflict, "INVALID_TRANSITION");
            Assert.Equal(BookingState.PENDING, (await db.DonDatLichSet.SingleAsync(booking => booking.Id == 1)).TrangThai);
            Assert.False(await db.AuditLogSet.AnyAsync(audit => audit.RequestId == requestId));
        }

        var slots = await db.KhungGioDatLichSet.OrderBy(slot => slot.Id).ToListAsync();
        var expected = accepted ? new[] { (1, 15), (2, 50), (1, 30) } : [(1, 15), (4, 140), (2, 50)];
        Assert.Equal(expected, slots.Select(slot => (slot.SlotDaDat, slot.PhutDaGiu)));
    }

    [Fact]
    public async Task Tracking_RequiresExactPairBeforeSigningOnlyItsOwnCarePhotos()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (var db = fixture.Database.CreateContext())
        {
            db.NhatKyChamSocSet.AddRange(
                Log(1, "CARE_LOG", "care/own.jpg", "Ảnh đang chăm sóc"),
                Log(1, "CHECK_IN", "care/arrival.jpg", "internal-health-note"),
                Log(3, "CARE_LOG", "care/other-booking.jpg", "other-booking-note"));
            await db.ChiTietDonSet.Where(pet => pet.Id == 1).ExecuteUpdateAsync(pets => pets
                .SetProperty(pet => pet.TinhTrangLong, "[\"ROI_NHE\"]"));
            await db.SaveChangesAsync();
        }

        var mismatched = await ErrorAsync(await LookupAsync(fixture.Public, "0902222222", TrackingCode),
            HttpStatusCode.NotFound, "TRACKING_NOT_FOUND");
        var unknown = await ErrorAsync(await LookupAsync(fixture.Public, Phone, "UNKNOWN1"),
            HttpStatusCode.NotFound, "TRACKING_NOT_FOUND");
        Assert.Equal((string?)unknown["message"], (string?)mismatched["message"]);
        Assert.Empty(fixture.Storage.Requests);

        var data = await DataAsync(await LookupAsync(fixture.Public, Phone, TrackingCode));
        Assert.Equal(1, (int?)data["maDon"]);
        Assert.Equal("PENDING", (string?)data["trangThai"]);
        Assert.Empty(data["hanhDongChoPhep"]!.AsArray());
        var json = data.ToJsonString();
        foreach (var secret in new[] { "objectPath", "internal-health-note", "other-booking-note", "other-booking.jpg",
                     "lienHe", "emailDelivery", "staff-login", "noi-bo-ghi-chu", "care/own.jpg" })
            Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
        var pet = data["thuCung"]![0]!;
        Assert.Equal("ROI_NHE", (string?)pet["tinhTrangLong"]![0]);
        var logs = pet["nhatKy"]!.AsArray();
        Assert.Equal(2, logs.Count);
        Assert.All(logs, log => Assert.StartsWith("https://photos.example/", (string?)log!["anhUrl"]));
        Assert.Contains(logs, log => (string?)log!["ghiChu"] == "Ảnh đang chăm sóc");
        Assert.Equal(["care/arrival.jpg", "care/own.jpg"], fixture.Storage.Requests.Order());
        await ErrorAsync(await LookupAsync(fixture.Public, Phone, TrackingCode.ToLowerInvariant()),
            HttpStatusCode.NotFound, "TRACKING_NOT_FOUND");
    }

    [Fact]
    public async Task Tracking_KeepsLegacyPhotoLogsWithoutCallingThePrivateSignerForThem()
    {
        await using var fixture = await Fixture.CreateAsync();
        const string privatePath = "8c0472c3473c420b92208d9692644e8a.jpg";
        await using (var db = fixture.Database.CreateContext())
        {
            db.AddRange(Log(1, "CHECK_IN", "check-in/legacy.jpg", "Ghi chú nội bộ"),
                Log(1, "CARE_LOG", "care-logs/legacy.jpg", "Đã chăm sóc"),
                Log(1, "CARE_READY", privatePath, "Hoàn thành"));
            await db.SaveChangesAsync();
        }

        var data = await DataAsync(await LookupAsync(fixture.Public, Phone, TrackingCode));
        var logs = data["thuCung"]![0]!["nhatKy"]!.AsArray();
        Assert.Equal(3, logs.Count);
        foreach (var type in new[] { "CHECK_IN", "CARE_LOG" })
        {
            var legacyLog = Assert.IsType<JsonObject>(logs.Single(log => (string?)log!["loaiNhatKy"] == type));
            Assert.True(legacyLog.ContainsKey("anhUrl"));
            Assert.Null(legacyLog["anhUrl"]);
        }
        var privateLog = logs.Single(log => (string?)log!["loaiNhatKy"] == "CARE_READY");
        Assert.StartsWith("https://photos.example/", (string?)privateLog!["anhUrl"]);
        Assert.Equal(privatePath, Assert.Single(fixture.Storage.Requests));
    }

    [Fact]
    public async Task Tracking_ReturnsStorageUnavailableWhenAPrivatePhotoCannotBeSigned()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (var db = fixture.Database.CreateContext())
        {
            db.Add(Log(1, "CARE_LOG", "8c0472c3473c420b92208d9692644e8a.jpg", "Đã chăm sóc"));
            await db.SaveChangesAsync();
        }
        fixture.Storage.Fail = true;

        await ErrorAsync(await LookupAsync(fixture.Public, Phone, TrackingCode),
            HttpStatusCode.ServiceUnavailable, "STORAGE_UNAVAILABLE");
    }

    [Fact]
    public async Task Tracking_ConcurrentFailuresLockPairAtFifthAttemptAndResetAtExpiry()
    {
        await using var fixture = await Fixture.CreateAsync();
        var wrong = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => LookupAsync(fixture.Public, Phone, "WRONG123")));
        Assert.Equal(4, wrong.Count(response => response.StatusCode == HttpStatusCode.NotFound));
        var fifth = Assert.Single(wrong, response => response.StatusCode == HttpStatusCode.TooManyRequests);
        Assert.Equal("900", fifth.Headers.GetValues("Retry-After").Single());
        await ErrorAsync(fifth, HttpStatusCode.TooManyRequests, "RATE_LIMITED");
        await ErrorAsync(await LookupAsync(fixture.Public, Phone, TrackingCode),
            HttpStatusCode.TooManyRequests, "RATE_LIMITED");
        await DataAsync(await LookupAsync(fixture.Public, "0902222222", "TRK-3333"));

        await using var db = fixture.Database.CreateContext();
        var counter = await db.BaoVeTrackingSet.AsNoTracking().SingleAsync();
        Assert.Matches("^[0-9A-F]{64}$", counter.KhoaIpHash);
        Assert.Matches("^[0-9A-F]{64}$", counter.KhoaSdtHash);
        Assert.Equal(Now.AddMinutes(15), counter.KhoaDen);
        Assert.Equal(5, counter.SoLanSai);
        Assert.Empty(await db.AuditLogSet.ToListAsync());
        fixture.Clock.Now = Now.AddMinutes(15);
        await DataAsync(await LookupAsync(fixture.Public, Phone, TrackingCode));
        await ErrorAsync(await LookupAsync(fixture.Public, Phone, "WRONG123"), HttpStatusCode.NotFound, "TRACKING_NOT_FOUND");
        var reset = await db.BaoVeTrackingSet.AsNoTracking().SingleAsync();
        Assert.Equal(1, reset.SoLanSai);
        Assert.Null(reset.KhoaDen);
    }

    private static object Command() => new { requestId = Guid.NewGuid() };
    private static object Cancel(string type) => new
    {
        requestId = Guid.NewGuid(), loaiHuy = type, lyDo = type == "NO_SHOW" ? "Khách không liên hệ" : "Có lý do"
    };
    private static Task<HttpResponseMessage> LookupAsync(HttpClient client, string phone, string code) =>
        client.PostAsJsonAsync(Tracking, new { soDienThoai = phone, trackingCode = code });
    private static int[] BookingIds(JsonNode data) => data["items"]!.AsArray().Select(row => (int)row!["maDon"]!).ToArray();

    private static async Task<JsonNode> DataAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Expected 200, got {response.StatusCode}. {content}");
        var body = JsonNode.Parse(content);
        Assert.True((bool?)body!["success"]);
        return Assert.IsAssignableFrom<JsonNode>(body["data"]);
    }

    private static async Task<JsonNode> ErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var body = Assert.IsAssignableFrom<JsonNode>(await response.Content.ReadFromJsonAsync<JsonNode>());
        Assert.Equal(code, (string?)body["errorCode"]);
        Assert.Null(body["data"]);
        Assert.False((bool?)body["success"]);
        Assert.False(string.IsNullOrWhiteSpace((string?)body["traceId"]));
        return body;
    }

    private static NhatKyChamSoc Log(int pet, string type, string path, string note) => new()
    {
        ChiTietDonId = pet, NhanVienId = 1, LoaiNhatKy = type, ObjectPath = path, GhiChu = note, GhiNhanAt = Now
    };

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly PostgresWebApplicationFactory _baseFactory;
        private readonly WebApplicationFactory<Program> _factory;
        public PostgresTestDatabase Database { get; }
        public TestClock Clock { get; } = new();
        public TestStorage Storage { get; } = new();
        public TestEmail Email { get; }
        public SlotLockGate SlotLock { get; } = new();
        public TrackingReadGate TrackingRead { get; } = new();
        public HttpClient Staff { get; }
        public HttpClient Admin { get; }
        public HttpClient Unprivileged { get; }
        public HttpClient Public { get; }

        private Fixture(PostgresTestDatabase database)
        {
            Database = database;
            Email = new TestEmail(database);
            _baseFactory = new PostgresWebApplicationFactory(database.ConnectionString);
            _factory = _baseFactory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Billing:VietQrImageUrl", "https://payments.example/qr");
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton<TimeProvider>(Clock);
                    services.AddSingleton<IFileStorageService>(Storage);
                    services.AddSingleton<IEmailSender>(Email);
                    services.AddDbContext<PetSpaDbContext>((_, options) => options.AddInterceptors(SlotLock, TrackingRead));
                });
            });
            Staff = CreateClient(Employee(1));
            Admin = CreateClient(Employee(2));
            Unprivileged = CreateClient(Employee(3));
            Public = _factory.CreateClient();
        }

        private HttpClient CreateClient(NhanVien actor)
        {
            var client = _factory.CreateClient();
            using var scope = _factory.Services.CreateScope();
            var token = scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateToken(actor).Token;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return client;
        }

        public static async Task<Fixture> CreateAsync()
        {
            var database = await PostgresTestDatabase.CreateAsync();
            try
            {
                await using var db = database.CreateContext();
                await SeedAsync(db);
                return new Fixture(database);
            }
            catch
            {
                await database.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            Public.Dispose();
            Staff.Dispose();
            Admin.Dispose();
            Unprivileged.Dispose();
            await _factory.DisposeAsync();
            await _baseFactory.DisposeAsync();
            await Database.DisposeAsync();
        }

        private static NhanVien Employee(int id) => new()
        {
            Id = id, TenDangNhap = $"staff-login-{id}", TenNhanVien = $"Nhân viên {id}",
            VaiTro = id == 2 ? Role.ADMIN : Role.STAFF, IsActive = true
        };

        private static async Task SeedAsync(PetSpaDbContext db)
        {
            db.AddRange(Employee(1), Employee(2), Employee(3), new Quyen { MaQuyen = "BOOKING_APPROVE", TenQuyen = "Duyệt đơn" });
            db.AddRange(new NhanVienQuyen { NhanVienId = 1, MaQuyen = "BOOKING_APPROVE", CapBoiId = 2 },
                new NhanVienQuyen { NhanVienId = 2, MaQuyen = "BOOKING_APPROVE", CapBoiId = 2 });
            var (today, yesterday) = (new DateOnly(2035, 1, 1), new DateOnly(2034, 12, 31));
            db.AddRange(Shift(1, 8), Shift(2, 12), Shift(3, 16));
            db.AddRange(Slot(1, today, 1, 1, 15), Slot(2, today, 2, 4, 140), Slot(3, yesterday, 3, 2, 50));
            db.Add(new DichVu { Id = 1, TenDichVu = "Spa", LoaiDichVu = "SPA", ThoiLuongPhut = 60, IsActive = true });
            db.Add(new KhachHang { Id = 1, HoTen = "Khách", SoDienThoai = "0909999999", Email = "master@example.com" });
            db.AddRange(Booking(1, TrackingCode, Phone, 1, BookingState.PENDING, Now),
                Booking(2, "TRK-2222", Phone, 1, BookingState.PENDING, Now),
                Booking(3, "TRK-3333", "0902222222", 1, BookingState.CONFIRMED, Now),
                Booking(4, "TRK-4444", Phone, 3, BookingState.PENDING, Now.AddHours(-1)));
            for (var id = 1; id <= 3; id++)
            {
                db.Add(new ThuCung { Id = id, KhachHangId = 1, LoaiPet = PetType.CHO, TenThuCung = $"Pet {id}", CanNang = 5 });
                db.Add(new ChiTietDon { Id = id, ThuCungId = id, DonDatLichId = id == 3 ? 3 : 1,
                    TenPetSnapshot = $"Pet {id}", CanNangKhaiBao = 5, NhanVienId = 1 });
            }
            db.AddRange(Service(1, 2, 40), Service(1, 2, 30), Service(1, 3, 20), Service(2, 2, 20));
            await db.SaveChangesAsync();
        }

        private static DonDatLich Booking(int id, string code, string phone, int slot, BookingState state, DateTimeOffset created) => new()
        {
            Id = id, RequestId = Guid.NewGuid(), TrackingCode = code, KhachHangId = 1, TenNguoiDat = "Khách",
            EmailSnapshot = "booking@example.com", SoDienThoaiSnapshot = phone, KhungGioId = slot, TrangThai = state,
            BatDauDuKien = Now.AddHours(3), KetThucDuKien = Now.AddHours(6), MocCuoiCa = Now.AddHours(4), CreatedAt = created,
            ChinhSachSnapshot = """{"ghiChu":"noi-bo-ghi-chu"}""", TongTamTinh = 100_000m
        };

        private static CaLamViec Shift(int id, int start) => new()
        {
            Id = id, TenCa = $"Ca {id}", GioBatDau = new TimeOnly(start, 0), GioKetThuc = new TimeOnly(start + 3, 0),
            SucChuaPet = 10, TranPhut = 180, IsActive = true
        };

        private static KhungGioDatLich Slot(int id, DateOnly day, int shift, int seats, int minutes) => new()
        {
            Id = id, Ngay = day, CaLamViecId = shift, MoNhanKhach = true, TongSlot = 10, TranPhut = 180,
            SlotDaDat = seats, PhutDaGiu = minutes
        };

        private static DichVuTrongDon Service(int pet, int slot, int minutes) => new()
        {
            ChiTietDonId = pet, KhungGioId = slot, ThoiLuongSnapshot = minutes, RequestId = Guid.NewGuid(),
            TenSnapshot = "Spa", DichVuId = 1
        };
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = BookingManagementEndpointTests.Now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class TestStorage : IFileStorageService
    {
        public ConcurrentQueue<string> Requests { get; } = new();
        public bool Fail { get; set; }
        public Task<Uri> CreateReadUrlAsync(string objectPath, CancellationToken cancellationToken = default)
        {
            Requests.Enqueue(objectPath);
            if (Fail)
                throw new FileStorageException("Không thể truy cập kho lưu trữ ảnh.", "STORAGE_UNAVAILABLE");
            return Task.FromResult(new Uri($"https://photos.example/{Guid.NewGuid():N}"));
        }
        public Task<string> UploadPhotoAsync(byte[] content, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task DeleteAsync(string objectPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    // Pause one request after its graph read, then let an independent booking release the shared slot.
    private sealed class SlotLockGate : DbCommandInterceptor
    {
        private int _armed;
        public TaskCompletionSource BeforeLock { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Arm() => Interlocked.Exchange(ref _armed, 1);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM khung_gio_dat_lich", StringComparison.Ordinal) &&
                command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal) &&
                Interlocked.Exchange(ref _armed, 0) == 1)
            {
                BeforeLock.TrySetResult();
                await Resume.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class TrackingReadGate : DbCommandInterceptor
    {
        private int _armed;
        public TaskCompletionSource BeforePets { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Arm() => Interlocked.Exchange(ref _armed, 1);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            // A split SELECT for the pets runs after the parent booking row has already been read.
            if (command.CommandText.Contains("FROM don_dat_lich AS", StringComparison.Ordinal) &&
                command.CommandText.Contains("chi_tiet_don AS", StringComparison.Ordinal) &&
                Interlocked.Exchange(ref _armed, 0) == 1)
            {
                BeforePets.TrySetResult();
                await Resume.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class TestEmail(PostgresTestDatabase database) : IEmailSender
    {
        public int ApprovalAttempts { get; private set; }
        public bool ConfirmedBeforeAttempt { get; private set; }
        public bool Fail { get; set; }

        public Task SendTrackingCodeAsync(string recipient, string trackingCode, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async Task SendBookingConfirmedAsync(string recipient, string trackingCode, CancellationToken cancellationToken)
        {
            ApprovalAttempts++;
            await using var db = database.CreateContext();
            ConfirmedBeforeAttempt = await db.DonDatLichSet.AnyAsync(booking =>
                booking.TrackingCode == trackingCode && booking.EmailSnapshot == recipient
                && booking.TrangThai == BookingState.CONFIRMED, cancellationToken);
            if (Fail)
                throw new InvalidOperationException("Simulated SMTP failure.");
        }
    }
}
