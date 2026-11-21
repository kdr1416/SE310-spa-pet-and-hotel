using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PetSpa.Api.Data;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Booking;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Enums;

namespace PetSpa.Api.Services.Booking;

public sealed class BookingManagementService(
    PetSpaDbContext db, IEmailSender email, IConfiguration configuration, TimeProvider clock,
    ILogger<BookingManagementService> logger)
{
    private const string ConfirmOperation = "BOOKING_CONFIRM";
    private const string CancelOperation = "BOOKING_CANCEL";
    private const string Permission = "BOOKING_APPROVE";
    private const string NoShowAttestation = "Khách không liên hệ";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ApiResponse<PageBooking>> ListAsync(
        BookingQuery query, int actorId, CancellationToken cancellationToken)
    {
        var state = BookingState.PENDING;
        if (query.TrangThai is not null &&
            (!Enum.TryParse(query.TrangThai, out state) || state.ToString() != query.TrangThai))
            return ApiResponse<PageBooking>.Fail("Trạng thái lịch hẹn không hợp lệ.", "VALIDATION_FAILED");

        if (query.TuNgay is DateOnly from && query.DenNgay is DateOnly to &&
            (from > to || to.DayNumber - from.DayNumber >= 90))
            return ApiResponse<PageBooking>.Fail("Khoảng ngày phải hợp lệ và không vượt quá 90 ngày.", "INVALID_DATE_RANGE");

        try
        {
            if (await GetRoleAsync(actorId, cancellationToken) is null)
                return Forbidden<PageBooking>();

            var matches = db.DonDatLichSet.AsNoTracking().Where(booking => booking.TrangThai == state);
            if (query.TuNgay is DateOnly first)
                matches = matches.Where(booking => booking.KhungGio.Ngay >= first);
            if (query.DenNgay is DateOnly last)
                matches = matches.Where(booking => booking.KhungGio.Ngay <= last);

            var total = await matches.CountAsync(cancellationToken);
            var offset = (query.Trang - 1L) * query.KichThuocTrang;
            if (offset >= total)
                return ApiResponse<PageBooking>.Ok(new PageBooking([], query.Trang, query.KichThuocTrang, total));

            var page = await BookingProjection.IncludeDetails(matches)
                .OrderByDescending(booking => booking.CreatedAt).ThenByDescending(booking => booking.Id)
                .Skip((int)offset).Take(query.KichThuocTrang).ToListAsync(cancellationToken);
            var items = page.Select(booking => BookingProjection.ProjectCurrent(booking, configuration)).ToList();
            return ApiResponse<PageBooking>.Ok(new PageBooking(items, query.Trang, query.KichThuocTrang, total));
        }
        catch (NpgsqlException exception) when (exception.IsTransient)
        {
            return TemporaryFailure<PageBooking>();
        }
    }

    public async Task<ApiResponse<BookingResponse>> ConfirmAsync(
        int id, BookingCommand request, int actorId, string traceId, CancellationToken cancellationToken)
    {
        var (result, committed) = await ApplyAsync(id, actorId, traceId, request.RequestId,
            ConfirmOperation, null, null, cancellationToken);
        if (committed)
        {
            // Keep the original booking email status and avoid retrying SMTP on command replays.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            try
            {
                await email.SendBookingConfirmedAsync(result.Data!.LienHe.Email, result.Data.TrackingCode, timeout.Token);
            }
            catch (Exception exception)
            {
                logger.LogWarning("Approval email failed for booking {BookingId}. Failure type: {FailureType}.",
                    id, exception.GetType().Name);
            }
        }
        return result;
    }

    public async Task<ApiResponse<BookingResponse>> CancelAsync(
        int id, CancelBooking request, int actorId, string traceId, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<CancellationType>(request.LoaiHuy, out var type) || type.ToString() != request.LoaiHuy ||
            string.IsNullOrWhiteSpace(request.LyDo) || request.LyDo.Length > 1000)
            return ApiResponse<BookingResponse>.Fail("Loại hủy hoặc lý do hủy không hợp lệ.", "VALIDATION_FAILED");

        var (result, _) = await ApplyAsync(id, actorId, traceId, request.RequestId,
            CancelOperation, type, request.LyDo.Trim(), cancellationToken);
        return result;
    }

    private async Task<(ApiResponse<BookingResponse> Response, bool Committed)> ApplyAsync(
        int id, int actorId, string traceId, Guid requestId, string operation, CancellationType? cancellationType,
        string? reason, CancellationToken cancellationToken)
    {
        if (id <= 0 || requestId == Guid.Empty)
            return (ApiResponse<BookingResponse>.Fail("Mã lịch hẹn hoặc RequestId không hợp lệ.", "VALIDATION_FAILED"), false);

        try
        {
            if (await GetRoleAsync(actorId, cancellationToken) is null)
                return (Forbidden<BookingResponse>(), false);

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            // A request ID is unique for its actor and operation even when callers supply different bookings.
            var key = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(
                Encoding.UTF8.GetBytes($"booking-management:{actorId}:{operation}:{requestId:N}")));
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", cancellationToken);
            var exists = await BookingRowLock.AcquireAsync(db, id, cancellationToken);

            var role = await GetRoleAsync(actorId, cancellationToken);
            if (role is null)
                return (Forbidden<BookingResponse>(), false);

            var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
                new { id, cancellationType, reason }, JsonOptions)));
            var replay = await db.AuditLogSet.AsNoTracking().SingleOrDefaultAsync(
                audit => audit.ActorId == actorId && audit.HanhDong == operation && audit.RequestId == requestId,
                cancellationToken);
            if (replay is not null)
            {
                var response = replay.Fingerprint == fingerprint
                    ? ApiResponse<BookingResponse>.Ok(JsonSerializer.Deserialize<BookingResponse>(replay.KetQua!, JsonOptions)!)
                    : ApiResponse<BookingResponse>.Fail("RequestId đã được sử dụng với dữ liệu khác.", "IDEMPOTENCY_CONFLICT");
                return (response, false);
            }
            if (!exists)
                return (ApiResponse<BookingResponse>.Fail("Không tìm thấy lịch hẹn.", "NOT_FOUND"), false);

            var booking = await BookingProjection.IncludeDetails(db.DonDatLichSet)
                .Include(booking => booking.TienCocDonDatLich)
                .SingleAsync(booking => booking.Id == id, cancellationToken);
            var now = clock.GetUtcNow();
            var previousState = booking.TrangThai;

            if (cancellationType is CancellationType type)
            {
                var error = await ApplyCancellationAsync(booking, type, reason!, actorId, role.Value, now, cancellationToken);
                if (error is not null)
                    return (error, false);
            }
            else
            {
                if (booking.TrangThai != BookingState.PENDING)
                    return (InvalidTransition("Chỉ có thể duyệt lịch hẹn đang chờ duyệt."), false);
                if (booking.TienCocDonDatLich is { TrangThaiYeuCau: not DepositRequestState.DA_DAP_UNG })
                    return (InvalidTransition("Yêu cầu tiền cọc chưa được đáp ứng."), false);
                booking.TrangThai = BookingState.CONFIRMED;
                booking.DuyetBoiId = actorId;
                booking.DuyetAt = now;
            }

            booking.UpdatedAt = now;
            var result = WithoutQr(BookingProjection.ProjectCurrent(booking, configuration));
            db.AuditLogSet.Add(new AuditLog
            {
                ActorId = actorId,
                ActorLoai = role.Value.ToString(),
                HanhDong = operation,
                LoaiDoiTuong = "DonDatLich",
                DoiTuongId = booking.Id,
                LyDo = reason,
                RequestId = requestId,
                Fingerprint = fingerprint,
                TraceId = traceId[..Math.Min(40, traceId.Length)],
                Truoc = JsonSerializer.Serialize(new { trangThai = previousState.ToString() }, JsonOptions),
                Sau = JsonSerializer.Serialize(new { trangThai = booking.TrangThai.ToString(),
                    loaiHuy = cancellationType?.ToString() }, JsonOptions),
                KetQua = JsonSerializer.Serialize(result, JsonOptions),
                CreatedAt = now
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return (ApiResponse<BookingResponse>.Ok(result), true);
        }
        catch (NpgsqlException exception) when (exception.IsTransient)
        {
            return (TemporaryFailure<BookingResponse>(), false);
        }
        catch (DbUpdateException exception) when (exception.InnerException is NpgsqlException { IsTransient: true })
        {
            return (TemporaryFailure<BookingResponse>(), false);
        }
    }

    private async Task<ApiResponse<BookingResponse>?> ApplyCancellationAsync(
        DonDatLich booking, CancellationType type, string reason, int actorId, Role role, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var pets = booking.ChiTietDonDonDatLichItems;
        if (booking.TrangThai is not (BookingState.PENDING or BookingState.CONFIRMED) ||
            pets.Any(pet => pet.CheckInAt is not null || pet.CheckInBoiId is not null))
            return InvalidTransition("Chỉ có thể hủy lịch hẹn trước khi tiếp nhận thú cưng.");
        if (booking.TienCocDonDatLich is not null || pets.Any(pet => pet.LoaiPhongId is not null))
            return InvalidTransition("Lịch hẹn có yêu cầu tiền cọc hoặc đặt phòng cần được xử lý tại quầy.");

        switch (type)
        {
            case CancellationType.STAFF_REJECTED when booking.TrangThai != BookingState.PENDING:
                return InvalidTransition("Chỉ có thể từ chối lịch hẹn đang chờ duyệt.");
            case CancellationType.NO_SHOW when now < booking.MocCuoiCa.AddMinutes(30):
                return InvalidTransition("Chỉ có thể đánh dấu vắng mặt sau khi ca kết thúc 30 phút.");
            case CancellationType.NO_SHOW when !reason.Equals(NoShowAttestation, StringComparison.OrdinalIgnoreCase) &&
                !(reason.Length > NoShowAttestation.Length + 1 &&
                    reason.StartsWith(NoShowAttestation + ":", StringComparison.OrdinalIgnoreCase)):
                return InvalidTransition("Lý do vắng mặt phải xác nhận “Khách không liên hệ”.");
            case CancellationType.STAFF_CANCELLED or CancellationType.CUSTOMER_CANCELLED
                when booking.BatDauDuKien - now < TimeSpan.FromHours(2) && role != Role.ADMIN:
                return ApiResponse<BookingResponse>.Fail("Chỉ quản trị viên được hủy trong vòng 2 giờ trước ca.", "FORBIDDEN");
        }

        var allocations = pets.SelectMany(pet => pet.DichVuTrongDonChiTietDonItems)
            .Where(service => service.TrangThai != OrderServiceState.HUY)
            .GroupBy(service => service.KhungGioId)
            .ToDictionary(group => group.Key, group =>
                (Seats: group.Select(service => service.ChiTietDonId).Distinct().Count(),
                    Minutes: group.Sum(service => service.ThoiLuongSnapshot)));
        if (allocations.Count > 0)
        {
            var slotIds = allocations.Keys.ToArray();
            var slots = await db.KhungGioDatLichSet.FromSqlInterpolated(
                    $"SELECT * FROM khung_gio_dat_lich WHERE id = ANY({slotIds}) ORDER BY id FOR UPDATE")
                .ToListAsync(cancellationToken);

            // EF can reuse the intake slot loaded with the booking before the locks were acquired.
            if (allocations.ContainsKey(booking.KhungGioId))
                await db.Entry(booking.KhungGio).ReloadAsync(cancellationToken);

            if (slots.Count != allocations.Count || slots.Any(slot =>
                    slot.SlotDaDat < allocations[slot.Id].Seats || slot.PhutDaGiu < allocations[slot.Id].Minutes))
                return InvalidTransition("Không thể đối chiếu công suất đã giữ cho lịch hẹn.");

            foreach (var slot in slots)
            {
                var allocation = allocations[slot.Id];
                slot.SlotDaDat -= allocation.Seats;
                slot.PhutDaGiu -= allocation.Minutes;
                slot.UpdatedAt = now;
            }
        }

        booking.TrangThai = BookingState.CANCELLED;
        booking.LoaiHuy = type;
        booking.LyDoHuy = reason;
        booking.HuyBoiId = actorId;
        booking.HuyAt = now;
        return null;
    }

    private Task<Role?> GetRoleAsync(int actorId, CancellationToken cancellationToken) =>
        db.NhanVienSet.AsNoTracking().Where(employee => employee.Id == actorId && employee.IsActive &&
                employee.NhanVienQuyenNhanVienItems.Any(permission => permission.MaQuyen == Permission))
            .Select(employee => (Role?)employee.VaiTro).SingleOrDefaultAsync(cancellationToken);

    private static BookingResponse WithoutQr(BookingResponse response) =>
        response.HoaDon is null ? response : response with { HoaDon = response.HoaDon with { Qr = null } };

    private static ApiResponse<BookingResponse> InvalidTransition(string message) =>
        ApiResponse<BookingResponse>.Fail(message, "INVALID_TRANSITION");

    private static ApiResponse<T> Forbidden<T>() =>
        ApiResponse<T>.Fail("Nhân viên không có quyền duyệt lịch hẹn.", "FORBIDDEN");

    private static ApiResponse<T> TemporaryFailure<T>() =>
        ApiResponse<T>.Fail("Chưa thể xử lý lịch hẹn. Vui lòng thử lại.", "TEMPORARY_FAILURE");
}
