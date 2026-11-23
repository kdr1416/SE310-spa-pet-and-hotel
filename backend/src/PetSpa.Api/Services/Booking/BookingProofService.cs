using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetSpa.Api.Data;
using PetSpa.Core.DTOs.Booking;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Enums;

namespace PetSpa.Api.Services.Booking;

public sealed record BookingProofScope(int? CustomerId, Dictionary<int, ThuCung> Pets, int[] ChallengeIds);

public sealed class BookingProofService(PetSpaDbContext db)
{
    public async Task<BookingProofScope?> ResolveAsync(
        BookingCreate request, bool lockChallenges, CancellationToken cancellationToken, bool isInternal = false)
    {
        if (isInternal)
        {
            var internalPetIds = request.ThuCung.Where(pet => pet.MaThuCung.HasValue)
                .Select(pet => pet.MaThuCung!.Value).ToList();

            if (request.MaKhachHang is { } internalCustomerId)
            {
                var customer = await db.KhachHangSet.AsNoTracking()
                    .SingleOrDefaultAsync(c => c.Id == internalCustomerId && c.DeletedAt == null, cancellationToken);
                if (customer is null)
                    return null;

                var internalPets = await db.ThuCungSet.AsNoTracking()
                    .Where(pet => internalPetIds.Contains(pet.Id) && pet.KhachHangId == internalCustomerId && pet.DeletedAt == null)
                    .ToDictionaryAsync(pet => pet.Id, cancellationToken);
                if (internalPets.Count != internalPetIds.Count)
                    return null;

                return new BookingProofScope(internalCustomerId, internalPets, []);
            }

            if (internalPetIds.Count > 0)
                return null;

            return new BookingProofScope(null, [], []);
        }

        var settings = await db.CauHinhHeThongSet.Where(setting => setting.Khoa == "EXTENSION_SETTINGS")
            .Select(setting => setting.GiaTri).SingleOrDefaultAsync(cancellationToken);
        var otpRequired = false;
        if (settings is not null)
        {
            using var flags = JsonDocument.Parse(settings);
            if (flags.RootElement.ValueKind != JsonValueKind.Object)
                return null;
            if (flags.RootElement.TryGetProperty("otpEmail", out var option))
            {
                if (option.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return null;
                otpRequired = option.GetBoolean();
            }
        }

        var existingPetIds = request.ThuCung.Where(pet => pet.MaThuCung.HasValue)
            .Select(pet => pet.MaThuCung!.Value).ToList();
        if ((otpRequired && request.BookingProof is null)
            || ((existingPetIds.Count > 0 || request.MaKhachHang.HasValue
                || request.ThuCung.Any(pet => pet.MaHoSoTiemChung.HasValue)) && request.PetProof is null))
            return null;

        var bookingDigest = Digest(request.BookingProof);
        var petDigest = Digest(request.PetProof);
        var digests = new[] { bookingDigest, petDigest }.OfType<string>().Distinct().ToList();
        if (digests.Count == 0)
            return new BookingProofScope(null, [], []);

        if (lockChallenges)
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM otp_challenge WHERE proof_digest = ANY({digests.ToArray()}) ORDER BY id FOR UPDATE", cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var email = request.LienHe.Email.ToLowerInvariant();
        var challenges = await db.OtpChallengeSet.AsNoTracking()
            .Where(challenge => challenge.ProofDigest != null && digests.Contains(challenge.ProofDigest)
                && challenge.SuDungAt == null && challenge.HetHanAt > now
                && challenge.SoDienThoai == request.LienHe.SoDienThoai && challenge.Email.ToLower() == email)
            .ToListAsync(cancellationToken);

        var bookingProof = challenges.Where(challenge => challenge.ProofDigest == bookingDigest
            && challenge.MucDich == OtpScope.BOOKING).ToArray();
        var petProof = challenges.Where(challenge => challenge.ProofDigest == petDigest
            && challenge.MucDich == OtpScope.EXISTING_PETS && challenge.KhachHangId.HasValue).ToArray();
        if ((bookingDigest is not null && bookingProof.Length != 1) || (petDigest is not null && petProof.Length != 1))
            return null;

        var customerId = petProof.SingleOrDefault()?.KhachHangId;
        if ((request.MaKhachHang.HasValue && request.MaKhachHang != customerId)
            || (request.ThuCung.Any(pet => pet.MaHoSoTiemChung.HasValue && !pet.MaThuCung.HasValue)))
            return null;

        if (customerId.HasValue && !await db.KhachHangSet.AnyAsync(customer =>
                customer.Id == customerId && customer.DeletedAt == null && customer.Email.ToLower() == email
                && customer.SoDienThoai == request.LienHe.SoDienThoai, cancellationToken))
            return null;

        var pets = await db.ThuCungSet.AsNoTracking()
            .Where(pet => existingPetIds.Contains(pet.Id) && pet.KhachHangId == customerId && pet.DeletedAt == null)
            .ToDictionaryAsync(pet => pet.Id, cancellationToken);
        if (pets.Count != existingPetIds.Count)
            return null;

        var vaccinations = request.ThuCung.Where(pet => pet.MaHoSoTiemChung.HasValue).ToArray();
        if (vaccinations.Length > 0)
        {
            var ids = vaccinations.Select(pet => pet.MaHoSoTiemChung!.Value).ToList();
            var owners = await db.HoSoTiemChungSet.AsNoTracking().Where(record => ids.Contains(record.Id))
                .ToDictionaryAsync(record => record.Id, record => record.ThuCungId, cancellationToken);
            if (vaccinations.Any(pet => !owners.TryGetValue(pet.MaHoSoTiemChung!.Value, out var petId)
                    || petId != pet.MaThuCung))
                return null;
        }

        return new BookingProofScope(customerId, pets, bookingProof.Concat(petProof).Select(proof => proof.Id).ToArray());
    }

    // Sprint 3 must store the same digest format for a verified, opaque, high-entropy proof.
    private static string? Digest(string? token) => token is null
        ? null
        : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
