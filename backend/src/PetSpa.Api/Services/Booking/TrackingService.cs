using System.Buffers.Binary;
using System.Data;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PetSpa.Api.Data;
using PetSpa.Api.Services.Storage;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Booking;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Scheduling;
using PetSpa.Core.Enums;

namespace PetSpa.Api.Services.Booking;

public sealed class TrackingService(
    PetSpaDbContext db, IFileStorageService storage, IEmailSender email,
    IConfiguration configuration, TimeProvider clock)
{
    private const int AttemptLimit = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<(ApiResponse<TrackingResponse> Response, int? RetryAfterSeconds)> LookupAsync(
        TrackingPair pair, IPAddress? address, CancellationToken cancellationToken)
    {
        try
        {
            var (bookingId, retryAfter) = await VerifyPairAsync(pair, address, cancellationToken);
            if (retryAfter is not null)
                return (ApiResponse<TrackingResponse>.Fail("Tạm khóa tra cứu. Vui lòng thử lại sau.", "RATE_LIMITED"), retryAfter);
            if (bookingId is null)
                return (NotFound(), null);

            DonDatLich? booking;
            await using (var snapshot = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken)
                : null)
            {
                booking = await BookingProjection.IncludeDetails(db.DonDatLichSet.AsNoTracking())
                    .SingleOrDefaultAsync(booking => booking.Id == bookingId && booking.TrackingCode == pair.TrackingCode &&
                        booking.SoDienThoaiSnapshot == pair.SoDienThoai, cancellationToken);
                if (snapshot is not null)
                    await snapshot.CommitAsync(cancellationToken);
            }
            if (booking is null)
                return (NotFound(), null);

            var display = BookingProjection.ProjectCurrent(booking, configuration);
            var files = booking.ChiTietDonDonDatLichItems
                .SelectMany(pet => pet.NhatKyChamSocChiTietDonItems)
                .Where(log => log.ObjectPath is { } path && !string.IsNullOrWhiteSpace(path)
                    && !path.StartsWith("check-in/", StringComparison.Ordinal)
                    && !path.StartsWith("care-logs/", StringComparison.Ordinal))
                .ToDictionary(log => log.Id, log => log.ObjectPath!);
            var signedUrls = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var log in display.ThuCung.SelectMany(pet => pet.NhatKy))
            {
                if (!files.TryGetValue(log.MaNhatKy, out var path))
                    continue;
                if (!signedUrls.TryGetValue(path, out var url))
                {
                    url = (await storage.CreateReadUrlAsync(path, cancellationToken)).AbsoluteUri;
                    signedUrls.Add(path, url);
                }
                log.AnhUrl = url;
            }

            var now = clock.GetUtcNow();
            var actions = new List<string>();
            var canCancel = booking.TrangThai is (BookingState.PENDING or BookingState.CONFIRMED) &&
                            booking.BatDauDuKien - now >= TimeSpan.FromHours(2) &&
                            !booking.ChiTietDonDonDatLichItems.Any(pet => pet.CheckInAt.HasValue || pet.CheckInBoiId.HasValue);
            if (canCancel)
                actions.Add("CANCEL");

            var response = new TrackingResponse(display.MaDon, display.TrackingCode, display.TrangThai,
                display.BatDauDuKien, display.KetThucDuKien, display.TongTamTinh, display.ThuCung,
                display.HoaDon, display.TienCoc, actions);
            return (ApiResponse<TrackingResponse>.Ok(response), null);
        }
        catch (NpgsqlException exception) when (exception.IsTransient)
        {
            return (TemporaryFailure(), null);
        }
        catch (DbUpdateException exception) when (exception.InnerException is NpgsqlException { IsTransient: true })
        {
            return (TemporaryFailure(), null);
        }
    }

    public async Task<(ApiResponse<BookingResponse> Response, int? RetryAfterSeconds)> CancelAsync(
        TrackingCancel request, IPAddress? address, string traceId, CancellationToken cancellationToken)
    {
        if (request.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(request.LyDo) || request.LyDo.Length > 1000)
            return (ApiResponse<BookingResponse>.Fail("Dữ liệu hủy lịch không hợp lệ.", "VALIDATION_FAILED"), null);

        try
        {
            var (bookingId, retryAfter) = await VerifyPairAsync(
                new TrackingPair(request.SoDienThoai, request.TrackingCode), address, cancellationToken);
            if (retryAfter is not null)
                return (ApiResponse<BookingResponse>.Fail("Tạm khóa tra cứu. Vui lòng thử lại sau.", "RATE_LIMITED"), retryAfter);
            if (bookingId is null)
                return (ApiResponse<BookingResponse>.Fail("Không tìm thấy lịch hẹn với thông tin đã nhập.", "TRACKING_NOT_FOUND"), null);

            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(cancellationToken)
                : null;
            var lockKey = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(
                Encoding.UTF8.GetBytes($"tracking:cancel:{request.RequestId:N}")));
            if (db.Database.IsRelational())
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
            await BookingRowLock.AcquireAsync(db, bookingId.Value, cancellationToken);

            var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
                new { bookingId = bookingId.Value, request.TrackingCode, request.SoDienThoai, lyDo = request.LyDo.Trim() }, JsonOptions)));
            var replay = await db.AuditLogSet.AsNoTracking().SingleOrDefaultAsync(
                audit => audit.HanhDong == "TRACKING_CANCEL" && audit.RequestId == request.RequestId, cancellationToken);
            if (replay is not null)
            {
                var response = replay.Fingerprint == fingerprint
                    ? ApiResponse<BookingResponse>.Ok(JsonSerializer.Deserialize<BookingResponse>(replay.KetQua!, JsonOptions)!)
                    : ApiResponse<BookingResponse>.Fail("RequestId đã được sử dụng với dữ liệu khác.", "IDEMPOTENCY_CONFLICT");
                return (response, null);
            }

            var booking = await BookingProjection.IncludeDetails(db.DonDatLichSet)
                .SingleOrDefaultAsync(b => b.Id == bookingId.Value, cancellationToken);
            if (booking is null)
                return (ApiResponse<BookingResponse>.Fail("Không tìm thấy lịch hẹn với thông tin đã nhập.", "TRACKING_NOT_FOUND"), null);

            var now = clock.GetUtcNow();
            if (booking.TrangThai is not (BookingState.PENDING or BookingState.CONFIRMED))
                return (ApiResponse<BookingResponse>.Fail("Chỉ có thể hủy lịch hẹn đang chờ duyệt hoặc đã xác nhận.", "INVALID_TRANSITION"), null);

            var pets = booking.ChiTietDonDonDatLichItems;
            if (pets.Any(p => p.CheckInAt.HasValue || p.CheckInBoiId.HasValue))
                return (ApiResponse<BookingResponse>.Fail("Chỉ có thể hủy lịch hẹn trước khi tiếp nhận thú cưng.", "INVALID_TRANSITION"), null);

            if (booking.BatDauDuKien - now < TimeSpan.FromHours(2))
                return (ApiResponse<BookingResponse>.Fail("Khách hàng chỉ có thể tự hủy lịch hẹn trước giờ hẹn ít nhất 2 giờ.", "INVALID_TRANSITION"), null);

            var allocations = pets.SelectMany(pet => pet.DichVuTrongDonChiTietDonItems)
                .Where(service => service.TrangThai != OrderServiceState.HUY)
                .GroupBy(service => service.KhungGioId)
                .ToDictionary(group => group.Key, group =>
                    (Seats: group.Select(service => service.ChiTietDonId).Distinct().Count(),
                        Minutes: group.Sum(service => service.ThoiLuongSnapshot)));
            if (allocations.Count > 0)
            {
                List<KhungGioDatLich> slots;
                if (db.Database.IsRelational())
                {
                    var slotIds = allocations.Keys.ToArray();
                    slots = await db.KhungGioDatLichSet.FromSqlInterpolated(
                            $"SELECT * FROM khung_gio_dat_lich WHERE id = ANY({slotIds}) ORDER BY id FOR UPDATE")
                        .ToListAsync(cancellationToken);
                }
                else
                {
                    var slotIds = allocations.Keys.ToList();
                    slots = await db.KhungGioDatLichSet.Where(s => slotIds.Contains(s.Id)).ToListAsync(cancellationToken);
                }

                if (allocations.ContainsKey(booking.KhungGioId))
                    await db.Entry(booking.KhungGio).ReloadAsync(cancellationToken);

                if (slots.Count != allocations.Count || slots.Any(slot =>
                        slot.SlotDaDat < allocations[slot.Id].Seats || slot.PhutDaGiu < allocations[slot.Id].Minutes))
                    return (ApiResponse<BookingResponse>.Fail("Không thể đối chiếu công suất đã giữ cho lịch hẹn.", "INVALID_TRANSITION"), null);

                foreach (var slot in slots)
                {
                    var allocation = allocations[slot.Id];
                    slot.SlotDaDat -= allocation.Seats;
                    slot.PhutDaGiu -= allocation.Minutes;
                    slot.UpdatedAt = now;
                }
            }

            if (booking.TienCocDonDatLich is not null)
            {
                var deposit = booking.TienCocDonDatLich;
                var received = deposit.GiaoDichThuTienCocItems.Sum(x => x.SoTien);
                if (deposit.TrangThaiYeuCau == DepositRequestState.CHO_THU)
                {
                    deposit.TrangThaiYeuCau = DepositRequestState.HUY;
                    deposit.KetThucAt = now;
                    deposit.UpdatedAt = now;
                }

                if (received > 0)
                {
                    var alreadyRefunded = deposit.HoanCocTienCocItems.Sum(x => x.SoTien);
                    var refundable = received - alreadyRefunded;
                    if (refundable > 0)
                    {
                        var staffId = booking.DuyetBoiId ?? booking.TaoBoiId ??
                            await db.NhanVienSet.Where(nv => nv.VaiTro == Role.ADMIN && nv.IsActive).Select(nv => nv.Id).FirstOrDefaultAsync(cancellationToken);
                        var refund = new HoanCoc
                        {
                            RequestId = request.RequestId,
                            SoTien = refundable,
                            LyDo = request.LyDo.Trim(),
                            TrangThai = RefundState.CHO_HOAN,
                            TaoBoiId = staffId,
                            TienCocId = deposit.Id,
                            CreatedAt = now,
                            UpdatedAt = now
                        };
                        db.HoanCocSet.Add(refund);
                        if (!deposit.HoanCocTienCocItems.Contains(refund))
                            deposit.HoanCocTienCocItems.Add(refund);
                    }
                }
            }

            var previousState = booking.TrangThai;
            booking.TrangThai = BookingState.CANCELLED;
            booking.LoaiHuy = CancellationType.CUSTOMER_CANCELLED;
            booking.LyDoHuy = request.LyDo.Trim();
            booking.HuyAt = now;
            booking.UpdatedAt = now;

            var result = BookingProjection.ProjectCurrent(booking, configuration);
            db.AuditLogSet.Add(new AuditLog
            {
                ActorId = 0,
                ActorLoai = "CUSTOMER",
                HanhDong = "TRACKING_CANCEL",
                LoaiDoiTuong = "DonDatLich",
                DoiTuongId = booking.Id,
                LyDo = request.LyDo.Trim(),
                RequestId = request.RequestId,
                Fingerprint = fingerprint,
                TraceId = traceId[..Math.Min(40, traceId.Length)],
                Truoc = JsonSerializer.Serialize(new { trangThai = previousState.ToString() }, JsonOptions),
                Sau = JsonSerializer.Serialize(new { trangThai = booking.TrangThai.ToString(), loaiHuy = CancellationType.CUSTOMER_CANCELLED.ToString() }, JsonOptions),
                KetQua = JsonSerializer.Serialize(result, JsonOptions),
                CreatedAt = now
            });

            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return (ApiResponse<BookingResponse>.Ok(result), null);
        }
        catch (NpgsqlException exception) when (exception.IsTransient)
        {
            return (ApiResponse<BookingResponse>.Fail("Chưa thể xử lý yêu cầu. Vui lòng thử lại.", "TEMPORARY_FAILURE"), null);
        }
        catch (DbUpdateException exception) when (exception.InnerException is NpgsqlException { IsTransient: true })
        {
            return (ApiResponse<BookingResponse>.Fail("Chưa thể xử lý yêu cầu. Vui lòng thử lại.", "TEMPORARY_FAILURE"), null);
        }
    }

    public async Task<ApiResponse<Acknowledged>> ResendAsync(
        TrackingResend request, CancellationToken cancellationToken)
    {
        try
        {
            var matches = db.DonDatLichSet.AsNoTracking().Where(booking =>
                booking.SoDienThoaiSnapshot == request.SoDienThoai &&
                booking.EmailSnapshot == request.Email &&
                booking.TrangThai != BookingState.COMPLETED &&
                booking.TrangThai != BookingState.CANCELLED);

            if (request.NgayHen is DateOnly appointmentDate)
            {
                matches = matches.Where(booking => booking.KhungGio.Ngay == appointmentDate);
            }

            var trackingCodes = await matches.Select(booking => booking.TrackingCode).ToListAsync(cancellationToken);

            foreach (var code in trackingCodes)
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
                    await email.SendTrackingResendAsync(request.Email, code, linked.Token);
                }
                catch
                {
                    // Ignore SMTP errors to preserve 202 response
                }
            }
        }
        catch
        {
            // Ignore DB errors to preserve 202 response
        }

        return ApiResponse<Acknowledged>.Ok(
            new Acknowledged(true, "Nếu email tồn tại trong hệ thống, mã theo dõi sẽ được gửi lại."));
    }

    private async Task<(int? BookingId, int? RetryAfter)> VerifyPairAsync(
        TrackingPair pair, IPAddress? address, CancellationToken cancellationToken)
    {
        var configuredSecret = configuration["Tracking:SecretKey"];
        var secret = string.IsNullOrWhiteSpace(configuredSecret) ? configuration["Jwt:SecretKey"] : configuredSecret;
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Tracking:SecretKey hoặc Jwt:SecretKey chưa được cấu hình.");

        var key = Encoding.UTF8.GetBytes(secret);
        var ip = address is { IsIPv4MappedToIPv6: true } ? address.MapToIPv4() : address;
        var ipHash = Hash(key, "ip", ip?.ToString() ?? "unknown");
        var phoneHash = Hash(key, "phone", pair.SoDienThoai);
        var pairLock = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(
            Encoding.UTF8.GetBytes($"tracking:{ipHash}:{phoneHash}")));

        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        if (db.Database.IsRelational())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({pairLock})", cancellationToken);
        var counter = await db.BaoVeTrackingSet.SingleOrDefaultAsync(
            entry => entry.KhoaIpHash == ipHash && entry.KhoaSdtHash == phoneHash, cancellationToken);
        var now = clock.GetUtcNow();
        if (counter?.KhoaDen is DateTimeOffset lockedUntil && lockedUntil > now)
            return (null, (int)Math.Ceiling((lockedUntil - now).TotalSeconds));

        var bookingId = await db.DonDatLichSet.AsNoTracking()
            .Where(booking => booking.TrackingCode == pair.TrackingCode && booking.SoDienThoaiSnapshot == pair.SoDienThoai)
            .Select(booking => (int?)booking.Id).SingleOrDefaultAsync(cancellationToken);

        if (counter is not null && (counter.KhoaDen.HasValue || now - counter.BatDauCuaSo >= Window))
        {
            counter.KhoaDen = null;
            counter.SoLanSai = 0;
            counter.BatDauCuaSo = now;
            counter.UpdatedAt = now;
        }
        if (bookingId is null)
        {
            if (counter is null)
            {
                counter = new BaoVeTracking
                {
                    KhoaIpHash = ipHash, KhoaSdtHash = phoneHash, BatDauCuaSo = now, CreatedAt = now
                };
                db.BaoVeTrackingSet.Add(counter);
            }
            if (counter.SoLanSai == 0)
                counter.BatDauCuaSo = now;
            counter.SoLanSai++;
            counter.UpdatedAt = now;
            if (counter.SoLanSai >= AttemptLimit)
                counter.KhoaDen = now + Window;
        }

        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        return (bookingId, counter?.KhoaDen is not null ? (int)Window.TotalSeconds : null);
    }

    private static string Hash(byte[] key, string label, string value) =>
        Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"petspa:tracking:v1:{label}:{value}")));

    private static ApiResponse<TrackingResponse> NotFound() =>
        ApiResponse<TrackingResponse>.Fail("Không tìm thấy lịch hẹn với thông tin đã nhập.", "TRACKING_NOT_FOUND");

    private static ApiResponse<TrackingResponse> TemporaryFailure() =>
        ApiResponse<TrackingResponse>.Fail("Chưa thể tra cứu lịch hẹn. Vui lòng thử lại.", "TEMPORARY_FAILURE");
}
