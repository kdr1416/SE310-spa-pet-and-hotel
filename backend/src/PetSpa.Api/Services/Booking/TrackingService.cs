using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PetSpa.Api.Data;
using PetSpa.Api.Services.Storage;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Booking;
using PetSpa.Core.Entities.Identity;

namespace PetSpa.Api.Services.Booking;

public sealed class TrackingService(PetSpaDbContext db, IFileStorageService storage, IConfiguration configuration, TimeProvider clock)
{
    private const int AttemptLimit = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

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

            // Only load and sign assets for a verified pair, after releasing its limiter transaction.
            var booking = await BookingProjection.IncludeDetails(db.DonDatLichSet.AsNoTracking())
                .SingleOrDefaultAsync(booking => booking.Id == bookingId && booking.TrackingCode == pair.TrackingCode &&
                    booking.SoDienThoaiSnapshot == pair.SoDienThoai, cancellationToken);
            if (booking is null)
                return (NotFound(), null);

            var display = BookingProjection.ProjectCurrent(booking, configuration);
            var files = booking.ChiTietDonDonDatLichItems
                .SelectMany(pet => pet.NhatKyChamSocChiTietDonItems)
                .Where(log => !string.IsNullOrWhiteSpace(log.ObjectPath))
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

            // Public cancellation and reviews have no handler in Sprint 1.
            var response = new TrackingResponse(display.MaDon, display.TrackingCode, display.TrangThai,
                display.BatDauDuKien, display.KetThucDuKien, display.TongTamTinh, display.ThuCung,
                display.HoaDon, display.TienCoc, []);
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

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
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
