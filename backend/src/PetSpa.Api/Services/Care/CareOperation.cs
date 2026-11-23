using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PetSpa.Api.Data;
using PetSpa.Api.Services.Booking;
using PetSpa.Api.Services.Storage;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Care;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Care;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Enums;

namespace PetSpa.Api.Services.Care;

internal sealed record CareCommand(
    int PetId, int ActorId, Guid RequestId, string Permission, string Operation, string TraceId,
    string Fingerprint, string? LegacyFingerprint = null);

internal sealed record CareResult<T>(
    int StatusCode, T? Data = null, string Message = "", string? ErrorCode = null,
    List<ApiErrorItem>? Errors = null, int? AssigneeId = null, string? OverrideReason = null, string? Action = null)
    where T : class
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;

    public static CareResult<T> NotFound() =>
        new(404, Message: "Không tìm thấy thú cưng trong đơn đặt lịch.", ErrorCode: "NOT_FOUND");

    public static CareResult<T> Forbidden() =>
        new(403, Message: "Bạn không có quyền thực hiện thao tác cho thú cưng này.", ErrorCode: "FORBIDDEN");

    public static CareResult<T> InvalidTransition(string message) =>
        new(409, Message: message, ErrorCode: "INVALID_TRANSITION");
}

public sealed class CareOperation(
    PetSpaDbContext db, IFileStorageService storage, TimeProvider clock, ILogger<CareOperation> logger)
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string TargetType = "ChiTietDon";
    internal const long MaxPhotoBytes = 5 * 1024 * 1024;
    internal static readonly TimeZoneInfo ClinicTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

    internal static DateOnly HcmDate(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, ClinicTimeZone).DateTime);

    internal async Task<CareResult<T>> ExecuteAsync<T>(
        CareCommand command,
        string responseMessage,
        byte[]? photo,
        Func<NhanVien, DateTimeOffset, string?, bool, CancellationToken, Task<CareResult<T>>> perform,
        CancellationToken cancellationToken) where T : class
    {
        var actor = await FindActorAsync(command, cancellationToken);
        if (actor is null)
            return CareResult<T>.Forbidden();

        var replay = await FindReplayAsync<T>(command, responseMessage, cancellationToken);
        if (replay is not null)
            return await SignMediaAsync(command, replay, cancellationToken);

        if (photo is not null)
        {
            var preflight = await perform(actor, clock.GetUtcNow(), null, true, cancellationToken);
            if (!preflight.IsSuccess)
            {
                // A concurrent request may have committed after the first replay lookup.
                replay = await FindReplayAsync<T>(command, responseMessage, cancellationToken);
                return replay is null ? preflight : await SignMediaAsync(command, replay, cancellationToken);
            }

            // The post-upload command needs a fresh view of all mutable rows.
            db.ChangeTracker.Clear();
        }

        string? objectPath = null;
        var retainUpload = false;
        CareResult<T> result;
        try
        {
            if (photo is not null)
                objectPath = await storage.UploadPhotoAsync(photo, cancellationToken);

            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(cancellationToken)
                : null;

            var bookingId = await db.ChiTietDonSet.AsNoTracking()
                .Where(pet => pet.Id == command.PetId)
                .Select(pet => (int?)pet.DonDatLichId)
                .SingleOrDefaultAsync(cancellationToken);
            if (bookingId is null || (transaction is not null &&
                !await BookingRowLock.AcquireAsync(db, bookingId.Value, cancellationToken)))
                return CareResult<T>.NotFound();

            actor = await FindActorAsync(command, cancellationToken);
            if (actor is null)
                return CareResult<T>.Forbidden();

            replay = await FindReplayAsync<T>(command, responseMessage, cancellationToken);
            if (replay is not null)
            {
                result = replay;
            }
            else
            {
                var now = clock.GetUtcNow();
                result = await perform(actor, now, objectPath, false, cancellationToken);
                if (!result.IsSuccess)
                    return result;

                db.AuditLogSet.Add(new AuditLog
                {
                    ActorLoai = actor.VaiTro.ToString(),
                    ActorId = actor.Id,
                    HanhDong = command.Operation,
                    LoaiDoiTuong = TargetType,
                    DoiTuongId = command.PetId,
                    TraceId = command.TraceId,
                    RequestId = command.RequestId,
                    Fingerprint = command.Fingerprint,
                    LyDo = result.OverrideReason,
                    Sau = JsonSerializer.Serialize(new { MaNhanVienPhuTrach = result.AssigneeId, HanhDongChamSoc = result.Action }, JsonOptions),
                    KetQua = JsonSerializer.Serialize(result.Data, JsonOptions),
                    CreatedAt = now
                });
                await db.SaveChangesAsync(cancellationToken);
                // Once the commit is sent, a connection failure can leave its outcome unknown.
                // Retain the private upload because the database may already reference it.
                retainUpload = true;
                if (transaction is not null)
                    await transaction.CommitAsync(CancellationToken.None);
                result = result with { Message = responseMessage };
            }
        }
        catch (FileStorageException error)
        {
            return new CareResult<T>(503, Message: error.Message, ErrorCode: error.ErrorCode);
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ix_audit_log_actor_id_hanh_dong_request_id"
        })
        {
            // Commands for different bookings can collide on the same actor/operation/request key.
            // The failed transaction has already been disposed before inspecting the winner.
            db.ChangeTracker.Clear();
            if (await FindActorAsync(command, cancellationToken) is null)
                return CareResult<T>.Forbidden();
            result = await FindReplayAsync<T>(command, responseMessage, cancellationToken)
                ?? throw new InvalidOperationException("The winning care request could not be read.", error);
        }
        finally
        {
            if (!retainUpload && objectPath is not null)
                await DeleteOrphanAsync(objectPath);
        }

        return await SignMediaAsync(command, result, cancellationToken);
    }

    internal async Task<NhanVien?> FindEmployeeWithGrantAsync(int employeeId, string permission, CancellationToken cancellationToken) =>
        await db.NhanVienSet.AsNoTracking().SingleOrDefaultAsync(e => e.Id == employeeId && e.IsActive &&
            e.NhanVienQuyenNhanVienItems.Any(grant => grant.MaQuyen == permission), cancellationToken);

    internal async Task<bool> HasCurrentShiftAsync(int employeeId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var local = TimeZoneInfo.ConvertTime(now, ClinicTimeZone);
        var date = DateOnly.FromDateTime(local.DateTime);
        var time = TimeOnly.FromDateTime(local.DateTime);

        return await db.DangKyCaSet.AsNoTracking().AnyAsync(shift =>
            shift.NhanVienId == employeeId && shift.Ngay == date &&
            shift.TrangThai == ShiftRegistrationState.APPROVED &&
            shift.CaLamViec.IsActive && shift.CaLamViec.GioBatDau <= time &&
            time < shift.CaLamViec.GioKetThuc, cancellationToken);
    }

    internal static CareResult<T>? ValidatePhoto<T>(byte[]? photo, string field) where T : class
    {
        if (photo is null)
            return null;
        if (photo.Length > MaxPhotoBytes)
            return new(413, Message: "Ảnh vượt quá dung lượng tối đa 5 MiB.", ErrorCode: "FILE_TOO_LARGE",
                Errors: [new ApiErrorItem { Field = field, Message = "Kích thước ảnh tối đa 5 MiB." }]);

        ReadOnlySpan<byte> header = photo;
        if (header.StartsWith(new byte[] { 0xff, 0xd8, 0xff }) ||
            header.StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }) ||
            (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8)))
            return null;

        return new(415, Message: "Định dạng ảnh không được hỗ trợ.", ErrorCode: "UNSUPPORTED_FILE",
            Errors: [new ApiErrorItem { Field = field, Message = "Chỉ chấp nhận JPEG, PNG hoặc WebP." }]);
    }

    internal static string Fingerprint(object request) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));

    internal static string? PhotoFingerprint(byte[]? photo) =>
        photo is null ? null : Convert.ToHexString(SHA256.HashData(photo));

    private Task<NhanVien?> FindActorAsync(CareCommand command, CancellationToken cancellationToken) =>
        FindEmployeeWithGrantAsync(command.ActorId, command.Permission, cancellationToken);

    private async Task<CareResult<T>?> FindReplayAsync<T>(CareCommand command, string message, CancellationToken cancellationToken)
        where T : class
    {
        var prior = await db.AuditLogSet.AsNoTracking().SingleOrDefaultAsync(audit =>
            audit.ActorId == command.ActorId && audit.HanhDong == command.Operation &&
            audit.RequestId == command.RequestId, cancellationToken);
        if (prior is null)
            return null;

        if (prior.LoaiDoiTuong == TargetType && prior.DoiTuongId == command.PetId &&
            (prior.Fingerprint == command.Fingerprint ||
                (command.LegacyFingerprint is not null && prior.Fingerprint == command.LegacyFingerprint)) &&
            prior.KetQua is not null && JsonSerializer.Deserialize<T>(prior.KetQua, JsonOptions) is { } data)
            return new CareResult<T>(200, data, message);

        return new CareResult<T>(409, Message: "RequestId đã được sử dụng với dữ liệu khác.", ErrorCode: "IDEMPOTENCY_CONFLICT");
    }

    private async Task<CareResult<T>> SignMediaAsync<T>(CareCommand command, CareResult<T> result, CancellationToken cancellationToken)
        where T : class
    {
        IReadOnlyList<CareLog> logs = result.Data switch
        {
            BookingPet pet => pet.NhatKy,
            CareLog log => [log],
            _ => []
        };
        if (logs.Count == 0)
            return result;

        foreach (var log in logs)
            log.AnhUrl = null;

        if (!await CanReadMediaAsync(command, cancellationToken))
            return result;

        var ids = logs.Select(log => log.MaNhatKy).ToList();
        var paths = await db.NhatKyChamSocSet.AsNoTracking()
            .Where(log => log.ChiTietDonId == command.PetId && ids.Contains(log.Id) && log.ObjectPath != null)
            .ToDictionaryAsync(log => log.Id, log => log.ObjectPath!, cancellationToken);

        var urls = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var log in logs)
        {
            // The adapter stores keys at the bucket root. Historical local paths remain private.
            if (!paths.TryGetValue(log.MaNhatKy, out var path) || string.IsNullOrWhiteSpace(path) ||
                path is "." or ".." || path.Contains('/') || path.Contains('\\') || path.Contains(':'))
                continue;

            if (!urls.TryGetValue(path, out var url))
            {
                try
                {
                    url = (await storage.CreateReadUrlAsync(path, cancellationToken)).AbsoluteUri;
                }
                catch (FileStorageException)
                {
                    logger.LogWarning("Could not sign care photo for pet {PetId}, log {LogId}.", command.PetId, log.MaNhatKy);
                }

                urls.Add(path, url);
            }
            log.AnhUrl = url;
        }
        return result;
    }

    private async Task<bool> CanReadMediaAsync(CareCommand command, CancellationToken cancellationToken)
    {
        var actor = await FindActorAsync(command, cancellationToken);
        if (actor is null)
            return false;
        if (actor.VaiTro == Role.ADMIN || command.Permission is not ("CARE_UPDATE" or "PHOTO_UPLOAD"))
            return true;

        return await db.ChiTietDonSet.AsNoTracking().AnyAsync(pet =>
            pet.Id == command.PetId && pet.NhanVienId == actor.Id, cancellationToken) &&
            await HasCurrentShiftAsync(actor.Id, clock.GetUtcNow(), cancellationToken);
    }

    private async Task DeleteOrphanAsync(string path)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await storage.DeleteAsync(path, timeout.Token);
        }
        catch (Exception error)
        {
            logger.LogWarning("Could not clean up a private care upload. Failure type: {FailureType}.", error.GetType().Name);
        }
    }
}

