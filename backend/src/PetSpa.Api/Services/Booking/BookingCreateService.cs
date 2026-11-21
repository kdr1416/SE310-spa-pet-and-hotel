using System.Buffers.Binary;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PetSpa.Api.Data;
using PetSpa.Api.Services.Storage;
using PetSpa.Core.DTOs.Booking;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Scheduling;
using PetSpa.Core.Enums;

namespace PetSpa.Api.Services.Booking;

public sealed record BookingCreateResult(
    BookingResponse? Data = null, bool Replayed = false, string? ErrorCode = null, string? Message = null);

public sealed class BookingCreateService(
    PetSpaDbContext db,
    SpaCapacityCalculator capacity,
    BookingProofService proofs,
    IFileStorageService storage,
    IEmailSender email,
    ILogger<BookingCreateService> logger)
{
    private const int MaximumPhotoBytes = 5 * 1024 * 1024;
    private const string TrackingIndex = "ix_don_dat_lich_tracking_code";
    private const string RequestIndex = "ix_don_dat_lich_request_id";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeZoneInfo BusinessTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

    public async Task<BookingCreateResult> CreateAsync(
        BookingCreate request, IFormFileCollection files, CancellationToken cancellationToken)
    {
        if (!IsValid(request))
            return Invalid();

        var keys = request.ThuCung.Where(pet => pet.VaccinationFileKey is not null)
            .Select(pet => pet.VaccinationFileKey!).ToHashSet(StringComparer.Ordinal);
        if (files.Count > 10 || files.Any(file => file.Length > MaximumPhotoBytes))
            return Failure("FILE_TOO_LARGE", "Chỉ tải tối đa 10 ảnh, mỗi ảnh không quá 5 MiB.");
        if (files.Count != keys.Count || !keys.SetEquals(files.Select(file => file.Name))
            || request.ThuCung.Count(pet => pet.VaccinationFileKey is not null) != keys.Count)
            return Invalid("Mỗi ảnh đính kèm phải khớp đúng một vaccinationFileKey.");

        var uploaded = new Dictionary<string, string>(StringComparer.Ordinal);
        var preserveUploads = false;
        string? fingerprint = null;
        try
        {
            fingerprint = await FingerprintAsync(request, files, cancellationToken);
            var replay = await FindReplayAsync(request.RequestId, fingerprint, cancellationToken);
            if (replay is not null)
                return replay;

            if (await proofs.ResolveAsync(request, lockChallenges: false, cancellationToken) is null)
            {
                // A concurrent writer can consume the proof after our first replay lookup.
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                await LockRequestKeyAsync(request.RequestId, cancellationToken);
                return await FindReplayAsync(request.RequestId, fingerprint, cancellationToken) ?? InvalidProof();
            }

            foreach (var file in files)
            {
                using var buffer = new MemoryStream(checked((int)file.Length));
                await file.CopyToAsync(buffer, cancellationToken);
                uploaded.Add(file.Name, await storage.UploadPhotoAsync(buffer.ToArray(), cancellationToken));
            }

            BookingResponse response;
            await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
            {
                await LockRequestKeyAsync(request.RequestId, cancellationToken);
                replay = await FindReplayAsync(request.RequestId, fingerprint, cancellationToken);
                if (replay is not null)
                    return replay;

                var proof = await proofs.ResolveAsync(request, lockChallenges: true, cancellationToken);
                if (proof is null)
                    return InvalidProof();

                var lines = request.ThuCung.SelectMany((pet, index) =>
                    pet.DichVu.Select(service => new RequestedService(index, service.MaDichVu!.Value, service.MaKhungGio)))
                    .ToArray();
                var slotIds = lines.Select(line => line.SlotId).Append(request.MaKhungGio).Distinct().Order().ToArray();
                await LockSlotsAndStaffAsync(slotIds, cancellationToken);
                var slots = await db.KhungGioDatLichSet.Include(slot => slot.CaLamViec)
                    .Where(slot => slotIds.Contains(slot.Id)).ToDictionaryAsync(slot => slot.Id, cancellationToken);
                if (!slots.TryGetValue(request.MaKhungGio, out var intake))
                    return Failure("NOT_FOUND", "Không tìm thấy khung giờ đặt lịch.");
                if (slots.Count != slotIds.Length || request.NgayTra != intake.Ngay
                    || slots.Values.Any(slot => slot.Ngay != intake.Ngay || slot.CaLamViec.GioBatDau < intake.CaLamViec.GioBatDau))
                    return Invalid("Ca thực hiện phải từ ca tiếp nhận trở đi, trong cùng ngày.");

                var services = await LoadServicesAsync(lines.Select(line => line.ServiceId).Distinct().ToArray(), cancellationToken);
                if (services is null)
                    return Invalid("Dịch vụ không tồn tại hoặc không thể đặt riêng.");
                if (services.Any(service => service.Value.Prices.Count != 4))
                    return TemporaryFailure();

                var demands = lines.GroupBy(line => line.SlotId).ToDictionary(group => group.Key, group => new SpaSlotDemand(
                    slots[group.Key], group.Select(line => line.PetIndex).Distinct().Count(),
                    group.Sum(line => (long)services[line.ServiceId].Duration)));
                demands.TryAdd(intake.Id, new SpaSlotDemand(intake, 0, 0));
                var availability = await capacity.CheckAsync(demands.Values, cancellationToken);
                var capacityError = availability.Values.FirstOrDefault(slot => slot.CapacityError is not null)?.CapacityError;
                if (capacityError is not null)
                    return Failure("CAPACITY_EXCEEDED", capacityError);

                var weights = await db.HeSoCanNangSet.AsNoTracking().ToListAsync(cancellationToken);
                var sizes = request.ThuCung.Select(pet => weights.Find(band =>
                    pet.CanNangKhaiBao >= band.CanNangTu && (band.CanNangDen == null || pet.CanNangKhaiBao < band.CanNangDen)))
                    .ToArray();
                if (sizes.Any(size => size is null))
                    return TemporaryFailure();

                var total = lines.Sum(line => services[line.ServiceId].Prices[sizes[line.PetIndex]!.MaSize]);
                if (total > 99_999_999_999_999m)
                    return Invalid("Tổng giá trị đơn đặt lịch vượt quá giới hạn.");

                var booking = BuildBooking(request, fingerprint, proof, intake, slots.Values, services, total);
                db.DonDatLichSet.Add(booking);
                foreach (var demand in demands.Values.Where(demand => demand.Pets > 0))
                {
                    demand.Slot.SlotDaDat += demand.Pets;
                    demand.Slot.PhutDaGiu += (int)demand.Minutes;
                    demand.Slot.UpdatedAt = DateTimeOffset.UtcNow;
                }

                if (!await SaveWithUniqueTrackingCodeAsync(booking, cancellationToken))
                    return TemporaryFailure();

                response = BookingProjection.Project(booking);
                var petPolicies = booking.ChiTietDonDonDatLichItems.Select((pet, index) =>
                {
                    var selection = request.ThuCung[index];
                    var objectPath = selection.VaccinationFileKey is { } key ? uploaded[key] : null;
                    var vaccinationRequired = selection.DichVu.Any(service => services[service.MaDichVu!.Value].VaccinationRequired);
                    return new PetPolicy(pet.Id, selection.LoaiLong, selection.GhiChuDiUng, vaccinationRequired, objectPath);
                }).ToArray();
                booking.ChinhSachSnapshot = JsonSerializer.Serialize(new BookingPolicy(request.GhiChu, petPolicies, response), JsonOptions);
                await db.SaveChangesAsync(cancellationToken);

                if (proof.ChallengeIds.Length > 0)
                {
                    var usedAt = DateTimeOffset.UtcNow;
                    await db.OtpChallengeSet.Where(challenge => proof.ChallengeIds.Contains(challenge.Id))
                        .ExecuteUpdateAsync(update => update.SetProperty(challenge => challenge.SuDungAt, usedAt)
                            .SetProperty(challenge => challenge.UpdatedAt, usedAt), cancellationToken);
                }

                // From here a connection failure may conceal a successful commit. Keep evidence private
                // until its absence can be established instead of deleting a potentially committed reference.
                preserveUploads = true;
                await transaction.CommitAsync(CancellationToken.None);
            }

            return new BookingCreateResult(response with { EmailDelivery = await DeliverEmailAsync(response) });
        }
        catch (FileStorageException exception)
        {
            return Failure(exception.ErrorCode, exception.Message);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception, RequestIndex))
        {
            return await FindReplayAsync(request.RequestId, fingerprint!, cancellationToken) ?? TemporaryFailure();
        }
        catch (Exception exception) when (exception is DbUpdateException or NpgsqlException)
        {
            logger.LogWarning("Could not commit booking. Database failure type: {FailureType}.", exception.GetType().Name);
            return TemporaryFailure();
        }
        finally
        {
            if (!preserveUploads)
            {
                foreach (var path in uploaded.Values)
                    await DeleteOrphanAsync(path);
            }
        }
    }

    private async Task<BookingCreateResult?> FindReplayAsync(Guid requestId, string fingerprint, CancellationToken cancellationToken)
    {
        var booking = await db.DonDatLichSet.AsNoTracking().SingleOrDefaultAsync(row => row.RequestId == requestId, cancellationToken);
        if (booking is null)
            return null;
        if (!string.Equals(booking.Fingerprint, fingerprint, StringComparison.Ordinal))
            return Failure("IDEMPOTENCY_CONFLICT", "Mã yêu cầu đã được dùng cho nội dung đặt lịch khác.");

        using var snapshot = JsonDocument.Parse(booking.ChinhSachSnapshot);
        if (snapshot.RootElement.ValueKind == JsonValueKind.Object
            && snapshot.RootElement.TryGetProperty("creationResponse", out var created)
            && created.Deserialize<BookingResponse>(JsonOptions) is { } response)
            return new BookingCreateResult(response with { EmailDelivery = booking.EmailDelivery ?? "FAILED" }, Replayed: true);

        var legacy = await db.DonDatLichSet.AsNoTracking()
            .Include(row => row.ChiTietDonDonDatLichItems)
            .ThenInclude(pet => pet.DichVuTrongDonChiTietDonItems)
            .ThenInclude(service => service.ThanhPhanDichVuTrongDonDichVuTrongDonItems)
            .SingleAsync(row => row.Id == booking.Id, cancellationToken);
        return new BookingCreateResult(BookingProjection.Project(legacy), Replayed: true);
    }

    private async Task LockSlotsAndStaffAsync(int[] slotIds, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT id FROM khung_gio_dat_lich WHERE id = ANY({slotIds}) ORDER BY id FOR UPDATE", cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT registration.id FROM dang_ky_ca registration
            WHERE EXISTS (
                SELECT 1 FROM khung_gio_dat_lich slot WHERE slot.id = ANY({slotIds})
                    AND slot.ngay = registration.ngay AND slot.ca_lam_viec_id = registration.ca_lam_viec_id
            )
            ORDER BY registration.id FOR UPDATE
            """, cancellationToken);
    }

    private Task LockRequestKeyAsync(Guid requestId, CancellationToken cancellationToken)
    {
        // Serialize only this request key across instances, before acquiring any resource locks.
        var requestLock = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(requestId.ToByteArray()));
        return db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({requestLock})", cancellationToken);
    }

    private async Task<Dictionary<int, ServiceSnapshot>?> LoadServicesAsync(int[] ids, CancellationToken cancellationToken)
    {
        var services = await db.DichVuSet.AsNoTracking()
            .Where(service => ids.Contains(service.Id) && service.IsActive
                && service.LoaiDichVu != "SPA_ADVANCED" && service.TenDichVu != "Cắt tỉa tạo kiểu"
                && service.ThoiLuongPhut > 0)
            .Include(service => service.BangGiaSizeDichVuItems).ThenInclude(price => price.KhoangSize)
            .ToListAsync(cancellationToken);
        if (services.Count != ids.Length)
            return null;
        return services.ToDictionary(service => service.Id, service => new ServiceSnapshot(
            service.TenDichVu, service.ThoiLuongPhut, service.YeuCauTiemChung,
            service.BangGiaSizeDichVuItems.ToDictionary(price => price.KhoangSize.MaSize, price => price.DonGia)));
    }

    private static DonDatLich BuildBooking(
        BookingCreate request, string fingerprint, BookingProofScope proof, KhungGioDatLich intake,
        IEnumerable<KhungGioDatLich> slots, Dictionary<int, ServiceSnapshot> services, decimal total)
    {
        var contact = request.LienHe;
        var booking = new DonDatLich
        {
            RequestId = request.RequestId,
            Fingerprint = fingerprint,
            TrangThai = BookingState.PENDING,
            TenNguoiDat = contact.HoTen,
            SoDienThoaiSnapshot = contact.SoDienThoai,
            EmailSnapshot = contact.Email,
            KhungGioId = intake.Id,
            BatDauDuKien = BusinessInstant(intake.Ngay, intake.CaLamViec.GioBatDau),
            KetThucDuKien = BusinessInstant(intake.Ngay, slots.Max(slot => slot.CaLamViec.GioKetThuc)),
            MocCuoiCa = BusinessInstant(intake.Ngay, intake.CaLamViec.GioKetThuc),
            TongTamTinh = total
        };
        if (proof.CustomerId is { } id)
            booking.KhachHangId = id;
        else
            booking.KhachHang = new KhachHang { HoTen = contact.HoTen, SoDienThoai = contact.SoDienThoai, Email = contact.Email };

        foreach (var selection in request.ThuCung)
        {
            var pet = new ChiTietDon
            {
                CanNangKhaiBao = selection.CanNangKhaiBao,
                TrangThaiChamSoc = PetCareState.NOT_ARRIVED,
                HoSoTiemChungId = selection.MaHoSoTiemChung
            };
            if (selection.MaThuCung is { } petId)
            {
                pet.ThuCungId = petId;
                pet.TenPetSnapshot = proof.Pets[petId].TenThuCung;
            }
            else
            {
                pet.ThuCung = new ThuCung
                {
                    TenThuCung = selection.TenThuCung!,
                    LoaiPet = Enum.Parse<PetType>(selection.LoaiPet!),
                    CanNang = selection.CanNangKhaiBao,
                    Giong = selection.Giong,
                    LoaiLong = selection.LoaiLong,
                    GhiChuDiUng = selection.GhiChuDiUng
                };
                if (proof.CustomerId is { } ownerId)
                    pet.ThuCung.KhachHangId = ownerId;
                else
                    pet.ThuCung.KhachHang = booking.KhachHang;
                pet.TenPetSnapshot = pet.ThuCung.TenThuCung;
            }

            foreach (var selectionService in selection.DichVu)
            {
                var serviceId = selectionService.MaDichVu!.Value;
                var service = services[serviceId];
                pet.DichVuTrongDonChiTietDonItems.Add(new DichVuTrongDon
                {
                    RequestId = Guid.NewGuid(),
                    DichVuId = serviceId,
                    KhungGioId = selectionService.MaKhungGio,
                    TenSnapshot = service.Name,
                    BangGiaSizeSnapshot = JsonSerializer.Serialize(service.Prices, JsonOptions),
                    ThoiLuongSnapshot = service.Duration,
                    TrangThai = OrderServiceState.CHO_THUC_HIEN
                });
            }
            booking.ChiTietDonDonDatLichItems.Add(pet);
        }
        return booking;
    }

    private async Task<bool> SaveWithUniqueTrackingCodeAsync(DonDatLich booking, CancellationToken cancellationToken)
    {
        const int collisionRetries = 3;
        for (var attempt = 0; attempt <= collisionRetries; attempt++)
        {
            booking.TrackingCode = RandomNumberGenerator.GetString("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", 8);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateException exception) when (IsUniqueViolation(exception, TrackingIndex))
            {
                // EF rolls an unsuccessful save back to the transaction's savepoint.
            }
        }
        return false;
    }

    private async Task<string> DeliverEmailAsync(BookingResponse response)
    {
        var attemptedAt = DateTimeOffset.UtcNow;
        var result = "FAILED";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await email.SendTrackingCodeAsync(response.LienHe.Email, response.TrackingCode, timeout.Token);
            result = "SENT";
        }
        catch (Exception exception)
        {
            logger.LogWarning("Tracking email failed for booking {BookingId}. Failure type: {FailureType}.",
                response.MaDon, exception.GetType().Name);
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await db.DonDatLichSet.Where(booking => booking.Id == response.MaDon)
                .ExecuteUpdateAsync(update => update.SetProperty(booking => booking.EmailDelivery, result)
                    .SetProperty(booking => booking.EmailAttemptAt, attemptedAt), timeout.Token);
        }
        catch (Exception exception)
        {
            logger.LogWarning("Could not record tracking email result for booking {BookingId}. Failure type: {FailureType}.",
                response.MaDon, exception.GetType().Name);
        }
        return result;
    }

    private async Task DeleteOrphanAsync(string path)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await storage.DeleteAsync(path, timeout.Token);
        }
        catch (Exception exception)
        {
            logger.LogWarning("Could not clean up a private booking upload. Failure type: {FailureType}.", exception.GetType().Name);
        }
    }

    private static async Task<string> FingerprintAsync(
        BookingCreate request, IFormFileCollection files, CancellationToken cancellationToken)
    {
        var fileHashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            await using var stream = file.OpenReadStream();
            fileHashes.Add(file.Name, Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)));
        }
        var data = JsonSerializer.SerializeToUtf8Bytes(new { Request = request, Files = fileHashes }, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(data));
    }

    private static bool IsValid(BookingCreate request)
    {
        if (request.RequestId == Guid.Empty || request.MaKhungGio <= 0 || request.NgayTra == default
            || request.MaKhachHang is <= 0 || request.ThuCung.Count is < 1 or > 20
            || !ValidText(request.LienHe.HoTen, 1, 150)
            || request.LienHe.SoDienThoai.Length != 10
            || !request.LienHe.SoDienThoai.All(char.IsAsciiDigit)
            || !ValidText(request.LienHe.Email, 1, 254)
            || request.LienHe.Email.Any(char.IsWhiteSpace)
            || !MailAddress.TryCreate(request.LienHe.Email, out var address) || address.Address != request.LienHe.Email
            || !ValidOptionalText(request.GhiChu, 0, 2000)
            || !ValidOptionalText(request.BookingProof, 0, 2000)
            || !ValidOptionalText(request.PetProof, 0, 2000))
            return false;

        var existingPets = new HashSet<int>();
        return request.ThuCung.All(pet => pet is
            { MaLoaiPhong: null, MaThuCung: not <= 0, MaHoSoTiemChung: not <= 0, CanNangKhaiBao: > 0 and <= 999.99m }
            && decimal.Round(pet.CanNangKhaiBao, 2) == pet.CanNangKhaiBao
            && (pet.MaThuCung is { } id ? existingPets.Add(id)
                : ValidText(pet.TenThuCung, 1, 150) && pet.LoaiPet is "CHO" or "MEO")
            && ValidOptionalText(pet.TenThuCung, 1, 150)
            && (pet.LoaiPet is null or "CHO" or "MEO")
            && ValidOptionalText(pet.Giong, 0, 150)
            && ValidOptionalText(pet.LoaiLong, 0, 100)
            && ValidOptionalText(pet.GhiChuDiUng, 0, 2000)
            && ValidOptionalText(pet.VaccinationFileKey, 1, 2000)
            && pet.DichVu is { Count: > 0 }
            && pet.DichVu.All(service => service is { MaDichVu: > 0, MaKhungGio: > 0, MaCombo: null }));
    }

    private static bool ValidText(string? value, int minimum, int maximum) =>
        value is not null && value.Length >= minimum && value.Length <= maximum && !value.Contains('\0')
        && (minimum == 0 || !string.IsNullOrWhiteSpace(value));

    private static bool ValidOptionalText(string? value, int minimum, int maximum) =>
        value is null || ValidText(value, minimum, maximum);

    private static bool IsUniqueViolation(DbUpdateException exception, string constraint) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } error
        && error.ConstraintName == constraint;

    private static DateTimeOffset BusinessInstant(DateOnly day, TimeOnly time) =>
        TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(time), BusinessTimeZone);

    private static BookingCreateResult Invalid(string message = "Dữ liệu đặt lịch không hợp lệ.") =>
        Failure("VALIDATION_FAILED", message);

    private static BookingCreateResult InvalidProof() =>
        Failure("PROOF_SCOPE_INVALID", "Không thể xác minh quyền đặt lịch cho hồ sơ đã chọn.");

    private static BookingCreateResult TemporaryFailure() =>
        Failure("TEMPORARY_FAILURE", "Chưa thể lưu đơn đặt lịch. Vui lòng thử lại.");

    private static BookingCreateResult Failure(string code, string message) => new(ErrorCode: code, Message: message);

    private sealed record RequestedService(int PetIndex, int ServiceId, int SlotId);
    private sealed record ServiceSnapshot(string Name, int Duration, bool VaccinationRequired, Dictionary<SizeBand, decimal> Prices);
    private sealed record BookingPolicy(string? GhiChu, IReadOnlyList<PetPolicy> ThuCung, BookingResponse CreationResponse);
    private sealed record PetPolicy(
        int MaChiTietDon, string? LoaiLong, string? GhiChuDiUng, bool YeuCauTiemChung, string? VaccinationObjectPath);
}
