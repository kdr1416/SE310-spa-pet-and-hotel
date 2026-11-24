using System.Net;
using System.Data.Common;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PetSpa.Api.Data;
using PetSpa.Api.Services.Identity;
using PetSpa.Core.Entities.Scheduling;
using PetSpa.Core.Enums;
using PetSpa.Tests.Foundation;

namespace PetSpa.Tests.Scheduling;

public class WorkScheduleEndpointTests
{
    [Fact]
    public async Task E54_RejectsIntervalThatCollapsesAtPostgresPrecision()
    {
        await using var f = await SchedulingTestSupport.CreateAsync();
        using var client = await f.AdminClientAsync();
        var response = await client.PostAsJsonAsync("/api/admin/shift-windows", new { requestId = Guid.NewGuid(),
            tuNgay = "2035-01-02", denNgay = "2035-01-08", moAt = "2035-01-01T01:00:00.0000001Z", dongAt = "2035-01-01T01:00:00.0000002Z" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await SchedulingTestSupport.ErrorAsync(response, "VALIDATION_FAILED");
        await using var db = f.Database.CreateContext();
        (await db.AuditLogSet.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("2035-01-01T01:00:00.1234567Z", "2035-01-01T02:00:00.1234567Z")]
    [InlineData("1999-01-01T01:00:00.1234567Z", "1999-01-01T02:00:00.1234567Z")]
    public async Task E54_ResponseAndFingerprintUsePersistedTimestampPrecision(string moAt, string dongAt)
    {
        await using var f = await SchedulingTestSupport.CreateAsync();
        using var client = await f.AdminClientAsync();
        var requestId = Guid.NewGuid();
        var response = await client.PostAsJsonAsync("/api/admin/shift-windows", new { requestId,
            tuNgay = "2035-01-02", denNgay = "2035-01-08", moAt, dongAt });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var data = await SchedulingTestSupport.DataAsync(response);
        await using var db = f.Database.CreateContext();
        var window = await db.DotDangKyCaSet.SingleAsync(x => x.Id == data.GetProperty("maDot").GetInt32());
        data.GetProperty("moAt").GetDateTimeOffset().Should().Be(window.MoAt);
        data.GetProperty("dongAt").GetDateTimeOffset().Should().Be(window.DongAt);
        var replay = await client.PostAsJsonAsync("/api/admin/shift-windows", new { requestId,
            tuNgay = "2035-01-02", denNgay = "2035-01-08", moAt = window.MoAt, dongAt = window.DongAt });
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await db.AuditLogSet.CountAsync(x => x.RequestId == requestId)).Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Scheduling_TransientDatabaseOutageReturns503(bool mutation)
    {
        await using var f = await SchedulingTestSupport.CreateAsync();
        var outage = new ReadOutage();
        await using var host = f.Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddDbContext<PetSpaDbContext>(options => options.AddInterceptors(outage))));
        using var admin = await f.AdminClientAsync();
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = admin.DefaultRequestHeaders.Authorization;
        outage.Enabled = true;
        var response = mutation
            ? await client.PostAsJsonAsync("/api/admin/shift-windows", new { requestId = Guid.NewGuid(), tuNgay = "2035-01-02", denNgay = "2035-01-08", moAt = "2035-01-01T01:00:00Z", dongAt = "2035-01-01T02:00:00Z" })
            : await client.GetAsync("/api/internal/work-schedule?tuNgay=2035-01-01&denNgay=2035-01-02");
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        await SchedulingTestSupport.ErrorAsync(response, "TEMPORARY_FAILURE");
        (await response.Content.ReadAsStringAsync()).Should().NotContain("NpgsqlException").And.NotContain("SELECT");
        await using var db = f.Database.CreateContext();
        (await db.AuditLogSet.CountAsync()).Should().Be(0);
        (await db.DotDangKyCaSet.CountAsync()).Should().Be(1);
    }

    private sealed class ReadOutage : DbCommandInterceptor
    {
        internal bool Enabled { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Enabled) throw new NpgsqlException("Test read outage", new TimeoutException());
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private const string Schedule = "/api/internal/work-schedule";

    [Fact]
    public async Task E27_StaffSeesOnlyOwnRowsAndAdminCanFilterOrReadAll()
    {
        await using var f = await SchedulingTestSupport.CreateAsync();
        using var staff = await SchedulingTestSupport.ClientAsync(f, "staff2");
        using var admin = await f.AdminClientAsync();
        await using var db = f.Database.CreateContext();
        var staffId = await db.NhanVienSet.Where(x => x.TenDangNhap == "staff2").Select(x => x.Id).SingleAsync();
        await SchedulingTestSupport.RegistrationAsync(f, "staff2", ShiftRegistrationState.PENDING);
        await SchedulingTestSupport.RegistrationAsync(f, "staff2", ShiftRegistrationState.REJECTED, new(2035, 1, 3));
        var response = await staff.GetAsync(Schedule + "?tuNgay=2035-01-01&denNgay=2035-01-03");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = (await SchedulingTestSupport.DataAsync(response)).EnumerateArray().ToArray();
        rows.Should().HaveCount(4);
        rows.Should().OnlyContain(x => x.GetProperty("maNhanVien").GetInt32() == staffId);
        rows.Select(x => x.GetProperty("ngay").GetString()).Should().BeInAscendingOrder();
        rows.Select(x => x.GetProperty("trangThai").GetString()).Distinct().Should().HaveCount(3);
        foreach (var id in new[] { staffId, 999 })
            (await staff.GetAsync(Schedule + $"?tuNgay=2035-01-01&denNgay=2035-01-01&maNhanVien={id}"))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.GetAsync(Schedule + "?tuNgay=2035-01-01&denNgay=2035-01-01")).StatusCode.Should().Be(HttpStatusCode.OK);
        var empty = await admin.GetAsync(Schedule + "?tuNgay=2035-01-01&denNgay=2035-01-01&maNhanVien=999");
        (await SchedulingTestSupport.DataAsync(empty)).GetArrayLength().Should().Be(0);
    }

    [Theory]
    [InlineData("tuNgay=2035-01-01&denNgay=2035-03-31", 200, null)]
    [InlineData("tuNgay=2035-01-01&denNgay=2035-04-01", 400, "INVALID_DATE_RANGE")]
    [InlineData("tuNgay=2035-01-02&denNgay=2035-01-01", 400, "INVALID_DATE_RANGE")]
    [InlineData("tuNgay=2035-01-01", 400, "VALIDATION_FAILED")]
    [InlineData("tuNgay=not-a-date&denNgay=2035-01-01", 400, "VALIDATION_FAILED")]
    [InlineData("tuNgay=2035-1-1&denNgay=2035-01-01", 400, "VALIDATION_FAILED")]
    [InlineData("tuNgay=2035-01-01&denNgay=2035-01-01&maNhanVien=0", 400, "VALIDATION_FAILED")]
    [InlineData("tuNgay=2035-01-01&denNgay=2035-01-01&maNhanVien=abc", 400, "VALIDATION_FAILED")]
    [InlineData("tuNgay=2035-01-01&denNgay=2035-01-01&maNhanVien=", 400, "VALIDATION_FAILED")]
    public async Task E27_RejectsBadDatesAndAllowsExactly90Days(string query, int status, string? code)
    {
        await using var f = await SchedulingTestSupport.CreateAsync();
        using var client = await f.AdminClientAsync();
        var response = await client.GetAsync(Schedule + "?" + query);
        ((int)response.StatusCode).Should().Be(status);
        if (code is not null) await SchedulingTestSupport.ErrorAsync(response, code);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("revoke")]
    [InlineData("demote")]
    public async Task E27_ReadsRoleAndGrantsFromDatabase(string change)
    {
        await using var f = await SchedulingTestSupport.CreateAsync();
        using var client = await f.AdminClientAsync();
        await using var db = f.Database.CreateContext();
        var actor = await db.NhanVienSet.SingleAsync(x => x.TenDangNhap == "admin");
        if (change == "inactive") actor.IsActive = false;
        if (change == "demote") actor.VaiTro = Role.STAFF;
        if (change == "revoke") db.NhanVienQuyenSet.Remove(await db.NhanVienQuyenSet.SingleAsync(x => x.NhanVienId == actor.Id && x.MaQuyen == "SCHEDULE_REGISTER"));
        await db.SaveChangesAsync();
        var response = await client.GetAsync(Schedule + "?tuNgay=2035-01-01&denNgay=2035-01-01&maNhanVien=999");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task E27_RequiresAuthentication()
    {
        await using var f = await SchedulingTestSupport.CreateAsync();
        using var client = f.Host.CreateClient();
        (await client.GetAsync(Schedule + "?tuNgay=2035-01-01&denNgay=2035-01-01")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task E54_CreatesFirstWindowAndReplaysWithoutWritingTwice()
    {
        await using var f = await SchedulingTestSupport.CreateAsync();
        using var client = await f.AdminClientAsync();
        await using var db = f.Database.CreateContext();
        await db.DangKyCaSet.ExecuteDeleteAsync();
        await db.DotDangKyCaSet.ExecuteDeleteAsync();
        var requestId = Guid.NewGuid();
        var payload = new { requestId, tuNgay = "2035-01-02", denNgay = "2035-01-08", moAt = "2035-01-01T08:00:00+07:00", dongAt = "2035-01-01T09:00:00+07:00" };
        var created = await client.PostAsJsonAsync("/api/admin/shift-windows", payload);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await SchedulingTestSupport.DataAsync(created)).GetProperty("maDot").GetInt32();
        await db.DotDangKyCaSet.Where(x => x.Id == id).ExecuteUpdateAsync(x => x.SetProperty(w => w.DenNgay, new DateOnly(2035, 1, 9)));
        var replay = await client.PostAsJsonAsync("/api/admin/shift-windows", payload);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await SchedulingTestSupport.DataAsync(replay)).GetProperty("denNgay").GetString().Should().Be("2035-01-08");
        // Same instants with different offsets are the same logical command.
        var utc = new { requestId, tuNgay = "2035-01-02", denNgay = "2035-01-08", moAt = "2035-01-01T01:00:00Z", dongAt = "2035-01-01T02:00:00Z" };
        (await client.PostAsJsonAsync("/api/admin/shift-windows", utc)).StatusCode.Should().Be(HttpStatusCode.OK);
        var conflict = await client.PostAsJsonAsync("/api/admin/shift-windows", new { requestId, tuNgay = "2035-01-03", denNgay = "2035-01-08", payload.moAt, payload.dongAt });
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await SchedulingTestSupport.ErrorAsync(conflict, "IDEMPOTENCY_CONFLICT");
        (await db.DotDangKyCaSet.CountAsync()).Should().Be(1);
        (await db.AuditLogSet.CountAsync(x => x.RequestId == requestId && x.DoiTuongId == id)).Should().Be(1);
        var admin = await db.NhanVienSet.SingleAsync(x => x.TenDangNhap == "admin");
        admin.IsActive = false;
        await db.SaveChangesAsync();
        (await client.PostAsJsonAsync("/api/admin/shift-windows", payload)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("staff1", false)]
    [InlineData("admin", true)]
    public async Task E54_RequiresAdminRoleAndCurrentGrant(string username, bool revoke)
    {
        await using var f = await SchedulingTestSupport.CreateAsync();
        using var client = await SchedulingTestSupport.ClientAsync(f, username);
        await using var db = f.Database.CreateContext();
        var actor = await db.NhanVienSet.SingleAsync(x => x.TenDangNhap == username);
        if (revoke) db.NhanVienQuyenSet.Remove(await db.NhanVienQuyenSet.SingleAsync(x => x.NhanVienId == actor.Id && x.MaQuyen == "SCHEDULE_APPROVE"));
        else db.NhanVienQuyenSet.Add(new() { NhanVienId = actor.Id, MaQuyen = "SCHEDULE_APPROVE", CapBoiId = await db.NhanVienSet.Where(x => x.TenDangNhap == "admin").Select(x => x.Id).SingleAsync() });
        await db.SaveChangesAsync();
        (await client.PostAsJsonAsync("/api/admin/shift-windows", new { requestId = Guid.NewGuid(), tuNgay = "2035-01-02", denNgay = "2035-01-08", moAt = "2035-01-01T01:00:00Z", dongAt = "2035-01-01T02:00:00Z" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("2035-01-08", "2035-01-02", "2035-01-01T01:00:00Z", "2035-01-01T02:00:00Z")]
    [InlineData("2035-01-02", "2035-01-08", "2035-01-01T02:00:00Z", "2035-01-01T01:00:00Z")]
    [InlineData("2035-01-02", "2035-01-08", "2035-01-01T01:00:00", "2035-01-01T02:00:00Z")]
    public async Task E54_ValidatesWindow(string tuNgay, string denNgay, string moAt, string dongAt)
    {
        await using var f = await SchedulingTestSupport.CreateAsync();
        using var client = await f.AdminClientAsync();
        var response = await client.PostAsJsonAsync("/api/admin/shift-windows", new { requestId = Guid.NewGuid(), tuNgay, denNgay, moAt, dongAt });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await SchedulingTestSupport.ErrorAsync(response, "VALIDATION_FAILED");
    }

    [Theory]
    [InlineData(-1, 409)]
    [InlineData(0, 201)]
    [InlineData(3600, 201)]
    [InlineData(3601, 409)]
    public async Task E28_WindowBoundariesUseServerClock(int seconds, int status)
    {
        await using var f = await SchedulingTestSupport.CreateAsync();
        await SchedulingTestSupport.OpenWindowAsync(f);
        f.Clock.UtcNow = SchedulingTestSupport.Now.AddSeconds(seconds);
        using var client = await SchedulingTestSupport.ClientAsync(f, "staff1");
        await using var db = f.Database.CreateContext();
        var shift = await db.CaLamViecSet.OrderBy(x => x.Id).FirstAsync();
        var response = await client.PostAsJsonAsync("/api/internal/shift-registrations", new { requestId = Guid.NewGuid(), ngay = "2035-01-02", maCa = shift.Id });
        ((int)response.StatusCode).Should().Be(status);
        if (status == 409) await SchedulingTestSupport.ErrorAsync(response, "INVALID_TRANSITION");
    }

    [Fact]
    public async Task E28_RegistersPendingAndReplayReturnsOriginalSnapshot()
    {
        await using var f = await SchedulingTestSupport.CreateAsync();
        await SchedulingTestSupport.OpenWindowAsync(f);
        using var client = await SchedulingTestSupport.ClientAsync(f, "staff1");
        await using var db = f.Database.CreateContext();
        var shift = await db.CaLamViecSet.OrderBy(x => x.Id).FirstAsync();
        var payload = new { requestId = Guid.NewGuid(), ngay = "2035-01-02", maCa = shift.Id };
        var created = await client.PostAsJsonAsync("/api/internal/shift-registrations", payload);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var data = await SchedulingTestSupport.DataAsync(created);
        data.GetProperty("trangThai").GetString().Should().Be("PENDING");
        var registration = await db.DangKyCaSet.SingleAsync(x => x.Id == data.GetProperty("maDangKy").GetInt32());
        registration.TrangThai = ShiftRegistrationState.REJECTED;
        await db.SaveChangesAsync();
        var replay = await client.PostAsJsonAsync("/api/internal/shift-registrations", payload);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await SchedulingTestSupport.DataAsync(replay)).GetProperty("trangThai").GetString().Should().Be("PENDING");
        (await db.AuditLogSet.CountAsync(x => x.RequestId == payload.requestId)).Should().Be(1);
        var conflict = await client.PostAsJsonAsync("/api/internal/shift-registrations", new { payload.requestId, ngay = "2035-01-03", payload.maCa });
        await SchedulingTestSupport.ErrorAsync(conflict, "IDEMPOTENCY_CONFLICT");
    }

    [Theory]
    [InlineData(ShiftRegistrationState.PENDING)]
    [InlineData(ShiftRegistrationState.APPROVED)]
    [InlineData(ShiftRegistrationState.REJECTED)]
    public async Task E28_DuplicateAnyStateIsRejected(ShiftRegistrationState state)
    {
        await using var f = await SchedulingTestSupport.CreateAsync();
        var registration = await SchedulingTestSupport.RegistrationAsync(f, "staff1", state);
        using var client = await SchedulingTestSupport.ClientAsync(f, "staff1");
        var response = await client.PostAsJsonAsync("/api/internal/shift-registrations", new { requestId = Guid.NewGuid(), ngay = registration.Ngay, maCa = registration.CaLamViecId });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await SchedulingTestSupport.ErrorAsync(response, "INVALID_TRANSITION");
    }

    [Theory]
    [InlineData(ShiftRegistrationState.PENDING, 9, 409)]
    [InlineData(ShiftRegistrationState.APPROVED, 9, 409)]
    [InlineData(ShiftRegistrationState.REJECTED, 9, 201)]
    [InlineData(ShiftRegistrationState.PENDING, 12, 201)]
    public async Task E28_OverlapOnlyForPendingOrApprovedAndAdjacentAllowed(ShiftRegistrationState state, int hour, int status)
    {
        await using var f = await SchedulingTestSupport.CreateAsync();
        var registration = await SchedulingTestSupport.RegistrationAsync(f, "staff1", state);
        await using var db = f.Database.CreateContext();
        var shift = new CaLamViec { TenCa = "Test overlap", GioBatDau = new(hour, 0), GioKetThuc = new(13, 0), IsActive = true, SucChuaPet = 3, TranPhut = 60 };
        db.CaLamViecSet.Add(shift); await db.SaveChangesAsync();
        using var client = await SchedulingTestSupport.ClientAsync(f, "staff1");
        var response = await client.PostAsJsonAsync("/api/internal/shift-registrations", new { requestId = Guid.NewGuid(), ngay = registration.Ngay, maCa = shift.Id });
        ((int)response.StatusCode).Should().Be(status);
    }

    [Theory]
    [InlineData("staff1", 409)]
    [InlineData("staff2", 201)]
    public async Task E28_HolidayRequiresCarePermission(string username, int status)
    {
        await using var f = await SchedulingTestSupport.CreateAsync();
        await SchedulingTestSupport.OpenWindowAsync(f);
        await using var db = f.Database.CreateContext();
        db.NgayNghiSet.Add(new() { Ngay = new(2035, 1, 2), LyDo = "Holiday", TaoBoiId = await db.NhanVienSet.Where(x => x.TenDangNhap == "admin").Select(x => x.Id).SingleAsync() });
        await db.SaveChangesAsync();
        using var client = await SchedulingTestSupport.ClientAsync(f, username);
        var response = await client.PostAsJsonAsync("/api/internal/shift-registrations", new { requestId = Guid.NewGuid(), ngay = "2035-01-02", maCa = await db.CaLamViecSet.Select(x => x.Id).FirstAsync() });
        ((int)response.StatusCode).Should().Be(status);
    }

    [Theory]
    [InlineData("APPROVE")]
    [InlineData("REJECT")]
    public async Task E29_DecidesAfterDeadlineAndRecordsReasonAndReplay(string decision)
    {
        await using var f = await SchedulingTestSupport.CreateAsync();
        var row = await SchedulingTestSupport.RegistrationAsync(f, "staff2", ShiftRegistrationState.PENDING);
        f.Clock.UtcNow = SchedulingTestSupport.Now.AddHours(2);
        using var admin = await f.AdminClientAsync();
        var payload = new { requestId = Guid.NewGuid(), quyetDinh = decision, lyDo = "  Lý do  " };
        var route = $"/api/admin/shift-registrations/{row.Id}/decision";
        var response = await admin.PostAsJsonAsync(route, payload);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var db = f.Database.CreateContext();
        var saved = await db.DangKyCaSet.SingleAsync(x => x.Id == row.Id);
        saved.TrangThai.Should().Be(decision == "APPROVE" ? ShiftRegistrationState.APPROVED : ShiftRegistrationState.REJECTED);
        saved.DuyetAt.Should().Be(f.Clock.UtcNow);
        saved.DuyetBoiId.Should().NotBeNull();
        saved.LyDoDieuChuyen.Should().BeNull();
        (await db.AuditLogSet.SingleAsync(x => x.RequestId == payload.requestId)).LyDo.Should().Be("Lý do");
        var slots = await db.KhungGioDatLichSet.Where(x => x.Ngay == row.Ngay).ToListAsync();
        slots.Should().HaveCount(decision == "APPROVE" ? 1 : 0);
        (await admin.PostAsJsonAsync(route, new { payload.requestId, quyetDinh = decision, lyDo = "Lý do" })).StatusCode.Should().Be(HttpStatusCode.OK);
        var other = await admin.PostAsJsonAsync(route, new { requestId = Guid.NewGuid(), quyetDinh = decision });
        other.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await SchedulingTestSupport.ErrorAsync(other, "INVALID_TRANSITION");
        var wrongTarget = await admin.PostAsJsonAsync($"/api/admin/shift-registrations/99999/decision", payload);
        await SchedulingTestSupport.ErrorAsync(wrongTarget, "IDEMPOTENCY_CONFLICT");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task E29_ApprovalCreatesRealSlotOrPreservesExistingSlot(bool holiday, bool existing)
    {
        await using var f = await SchedulingTestSupport.CreateAsync();
        var row = await SchedulingTestSupport.RegistrationAsync(f, "staff2", ShiftRegistrationState.PENDING);
        await using var db = f.Database.CreateContext();
        var shift = await db.CaLamViecSet.SingleAsync(x => x.Id == row.CaLamViecId);
        if (holiday) db.NgayNghiSet.Add(new() { Ngay = row.Ngay, LyDo = "Holiday", TaoBoiId = await db.NhanVienSet.Where(x => x.TenDangNhap == "admin").Select(x => x.Id).SingleAsync() });
        if (existing) db.KhungGioDatLichSet.Add(new() { Ngay = row.Ngay, CaLamViecId = shift.Id, MoNhanKhach = false, TongSlot = 7, TranPhut = 200, SlotDaDat = 1, PhutDaGiu = 50 });
        await db.SaveChangesAsync();
        using var client = await f.AdminClientAsync();
        (await client.PostAsJsonAsync($"/api/admin/shift-registrations/{row.Id}/decision", new { requestId = Guid.NewGuid(), quyetDinh = "APPROVE" })).StatusCode.Should().Be(HttpStatusCode.OK);
        db.ChangeTracker.Clear();
        var slot = await db.KhungGioDatLichSet.SingleAsync(x => x.Ngay == row.Ngay && x.CaLamViecId == row.CaLamViecId);
        slot.MoNhanKhach.Should().Be(!holiday && !existing);
        slot.TongSlot.Should().Be(existing ? 7 : shift.SucChuaPet);
        slot.TranPhut.Should().Be(existing ? 200 : shift.TranPhut);
        slot.SlotDaDat.Should().Be(existing ? 1 : 0);
        slot.PhutDaGiu.Should().Be(existing ? 50 : 0);
        var options = await SchedulingTestSupport.DataAsync(await client.GetAsync("/api/booking-options?ngay=2035-01-02"));
        options.GetProperty("ca").EnumerateArray().Should().Contain(x => x.GetProperty("maCa").GetInt32() == shift.Id && x.GetProperty("maKhungGio").GetInt32() == slot.Id);
        var candidates = await SchedulingTestSupport.DataAsync(await client.GetAsync($"/api/internal/assignment-candidates?ngay=2035-01-02&maCa={shift.Id}"));
        candidates.EnumerateArray().Should().Contain(x => x.GetProperty("maNhanVien").GetInt32() == row.NhanVienId);
    }

    [Theory]
    [InlineData("0", 400)]
    [InlineData("-1", 400)]
    [InlineData("bad", 400)]
    [InlineData("99999", 404)]
    public async Task E29_ValidatesTargetId(string id, int status)
    {
        await using var f = await SchedulingTestSupport.CreateAsync();
        using var client = await f.AdminClientAsync();
        var response = await client.PostAsJsonAsync($"/api/admin/shift-registrations/{id}/decision", new { requestId = Guid.NewGuid(), quyetDinh = "APPROVE" });
        ((int)response.StatusCode).Should().Be(status);
    }

    [Fact]
    public async Task E55_TransferAfterDeadlinePreservesProvenanceAndReplaySnapshot()
    {
        await using var f = await SchedulingTestSupport.CreateAsync();
        var row = await SchedulingTestSupport.RegistrationAsync(f, "staff2", ShiftRegistrationState.APPROVED);
        await using var db = f.Database.CreateContext();
        var saved = await db.DangKyCaSet.SingleAsync(x => x.Id == row.Id);
        saved.DuyetAt = SchedulingTestSupport.Now.AddDays(-1);
        saved.DuyetBoiId = await db.NhanVienSet.Where(x => x.TenDangNhap == "admin").Select(x => x.Id).SingleAsync();
        await db.SaveChangesAsync();
        var originalApproval = saved.DuyetAt;
        f.Clock.UtcNow = SchedulingTestSupport.Now.AddHours(2);
        using var client = await f.AdminClientAsync();
        var route = $"/api/admin/shift-registrations/{row.Id}/transfer";
        var payload = new { requestId = Guid.NewGuid(), ngay = "2035-01-03", maCa = row.CaLamViecId, lyDo = "  Điều phối  " };
        var response = await client.PostAsJsonAsync(route, payload);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await db.Entry(saved).ReloadAsync();
        saved.DotId.Should().Be(row.DotId); saved.DuyetAt.Should().Be(originalApproval);
        saved.TrangThai.Should().Be(ShiftRegistrationState.APPROVED);
        saved.LyDoDieuChuyen.Should().Be("Điều phối");
        var audit = await db.AuditLogSet.SingleAsync(x => x.RequestId == payload.requestId);
        audit.Truoc.Should().Contain("2035-01-02"); audit.Sau.Should().Contain("2035-01-03");
        (await db.KhungGioDatLichSet.AnyAsync(x => x.Ngay == saved.Ngay && x.CaLamViecId == saved.CaLamViecId)).Should().BeTrue();
        (await client.PostAsJsonAsync(route, new { requestId = Guid.NewGuid(), ngay = "2035-01-04", payload.maCa, lyDo = "Lần tiếp theo" })).StatusCode.Should().Be(HttpStatusCode.OK);
        var replay = await client.PostAsJsonAsync(route, new { payload.requestId, payload.ngay, payload.maCa, lyDo = "Điều phối" });
        (await SchedulingTestSupport.DataAsync(replay)).GetProperty("ngay").GetString().Should().Be("2035-01-03");
        (await db.AuditLogSet.CountAsync(x => x.RequestId == payload.requestId)).Should().Be(1);
    }

    [Theory]
    [InlineData(ShiftRegistrationState.PENDING)]
    [InlineData(ShiftRegistrationState.REJECTED)]
    public async Task E55_OnlyApprovedCanTransfer(ShiftRegistrationState state)
    {
        await using var f = await SchedulingTestSupport.CreateAsync();
        var row = await SchedulingTestSupport.RegistrationAsync(f, "staff2", state);
        using var client = await f.AdminClientAsync();
        var response = await client.PostAsJsonAsync($"/api/admin/shift-registrations/{row.Id}/transfer", new { requestId = Guid.NewGuid(), ngay = "2035-01-03", maCa = row.CaLamViecId, lyDo = "Đổi ca" });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await SchedulingTestSupport.ErrorAsync(response, "INVALID_TRANSITION");
    }

    [Theory]
    [InlineData(ShiftRegistrationState.PENDING)]
    [InlineData(ShiftRegistrationState.APPROVED)]
    [InlineData(ShiftRegistrationState.REJECTED)]
    public async Task E55_DestinationDuplicateAnyStateIsRejected(ShiftRegistrationState state)
    {
        await using var f = await SchedulingTestSupport.CreateAsync();
        var row = await SchedulingTestSupport.RegistrationAsync(f, "staff2", ShiftRegistrationState.APPROVED);
        var destination = await SchedulingTestSupport.RegistrationAsync(f, "staff2", state, new(2035, 1, 3));
        using var client = await f.AdminClientAsync();
        var response = await client.PostAsJsonAsync($"/api/admin/shift-registrations/{row.Id}/transfer", new { requestId = Guid.NewGuid(), ngay = destination.Ngay, maCa = destination.CaLamViecId, lyDo = "Đổi ca" });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await SchedulingTestSupport.ErrorAsync(response, "INVALID_TRANSITION");
        await using var db = f.Database.CreateContext();
        (await db.KhungGioDatLichSet.AnyAsync(x => x.Ngay == destination.Ngay)).Should().BeFalse("failed command must roll back any new slot");
    }

    [Theory]
    [InlineData(false, PetCareState.WAITING, 409)]
    [InlineData(true, PetCareState.WAITING, 409)]
    [InlineData(true, PetCareState.READY, 409)]
    [InlineData(true, PetCareState.DONE, 200)]
    [InlineData(true, PetCareState.CANCELLED, 200)]
    public async Task E55_ActiveAssignmentsAndHotelOverstayAreProtected(bool hotel, PetCareState state, int expected)
    {
        await using var f = await SchedulingTestSupport.CreateAsync();
        await using var db = f.Database.CreateContext();
        var employeeId = await db.NhanVienSet.Where(x => x.TenDangNhap == "staff2").Select(x => x.Id).SingleAsync();
        var source = hotel ? await SchedulingTestSupport.RegistrationAsync(f, "staff2", ShiftRegistrationState.APPROVED) :
            await db.DangKyCaSet.OrderBy(x => x.CaLamViecId).FirstAsync(x => x.NhanVienId == employeeId);
        var room = hotel ? (int?)await db.LoaiPhongSet.Select(x => x.Id).FirstAsync() : null;
        var booking = await f.AddBookingAsync(db, spa: !hotel, roomId: room, deposit: false);
        var pet = booking.ChiTietDonDonDatLichItems.Single();
        pet.NhanVienId = employeeId; pet.CheckInAt = SchedulingTestSupport.Now.AddDays(-3); pet.TrangThaiChamSoc = state;
        booking.BatDauDuKien = SchedulingTestSupport.Now.AddDays(-4);
        booking.KetThucDuKien = SchedulingTestSupport.Now.AddDays(-1); // already past expected checkout
        var slot = await db.KhungGioDatLichSet.FirstAsync(x => x.Id == booking.KhungGioId);
        slot.SlotDaDat = 0; slot.PhutDaGiu = 0; // isolate assignment guard from the separate capacity tests
        await db.SaveChangesAsync();
        using var client = await f.AdminClientAsync();
        var response = await client.PostAsJsonAsync($"/api/admin/shift-registrations/{source.Id}/transfer", new { requestId = Guid.NewGuid(), ngay = "2035-01-04", maCa = source.CaLamViecId, lyDo = "Đổi ca" });
        ((int)response.StatusCode).Should().Be(expected);
        if (expected == 409) await SchedulingTestSupport.ErrorAsync(response, "INVALID_TRANSITION");
    }

    [Theory]
    [InlineData("none", 0, 409)]
    [InlineData("staff1", 60, 409)]
    [InlineData("admin", 240, 200)]
    [InlineData("admin", 241, 409)]
    [InlineData("inactive", 60, 409)]
    public async Task E55_TransferCannotBreakCommittedCapacity(string remaining, int minutes, int expected)
    {
        await using var f = await SchedulingTestSupport.CreateAsync();
        await using var db = f.Database.CreateContext();
        var row = await db.DangKyCaSet.OrderBy(x => x.CaLamViecId).FirstAsync();
        var slot = await db.KhungGioDatLichSet.SingleAsync(x => x.Ngay == row.Ngay && x.CaLamViecId == row.CaLamViecId);
        slot.SlotDaDat = 1; slot.PhutDaGiu = minutes; slot.TranPhut = 480;
        if (remaining != "none")
        {
            var name = remaining == "inactive" ? "admin" : remaining;
            var employee = await db.NhanVienSet.SingleAsync(x => x.TenDangNhap == name);
            if (remaining == "inactive") employee.IsActive = false;
            db.DangKyCaSet.Add(new() { NhanVienId = employee.Id, Ngay = row.Ngay, CaLamViecId = row.CaLamViecId, DotId = row.DotId, TrangThai = ShiftRegistrationState.APPROVED });
        }
        await db.SaveChangesAsync();
        // If admin was disabled, use a second active ADMIN for the command.
        using var client = await f.AdminClientAsync();
        if (remaining == "inactive")
        {
            var actor = await db.NhanVienSet.SingleAsync(x => x.TenDangNhap == "staff1");
            actor.VaiTro = Role.ADMIN;
            db.NhanVienQuyenSet.Add(new() { NhanVienId = actor.Id, MaQuyen = "SCHEDULE_APPROVE", CapBoiId = actor.Id });
            await db.SaveChangesAsync();
            using var other = await SchedulingTestSupport.ClientAsync(f, "staff1");
            client.DefaultRequestHeaders.Authorization = other.DefaultRequestHeaders.Authorization;
        }
        var response = await client.PostAsJsonAsync($"/api/admin/shift-registrations/{row.Id}/transfer", new { requestId = Guid.NewGuid(), ngay = "2035-01-03", maCa = row.CaLamViecId, lyDo = "Đổi ca" });
        ((int)response.StatusCode).Should().Be(expected);
        if (expected == 409) await SchedulingTestSupport.ErrorAsync(response, "CAPACITY_EXCEEDED");
        await db.Entry(slot).ReloadAsync(); slot.PhutDaGiu.Should().Be(minutes); slot.SlotDaDat.Should().Be(1);
    }

    [Theory]
    [InlineData("inactive-shift", 409)]
    [InlineData("missing-shift", 404)]
    [InlineData("outside-window", 409)]
    public async Task E28_ValidatesShiftAndWindowCoverage(string mode, int status)
    {
        await using var f = await SchedulingTestSupport.CreateAsync();
        await SchedulingTestSupport.OpenWindowAsync(f);
        await using var db = f.Database.CreateContext();
        var shift = await db.CaLamViecSet.FirstAsync();
        if (mode == "inactive-shift") { shift.IsActive = false; await db.SaveChangesAsync(); }
        using var client = await SchedulingTestSupport.ClientAsync(f, "staff1");
        var response = await client.PostAsJsonAsync("/api/internal/shift-registrations", new { requestId = Guid.NewGuid(), ngay = mode == "outside-window" ? "2035-01-09" : "2035-01-02", maCa = mode == "missing-shift" ? 99999 : shift.Id });
        ((int)response.StatusCode).Should().Be(status);
    }

    [Fact]
    public async Task E28_SelectsLatestOpenWindowAndRequestKeysAreScopedByActorAndOperation()
    {
        await using var f = await SchedulingTestSupport.CreateAsync();
        await SchedulingTestSupport.OpenWindowAsync(f);
        var latest = await SchedulingTestSupport.OpenWindowAsync(f);
        await using var db = f.Database.CreateContext();
        var futureId = await SchedulingTestSupport.OpenWindowAsync(f);
        await db.DotDangKyCaSet.Where(x => x.Id == futureId).ExecuteUpdateAsync(x => x.SetProperty(w => w.MoAt, SchedulingTestSupport.Now.AddMinutes(1)));
        var requestId = Guid.NewGuid(); var shiftId = await db.CaLamViecSet.Select(x => x.Id).FirstAsync();
        using var one = await SchedulingTestSupport.ClientAsync(f, "staff1");
        using var two = await SchedulingTestSupport.ClientAsync(f, "staff2");
        foreach (var client in new[] { one, two })
        {
            var response = await client.PostAsJsonAsync("/api/internal/shift-registrations", new { requestId, ngay = "2035-01-02", maCa = shiftId });
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            var id = (await SchedulingTestSupport.DataAsync(response)).GetProperty("maDangKy").GetInt32();
            (await db.DangKyCaSet.SingleAsync(x => x.Id == id)).DotId.Should().Be(latest);
        }
        using var admin = await f.AdminClientAsync();
        var staffId = await db.NhanVienSet.Where(e => e.TenDangNhap == "staff2").Select(e => e.Id).SingleAsync();
        var registration = await db.DangKyCaSet.SingleAsync(x => x.DotId == latest && x.NhanVienId == staffId);
        (await admin.PostAsJsonAsync($"/api/admin/shift-registrations/{registration.Id}/decision", new { requestId, quyetDinh = "APPROVE" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await db.AuditLogSet.CountAsync(x => x.RequestId == requestId)).Should().Be(3);
    }

    [Theory]
    [InlineData("same", 409)]
    [InlineData("blank", 400)]
    [InlineData("inactive", 409)]
    [InlineData("missing", 404)]
    [InlineData("holiday", 409)]
    public async Task E55_DestinationAndReasonGuards(string mode, int status)
    {
        await using var f = await SchedulingTestSupport.CreateAsync();
        // staff1 lacks CARE_UPDATE while admin has it: holiday guard must check the subject.
        var row = await SchedulingTestSupport.RegistrationAsync(f, "staff1", ShiftRegistrationState.APPROVED);
        await using var db = f.Database.CreateContext();
        if (mode == "inactive") await db.CaLamViecSet.Where(x => x.Id == row.CaLamViecId).ExecuteUpdateAsync(x => x.SetProperty(s => s.IsActive, false));
        if (mode == "holiday")
        {
            db.NgayNghiSet.Add(new() { Ngay = new(2035, 1, 3), LyDo = "Holiday", TaoBoiId = await db.NhanVienSet.Where(x => x.TenDangNhap == "admin").Select(x => x.Id).SingleAsync() });
            await db.SaveChangesAsync();
        }
        using var admin = await f.AdminClientAsync();
        var response = await admin.PostAsJsonAsync($"/api/admin/shift-registrations/{row.Id}/transfer", new { requestId = Guid.NewGuid(), ngay = mode == "same" ? "2035-01-02" : "2035-01-03", maCa = mode == "missing" ? 99999 : row.CaLamViecId, lyDo = mode == "blank" ? "  " : "Đổi ca" });
        ((int)response.StatusCode).Should().Be(status);
        if (status == 409) await SchedulingTestSupport.ErrorAsync(response, "INVALID_TRANSITION");
    }

    [Theory]
    [InlineData("register")]
    [InlineData("decision")]
    [InlineData("transfer")]
    public async Task MutationRoutesRequireAuthenticationAndCurrentPermission(string operation)
    {
        await using var f = await SchedulingTestSupport.CreateAsync();
        var row = await SchedulingTestSupport.RegistrationAsync(f, "staff2", operation == "decision" ? ShiftRegistrationState.PENDING : ShiftRegistrationState.APPROVED);
        var route = operation == "register" ? "/api/internal/shift-registrations" : $"/api/admin/shift-registrations/{row.Id}/{operation}";
        object payload = operation == "decision" ? new { requestId = Guid.NewGuid(), quyetDinh = "APPROVE" } :
            new { requestId = Guid.NewGuid(), ngay = "2035-01-03", maCa = row.CaLamViecId, lyDo = "Đổi ca" };
        using var anonymous = f.Host.CreateClient();
        (await anonymous.PostAsJsonAsync(route, payload)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var actor = await SchedulingTestSupport.ClientAsync(f, operation == "register" ? "staff1" : "admin");
        await using var db = f.Database.CreateContext();
        var name = operation == "register" ? "staff1" : "admin";
        var actorId = await db.NhanVienSet.Where(x => x.TenDangNhap == name).Select(x => x.Id).SingleAsync();
        var permission = operation == "register" ? "SCHEDULE_REGISTER" : "SCHEDULE_APPROVE";
        await db.NhanVienQuyenSet.Where(x => x.NhanVienId == actorId && x.MaQuyen == permission).ExecuteDeleteAsync();
        var forbidden = await actor.PostAsJsonAsync(route, payload);
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await SchedulingTestSupport.ErrorAsync(forbidden, "FORBIDDEN");
    }
}

internal static class SchedulingTestSupport
{
    internal static readonly DateTimeOffset Now = new(2035, 1, 1, 1, 0, 0, TimeSpan.Zero);
    internal static Task<FoundationFixture> CreateAsync() => FoundationTestSupport.CreateAsync(Now);

    internal static async Task<int> OpenWindowAsync(FoundationFixture f)
    {
        await using var db = f.Database.CreateContext();
        var window = new DotDangKyCa
        {
            TuNgay = new(2035, 1, 2),
            DenNgay = new(2035, 1, 8),
            MoAt = Now,
            DongAt = Now.AddHours(1),
            TaoBoiId = await db.NhanVienSet.Where(x => x.TenDangNhap == "admin").Select(x => x.Id).SingleAsync()
        };
        db.DotDangKyCaSet.Add(window); await db.SaveChangesAsync(); return window.Id;
    }

    internal static async Task<DangKyCa> RegistrationAsync(FoundationFixture f, string username, ShiftRegistrationState state, DateOnly? day = null)
    {
        var windowId = await OpenWindowAsync(f);
        await using var db = f.Database.CreateContext();
        var row = new DangKyCa
        {
            Ngay = day ?? new(2035, 1, 2),
            CaLamViecId = await db.CaLamViecSet.OrderBy(x => x.Id).Select(x => x.Id).FirstAsync(),
            NhanVienId = await db.NhanVienSet.Where(x => x.TenDangNhap == username).Select(x => x.Id).SingleAsync(),
            DotId = windowId,
            TrangThai = state
        };
        db.DangKyCaSet.Add(row); await db.SaveChangesAsync(); return row;
    }

    internal static async Task<HttpClient> ClientAsync(FoundationFixture f, string username)
    {
        await using var db = f.Database.CreateContext();
        var actor = await db.NhanVienSet.SingleAsync(x => x.TenDangNhap == username);
        using var scope = f.Host.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateToken(actor).Token;
        var client = f.Host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    internal static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("data").Clone();
    }

    internal static async Task ErrorAsync(HttpResponseMessage response, string code)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("errorCode").GetString().Should().Be(code);
        json.RootElement.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
    }
}
