using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PetSpa.Api.Data;
using PetSpa.Api.Services.Care;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Scheduling;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Scheduling;
using PetSpa.Core.Enums;

namespace PetSpa.Api.Services.Scheduling;

public sealed record ScheduleResult<T>(int StatusCode, T? Data = default, string Message = "",
    string? ErrorCode = null, List<ApiErrorItem>? Errors = null);

public sealed class WorkScheduleService(PetSpaDbContext db, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly long PgEpochTicks = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;

    public Task<ScheduleResult<ShiftRegistrationDto>> TransferAsync(int actorId, int registrationId,
        ShiftTransferRequest request, string traceId, CancellationToken ct)
    {
        ShiftRegistrationDto? previous = null;
        return ExecuteMutationAsync(actorId, request.RequestId, "SHIFT_TRANSFER",
            new { registrationId, ngay = request.Ngay, maCa = request.MaCa, lyDo = request.LyDo.Trim() }, traceId, async _ =>
            {
                if (string.IsNullOrWhiteSpace(request.LyDo)) return Validation<ShiftRegistrationDto>("lyDo", "Lý do điều chuyển là bắt buộc.");
                var source = await db.DangKyCaSet.AsNoTracking().SingleOrDefaultAsync(x => x.Id == registrationId, ct);
                if (source is null) return Missing<ShiftRegistrationDto>();
                await EmployeeScheduleLock.AcquireAsync(db, source.NhanVienId, ct);
                if (await FindActorAsync(actorId, "SCHEDULE_APPROVE", true, ct) is null) return Forbidden<ShiftRegistrationDto>();
                source = await db.DangKyCaSet.AsNoTracking().SingleAsync(x => x.Id == registrationId, ct);
                if (source.TrangThai != ShiftRegistrationState.APPROVED || (source.Ngay == request.Ngay && source.CaLamViecId == request.MaCa))
                    return Invalid<ShiftRegistrationDto>("Chỉ điều chuyển ca đã duyệt sang ngày hoặc ca khác.");
                var destination = await db.CaLamViecSet.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.MaCa, ct);
                if (destination is null) return Missing<ShiftRegistrationDto>();
                if (!destination.IsActive || !await EmployeeActiveAsync(source.NhanVienId, ct) || !await HolidayAllowedAsync(source.NhanVienId, request.Ngay, ct))
                    return Invalid<ShiftRegistrationDto>("Nhân viên hoặc ca đích không đủ điều kiện điều chuyển.");
                var sourceShift = await db.CaLamViecSet.AsNoTracking().SingleAsync(x => x.Id == source.CaLamViecId, ct);
                var resources = new[] { (Day: source.Ngay, Shift: sourceShift), (Day: request.Ngay, Shift: destination) }
                    .OrderBy(x => x.Day).ThenBy(x => x.Shift.Id).ToArray();
                foreach (var item in resources) await EnsureSlotAsync(item.Day, item.Shift, ct);
                var slotIds = await db.KhungGioDatLichSet.Where(x => (x.Ngay == source.Ngay && x.CaLamViecId == sourceShift.Id) ||
                    (x.Ngay == request.Ngay && x.CaLamViecId == destination.Id)).Select(x => x.Id).ToArrayAsync(ct);
                await LockSlotsAsync(slotIds, ct);
                await LockRegistrationsAsync(source.NhanVienId, [source.Ngay, request.Ngay], ct);
                if (await FindActorAsync(actorId, "SCHEDULE_APPROVE", true, ct) is null) return Forbidden<ShiftRegistrationDto>();
                destination = await db.CaLamViecSet.AsNoTracking().SingleAsync(x => x.Id == request.MaCa, ct);
                sourceShift = await db.CaLamViecSet.AsNoTracking().SingleAsync(x => x.Id == source.CaLamViecId, ct);
                if (!destination.IsActive || !await EmployeeActiveAsync(source.NhanVienId, ct) || !await HolidayAllowedAsync(source.NhanVienId, request.Ngay, ct))
                    return Invalid<ShiftRegistrationDto>("Nhân viên hoặc ca đích không còn đủ điều kiện điều chuyển.");
                if (await ConflictsAsync(source.NhanVienId, request.Ngay, destination, source.Id, ct))
                    return Invalid<ShiftRegistrationDto>("Nhân viên đã có đăng ký hoặc ca chồng giờ tại đích.");
                var now = clock.GetUtcNow();
                var local = TimeZoneInfo.ConvertTime(now, CareOperation.ClinicTimeZone);
                var today = DateOnly.FromDateTime(local.DateTime);
                var time = TimeOnly.FromDateTime(local.DateTime);
                var currentOrFuture = source.Ngay > today || (source.Ngay == today && sourceShift.GioKetThuc > time);
                var assigned = await db.ChiTietDonSet.AsNoTracking().AnyAsync(p => p.NhanVienId == source.NhanVienId &&
                    p.TrangThaiChamSoc != PetCareState.DONE && p.TrangThaiChamSoc != PetCareState.CANCELLED &&
                    ((p.DonDatLich.KhungGio.Ngay == source.Ngay && p.DonDatLich.KhungGio.CaLamViecId == sourceShift.Id) ||
                     p.DichVuTrongDonChiTietDonItems.Any(line => line.TrangThai != OrderServiceState.HUY &&
                         line.KhungGio.Ngay == source.Ngay && line.KhungGio.CaLamViecId == sourceShift.Id) ||
                     (currentOrFuture && p.LoaiPhongId != null && p.CheckInAt != null && p.TraLaiAt == null)), ct);
                if (assigned) return Invalid<ShiftRegistrationDto>("Không thể điều chuyển ca khi nhân viên còn thú cưng đang phụ trách.");
                var slot = await db.KhungGioDatLichSet.AsNoTracking().SingleAsync(x => x.Ngay == source.Ngay && x.CaLamViecId == sourceShift.Id, ct);
                var remaining = await db.DangKyCaSet.AsNoTracking().Where(x => x.Ngay == source.Ngay && x.CaLamViecId == sourceShift.Id &&
                    x.Id != source.Id && x.TrangThai == ShiftRegistrationState.APPROVED && x.NhanVien.IsActive &&
                    x.NhanVien.NhanVienQuyenNhanVienItems.Any(g => g.MaQuyen == "CARE_UPDATE"))
                    .Select(x => x.NhanVienId).Distinct().CountAsync(ct);
                var minutes = (long)(sourceShift.GioKetThuc - sourceShift.GioBatDau).TotalMinutes;
                if ((slot.SlotDaDat > 0 && remaining == 0) || Math.Min(slot.TranPhut, remaining * minutes) < slot.PhutDaGiu)
                    return new ScheduleResult<ShiftRegistrationDto>(409, ErrorCode: "CAPACITY_EXCEEDED", Message: "Điều chuyển làm giảm công suất dưới mức đã cam kết.");
                var row = await db.DangKyCaSet.SingleAsync(x => x.Id == registrationId, ct);
                previous = Project(row);
                row.Ngay = request.Ngay; row.CaLamViecId = request.MaCa;
                row.LyDoDieuChuyen = request.LyDo.Trim(); row.UpdatedAt = now;
                return new ScheduleResult<ShiftRegistrationDto>(200, Project(row), "Đã điều chuyển ca làm việc.");
            }, ct, request.LyDo.Trim(), () => previous);
    }

    public Task<ScheduleResult<ShiftRegistrationDto>> DecideAsync(int actorId, int registrationId,
        ShiftDecisionRequest request, string traceId, CancellationToken ct)
    {
        ShiftRegistrationDto? previous = null;
        return ExecuteMutationAsync(actorId, request.RequestId, "SHIFT_DECISION",
            new { registrationId, quyetDinh = request.QuyetDinh, lyDo = request.LyDo?.Trim() }, traceId, async _ =>
            {
                if (request.LyDo is not null && string.IsNullOrWhiteSpace(request.LyDo))
                    return Validation<ShiftRegistrationDto>("lyDo", "Lý do không được chỉ chứa khoảng trắng.");
                var source = await db.DangKyCaSet.AsNoTracking().SingleOrDefaultAsync(x => x.Id == registrationId, ct);
                if (source is null) return Missing<ShiftRegistrationDto>();
                await EmployeeScheduleLock.AcquireAsync(db, source.NhanVienId, ct);
                if (await FindActorAsync(actorId, "SCHEDULE_APPROVE", true, ct) is null) return Forbidden<ShiftRegistrationDto>();
                source = await db.DangKyCaSet.AsNoTracking().SingleAsync(x => x.Id == registrationId, ct);
                if (source.TrangThai != ShiftRegistrationState.PENDING) return Invalid<ShiftRegistrationDto>("Chỉ quyết định đăng ký đang chờ duyệt.");
                var shift = await db.CaLamViecSet.AsNoTracking().SingleAsync(x => x.Id == source.CaLamViecId, ct);
                var approve = request.QuyetDinh == "APPROVE";
                if (approve) await EnsureSlotAsync(source.Ngay, shift, ct);
                var slotIds = await db.KhungGioDatLichSet.Where(x => x.Ngay == source.Ngay && x.CaLamViecId == shift.Id).Select(x => x.Id).ToArrayAsync(ct);
                await LockSlotsAsync(slotIds, ct);
                await LockRegistrationsAsync(source.NhanVienId, [source.Ngay], ct);
                if (await FindActorAsync(actorId, "SCHEDULE_APPROVE", true, ct) is null) return Forbidden<ShiftRegistrationDto>();
                var row = await db.DangKyCaSet.SingleAsync(x => x.Id == registrationId, ct);
                if (row.TrangThai != ShiftRegistrationState.PENDING) return Invalid<ShiftRegistrationDto>("Chỉ quyết định đăng ký đang chờ duyệt.");
                shift = await db.CaLamViecSet.AsNoTracking().SingleAsync(x => x.Id == row.CaLamViecId, ct);
                if (approve && (!shift.IsActive || !await EmployeeActiveAsync(row.NhanVienId, ct) ||
                    !await HolidayAllowedAsync(row.NhanVienId, row.Ngay, ct) || await ConflictsAsync(row.NhanVienId, row.Ngay, shift, row.Id, ct)))
                    return Invalid<ShiftRegistrationDto>("Nhân viên hoặc ca không còn đủ điều kiện phê duyệt.");
                previous = Project(row);
                row.TrangThai = approve ? ShiftRegistrationState.APPROVED : ShiftRegistrationState.REJECTED;
                row.DuyetBoiId = actorId;
                row.DuyetAt = row.UpdatedAt = clock.GetUtcNow();
                return new ScheduleResult<ShiftRegistrationDto>(200, Project(row), "Đã quyết định đăng ký ca.");
            }, ct, request.LyDo?.Trim(), () => previous);
    }

    private async Task EnsureSlotAsync(DateOnly day, CaLamViec shift, CancellationToken ct)
    {
        var open = !await db.NgayNghiSet.AnyAsync(x => x.Ngay == day, ct);
        var now = clock.GetUtcNow();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO khung_gio_dat_lich (ngay, ca_lam_viec_id, mo_nhan_khach, tong_slot, tran_phut, slot_da_dat, phut_da_giu, created_at, updated_at)
            VALUES ({day}, {shift.Id}, {open}, {shift.SucChuaPet}, {shift.TranPhut}, 0, 0, {now}, {now})
            ON CONFLICT (ngay, ca_lam_viec_id) DO NOTHING
            """, ct);
    }

    private Task LockSlotsAsync(int[] ids, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT id FROM khung_gio_dat_lich WHERE id = ANY({ids}) ORDER BY id FOR UPDATE", ct);

    private Task LockRegistrationsAsync(int employeeId, DateOnly[] days, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT id FROM dang_ky_ca WHERE nhan_vien_id = {employeeId} AND ngay = ANY({days}) ORDER BY id FOR UPDATE", ct);

    private Task<bool> EmployeeActiveAsync(int employeeId, CancellationToken ct) =>
        db.NhanVienSet.AsNoTracking().AnyAsync(x => x.Id == employeeId && x.IsActive, ct);

    public Task<ScheduleResult<ShiftRegistrationDto>> RegisterAsync(int actorId, ShiftRegisterRequest request,
        string traceId, CancellationToken ct) => ExecuteMutationAsync(actorId, request.RequestId, "SHIFT_REGISTER",
        new { ngay = request.Ngay, maCa = request.MaCa }, traceId, async _ =>
        {
            await EmployeeScheduleLock.AcquireAsync(db, actorId, ct);
            if (await FindActorAsync(actorId, "SCHEDULE_REGISTER", false, ct) is null) return Forbidden<ShiftRegistrationDto>();
            var shift = await db.CaLamViecSet.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.MaCa, ct);
            if (shift is null) return Missing<ShiftRegistrationDto>();
            if (!shift.IsActive || request.Ngay == default || !await HolidayAllowedAsync(actorId, request.Ngay, ct))
                return Invalid<ShiftRegistrationDto>("Ca không hoạt động hoặc nhân viên không được đăng ký trực ngày nghỉ.");
            var now = clock.GetUtcNow();
            var window = await db.DotDangKyCaSet.AsNoTracking().Where(x => x.TuNgay <= request.Ngay && x.DenNgay >= request.Ngay &&
                x.MoAt <= now && now <= x.DongAt).OrderByDescending(x => x.MoAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
            if (window is null) return Invalid<ShiftRegistrationDto>("Không có đợt đăng ký đang mở cho ngày đã chọn.");
            if (await ConflictsAsync(actorId, request.Ngay, shift, null, ct))
                return Invalid<ShiftRegistrationDto>("Nhân viên đã đăng ký hoặc có ca chồng giờ.");
            var registration = new DangKyCa
            {
                Ngay = request.Ngay,
                CaLamViecId = shift.Id,
                DotId = window.Id,
                NhanVienId = actorId,
                TrangThai = ShiftRegistrationState.PENDING,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.DangKyCaSet.Add(registration);
            await db.SaveChangesAsync(ct);
            return new ScheduleResult<ShiftRegistrationDto>(201, Project(registration), "Đã đăng ký ca làm việc.");
        }, ct);

    private async Task<bool> HolidayAllowedAsync(int employeeId, DateOnly day, CancellationToken ct) =>
        !await db.NgayNghiSet.AsNoTracking().AnyAsync(x => x.Ngay == day, ct) ||
        await db.NhanVienQuyenSet.AsNoTracking().AnyAsync(x => x.NhanVienId == employeeId && x.MaQuyen == "CARE_UPDATE", ct);

    private Task<bool> ConflictsAsync(int employeeId, DateOnly day, CaLamViec shift, int? exceptId, CancellationToken ct) =>
        db.DangKyCaSet.AsNoTracking().AnyAsync(x => x.NhanVienId == employeeId && x.Ngay == day &&
            (!exceptId.HasValue || x.Id != exceptId) &&
            (x.CaLamViecId == shift.Id || ((x.TrangThai == ShiftRegistrationState.PENDING || x.TrangThai == ShiftRegistrationState.APPROVED) &&
                x.CaLamViec.GioBatDau < shift.GioKetThuc && shift.GioBatDau < x.CaLamViec.GioKetThuc)), ct);

    private static ShiftRegistrationDto Project(DangKyCa row) => new(row.Id, row.NhanVienId, row.Ngay, row.CaLamViecId,
        row.TrangThai.ToString(), row.LyDoDieuChuyen);

    public Task<ScheduleResult<ShiftWindowDto>> CreateWindowAsync(int actorId, ShiftWindowWriteRequest request,
        string traceId, CancellationToken ct)
    {
        var opensAt = NormalizePgInstant(request.MoAt);
        var closesAt = NormalizePgInstant(request.DongAt);
        return ExecuteMutationAsync(actorId, request.RequestId, "SHIFT_WINDOW_CREATE",
        new { tuNgay = request.TuNgay, denNgay = request.DenNgay, moAt = opensAt, dongAt = closesAt },
        traceId, async _ =>
        {
            if (opensAt >= closesAt)
                return Validation<ShiftWindowDto>("dongAt", "Thời gian đóng phải sau thời gian mở ở độ chính xác microsecond.");
            var now = clock.GetUtcNow();
            var window = new DotDangKyCa
            {
                TuNgay = request.TuNgay,
                DenNgay = request.DenNgay,
                MoAt = opensAt,
                DongAt = closesAt,
                TaoBoiId = actorId,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.DotDangKyCaSet.Add(window);
            await db.SaveChangesAsync(ct);
            return new ScheduleResult<ShiftWindowDto>(201,
                new(window.Id, window.TuNgay, window.DenNgay, window.MoAt, window.DongAt), "Đã mở đợt đăng ký ca.");
        }, ct);
    }

    private static DateTimeOffset NormalizePgInstant(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        // Match Npgsql's integer microseconds relative to PostgreSQL's epoch,
        // including truncation toward the epoch for instants before 2000.
        return utc.AddTicks(-((utc.Ticks - PgEpochTicks) % TimeSpan.TicksPerMicrosecond));
    }

    public async Task<ScheduleResult<IReadOnlyList<ShiftRegistrationDto>>> ListAsync(
        int actorId, ScheduleQuery query, CancellationToken ct)
    {
        try
        {
            var actor = await FindActorAsync(actorId, "SCHEDULE_REGISTER", false, ct);
            if (actor is null || (actor.VaiTro != Role.ADMIN && query.MaNhanVien.HasValue))
                return new(403, ErrorCode: "FORBIDDEN", Message: "Bạn không có quyền xem lịch này.");
            if (query.TuNgay == default || query.DenNgay == default || query.TuNgay > query.DenNgay ||
                query.DenNgay.DayNumber - query.TuNgay.DayNumber >= 90)
                return new(400, ErrorCode: "INVALID_DATE_RANGE", Message: "Khoảng ngày phải hợp lệ và không quá 90 ngày.");

            var employeeId = actor.VaiTro == Role.ADMIN ? query.MaNhanVien : actor.Id;
            var rows = await db.DangKyCaSet.AsNoTracking()
                .Where(x => x.Ngay >= query.TuNgay && x.Ngay <= query.DenNgay &&
                    (!employeeId.HasValue || x.NhanVienId == employeeId))
                .OrderBy(x => x.Ngay).ThenBy(x => x.CaLamViec.GioBatDau).ThenBy(x => x.Id)
                .Select(x => new ShiftRegistrationDto(x.Id, x.NhanVienId, x.Ngay, x.CaLamViecId,
                    x.TrangThai.ToString(), x.LyDoDieuChuyen)).ToListAsync(ct);
            return new(200, rows, "Lấy lịch làm việc thành công.");
        }
        catch (Exception error) when (IsTransient(error))
        {
            return TemporaryFailure<IReadOnlyList<ShiftRegistrationDto>>();
        }
    }

    private Task<NhanVien?> FindActorAsync(int id, string permission, bool adminOnly, CancellationToken ct) =>
        db.NhanVienSet.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.IsActive &&
            (!adminOnly || x.VaiTro == Role.ADMIN) &&
            x.NhanVienQuyenNhanVienItems.Any(g => g.MaQuyen == permission), ct);

    private async Task<ScheduleResult<T>> ExecuteMutationAsync<T>(int actorId, Guid requestId, string operation,
        object payload, string traceId, Func<NhanVien, Task<ScheduleResult<T>>> perform, CancellationToken ct,
        string? reason = null, Func<object?>? before = null) where T : class
    {
        var permission = operation == "SHIFT_REGISTER" ? "SCHEDULE_REGISTER" : "SCHEDULE_APPROVE";
        var adminOnly = operation != "SHIFT_REGISTER";
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload, Json)));
        try
        {
            var actor = await FindActorAsync(actorId, permission, adminOnly, ct);
            if (actor is null) return Forbidden<T>();
            var replay = await FindReplayAsync<T>(actorId, requestId, operation, fingerprint, ct);
            if (replay is not null) return replay;
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var key = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"shift-scheduling:{actorId}:{operation}:{requestId:N}")));
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
            actor = await FindActorAsync(actorId, permission, adminOnly, ct);
            if (actor is null) return Forbidden<T>();
            replay = await FindReplayAsync<T>(actorId, requestId, operation, fingerprint, ct);
            if (replay is not null) return replay;
            var result = await perform(actor);
            if (result.StatusCode is < 200 or >= 300) return result;
            var targetId = result.Data switch { ShiftWindowDto w => w.MaDot, ShiftRegistrationDto r => r.MaDangKy, _ => throw new InvalidOperationException("Unknown scheduling result.") };
            db.AuditLogSet.Add(new AuditLog
            {
                ActorId = actorId,
                ActorLoai = actor.VaiTro.ToString(),
                HanhDong = operation,
                LoaiDoiTuong = operation == "SHIFT_WINDOW_CREATE" ? "DotDangKyCa" : "DangKyCa",
                DoiTuongId = targetId,
                RequestId = requestId,
                Fingerprint = fingerprint,
                TraceId = traceId[..Math.Min(traceId.Length, 40)],
                LyDo = reason,
                Truoc = before?.Invoke() is { } previous ? JsonSerializer.Serialize(previous, Json) : null,
                Sau = JsonSerializer.Serialize(result.Data, Json),
                KetQua = JsonSerializer.Serialize(result.Data, Json),
                CreatedAt = clock.GetUtcNow()
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(CancellationToken.None);
            return result;
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ix_audit_log_actor_id_hanh_dong_request_id" })
        {
            db.ChangeTracker.Clear();
            if (await FindActorAsync(actorId, permission, adminOnly, ct) is null) return Forbidden<T>();
            return await FindReplayAsync<T>(actorId, requestId, operation, fingerprint, ct)
                ?? throw new InvalidOperationException("Committed scheduling request could not be read.", error);
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ix_dang_ky_ca_nhan_vien_id_ngay_ca_lam_viec_id" })
        {
            db.ChangeTracker.Clear();
            return Invalid<T>("Nhân viên đã đăng ký ca này.");
        }
        catch (Exception error) when (IsTransient(error))
        {
            return TemporaryFailure<T>();
        }
    }

    private async Task<ScheduleResult<T>?> FindReplayAsync<T>(int actorId, Guid requestId, string operation,
        string fingerprint, CancellationToken ct) where T : class
    {
        var audit = await db.AuditLogSet.AsNoTracking().SingleOrDefaultAsync(x => x.ActorId == actorId &&
            x.HanhDong == operation && x.RequestId == requestId, ct);
        if (audit is null) return null;
        if (audit.Fingerprint == fingerprint && audit.KetQua is not null && JsonSerializer.Deserialize<T>(audit.KetQua, Json) is { } data)
            return new(200, data, "Yêu cầu đã được thực hiện.");
        return new(409, ErrorCode: "IDEMPOTENCY_CONFLICT", Message: "RequestId đã được dùng với dữ liệu khác.");
    }

    private static ScheduleResult<T> Forbidden<T>() => new(403, ErrorCode: "FORBIDDEN", Message: "Bạn không có quyền thực hiện thao tác này.");
    private static ScheduleResult<T> Invalid<T>(string message) => new(409, ErrorCode: "INVALID_TRANSITION", Message: message);
    private static ScheduleResult<T> Missing<T>() => new(404, ErrorCode: "NOT_FOUND", Message: "Không tìm thấy ca hoặc đăng ký ca.");
    private static ScheduleResult<T> Validation<T>(string field, string message) => new(400, ErrorCode: "VALIDATION_FAILED",
        Message: "Dữ liệu không hợp lệ.", Errors: [new() { Field = field, Message = message }]);
    private static bool IsTransient(Exception error) => error is NpgsqlException { IsTransient: true } ||
        error.InnerException is NpgsqlException { IsTransient: true };
    private static ScheduleResult<T> TemporaryFailure<T>() => new(503, ErrorCode: "TEMPORARY_FAILURE",
        Message: "Dịch vụ lịch làm việc tạm thời không khả dụng. Vui lòng thử lại với cùng requestId.");
}
