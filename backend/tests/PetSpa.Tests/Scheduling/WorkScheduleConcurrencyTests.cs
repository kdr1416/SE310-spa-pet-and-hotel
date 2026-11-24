using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PetSpa.Api.Services.Booking;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PetSpa.Core.Entities.Scheduling;
using PetSpa.Core.Enums;
using PetSpa.Tests.Care;

namespace PetSpa.Tests.Scheduling;

public class WorkScheduleConcurrencyTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task Scheduling_RechecksPermissionImmediatelyAfterEmployeeWait(bool transfer, bool terminal)
    {
        await using var f = await SchedulingTestSupport.CreateAsync();
        var row = await SchedulingTestSupport.RegistrationAsync(f, "staff2", transfer && terminal ? ShiftRegistrationState.PENDING : ShiftRegistrationState.APPROVED);
        using var client = await f.AdminClientAsync();
        await using var db = f.Database.CreateContext();
        var actorId = await db.NhanVienSet.Where(x => x.TenDangNhap == "admin").Select(x => x.Id).SingleAsync();
        await using var blocker = new NpgsqlConnection(f.Database.ConnectionString);
        await blocker.OpenAsync(); await using var transaction = await blocker.BeginTransactionAsync();
        await HoldEmployeeAsync(blocker, row.NhanVienId);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var pending = transfer
            ? client.PostAsJsonAsync($"/api/admin/shift-registrations/{row.Id}/transfer", new { requestId = Guid.NewGuid(), ngay = "2035-01-03", maCa = terminal ? row.CaLamViecId : 99999, lyDo = "Đổi ca" })
            : client.PostAsJsonAsync($"/api/admin/shift-registrations/{row.Id}/decision", new { requestId = Guid.NewGuid(), quyetDinh = "APPROVE" });
        try
        {
            await AssertBlockedAsync(f.Database.ConnectionString, [pending], 1, timeout.Token);
            await db.NhanVienQuyenSet.Where(x => x.NhanVienId == actorId && x.MaQuyen == "SCHEDULE_APPROVE").ExecuteDeleteAsync();
        }
        finally { await transaction.RollbackAsync(); }
        var response = await pending;
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await SchedulingTestSupport.ErrorAsync(response, "FORBIDDEN");
        (await db.AuditLogSet.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task E28_RechecksDeadlineAfterEmployeeLockWait()
    {
        await using var f = await SchedulingTestSupport.CreateAsync();
        await SchedulingTestSupport.OpenWindowAsync(f);
        await using var db = f.Database.CreateContext();
        var employee = await db.NhanVienSet.SingleAsync(x => x.TenDangNhap == "staff1");
        var shiftId = await db.CaLamViecSet.Select(x => x.Id).FirstAsync();
        using var client = await SchedulingTestSupport.ClientAsync(f, "staff1");
        await using var blocker = new NpgsqlConnection(f.Database.ConnectionString);
        await blocker.OpenAsync(); await using var transaction = await blocker.BeginTransactionAsync();
        await HoldEmployeeAsync(blocker, employee.Id);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var pending = client.PostAsJsonAsync("/api/internal/shift-registrations", new { requestId = Guid.NewGuid(), ngay = "2035-01-02", maCa = shiftId });
        try
        {
            await AssertBlockedAsync(f.Database.ConnectionString, [pending], 1, timeout.Token);
            f.Clock.UtcNow = SchedulingTestSupport.Now.AddHours(2);
        }
        finally { await transaction.RollbackAsync(); }
        var response = await pending;
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await SchedulingTestSupport.ErrorAsync(response, "INVALID_TRANSITION");
        (await db.DangKyCaSet.AnyAsync(x => x.NhanVienId == employee.Id)).Should().BeFalse();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Scheduling_RechecksEligibilityAfterSlotWait(bool transfer, bool deactivateEmployee)
    {
        await using var f = await SchedulingTestSupport.CreateAsync();
        var row = await SchedulingTestSupport.RegistrationAsync(f, "staff2", transfer ? ShiftRegistrationState.APPROVED : ShiftRegistrationState.PENDING);
        await using var db = f.Database.CreateContext();
        var shift = await db.CaLamViecSet.SingleAsync(x => x.Id == row.CaLamViecId);
        var slot = new KhungGioDatLich { Ngay = row.Ngay, CaLamViecId = shift.Id, TongSlot = 10, TranPhut = 240, MoNhanKhach = true };
        db.KhungGioDatLichSet.Add(slot); await db.SaveChangesAsync();
        using var client = await f.AdminClientAsync();
        await using var blocker = new NpgsqlConnection(f.Database.ConnectionString);
        await blocker.OpenAsync(); await using var transaction = await blocker.BeginTransactionAsync();
        await using (var hold = new NpgsqlCommand("SELECT id FROM khung_gio_dat_lich WHERE id=$1 FOR UPDATE", blocker))
        { hold.Parameters.AddWithValue(slot.Id); await hold.ExecuteNonQueryAsync(); }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var pending = transfer
            ? client.PostAsJsonAsync($"/api/admin/shift-registrations/{row.Id}/transfer", new { requestId = Guid.NewGuid(), ngay = "2035-01-03", maCa = row.CaLamViecId, lyDo = "Đổi ca" })
            : client.PostAsJsonAsync($"/api/admin/shift-registrations/{row.Id}/decision", new { requestId = Guid.NewGuid(), quyetDinh = "APPROVE" });
        try
        {
            await CareTestSupport.WaitForLockWaitersAsync(f.Database.ConnectionString, "%khung_gio_dat_lich%FOR UPDATE%", 1, timeout.Token);
            if (deactivateEmployee) await db.NhanVienSet.Where(x => x.Id == row.NhanVienId).ExecuteUpdateAsync(x => x.SetProperty(e => e.IsActive, false));
            else await db.CaLamViecSet.Where(x => x.Id == shift.Id).ExecuteUpdateAsync(x => x.SetProperty(s => s.IsActive, false));
        }
        finally { await transaction.RollbackAsync(); }
        var response = await pending;
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await SchedulingTestSupport.ErrorAsync(response, "INVALID_TRANSITION");
        (await db.AuditLogSet.CountAsync()).Should().Be(0);
        (await db.KhungGioDatLichSet.CountAsync(x => x.Ngay == new DateOnly(2035, 1, 3))).Should().Be(0);
    }

    [Fact]
    public async Task E15_UploadDoesNotHoldEmployeeLock()
    {
        await using var f = await SchedulingTestSupport.CreateAsync();
        var future = await SchedulingTestSupport.RegistrationAsync(f, "staff2", ShiftRegistrationState.APPROVED);
        await using var db = f.Database.CreateContext();
        var booking = await f.AddBookingAsync(db, spa: false, roomId: await db.LoaiPhongSet.Select(x => x.Id).FirstAsync(), deposit: false);
        booking.TrangThai = BookingState.CONFIRMED; await db.SaveChangesAsync();
        var uploading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storage = new CareTestSupport.Storage { BeforeUpload = async ct => { uploading.TrySetResult(); await resume.Task.WaitAsync(ct); } };
        await using var host = CareTestSupport.Configure(f.Host, storage, new(SchedulingTestSupport.Now));
        using var reception = await SchedulingTestSupport.ClientAsync(f, "staff1");
        using var care = host.CreateClient(); care.DefaultRequestHeaders.Authorization = reception.DefaultRequestHeaders.Authorization;
        using var admin = await f.AdminClientAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var form = CheckinForm(future.NhanVienId);
        var checkin = care.PostAsync($"/api/internal/booking-pets/{booking.ChiTietDonDonDatLichItems.Single().Id}/check-in", form, timeout.Token);
        try
        {
            await uploading.Task.WaitAsync(timeout.Token);
            var transfer = await admin.PostAsJsonAsync($"/api/admin/shift-registrations/{future.Id}/transfer",
                new { requestId = Guid.NewGuid(), ngay = "2035-01-03", maCa = future.CaLamViecId, lyDo = "Đổi ca" }, timeout.Token);
            transfer.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally { resume.TrySetResult(); }
        (await checkin).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task E04_LastReservationVersusE55TransferRechecksCapacityUnderSlotLock(bool bookingFirst)
    {
        await using var f = await SchedulingTestSupport.CreateAsync();
        await using var db = f.Database.CreateContext();
        var row = await db.DangKyCaSet.OrderBy(x => x.Id).FirstAsync();
        var slot = await db.KhungGioDatLichSet.SingleAsync(x => x.Ngay == row.Ngay && x.CaLamViecId == row.CaLamViecId);
        slot.TongSlot = 1; slot.TranPhut = 60; await db.SaveChangesAsync();
        var serviceId = await db.DichVuSet.Where(x => x.TenDichVu == "Tắm sấy vệ sinh").Select(x => x.Id).SingleAsync();
        var email = new TestEmail();
        await using var host = f.Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<IEmailSender>(email)));
        using var publicClient = host.CreateClient();
        using var admin = await f.AdminClientAsync();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(JsonSerializer.Serialize(new
        {
            requestId = Guid.NewGuid(),
            lienHe = new { hoTen = "Khách test", soDienThoai = "0912345678", email = "guest@example.test" },
            maKhungGio = slot.Id,
            ngayTra = "2035-01-01",
            thuCung = new[] { new { tenThuCung = "Milo", loaiPet = "CHO", canNangKhaiBao = 4m,
                dichVu = new[] { new { maDichVu = serviceId, maKhungGio = slot.Id } } } }
        })), "payload");
        Task<HttpResponseMessage> Book() => publicClient.PostAsync("/api/bookings", form);
        Task<HttpResponseMessage> Transfer() => admin.PostAsJsonAsync($"/api/admin/shift-registrations/{row.Id}/transfer",
            new { requestId = Guid.NewGuid(), ngay = "2035-01-03", maCa = row.CaLamViecId, lyDo = "Đổi ca" });
        await using var blocker = new NpgsqlConnection(f.Database.ConnectionString);
        await blocker.OpenAsync(); await using var transaction = await blocker.BeginTransactionAsync();
        await using (var hold = new NpgsqlCommand("SELECT id FROM khung_gio_dat_lich WHERE id=$1 FOR UPDATE", blocker))
        { hold.Parameters.AddWithValue(slot.Id); await hold.ExecuteNonQueryAsync(); }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Task<HttpResponseMessage>? booking = null, transfer = null;
        try
        {
            if (bookingFirst) booking = Book(); else transfer = Transfer();
            await CareTestSupport.WaitForLockWaitersAsync(f.Database.ConnectionString, "%khung_gio_dat_lich%FOR UPDATE%", 1, timeout.Token);
            if (bookingFirst) transfer = Transfer(); else booking = Book();
            await CareTestSupport.WaitForLockWaitersAsync(f.Database.ConnectionString, "%khung_gio_dat_lich%FOR UPDATE%", 2, timeout.Token);
        }
        finally { await transaction.RollbackAsync(); }
        var booked = await booking!; var moved = await transfer!;
        booked.StatusCode.Should().Be(bookingFirst ? HttpStatusCode.Created : HttpStatusCode.Conflict);
        moved.StatusCode.Should().Be(bookingFirst ? HttpStatusCode.Conflict : HttpStatusCode.OK);
        await SchedulingTestSupport.ErrorAsync(bookingFirst ? moved : booked, "CAPACITY_EXCEEDED");
        await db.Entry(slot).ReloadAsync();
        slot.SlotDaDat.Should().Be(bookingFirst ? 1 : 0); slot.PhutDaGiu.Should().Be(bookingFirst ? 60 : 0);
        email.Attempts.Should().Be(bookingFirst ? 1 : 0);
    }

    private sealed class TestEmail : IEmailSender
    {
        internal int Attempts { get; private set; }
        public Task SendTrackingCodeAsync(string recipient, string trackingCode, CancellationToken ct) { Attempts++; return Task.CompletedTask; }
        public Task SendBookingConfirmedAsync(string recipient, string trackingCode, CancellationToken ct) => throw new NotSupportedException();
        public Task SendTrackingResendAsync(string recipient, string trackingCode, CancellationToken ct) => throw new NotSupportedException();
        public Task SendRefundConfirmedAsync(string recipient, string trackingCode, int refundId, decimal amount, DateTimeOffset at, CancellationToken ct) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData("SHIFT_WINDOW_CREATE", false)]
    [InlineData("SHIFT_REGISTER", false)]
    [InlineData("SHIFT_DECISION", false)]
    [InlineData("SHIFT_TRANSFER", false)]
    [InlineData("SHIFT_WINDOW_CREATE", true)]
    [InlineData("SHIFT_REGISTER", true)]
    [InlineData("SHIFT_DECISION", true)]
    [InlineData("SHIFT_TRANSFER", true)]
    public async Task ConcurrentRequestKeyReplaysOrConflictsBeforeStateGuards(string operation, bool different)
    {
        await using var f = await SchedulingTestSupport.CreateAsync();
        await SchedulingTestSupport.OpenWindowAsync(f);
        await using var db = f.Database.CreateContext();
        var shiftId = await db.CaLamViecSet.OrderBy(x => x.Id).Select(x => x.Id).FirstAsync();
        var requestId = Guid.NewGuid();
        var username = operation == "SHIFT_REGISTER" ? "staff1" : "admin";
        var actorId = await db.NhanVienSet.Where(x => x.TenDangNhap == username).Select(x => x.Id).SingleAsync();
        var registration = operation is "SHIFT_DECISION" or "SHIFT_TRANSFER"
            ? await SchedulingTestSupport.RegistrationAsync(f, "staff2", operation == "SHIFT_DECISION" ? ShiftRegistrationState.PENDING : ShiftRegistrationState.APPROVED) : null;
        var route = operation switch
        {
            "SHIFT_WINDOW_CREATE" => "/api/admin/shift-windows",
            "SHIFT_REGISTER" => "/api/internal/shift-registrations",
            "SHIFT_DECISION" => $"/api/admin/shift-registrations/{registration!.Id}/decision",
            _ => $"/api/admin/shift-registrations/{registration!.Id}/transfer"
        };
        object Payload(bool changed) => operation switch
        {
            "SHIFT_WINDOW_CREATE" => new { requestId, tuNgay = "2035-01-02", denNgay = changed ? "2035-01-09" : "2035-01-08", moAt = "2035-01-01T01:00:00Z", dongAt = "2035-01-01T02:00:00Z" },
            "SHIFT_REGISTER" => new { requestId, ngay = changed ? "2035-01-03" : "2035-01-02", maCa = shiftId },
            "SHIFT_DECISION" => new { requestId, quyetDinh = "APPROVE", lyDo = changed ? "Khác" : "Duyệt" },
            _ => new { requestId, ngay = changed ? "2035-01-04" : "2035-01-03", maCa = shiftId, lyDo = "Đổi ca" }
        };
        using var one = await SchedulingTestSupport.ClientAsync(f, username);
        using var two = await SchedulingTestSupport.ClientAsync(f, username);
        await using var blocker = new NpgsqlConnection(f.Database.ConnectionString);
        await blocker.OpenAsync(); await using var transaction = await blocker.BeginTransactionAsync();
        var key = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes($"shift-scheduling:{actorId}:{operation}:{requestId:N}")));
        await using (var hold = new NpgsqlCommand("SELECT pg_advisory_xact_lock($1)", blocker))
        { hold.Parameters.AddWithValue(key); await hold.ExecuteNonQueryAsync(); }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var tasks = new[] { one.PostAsJsonAsync(route, Payload(false)), two.PostAsJsonAsync(route, Payload(different)) };
        try { await AssertBlockedAsync(f.Database.ConnectionString, tasks, 2, timeout.Token); }
        finally { await transaction.RollbackAsync(); }
        var results = await Task.WhenAll(tasks);
        var created = operation is "SHIFT_WINDOW_CREATE" or "SHIFT_REGISTER" ? HttpStatusCode.Created : HttpStatusCode.OK;
        results.Select(x => x.StatusCode).Should().BeEquivalentTo([created, different ? HttpStatusCode.Conflict : HttpStatusCode.OK]);
        if (different) await SchedulingTestSupport.ErrorAsync(results.Single(x => x.StatusCode == HttpStatusCode.Conflict), "IDEMPOTENCY_CONFLICT");
        else (await SchedulingTestSupport.DataAsync(results[0])).GetRawText().Should().Be((await SchedulingTestSupport.DataAsync(results[1])).GetRawText());
        (await db.AuditLogSet.CountAsync(x => x.RequestId == requestId && x.HanhDong == operation)).Should().Be(1);
    }

    [Fact]
    public async Task E55_TwoEmployeesCannotBothWithdrawCommittedCapacity()
    {
        await using var f = await SchedulingTestSupport.CreateAsync();
        await using var db = f.Database.CreateContext();
        var source = await db.DangKyCaSet.OrderBy(x => x.Id).FirstAsync();
        var adminId = await db.NhanVienSet.Where(x => x.TenDangNhap == "admin").Select(x => x.Id).SingleAsync();
        var other = new DangKyCa { NhanVienId = adminId, CaLamViecId = source.CaLamViecId, Ngay = source.Ngay, DotId = source.DotId, TrangThai = ShiftRegistrationState.APPROVED };
        db.DangKyCaSet.Add(other);
        var slot = await db.KhungGioDatLichSet.SingleAsync(x => x.Ngay == source.Ngay && x.CaLamViecId == source.CaLamViecId);
        slot.SlotDaDat = 1; slot.PhutDaGiu = 60; await db.SaveChangesAsync();
        using var client = await f.AdminClientAsync();
        await using var blocker = new NpgsqlConnection(f.Database.ConnectionString);
        await blocker.OpenAsync(); await using var transaction = await blocker.BeginTransactionAsync();
        await using (var hold = new NpgsqlCommand("SELECT id FROM khung_gio_dat_lich WHERE id=$1 FOR UPDATE", blocker))
        { hold.Parameters.AddWithValue(slot.Id); await hold.ExecuteNonQueryAsync(); }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var tasks = new[] { source.Id, other.Id }.Select(id => client.PostAsJsonAsync($"/api/admin/shift-registrations/{id}/transfer",
            new { requestId = Guid.NewGuid(), ngay = "2035-01-03", maCa = source.CaLamViecId, lyDo = "Đổi ca" })).ToArray();
        try
        {
            // Both transfers target the same not-yet-committed slot. One queues on
            // the source row; the other first waits for the unique destination insert.
            await CareTestSupport.WaitForLockWaitersAsync(f.Database.ConnectionString, "%khung_gio_dat_lich%FOR UPDATE%", 1, timeout.Token);
            await CareTestSupport.WaitForLockWaitersAsync(f.Database.ConnectionString, "%INSERT INTO khung_gio_dat_lich%", 1, timeout.Token);
        }
        finally { await transaction.RollbackAsync(); }
        var results = await Task.WhenAll(tasks);
        results.Select(x => x.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
        await SchedulingTestSupport.ErrorAsync(results.Single(x => x.StatusCode == HttpStatusCode.Conflict), "CAPACITY_EXCEEDED");
        (await db.DangKyCaSet.CountAsync(x => x.Ngay == source.Ngay && x.CaLamViecId == source.CaLamViecId && x.TrangThai == ShiftRegistrationState.APPROVED)).Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentApprovalOrOppositeTransferCreatesUniqueSlotsWithoutDeadlock(bool transfer)
    {
        await using var f = await SchedulingTestSupport.CreateAsync();
        var first = await SchedulingTestSupport.RegistrationAsync(f, "staff1", transfer ? ShiftRegistrationState.APPROVED : ShiftRegistrationState.PENDING);
        var second = await SchedulingTestSupport.RegistrationAsync(f, "staff2", transfer ? ShiftRegistrationState.APPROVED : ShiftRegistrationState.PENDING,
            transfer ? new DateOnly(2035, 1, 3) : new DateOnly(2035, 1, 2));
        using var client = await f.AdminClientAsync();
        await using var blocker = new NpgsqlConnection(f.Database.ConnectionString);
        await blocker.OpenAsync(); await using var transaction = await blocker.BeginTransactionAsync();
        await HoldEmployeeAsync(blocker, first.NhanVienId); await HoldEmployeeAsync(blocker, second.NhanVienId);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var tasks = new[] { first, second }.Select(r => transfer
            ? client.PostAsJsonAsync($"/api/admin/shift-registrations/{r.Id}/transfer", new { requestId = Guid.NewGuid(), ngay = r.Id == first.Id ? second.Ngay : first.Ngay, maCa = r.CaLamViecId, lyDo = "Đổi ca" })
            : client.PostAsJsonAsync($"/api/admin/shift-registrations/{r.Id}/decision", new { requestId = Guid.NewGuid(), quyetDinh = "APPROVE" })).ToArray();
        try { await AssertBlockedAsync(f.Database.ConnectionString, tasks, 2, timeout.Token); }
        finally { await transaction.RollbackAsync(); }
        var results = await Task.WhenAll(tasks).WaitAsync(timeout.Token);
        results.Should().OnlyContain(x => x.StatusCode == HttpStatusCode.OK);
        await using var db = f.Database.CreateContext();
        (await db.KhungGioDatLichSet.CountAsync(x => x.Ngay > new DateOnly(2035, 1, 1))).Should().Be(transfer ? 2 : 1);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task E55_TransferVersusCheckinAndHotelAssignmentUsesSameEmployeeLock(bool checkin, bool careFirst, bool sourceToday)
    {
        await using var f = await SchedulingTestSupport.CreateAsync();
        await using var db = f.Database.CreateContext();
        var future = sourceToday ? await db.DangKyCaSet.OrderBy(x => x.Id).FirstAsync() :
            await SchedulingTestSupport.RegistrationAsync(f, "staff2", ShiftRegistrationState.APPROVED);
        var roomId = await db.LoaiPhongSet.Select(x => x.Id).FirstAsync();
        var booking = await f.AddBookingAsync(db, spa: false, roomId: roomId, deposit: false);
        var pet = booking.ChiTietDonDonDatLichItems.Single();
        booking.TrangThai = checkin ? BookingState.CONFIRMED : BookingState.IN_PROGRESS;
        pet.TrangThaiChamSoc = checkin ? PetCareState.NOT_ARRIVED : PetCareState.WAITING;
        if (!checkin) pet.CheckInAt = SchedulingTestSupport.Now;
        await db.SaveChangesAsync();
        await using var host = CareTestSupport.Configure(f.Host, clock: new(SchedulingTestSupport.Now));
        using var reception = await SchedulingTestSupport.ClientAsync(f, "staff1");
        using var careClient = host.CreateClient(); careClient.DefaultRequestHeaders.Authorization = reception.DefaultRequestHeaders.Authorization;
        using var admin = await f.AdminClientAsync();
        using var form = CheckinForm(future.NhanVienId);
        Task<HttpResponseMessage> StartCare() => checkin ? careClient.PostAsync($"/api/internal/booking-pets/{pet.Id}/check-in", form) :
            careClient.PutAsJsonAsync($"/api/internal/booking-pets/{pet.Id}/assignment", new { requestId = Guid.NewGuid(), maNhanVien = future.NhanVienId, lyDo = "Giao chăm sóc" });
        Task<HttpResponseMessage> StartTransfer() => admin.PostAsJsonAsync($"/api/admin/shift-registrations/{future.Id}/transfer",
            new { requestId = Guid.NewGuid(), ngay = "2035-01-03", maCa = future.CaLamViecId, lyDo = "Đổi ca" });
        await using var blocker = new NpgsqlConnection(f.Database.ConnectionString);
        await blocker.OpenAsync(); await using var transaction = await blocker.BeginTransactionAsync();
        await HoldEmployeeAsync(blocker, future.NhanVienId);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Task<HttpResponseMessage>? care = null, transfer = null;
        try
        {
            if (careFirst) { care = StartCare(); await AssertBlockedAsync(f.Database.ConnectionString, [care], 1, timeout.Token); }
            else { transfer = StartTransfer(); await AssertBlockedAsync(f.Database.ConnectionString, [transfer], 1, timeout.Token); }
            if (careFirst) transfer = StartTransfer(); else care = StartCare();
            await AssertBlockedAsync(f.Database.ConnectionString, [careFirst ? transfer! : care!], 2, timeout.Token);
        }
        finally { await transaction.RollbackAsync(); }
        var careResult = await care!;
        careResult.StatusCode.Should().Be(!careFirst && sourceToday ? HttpStatusCode.Conflict : HttpStatusCode.OK);
        if (!careFirst && sourceToday) await SchedulingTestSupport.ErrorAsync(careResult, "ASSIGNMENT_UNAVAILABLE");
        (await transfer!).StatusCode.Should().Be(careFirst ? HttpStatusCode.Conflict : HttpStatusCode.OK);
        if (careFirst) await SchedulingTestSupport.ErrorAsync(await transfer, "INVALID_TRANSITION");
        db.ChangeTracker.Clear();
        (await db.ChiTietDonSet.SingleAsync(x => x.Id == pet.Id)).NhanVienId.Should().Be(!careFirst && sourceToday ? null : future.NhanVienId);
        (await db.DangKyCaSet.SingleAsync(x => x.Id == future.Id)).Ngay.Should().Be(careFirst ? future.Ngay : new DateOnly(2035, 1, 3));
    }

    private static MultipartFormDataContent CheckinForm(int employeeId)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(JsonSerializer.Serialize(new
        {
            requestId = Guid.NewGuid(),
            canNangThucTe = 4m,
            tinhTrangLong = new[] { "BINH_THUONG" },
            maNhanVien = employeeId,
            ghiChuSucKhoe = "Khỏe mạnh",
            quyetDinhGia = "SIZE_THUC_TE",
            xacNhanKhach = true
        })), "payload");
        var image = new ByteArrayContent([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(image, "anhTiepNhan", "checkin.png");
        return form;
    }

    [Fact]
    public async Task E28_ConcurrentFirstOverlappingShiftsOnlyOneCommits()
    {
        await using var f = await SchedulingTestSupport.CreateAsync();
        await SchedulingTestSupport.OpenWindowAsync(f);
        await using var db = f.Database.CreateContext();
        var employeeId = await db.NhanVienSet.Where(x => x.TenDangNhap == "staff1").Select(x => x.Id).SingleAsync();
        var first = await db.CaLamViecSet.OrderBy(x => x.Id).FirstAsync();
        var second = new CaLamViec { TenCa = "Overlap", GioBatDau = new(9, 0), GioKetThuc = new(11, 0), IsActive = true, SucChuaPet = 2, TranPhut = 120 };
        db.CaLamViecSet.Add(second); await db.SaveChangesAsync();
        using var client1 = await SchedulingTestSupport.ClientAsync(f, "staff1");
        using var client2 = await SchedulingTestSupport.ClientAsync(f, "staff1");
        await using var blocker = new NpgsqlConnection(f.Database.ConnectionString);
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await HoldEmployeeAsync(blocker, employeeId);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var tasks = new[]
        {
            client1.PostAsJsonAsync("/api/internal/shift-registrations", new { requestId = Guid.NewGuid(), ngay = "2035-01-02", maCa = first.Id }),
            client2.PostAsJsonAsync("/api/internal/shift-registrations", new { requestId = Guid.NewGuid(), ngay = "2035-01-02", maCa = second.Id })
        };
        try
        {
            await AssertBlockedAsync(f.Database.ConnectionString, tasks, 2, timeout.Token);
        }
        finally { await transaction.RollbackAsync(); }
        var responses = await Task.WhenAll(tasks);
        responses.Select(x => x.StatusCode).Should().BeEquivalentTo([HttpStatusCode.Created, HttpStatusCode.Conflict]);
        (await db.DangKyCaSet.CountAsync(x => x.NhanVienId == employeeId && x.Ngay == new DateOnly(2035, 1, 2))).Should().Be(1);
    }

    internal static async Task HoldEmployeeAsync(NpgsqlConnection connection, int employeeId)
    {
        await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock($1, $2)", connection);
        command.Parameters.AddWithValue(0x53434844);
        command.Parameters.AddWithValue(employeeId);
        await command.ExecuteNonQueryAsync();
    }

    internal static async Task AssertBlockedAsync(string connection, Task<HttpResponseMessage>[] requests, int count, CancellationToken ct)
    {
        var all = Task.WhenAll(requests);
        var waiters = CareTestSupport.WaitForLockWaitersAsync(connection, "%pg_advisory_xact_lock%", count, ct);
        var finished = await Task.WhenAny(all, waiters);
        if (finished == all)
        {
            var responses = await all;
            throw new Xunit.Sdk.XunitException("Requests bypassed the held employee lock: " + string.Join(",", responses.Select(x => (int)x.StatusCode)));
        }
        await waiters;
        all.IsCompleted.Should().BeFalse("the blocker transaction still owns the employee key");
    }
}