internal static class CareProjection
{
    public static IQueryable<ChiTietDon> IncludeDetails(IQueryable<ChiTietDon> pets) =>
        pets.Include(pet => pet.NhanVien)
            .Include(pet => pet.DichVuTrongDonChiTietDonItems)
                .ThenInclude(line => line.ThanhPhanDichVuTrongDonDichVuTrongDonItems)
                    .ThenInclude(component => component.DichVu)
            .Include(pet => pet.NhatKyChamSocChiTietDonItems)
                .ThenInclude(log => log.NhanVien);

    public static BookingPet Pet(ChiTietDon pet, NhanVien actor, string? assigneeName = null) => new()
    {
        MaChiTietDon = pet.Id,
        MaThuCung = pet.ThuCungId,
        TenThuCung = pet.TenPetSnapshot,
        TrangThai = pet.TrangThaiChamSoc.ToString(),
        CanNangChot = pet.CanNangChot,
        SizeThucTe = pet.SizeThucTe?.ToString(),
        SizeChot = pet.SizeChot?.ToString(),
        TinhTrangLong = ParseFurConditions(pet.TinhTrangLong),
        MaLoaiPhong = pet.LoaiPhongId,
        TenNhanVienPhuTrach = assigneeName ?? pet.NhanVien?.TenNhanVien,
        DichVu = pet.DichVuTrongDonChiTietDonItems.OrderBy(line => line.Id).Select(line => new OrderService
        {
            MaDichVuTrongDon = line.Id,
            MaDichVu = line.DichVuId,
            MaCombo = line.ComboId,
            MaKhungGio = line.KhungGioId,
            TenDichVu = line.TenSnapshot,
            TrangThai = line.TrangThai.ToString(),
            SizeChot = line.SizeChot?.ToString(),
            GiaChot = line.GiaChot,
            ThanhPhan = line.ThanhPhanDichVuTrongDonDichVuTrongDonItems.OrderBy(component => component.Id)
                .Select(component => new Component
                {
                    MaThanhPhan = component.Id,
                    MaDichVu = component.DichVuId,
                    TenDichVu = component.DichVu.TenDichVu,
                    TrangThai = component.TrangThai.ToString(),
                    HoanTatAt = component.HoanTatAt
                }).ToList()
        }).ToList(),
        NhatKy = pet.NhatKyChamSocChiTietDonItems.OrderBy(log => log.GhiNhanAt).ThenBy(log => log.Id)
            .Select(log => Log(log, actor)).ToList(),
        NhanSom = pet.NhanSom,
        BanGiaoAt = pet.BanGiaoAt,
        TraLaiAt = pet.TraLaiAt
    };

    public static CareLog Log(NhatKyChamSoc log, NhanVien actor) => new()
    {
        MaNhatKy = log.Id,
        LoaiNhatKy = log.LoaiNhatKy,
        GhiChu = log.GhiChu,
        CanNangTheoDoi = log.CanNangGhiNhan,
        TenTacGia = log.NhanVien?.TenNhanVien ?? (log.NhanVienId == actor.Id ? actor.TenNhanVien : string.Empty),
        GhiNhanAt = log.GhiNhanAt
    };

    private static List<string>? ParseFurConditions(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return null;
        try
        {
            return JsonSerializer.Deserialize<List<string>>(rawJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
