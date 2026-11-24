using System.Buffers.Binary;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PetSpa.Api.Data;
using PetSpa.Api.Services.Billing;
using PetSpa.Api.Services.Storage;
using PetSpa.Core.DTOs.Booking;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Scheduling;
using PetSpa.Core.Enums;
using PetSpa.Core.Interfaces.Billing;

namespace PetSpa.Api.Services.Booking;

public sealed record BookingCreateResult(
    BookingResponse? Data = null, bool Replayed = false, string? ErrorCode = null, string? Message = null);

public sealed class BookingCreateService(
    PetSpaDbContext db,
    SpaCapacityCalculator capacity,
    HotelCapacityCalculator hotelCapacity,
    IDepositExpiryService depositExpiry,
    TimeProvider clock,
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
        BookingCreate request, IFormFileCollection files, int? actorId, CancellationToken cancellationToken)
    {
        var isInternal = actorId.HasValue;
        if (!IsValid(request, isInternal))
            return Invalid();

        if (files.Count > 10 || files.Any(file => file.Length > MaximumPhotoBytes))
            return Failure("FILE_TOO_LARGE", "Chỉ tải tối đa 10 ảnh, mỗi ảnh không quá 5 MiB.");

        if (isInternal)
        {
            var role = await GetRoleAsync(actorId!.Value, cancellationToken);
            if (role is null)
                return Failure("FORBIDDEN", "Nhân viên không có quyền tạo đơn đặt lịch tại quầy.");
        }

        var uploaded = new Dictionary<string, string>(StringComparer.Ordinal);
        var preserveUploads = false;
        string? fingerprint = null;
        try
        {
            fingerprint = await FingerprintAsync(request, files, cancellationToken);
            var replay = await FindReplayAsync(request.RequestId, fingerprint, cancellationToken);
            if (replay is not null)
                return replay;

            var references = request.ThuCung.Select(pet => pet.VaccinationFileKey).OfType<string>().ToArray();
            if (fingerprint is null || references.Length != files.Count
                || !references.ToHashSet(StringComparer.Ordinal).SetEquals(files.Select(file => file.Name)))
                return Invalid("Mỗi ảnh đính kèm phải khớp đúng một vaccinationFileKey.");

            if (await proofs.ResolveAsync(request, lockChallenges: false, cancellationToken, isInternal) is null)
            {
                return await FindReplayWithKeyLockAsync(request.RequestId, fingerprint, cancellationToken) ?? InvalidProof();
            }

            try
            {
                foreach (var file in files)
                {
                    using var buffer = new MemoryStream(checked((int)file.Length));
                    await file.CopyToAsync(buffer, cancellationToken);
                    uploaded.Add(file.Name, await storage.UploadPhotoAsync(buffer.ToArray(), cancellationToken));
                }
            }
            catch (FileStorageException exception)
            {
                return await FindReplayWithKeyLockAsync(request.RequestId, fingerprint, cancellationToken)
                    ?? Failure(exception.ErrorCode, exception.Message);
            }

            var lines = request.ThuCung.SelectMany((pet, index) =>
                pet.DichVu.Select(service => new RequestedService(index, service.MaDichVu, service.MaCombo, service.MaKhungGio)))
                .ToArray();
            var slotIds = lines.Select(line => line.SlotId).Append(request.MaKhungGio).Distinct().Order().ToArray();
            var roomTypeIds = request.ThuCung.Where(pet => pet.MaLoaiPhong.HasValue)
                .Select(pet => pet.MaLoaiPhong!.Value).Distinct().Order().ToArray();

            // Expire overdue deposits in a separate transaction BEFORE opening the main booking transaction
            var now = clock.GetUtcNow();
            await depositExpiry.ExpireOverdueDepositsAsync(
                now, null, cancellationToken, targetKhungGioIds: slotIds, targetRoomTypeIds: roomTypeIds);

            BookingResponse response;
            await using (var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(cancellationToken) : null)
            {
                if (db.Database.IsRelational())
                    await LockRequestKeyAsync(request.RequestId, cancellationToken);

                replay = await FindReplayAsync(request.RequestId, fingerprint, cancellationToken);
                if (replay is not null)
                    return replay;

                var proof = await proofs.ResolveAsync(request, lockChallenges: db.Database.IsRelational(), cancellationToken, isInternal);
                if (proof is null)
                    return InvalidProof();

                if (db.Database.IsRelational())
                {
                    await LockSlotsAndStaffAsync(slotIds, cancellationToken);
                    if (roomTypeIds.Length > 0)
                        await hotelCapacity.LockRoomTypesAsync(roomTypeIds, cancellationToken);
                }

                var slotIdList = slotIds.ToList();
                var slots = await db.KhungGioDatLichSet.Include(slot => slot.CaLamViec)
                    .Where(slot => slotIdList.Contains(slot.Id)).ToDictionaryAsync(slot => slot.Id, cancellationToken);
                if (!slots.TryGetValue(request.MaKhungGio, out var intake))
                    return Failure("NOT_FOUND", "Không tìm thấy khung giờ đặt lịch.");

                if (slots.Count != slotIds.Length)
                    return Invalid("Khung giờ dịch vụ không tồn tại.");

                var hasHotel = request.ThuCung.Any(pet => pet.MaLoaiPhong.HasValue);
                if (hasHotel)
                {
                    if (request.NgayTra <= intake.Ngay)
                    {
                        return request.NgayTra < intake.Ngay
                            ? Failure("INVALID_DATE_RANGE", "Ngày trả phòng phải sau ngày nhận phòng.")
                            : Invalid("Đặt phòng Hotel phải có ngày trả sau ngày nhận.");
                    }

                    if (slots.Values.Any(slot => slot.Ngay < intake.Ngay || slot.Ngay > request.NgayTra
                        || (slot.Ngay == intake.Ngay && slot.CaLamViec.GioBatDau < intake.CaLamViec.GioBatDau)))
                    {
                        return Invalid("Ca thực hiện phải nằm trong khoảng thời gian lưu trú và từ ca tiếp nhận trở đi.");
                    }

                    var holidayDates = await db.NgayNghiSet.AsNoTracking()
                        .Where(h => h.Ngay == intake.Ngay || h.Ngay == request.NgayTra)
                        .Select(h => h.Ngay).ToListAsync(cancellationToken);
                    if (holidayDates.Contains(intake.Ngay) || holidayDates.Contains(request.NgayTra))
                    {
                        return Failure("CAPACITY_EXCEEDED", "Ngày tiếp nhận hoặc ngày trả phòng rơi vào ngày nghỉ.");
                    }
                }
                else
                {
                    if (request.NgayTra != intake.Ngay
                        || slots.Values.Any(slot => slot.Ngay != intake.Ngay || slot.CaLamViec.GioBatDau < intake.CaLamViec.GioBatDau))
                    {
                        return Invalid("Ca thực hiện phải từ ca tiếp nhận trở đi, trong cùng ngày.");
                    }
                }

                var singleServiceIds = lines.Where(l => l.ServiceId.HasValue).Select(l => l.ServiceId!.Value).Distinct().ToArray();
                var services = await LoadServicesAsync(singleServiceIds, cancellationToken);
                if (services is null)
                    return Invalid("Dịch vụ không tồn tại hoặc không thể đặt riêng.");
                if (services.Any(service => service.Value.Prices.Count != 4))
                    return TemporaryFailure();

                var comboIds = lines.Where(l => l.ComboId.HasValue).Select(l => l.ComboId!.Value).Distinct().ToArray();
                var combos = await LoadCombosAsync(comboIds, cancellationToken);
                if (combos is null)
                    return Invalid("Combo không tồn tại hoặc đã ngừng hoạt động.");
                if (combos.Any(combo => combo.Value.Prices.Count != 4))
                    return TemporaryFailure();

                var roomTypes = await LoadRoomsAsync(roomTypeIds, cancellationToken);
                if (roomTypes is null)
                    return Invalid("Loại phòng không tồn tại hoặc đã ngừng hoạt động.");

                // Demands for Spa capacity
                var demands = lines.GroupBy(line => line.SlotId).ToDictionary(
                    group => group.Key,
                    group => new SpaSlotDemand(
                        slots[group.Key],
                        group.Select(line => line.PetIndex).Distinct().Count(),
                        group.Sum(line => line.ServiceId.HasValue
                            ? (long)services[line.ServiceId.Value].Duration
                            : (long)combos[line.ComboId!.Value].Duration)));
                demands.TryAdd(intake.Id, new SpaSlotDemand(intake, 0, 0));

                var availability = await capacity.CheckAsync(demands.Values, cancellationToken);
                var capacityError = availability.Values.FirstOrDefault(slot => slot.CapacityError is not null)?.CapacityError;
                if (capacityError is not null)
                    return Failure("CAPACITY_EXCEEDED", capacityError);

                if (hasHotel)
                {
                    var roomAvailabilities = await hotelCapacity.CheckRoomAvailabilityAsync(
                        intake.Ngay, request.NgayTra, roomTypeIds, excludeBookingId: null, now: now, ct: cancellationToken);

                    foreach (var roomId in roomTypeIds)
                    {
                        var needed = request.ThuCung.Count(p => p.MaLoaiPhong == roomId);
                        for (var day = intake.Ngay; day < request.NgayTra; day = day.AddDays(1))
                        {
                            var avail = roomAvailabilities.FirstOrDefault(r => r.MaLoaiPhong == roomId && r.Ngay == day);
                            if (avail is null || avail.SuatConLai < needed)
                                return Failure("CAPACITY_EXCEEDED", "Không đủ công suất phòng đã chọn.");
                        }
                    }
                }

                var weights = await db.HeSoCanNangSet.AsNoTracking().ToListAsync(cancellationToken);
                var sizes = request.ThuCung.Select(pet => weights.Find(band =>
                    pet.CanNangKhaiBao >= band.CanNangTu && (band.CanNangDen == null || pet.CanNangKhaiBao < band.CanNangDen)))
                    .ToArray();
                if (sizes.Any(size => size is null))
                    return TemporaryFailure();

                var totalServices = lines.Sum(line => line.ServiceId.HasValue
                    ? services[line.ServiceId.Value].Prices[sizes[line.PetIndex]!.MaSize]
                    : combos[line.ComboId!.Value].Prices[sizes[line.PetIndex]!.MaSize]);

                var soDemChot = (int)(request.NgayTra.DayNumber - intake.Ngay.DayNumber);
                var totalRooms = request.ThuCung
                    .Where(pet => pet.MaLoaiPhong.HasValue)
                    .Sum(pet => roomTypes[pet.MaLoaiPhong!.Value].GiaMoiDem * soDemChot);

                var total = totalServices + totalRooms;
                if (total > 99_999_999_999_999m)
                    return Invalid("Tổng giá trị đơn đặt lịch vượt quá giới hạn.");

                // Check deposit configuration
                var settingRow = await db.CauHinhHeThongSet.AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Khoa == "EXTENSION_SETTINGS", cancellationToken);
                var requireDeposit = false;
                if (settingRow is not null && !string.IsNullOrWhiteSpace(settingRow.GiaTri))
                {
                    using var doc = JsonDocument.Parse(settingRow.GiaTri);
                    var cocHotel = doc.RootElement.TryGetProperty("cocHotel", out var ch) && ch.GetBoolean();
                    var giuCho = doc.RootElement.TryGetProperty("giuCho", out var gc) && gc.GetBoolean();
                    requireDeposit = cocHotel && giuCho;
                }

                var booking = BuildBooking(request, fingerprint, proof, intake, slots.Values, services, combos, roomTypes, sizes, total, actorId, now, hasHotel && requireDeposit);
                db.DonDatLichSet.Add(booking);
                foreach (var demand in demands.Values.Where(demand => demand.Pets > 0))
                {
                    demand.Slot.SlotDaDat += demand.Pets;
                    demand.Slot.PhutDaGiu += (int)demand.Minutes;
                    demand.Slot.UpdatedAt = now;
                }

                if (!await SaveWithUniqueTrackingCodeAsync(booking, cancellationToken))
                    return TemporaryFailure();

                response = BookingProjection.Project(booking);
                var petPolicies = booking.ChiTietDonDonDatLichItems.Select((pet, index) =>
                {
                    var selection = request.ThuCung[index];
                    var objectPath = selection.VaccinationFileKey is { } key ? uploaded[key] : null;
                    var vaccinationRequired = selection.DichVu.Any(service => service.MaDichVu.HasValue
                        ? services[service.MaDichVu.Value].VaccinationRequired
                        : combos[service.MaCombo!.Value].VaccinationRequired);
                    return new PetPolicy(pet.Id, selection.LoaiLong, selection.GhiChuDiUng, vaccinationRequired, objectPath, sizes[index]!.MaSize.ToString());
                }).ToArray();
                booking.ChinhSachSnapshot = JsonSerializer.Serialize(new BookingPolicy(request.GhiChu, petPolicies, response), JsonOptions);
                await db.SaveChangesAsync(cancellationToken);

                if (proof.ChallengeIds.Length > 0)
                {
                    var challengeIdList = proof.ChallengeIds.ToList();
                    await db.OtpChallengeSet.Where(challenge => challengeIdList.Contains(challenge.Id))
                        .ExecuteUpdateAsync(update => update.SetProperty(challenge => challenge.SuDungAt, now)
                            .SetProperty(challenge => challenge.UpdatedAt, now), cancellationToken);
                }

                preserveUploads = true;
                if (transaction is not null)
                    await transaction.CommitAsync(CancellationToken.None);
            }

            return new BookingCreateResult(response with { EmailDelivery = await DeliverEmailAsync(response) });
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception, RequestIndex))
        {
            return await FindReplayAsync(request.RequestId, fingerprint, cancellationToken) ?? TemporaryFailure();
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

    private async Task<BookingCreateResult?> FindReplayWithKeyLockAsync(
        Guid requestId, string fingerprint, CancellationToken cancellationToken)
    {
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        if (db.Database.IsRelational())
            await LockRequestKeyAsync(requestId, cancellationToken);
        return await FindReplayAsync(requestId, fingerprint, cancellationToken);
    }

    private async Task<BookingCreateResult?> FindReplayAsync(Guid requestId, string? fingerprint, CancellationToken cancellationToken)
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

        var legacy = await BookingProjection.IncludeDetails(db.DonDatLichSet).AsNoTracking()
            .SingleAsync(row => row.Id == booking.Id, cancellationToken);
        return new BookingCreateResult(BookingProjection.Project(legacy), Replayed: true);
    }

    private Task LockRequestKeyAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var key = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"booking-create:{requestId:N}")));
        return db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", cancellationToken);
    }

    private Task LockSlotsAndStaffAsync(int[] slotIds, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT id FROM khung_gio_dat_lich WHERE id = ANY({slotIds}) ORDER BY id FOR UPDATE", cancellationToken);

    private async Task<Dictionary<int, ServiceSnapshot>?> LoadServicesAsync(int[] ids, CancellationToken cancellationToken)
    {
        var idList = ids.ToList();
        var services = await db.DichVuSet.AsNoTracking()
            .Where(service => idList.Contains(service.Id) && service.IsActive
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

    private async Task<Dictionary<int, ComboSnapshot>?> LoadCombosAsync(int[] ids, CancellationToken cancellationToken)
    {
        var idList = ids.ToList();
        var combos = await db.ComboSet.AsNoTracking()
            .Where(combo => idList.Contains(combo.Id) && combo.IsActive)
            .Include(combo => combo.ChiTietComboComboItems).ThenInclude(ct => ct.DichVu)
                .ThenInclude(d => d.BangGiaSizeDichVuItems).ThenInclude(p => p.KhoangSize)
            .Include(combo => combo.BangGiaSizeComboItems).ThenInclude(price => price.KhoangSize)
            .ToListAsync(cancellationToken);
        if (combos.Count != ids.Length)
            return null;
        return combos.ToDictionary(combo => combo.Id, combo => new ComboSnapshot(
            combo.TenCombo,
            combo.ChiTietComboComboItems.Where(ct => ct.DichVu.IsActive).Sum(ct => ct.DichVu.ThoiLuongPhut),
            combo.ChiTietComboComboItems.Any(ct => ct.DichVu.YeuCauTiemChung),
            combo.BangGiaSizeComboItems.ToDictionary(price => price.KhoangSize.MaSize, price => price.DonGia),
            combo.ChiTietComboComboItems.Where(ct => ct.DichVu.IsActive).Select(ct => new ComponentSnapshot(
                ct.DichVu.Id,
                ct.DichVu.TenDichVu,
                ct.DichVu.ThoiLuongPhut,
                ct.DichVu.BangGiaSizeDichVuItems.ToDictionary(price => price.KhoangSize.MaSize, price => price.DonGia)
            )).ToList()
        ));
    }

    private async Task<Dictionary<int, RoomTypeSnapshot>?> LoadRoomsAsync(int[] ids, CancellationToken cancellationToken)
    {
        var idList = ids.ToList();
        var rooms = await db.LoaiPhongSet.AsNoTracking()
            .Where(room => idList.Contains(room.Id) && room.IsActive)
            .ToListAsync(cancellationToken);
        if (rooms.Count != ids.Length)
            return null;
        return rooms.ToDictionary(r => r.Id, r => new RoomTypeSnapshot(r.Id, r.TenPhong, r.GiaMoiDem, r.SoLuongPhong));
    }

    private static DonDatLich BuildBooking(
        BookingCreate request,
        string fingerprint,
        BookingProofScope proof,
        KhungGioDatLich intake,
        IEnumerable<KhungGioDatLich> slots,
        Dictionary<int, ServiceSnapshot> services,
        Dictionary<int, ComboSnapshot> combos,
        Dictionary<int, RoomTypeSnapshot> roomTypes,
        HeSoCanNang?[] sizes,
        decimal total,
        int? actorId,
        DateTimeOffset now,
        bool createDeposit)
    {
        var contact = request.LienHe;
        var hasHotel = request.ThuCung.Any(p => p.MaLoaiPhong.HasValue);
        var soDemChot = (int)(request.NgayTra.DayNumber - intake.Ngay.DayNumber);

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
            KetThucDuKien = hasHotel
                ? BusinessInstant(request.NgayTra, intake.CaLamViec.GioKetThuc)
                : BusinessInstant(intake.Ngay, slots.Max(slot => slot.CaLamViec.GioKetThuc)),
            MocCuoiCa = BusinessInstant(intake.Ngay, intake.CaLamViec.GioKetThuc),
            TongTamTinh = total,
            TaoBoiId = actorId,
            CreatedAt = now,
            UpdatedAt = now
        };

        if (proof.CustomerId is { } id)
            booking.KhachHangId = id;
        else
            booking.KhachHang = new KhachHang { HoTen = contact.HoTen, SoDienThoai = contact.SoDienThoai, Email = contact.Email };

        for (var index = 0; index < request.ThuCung.Count; index++)
        {
            var selection = request.ThuCung[index];
            var pet = new ChiTietDon
            {
                CanNangKhaiBao = selection.CanNangKhaiBao,
                TrangThaiChamSoc = PetCareState.NOT_ARRIVED,
                HoSoTiemChungId = selection.MaHoSoTiemChung,
                CreatedAt = now,
                UpdatedAt = now
            };

            if (selection.MaLoaiPhong is { } roomId)
            {
                pet.LoaiPhongId = roomId;
                pet.GiaPhongCamKet = roomTypes[roomId].GiaMoiDem;
                pet.SoDemChot = soDemChot;
            }

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
                    GhiChuDiUng = selection.GhiChuDiUng,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                if (proof.CustomerId is { } ownerId)
                    pet.ThuCung.KhachHangId = ownerId;
                else
                    pet.ThuCung.KhachHang = booking.KhachHang;
                pet.TenPetSnapshot = pet.ThuCung.TenThuCung;
            }

            foreach (var selectionService in selection.DichVu)
            {
                if (selectionService.MaDichVu is { } serviceId)
                {
                    var service = services[serviceId];
                    pet.DichVuTrongDonChiTietDonItems.Add(new DichVuTrongDon
                    {
                        RequestId = Guid.NewGuid(),
                        DichVuId = serviceId,
                        KhungGioId = selectionService.MaKhungGio,
                        TenSnapshot = service.Name,
                        BangGiaSizeSnapshot = JsonSerializer.Serialize(service.Prices, JsonOptions),
                        ThoiLuongSnapshot = service.Duration,
                        TrangThai = OrderServiceState.CHO_THUC_HIEN,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                }
                else if (selectionService.MaCombo is { } comboId)
                {
                    var combo = combos[comboId];
                    var comboOrder = new DichVuTrongDon
                    {
                        RequestId = Guid.NewGuid(),
                        ComboId = comboId,
                        KhungGioId = selectionService.MaKhungGio,
                        TenSnapshot = combo.Name,
                        BangGiaSizeSnapshot = JsonSerializer.Serialize(combo.Prices, JsonOptions),
                        ThoiLuongSnapshot = combo.Duration,
                        TrangThai = OrderServiceState.CHO_THUC_HIEN,
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    foreach (var component in combo.Components)
                    {
                        comboOrder.ThanhPhanDichVuTrongDonDichVuTrongDonItems.Add(new ThanhPhanDichVuTrongDon
                        {
                            DichVuId = component.Id,
                            TenSnapshot = component.Name,
                            ThoiLuongSnapshot = component.Duration,
                            GiaLeSizeSnapshot = JsonSerializer.Serialize(component.Prices, JsonOptions),
                            TrangThai = OrderServiceState.CHO_THUC_HIEN,
                            CreatedAt = now,
                            UpdatedAt = now
                        });
                    }
                    pet.DichVuTrongDonChiTietDonItems.Add(comboOrder);
                }
            }
            booking.ChiTietDonDonDatLichItems.Add(pet);
        }

        if (createDeposit)
        {
            var deposit = new TienCoc
            {
                CoSoTinhSnapshot = total,
                TyLe = 0.30m,
                SoTienYeuCau = Math.Round(total * 0.30m, 0, MidpointRounding.AwayFromZero),
                HetHanAt = now.AddMinutes(30),
                TrangThaiYeuCau = DepositRequestState.CHO_THU,
                CreatedAt = now,
                UpdatedAt = now
            };
            booking.TienCocDonDatLich = deposit;
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

    private static async Task<string?> FingerprintAsync(
        BookingCreate request, IFormFileCollection files, CancellationToken cancellationToken)
    {
        var fileHashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            if (fileHashes.ContainsKey(file.Name))
                return null;

            await using var stream = file.OpenReadStream();
            fileHashes.Add(file.Name, Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)));
        }
        var data = JsonSerializer.SerializeToUtf8Bytes(new { Request = request, Files = fileHashes }, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(data));
    }

    private static bool IsValid(BookingCreate request, bool isInternal)
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
            { MaThuCung: not <= 0, MaHoSoTiemChung: not <= 0, CanNangKhaiBao: > 0 and <= 999.99m }
            && pet.MaLoaiPhong is not <= 0
            && (pet.DichVu.Count > 0 || pet.MaLoaiPhong.HasValue)
            && decimal.Round(pet.CanNangKhaiBao, 2) == pet.CanNangKhaiBao
            && (pet.MaThuCung is { } id ? existingPets.Add(id)
                : ValidText(pet.TenThuCung, 1, 150) && pet.LoaiPet is "CHO" or "MEO")
            && ValidOptionalText(pet.TenThuCung, 1, 150)
            && (pet.LoaiPet is null or "CHO" or "MEO")
            && ValidOptionalText(pet.Giong, 0, 150)
            && ValidOptionalText(pet.LoaiLong, 0, 100)
            && ValidOptionalText(pet.GhiChuDiUng, 0, 2000)
            && ValidOptionalText(pet.VaccinationFileKey, 1, 2000)
            && pet.DichVu.All(service => service.MaKhungGio > 0
                && ((service.MaDichVu is > 0 && service.MaCombo == null) || (service.MaCombo is > 0 && service.MaDichVu == null))));
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

    private Task<Role?> GetRoleAsync(int actorId, CancellationToken cancellationToken) =>
        db.NhanVienSet.AsNoTracking().Where(employee => employee.Id == actorId && employee.IsActive &&
                employee.NhanVienQuyenNhanVienItems.Any(permission => permission.MaQuyen == "BOOKING_CREATE"))
            .Select(employee => (Role?)employee.VaiTro).SingleOrDefaultAsync(cancellationToken);

    private static BookingCreateResult Invalid(string message = "Dữ liệu đặt lịch không hợp lệ.") =>
        Failure("VALIDATION_FAILED", message);

    private static BookingCreateResult InvalidProof() =>
        Failure("PROOF_SCOPE_INVALID", "Không thể xác minh quyền đặt lịch cho hồ sơ đã chọn.");

    private static BookingCreateResult TemporaryFailure() =>
        Failure("TEMPORARY_FAILURE", "Chưa thể lưu đơn đặt lịch. Vui lòng thử lại.");

    private static BookingCreateResult Failure(string code, string message) => new(ErrorCode: code, Message: message);

    private sealed record RequestedService(int PetIndex, int? ServiceId, int? ComboId, int SlotId);
    private sealed record ServiceSnapshot(string Name, int Duration, bool VaccinationRequired, Dictionary<SizeBand, decimal> Prices);
    private sealed record ComboSnapshot(string Name, int Duration, bool VaccinationRequired, Dictionary<SizeBand, decimal> Prices, List<ComponentSnapshot> Components);
    private sealed record ComponentSnapshot(int Id, string Name, int Duration, Dictionary<SizeBand, decimal> Prices);
    private sealed record RoomTypeSnapshot(int Id, string Name, decimal GiaMoiDem, int SoLuongPhong);
    private sealed record BookingPolicy(string? GhiChu, IReadOnlyList<PetPolicy> ThuCung, BookingResponse CreationResponse);
    private sealed record PetPolicy(
        int MaChiTietDon, string? LoaiLong, string? GhiChuDiUng, bool YeuCauTiemChung, string? VaccinationObjectPath, string SizeCamKet);
}
