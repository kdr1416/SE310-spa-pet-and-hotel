using Microsoft.EntityFrameworkCore;
using Npgsql;
using PetSpa.Api.Data;
using PetSpa.Api.Services.Storage;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Booking;
using PetSpa.Core.DTOs.Care;
using PetSpa.Core.Enums;

namespace PetSpa.Api.Services.Care;

public sealed class CareTasksService(PetSpaDbContext db, CareOperation care, IFileStorageService storage)
{
    private static readonly PetCareState[] ActiveStates = [PetCareState.WAITING, PetCareState.IN_PROGRESS, PetCareState.PAUSED, PetCareState.READY];

    public async Task<ApiResponse<PageBookingPet>> ListAsync(BookingQuery query, int actorId, CancellationToken cancellationToken)
    {
        PetCareState? state = null;
        if (query.TrangThai is not null)
        {
            if (!Enum.TryParse<PetCareState>(query.TrangThai, out var parsed) || parsed.ToString() != query.TrangThai || !ActiveStates.Contains(parsed))
                return ApiResponse<PageBookingPet>.Fail("Trạng thái chăm sóc không hợp lệ.", "VALIDATION_FAILED");
            state = parsed;
        }
        if (query.TuNgay is DateOnly from && query.DenNgay is DateOnly to && (from > to || to.DayNumber - from.DayNumber >= 90))
            return ApiResponse<PageBookingPet>.Fail("Khoảng ngày không hợp lệ hoặc vượt quá 90 ngày.", "INVALID_DATE_RANGE");
        try
        {
            var actor = await care.FindEmployeeWithGrantAsync(actorId, "CARE_UPDATE", cancellationToken);
            if (actor is null)
                return ApiResponse<PageBookingPet>.Fail("Bạn không có quyền xem công việc chăm sóc.", "FORBIDDEN");
            var matches = db.ChiTietDonSet.AsNoTracking().Where(pet =>
                pet.DonDatLich.TrangThai == BookingState.IN_PROGRESS && ActiveStates.Contains(pet.TrangThaiChamSoc));
            if (actor.VaiTro != Role.ADMIN) matches = matches.Where(pet => pet.NhanVienId == actorId);
            if (state.HasValue) matches = matches.Where(pet => pet.TrangThaiChamSoc == state.Value);
            if (query.TuNgay is DateOnly first) matches = matches.Where(pet => pet.DonDatLich.KhungGio.Ngay >= first);
            if (query.DenNgay is DateOnly last) matches = matches.Where(pet => pet.DonDatLich.KhungGio.Ngay <= last);
            var total = await matches.CountAsync(cancellationToken);
            var offset = (query.Trang - 1L) * query.KichThuocTrang;
            if (offset >= total)
                return ApiResponse<PageBookingPet>.Ok(new([], query.Trang, query.KichThuocTrang, total));
            var pets = await CareProjection.IncludeDetails(matches)
                .OrderBy(pet => pet.DonDatLich.BatDauDuKien).ThenBy(pet => pet.Id)
                .Skip((int)offset).Take(query.KichThuocTrang).ToListAsync(cancellationToken);
            var items = pets.Select(pet => CareProjection.Pet(pet, actor)).ToArray();
            var paths = pets.SelectMany(pet => pet.NhatKyChamSocChiTietDonItems)
                .Where(log => !string.IsNullOrWhiteSpace(log.ObjectPath)).ToDictionary(log => log.Id, log => log.ObjectPath!);
            var signed = new Dictionary<string, string?>();
            foreach (var log in items.SelectMany(pet => pet.NhatKy))
            {
                if (!paths.TryGetValue(log.MaNhatKy, out var path) || path is "." or ".." || path.IndexOfAny(['/', '\\', ':']) >= 0) continue;
                if (!signed.TryGetValue(path, out var url))
                {
                    try { url = (await storage.CreateReadUrlAsync(path, cancellationToken)).AbsoluteUri; }
                    catch (FileStorageException) { url = null; }
                    signed.Add(path, url);
                }
                log.AnhUrl = url;
            }
            return ApiResponse<PageBookingPet>.Ok(new(items, query.Trang, query.KichThuocTrang, total));
        }
        catch (Exception error) when (error is NpgsqlException { IsTransient: true } or
            InvalidOperationException { InnerException: NpgsqlException { IsTransient: true } })
        {
            return ApiResponse<PageBookingPet>.Fail("Dịch vụ tạm thời không khả dụng.", "TEMPORARY_FAILURE");
        }
    }
}
