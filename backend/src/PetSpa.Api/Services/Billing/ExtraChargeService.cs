using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PetSpa.Api.Data;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Billing;
using PetSpa.Core.DTOs.Care;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Enums;
using PetSpa.Core.Interfaces.Billing;

namespace PetSpa.Api.Services.Billing;

public sealed record ExtraChargeResult<T>(ApiResponse<T> Response, bool IsReplay = false);

public interface IExtraChargeService
{
    Task<ExtraChargeResult<OrderService>> AddExtraServiceAsync(
        int bookingPetId, ExtraServiceRequest request, int actorId, string traceId, CancellationToken ct);
    Task<ExtraChargeResult<FeeDto>> CreateFeeAsync(
        int bookingId, FeeCreate request, int actorId, string traceId, CancellationToken ct);
    Task<ExtraChargeResult<FeeDto>> EditFeeAsync(
        int feeId, FeeEdit request, int actorId, string traceId, CancellationToken ct);
    Task<ExtraChargeResult<FeeDto>> CancelFeeAsync(
        int feeId, ReasonCommand request, int actorId, string traceId, CancellationToken ct);
}

public sealed class ExtraChargeService(
    PetSpaDbContext context,
    SpaCapacityCalculator spaCapacity,
    IDepositExpiryService depositExpiry,
    TimeProvider clock) : IExtraChargeService
{
    private const string InvoiceCreatePermission = "INVOICE_CREATE";
    private const string AuditRequestIndex = "ix_audit_log_actor_id_hanh_dong_request_id";
    private const string ServiceRequestIndex = "ix_dich_vu_trong_don_request_id";
    private const decimal MaximumMoney = 99_999_999_999_999m;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // =========================================================================
    // E20: POST /api/internal/booking-pets/{id}/extra-services
    // =========================================================================
    public async Task<ExtraChargeResult<OrderService>> AddExtraServiceAsync(
        int bookingPetId, ExtraServiceRequest request, int actorId, string traceId, CancellationToken ct)
    {
        if (bookingPetId <= 0 || request.RequestId == Guid.Empty || request.MaKhungGio <= 0 ||
            request.MaDichVu.HasValue == request.MaCombo.HasValue)
        {
            return FailCmd<OrderService>("Dữ liệu dịch vụ phát sinh không hợp lệ.", "VALIDATION_FAILED");
        }

        if (await GetActorRoleAsync(actorId, InvoiceCreatePermission, ct) is null)
            return FailCmd<OrderService>("Không có quyền thêm dịch vụ phát sinh.", "FORBIDDEN");

        var petHeader = await context.ChiTietDonSet.AsNoTracking()
            .Where(p => p.Id == bookingPetId)
            .Select(p => new { p.DonDatLichId })
            .FirstOrDefaultAsync(ct);

        if (petHeader is null)
            return FailCmd<OrderService>("Không tìm thấy thú cưng trong đơn đặt lịch.", "NOT_FOUND");

        var fingerprint = ComputeFingerprint(new
        {
            bookingPetId,
            request.MaDichVu,
            request.MaCombo,
            request.MaKhungGio,
            lyDo = request.LyDo.Trim()
        });

        // Dọn hold cọc quá hạn trên ca đích trước khi mở transaction chính
        if (context.Database.IsRelational())
        {
            await depositExpiry.ExpireOverdueDepositsAsync(
                clock.GetUtcNow(), null, ct, targetKhungGioIds: [request.MaKhungGio], targetRoomTypeIds: []);
        }

        await using var tx = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(ct)
            : null;

        await LockBookingAndSlotAsync(petHeader.DonDatLichId, request.MaKhungGio, ct);

        var role = await GetActorRoleAsync(actorId, InvoiceCreatePermission, ct);
        if (role is null)
            return FailCmd<OrderService>("Không có quyền thêm dịch vụ phát sinh.", "FORBIDDEN");

        var replay = await ReadReplayAsync<OrderService>(actorId, "EXTRA_SERVICE_CREATE", request.RequestId, fingerprint, "Thêm dịch vụ phát sinh thành công.", ct);
        if (replay is not null)
        {
            if (tx is not null) await tx.CommitAsync(ct);
            return replay;
        }

        var booking = await context.DonDatLichSet
            .Include(b => b.KhungGio)
            .Include(b => b.HoaDonDonDatLichItems)
            .Include(b => b.ChiTietDonDonDatLichItems)
                .ThenInclude(p => p.DichVuTrongDonChiTietDonItems)
            .AsSplitQuery()
            .FirstOrDefaultAsync(b => b.Id == petHeader.DonDatLichId, ct);

        var pet = booking?.ChiTietDonDonDatLichItems.FirstOrDefault(p => p.Id == bookingPetId);
        if (booking is null || pet is null)
            return FailCmd<OrderService>("Không tìm thấy thú cưng trong đơn đặt lịch.", "NOT_FOUND");

        if (booking.HoaDonDonDatLichItems.Any(h => h.TrangThai != InvoiceState.REVOKED))
            return FailCmd<OrderService>("Đơn đã có hóa đơn hiệu lực, không thể thêm dịch vụ phát sinh.", "INVALID_TRANSITION");

        if (booking.TrangThai != BookingState.IN_PROGRESS ||
            pet.TrangThaiChamSoc is not (PetCareState.WAITING or PetCareState.IN_PROGRESS or PetCareState.PAUSED) ||
            !pet.SizeChot.HasValue)
        {
            return FailCmd<OrderService>("Trạng thái thú cưng hiện tại không cho phép thêm dịch vụ phát sinh.", "INVALID_TRANSITION");
        }

        var targetSlot = await context.KhungGioDatLichSet
            .Include(s => s.CaLamViec)
            .FirstOrDefaultAsync(s => s.Id == request.MaKhungGio, ct);

        if (targetSlot is null)
            return FailCmd<OrderService>("Không tìm thấy khung giờ thực hiện.", "NOT_FOUND");

        var hcmOffset = TimeSpan.FromHours(7);
        var startDay = DateOnly.FromDateTime(booking.BatDauDuKien.ToOffset(hcmOffset).DateTime);
        var endDay = DateOnly.FromDateTime(booking.KetThucDuKien.ToOffset(hcmOffset).DateTime);
        if (targetSlot.Ngay < startDay || targetSlot.Ngay > endDay)
            return FailCmd<OrderService>("Khung giờ phát sinh nằm ngoài khoảng ngày phục vụ của đơn.", "INVALID_TRANSITION");

        var sizeChot = pet.SizeChot.Value;
        string snapshotName;
        int totalMinutes;
        decimal unitPrice;
        string priceMapJson;
        var components = new List<ThanhPhanDichVuTrongDon>();
        var now = clock.GetUtcNow();

        if (request.MaDichVu.HasValue)
        {
            var service = await context.DichVuSet.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == request.MaDichVu.Value && s.IsActive, ct);

            if (service is null)
                return FailCmd<OrderService>("Không tìm thấy dịch vụ đang hoạt động.", "NOT_FOUND");

            if (string.Equals(service.LoaiDichVu, "SPA_ADVANCED", StringComparison.OrdinalIgnoreCase) ||
                service.TenDichVu.Contains("cắt tỉa tạo kiểu", StringComparison.OrdinalIgnoreCase))
            {
                return FailCmd<OrderService>("Dịch vụ nâng cao không được phép thêm dưới dạng phát sinh.", "INVALID_TRANSITION");
            }

            var priceMap = await LoadServicePriceMapAsync(service.Id, null, ct);
            if (!priceMap.TryGetValue(sizeChot, out unitPrice))
                return FailCmd<OrderService>("Dịch vụ chưa cấu hình giá cho size của thú cưng.", "INVALID_TRANSITION");

            snapshotName = service.TenDichVu;
            totalMinutes = service.ThoiLuongPhut;
            priceMapJson = JsonSerializer.Serialize(priceMap, JsonOptions);
        }
        else
        {
            var combo = await context.ComboSet.AsNoTracking()
                .Include(c => c.ChiTietComboComboItems)
                    .ThenInclude(cc => cc.DichVu)
                .FirstOrDefaultAsync(c => c.Id == request.MaCombo!.Value && c.IsActive, ct);

            if (combo is null || combo.ChiTietComboComboItems.Count == 0)
                return FailCmd<OrderService>("Không tìm thấy combo đang hoạt động.", "NOT_FOUND");

            var comboPriceMap = await LoadServicePriceMapAsync(null, combo.Id, ct);
            if (!comboPriceMap.TryGetValue(sizeChot, out unitPrice))
                return FailCmd<OrderService>("Combo chưa cấu hình giá cho size của thú cưng.", "INVALID_TRANSITION");

            snapshotName = combo.TenCombo;
            totalMinutes = combo.ChiTietComboComboItems.Sum(cc => cc.DichVu.ThoiLuongPhut);
            priceMapJson = JsonSerializer.Serialize(comboPriceMap, JsonOptions);

            foreach (var item in combo.ChiTietComboComboItems.OrderBy(x => x.DichVuId))
            {
                var itemPriceMap = await LoadServicePriceMapAsync(item.DichVuId, null, ct);
                components.Add(new ThanhPhanDichVuTrongDon
                {
                    TenSnapshot = item.DichVu.TenDichVu,
                    ThoiLuongSnapshot = item.DichVu.ThoiLuongPhut,
                    GiaLeSizeSnapshot = JsonSerializer.Serialize(itemPriceMap, JsonOptions),
                    TrangThai = OrderServiceState.CHO_THUC_HIEN,
                    DichVuId = item.DichVuId,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
        }

        var alreadyInSlot = pet.DichVuTrongDonChiTietDonItems
            .Any(s => s.TrangThai != OrderServiceState.HUY && s.KhungGioId == targetSlot.Id);
        var petsDemand = alreadyInSlot ? 0 : 1;

        var capacityResult = await spaCapacity.CheckAsync(
            [new SpaSlotDemand(targetSlot, petsDemand, totalMinutes)], ct);

        if (capacityResult[targetSlot.Id].CapacityError is { } capacityError)
            return FailCmd<OrderService>(capacityError, "CAPACITY_EXCEEDED");

        var newOrderService = new DichVuTrongDon
        {
            RequestId = request.RequestId,
            LaPhatSinh = true,
            TenSnapshot = snapshotName,
            BangGiaSizeSnapshot = priceMapJson,
            ThoiLuongSnapshot = totalMinutes,
            TrangThai = OrderServiceState.CHO_THUC_HIEN,
            SizeChot = sizeChot,
            GiaChot = unitPrice,
            LyDo = request.LyDo.Trim(),
            ChiTietDonId = pet.Id,
            DichVuId = request.MaDichVu,
            ComboId = request.MaCombo,
            KhungGioId = targetSlot.Id,
            CreatedAt = now,
            UpdatedAt = now,
            ThanhPhanDichVuTrongDonDichVuTrongDonItems = components
        };

        pet.DichVuTrongDonChiTietDonItems.Add(newOrderService);
        targetSlot.SlotDaDat += petsDemand;
        targetSlot.PhutDaGiu += totalMinutes;
        targetSlot.UpdatedAt = now;

        booking.TongTamTinh += unitPrice;
        booking.UpdatedAt = now;

        try
        {
            await context.SaveChangesAsync(ct);
            var dto = MapOrderService(newOrderService);
            RecordAudit(actorId, role.Value, "EXTRA_SERVICE_CREATE", "DichVuTrongDon", newOrderService.Id, request.RequestId, fingerprint, traceId, now, dto);
            await context.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);

            return new ExtraChargeResult<OrderService>(ApiResponse<OrderService>.Ok(dto, "Thêm dịch vụ phát sinh thành công."), IsReplay: false);
        }
        catch (DbUpdateException ex) when (UniqueIndex(ex) is AuditRequestIndex or ServiceRequestIndex)
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            context.ChangeTracker.Clear();
            return await ReadReplayAsync<OrderService>(actorId, "EXTRA_SERVICE_CREATE", request.RequestId, fingerprint, "Thêm dịch vụ phát sinh thành công.", ct)
                ?? FailCmd<OrderService>("RequestId đã được sử dụng với dữ liệu khác.", "IDEMPOTENCY_CONFLICT");
        }
    }

    // =========================================================================
    // E22a: POST /api/internal/bookings/{id}/fees
    // =========================================================================
    public async Task<ExtraChargeResult<FeeDto>> CreateFeeAsync(
        int bookingId, FeeCreate request, int actorId, string traceId, CancellationToken ct)
    {
        if (bookingId <= 0 || request.RequestId == Guid.Empty || !IsMoney(request.SoTien) || request.SoTien <= 0)
            return FailCmd<FeeDto>("Dữ liệu tạo phụ phí không hợp lệ.", "VALIDATION_FAILED");

        if (await GetActorRoleAsync(actorId, InvoiceCreatePermission, ct) is null)
            return FailCmd<FeeDto>("Không có quyền quản lý phụ phí.", "FORBIDDEN");

        var fingerprint = ComputeFingerprint(new
        {
            bookingId,
            tenPhuPhi = request.TenPhuPhi.Trim(),
            soTien = (long)request.SoTien,
            lyDo = request.LyDo.Trim(),
            request.MaChiTietDon
        });

        await using var tx = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(ct)
            : null;

        await LockBookingOnlyAsync(bookingId, ct);

        var role = await GetActorRoleAsync(actorId, InvoiceCreatePermission, ct);
        if (role is null)
            return FailCmd<FeeDto>("Không có quyền quản lý phụ phí.", "FORBIDDEN");

        var replay = await ReadReplayAsync<FeeDto>(actorId, "FEE_CREATE", request.RequestId, fingerprint, "Thêm phụ phí thành công.", ct);
        if (replay is not null)
        {
            if (tx is not null) await tx.CommitAsync(ct);
            return replay;
        }

        var booking = await context.DonDatLichSet
            .Include(b => b.HoaDonDonDatLichItems)
            .Include(b => b.ChiTietDonDonDatLichItems)
                .ThenInclude(p => p.NhatKyChamSocChiTietDonItems)
            .AsSplitQuery()
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

        if (booking is null)
            return FailCmd<FeeDto>("Không tìm thấy đơn đặt lịch.", "NOT_FOUND");

        if (booking.HoaDonDonDatLichItems.Any(h => h.TrangThai != InvoiceState.REVOKED))
            return FailCmd<FeeDto>("Đơn đã có hóa đơn hiệu lực, không thể thêm phụ phí.", "INVALID_TRANSITION");

        if (booking.TrangThai is not (BookingState.CONFIRMED or BookingState.IN_PROGRESS))
            return FailCmd<FeeDto>("Trạng thái đơn hiện tại không cho phép thêm phụ phí.", "INVALID_TRANSITION");

        ChiTietDon? targetPet = null;
        if (request.MaChiTietDon.HasValue)
        {
            targetPet = booking.ChiTietDonDonDatLichItems.FirstOrDefault(p => p.Id == request.MaChiTietDon.Value);
            if (targetPet is null)
                return FailCmd<FeeDto>("Thú cưng không thuộc đơn đặt lịch này.", "NOT_FOUND");

            if (targetPet.TrangThaiChamSoc is PetCareState.DONE or PetCareState.CANCELLED)
                return FailCmd<FeeDto>("Không thể thêm phụ phí cho thú cưng đã bàn giao hoặc đã hủy.", "INVALID_TRANSITION");
        }

        var normalizedName = request.TenPhuPhi.Trim();
        var isDetanglingFee = normalizedName.Contains("gỡ rối", StringComparison.OrdinalIgnoreCase) ||
                              normalizedName.Contains("go roi", StringComparison.OrdinalIgnoreCase);

        if (isDetanglingFee)
        {
            if (targetPet is null)
                return FailCmd<FeeDto>("Phụ phí gỡ rối lông bắt buộc gắn với một thú cưng cụ thể.", "INVALID_TRANSITION");

            var allowedFurCodes = await LoadAllowedDetanglingFurCodesAsync(ct);
            var petFurCodes = ReadFurConditions(targetPet.TinhTrangLong);
            var hasQualifyingFur = petFurCodes.Any(code => allowedFurCodes.Contains(code));
            var hasPhotoEvidence = targetPet.NhatKyChamSocChiTietDonItems.Any(log => !string.IsNullOrWhiteSpace(log.ObjectPath));

            if (!hasQualifyingFur && !hasPhotoEvidence)
            {
                return FailCmd<FeeDto>(
                    "Phụ phí gỡ rối lông yêu cầu thú cưng có tình trạng lông rối nặng hoặc có ảnh nhật ký chăm sóc chứng minh.",
                    "INVALID_TRANSITION");
            }
        }

        var now = clock.GetUtcNow();
        var fee = new PhuPhi
        {
            TenPhuPhi = normalizedName,
            SoTien = request.SoTien,
            LyDo = request.LyDo.Trim(),
            DonDatLichId = booking.Id,
            ChiTietDonId = targetPet?.Id,
            TaoBoiId = actorId,
            CreatedAt = now,
            UpdatedAt = now
        };
        context.PhuPhiSet.Add(fee);

        booking.TongTamTinh += fee.SoTien;
        booking.UpdatedAt = now;

        try
        {
            await context.SaveChangesAsync(ct);
            var dto = MapFeeDto(fee);
            RecordAudit(actorId, role.Value, "FEE_CREATE", "PhuPhi", fee.Id, request.RequestId, fingerprint, traceId, now, dto);
            await context.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);

            return new ExtraChargeResult<FeeDto>(ApiResponse<FeeDto>.Ok(dto, "Thêm phụ phí thành công."), IsReplay: false);
        }
        catch (DbUpdateException ex) when (UniqueIndex(ex) is AuditRequestIndex)
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            context.ChangeTracker.Clear();
            return await ReadReplayAsync<FeeDto>(actorId, "FEE_CREATE", request.RequestId, fingerprint, "Thêm phụ phí thành công.", ct)
                ?? FailCmd<FeeDto>("RequestId đã được sử dụng với dữ liệu khác.", "IDEMPOTENCY_CONFLICT");
        }
    }

    // =========================================================================
    // E22b: PATCH /api/internal/fees/{id}
    // =========================================================================
    public async Task<ExtraChargeResult<FeeDto>> EditFeeAsync(
        int feeId, FeeEdit request, int actorId, string traceId, CancellationToken ct)
    {
        if (feeId <= 0 || request.RequestId == Guid.Empty ||
            (request.TenPhuPhi is null && request.SoTien is null) ||
            (request.SoTien.HasValue && (!IsMoney(request.SoTien.Value) || request.SoTien.Value <= 0)))
        {
            return FailCmd<FeeDto>("Dữ liệu cập nhật phụ phí không hợp lệ.", "VALIDATION_FAILED");
        }

        if (await GetActorRoleAsync(actorId, InvoiceCreatePermission, ct) is null)
            return FailCmd<FeeDto>("Không có quyền quản lý phụ phí.", "FORBIDDEN");

        var feeHeader = await context.PhuPhiSet.AsNoTracking()
            .Where(f => f.Id == feeId)
            .Select(f => new { f.DonDatLichId })
            .FirstOrDefaultAsync(ct);

        if (feeHeader is null)
            return FailCmd<FeeDto>("Không tìm thấy phụ phí.", "NOT_FOUND");

        var fingerprint = ComputeFingerprint(new
        {
            feeId,
            tenPhuPhi = request.TenPhuPhi?.Trim(),
            soTien = request.SoTien.HasValue ? (long?)request.SoTien.Value : null,
            lyDo = request.LyDo.Trim()
        });

        await using var tx = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(ct)
            : null;

        await LockBookingOnlyAsync(feeHeader.DonDatLichId, ct);

        var role = await GetActorRoleAsync(actorId, InvoiceCreatePermission, ct);
        if (role is null)
            return FailCmd<FeeDto>("Không có quyền quản lý phụ phí.", "FORBIDDEN");

        var replay = await ReadReplayAsync<FeeDto>(actorId, "FEE_EDIT", request.RequestId, fingerprint, "Cập nhật phụ phí thành công.", ct);
        if (replay is not null)
        {
            if (tx is not null) await tx.CommitAsync(ct);
            return replay;
        }

        var booking = await context.DonDatLichSet
            .Include(b => b.HoaDonDonDatLichItems)
            .Include(b => b.ChiTietDonDonDatLichItems)
            .Include(b => b.PhuPhiDonDatLichItems)
            .AsSplitQuery()
            .FirstOrDefaultAsync(b => b.Id == feeHeader.DonDatLichId, ct);

        var fee = booking?.PhuPhiDonDatLichItems.FirstOrDefault(f => f.Id == feeId);
        if (booking is null || fee is null)
            return FailCmd<FeeDto>("Không tìm thấy phụ phí.", "NOT_FOUND");

        if (booking.HoaDonDonDatLichItems.Any(h => h.TrangThai != InvoiceState.REVOKED))
            return FailCmd<FeeDto>("Đơn đã có hóa đơn hiệu lực, không thể sửa phụ phí.", "INVALID_TRANSITION");

        if (fee.HuyAt.HasValue)
            return FailCmd<FeeDto>("Không thể sửa phụ phí đã bị hủy.", "INVALID_TRANSITION");

        if (fee.ChiTietDonId.HasValue)
        {
            var pet = booking.ChiTietDonDonDatLichItems.FirstOrDefault(p => p.Id == fee.ChiTietDonId.Value);
            if (pet is { TrangThaiChamSoc: PetCareState.DONE or PetCareState.CANCELLED })
                return FailCmd<FeeDto>("Không thể sửa phụ phí của thú cưng đã bàn giao hoặc đã hủy.", "INVALID_TRANSITION");
        }

        var now = clock.GetUtcNow();
        if (!string.IsNullOrWhiteSpace(request.TenPhuPhi))
        {
            fee.TenPhuPhi = request.TenPhuPhi.Trim();
        }
        if (request.SoTien.HasValue)
        {
            var delta = request.SoTien.Value - fee.SoTien;
            fee.SoTien = request.SoTien.Value;
            booking.TongTamTinh = Math.Max(0m, booking.TongTamTinh + delta);
        }
        fee.LyDo = request.LyDo.Trim();
        fee.UpdatedAt = now;
        booking.UpdatedAt = now;

        try
        {
            await context.SaveChangesAsync(ct);
            var dto = MapFeeDto(fee);
            RecordAudit(actorId, role.Value, "FEE_EDIT", "PhuPhi", fee.Id, request.RequestId, fingerprint, traceId, now, dto);
            await context.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);

            return new ExtraChargeResult<FeeDto>(ApiResponse<FeeDto>.Ok(dto, "Cập nhật phụ phí thành công."), IsReplay: false);
        }
        catch (DbUpdateException ex) when (UniqueIndex(ex) is AuditRequestIndex)
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            context.ChangeTracker.Clear();
            return await ReadReplayAsync<FeeDto>(actorId, "FEE_EDIT", request.RequestId, fingerprint, "Cập nhật phụ phí thành công.", ct)
                ?? FailCmd<FeeDto>("RequestId đã được sử dụng với dữ liệu khác.", "IDEMPOTENCY_CONFLICT");
        }
    }

    // =========================================================================
    // E22c: POST /api/internal/fees/{id}/cancel
    // =========================================================================
    public async Task<ExtraChargeResult<FeeDto>> CancelFeeAsync(
        int feeId, ReasonCommand request, int actorId, string traceId, CancellationToken ct)
    {
        if (feeId <= 0 || request.RequestId == Guid.Empty)
            return FailCmd<FeeDto>("Dữ liệu hủy phụ phí không hợp lệ.", "VALIDATION_FAILED");

        if (await GetActorRoleAsync(actorId, InvoiceCreatePermission, ct) is null)
            return FailCmd<FeeDto>("Không có quyền quản lý phụ phí.", "FORBIDDEN");

        var feeHeader = await context.PhuPhiSet.AsNoTracking()
            .Where(f => f.Id == feeId)
            .Select(f => new { f.DonDatLichId })
            .FirstOrDefaultAsync(ct);

        if (feeHeader is null)
            return FailCmd<FeeDto>("Không tìm thấy phụ phí.", "NOT_FOUND");

        var fingerprint = ComputeFingerprint(new
        {
            feeId,
            lyDo = request.LyDo.Trim()
        });

        await using var tx = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(ct)
            : null;

        await LockBookingOnlyAsync(feeHeader.DonDatLichId, ct);

        var role = await GetActorRoleAsync(actorId, InvoiceCreatePermission, ct);
        if (role is null)
            return FailCmd<FeeDto>("Không có quyền quản lý phụ phí.", "FORBIDDEN");

        var replay = await ReadReplayAsync<FeeDto>(actorId, "FEE_CANCEL", request.RequestId, fingerprint, "Hủy phụ phí thành công.", ct);
        if (replay is not null)
        {
            if (tx is not null) await tx.CommitAsync(ct);
            return replay;
        }

        var booking = await context.DonDatLichSet
            .Include(b => b.HoaDonDonDatLichItems)
            .Include(b => b.PhuPhiDonDatLichItems)
            .AsSplitQuery()
            .FirstOrDefaultAsync(b => b.Id == feeHeader.DonDatLichId, ct);

        var fee = booking?.PhuPhiDonDatLichItems.FirstOrDefault(f => f.Id == feeId);
        if (booking is null || fee is null)
            return FailCmd<FeeDto>("Không tìm thấy phụ phí.", "NOT_FOUND");

        if (booking.HoaDonDonDatLichItems.Any(h => h.TrangThai != InvoiceState.REVOKED))
            return FailCmd<FeeDto>("Đơn đã có hóa đơn hiệu lực, không thể hủy phụ phí.", "INVALID_TRANSITION");

        if (fee.HuyAt.HasValue)
            return FailCmd<FeeDto>("Phụ phí này đã được hủy trước đó.", "INVALID_TRANSITION");

        var now = clock.GetUtcNow();
        fee.HuyAt = now;
        fee.LyDo = request.LyDo.Trim();
        fee.UpdatedAt = now;

        booking.TongTamTinh = Math.Max(0m, booking.TongTamTinh - fee.SoTien);
        booking.UpdatedAt = now;

        try
        {
            await context.SaveChangesAsync(ct);
            var dto = MapFeeDto(fee);
            RecordAudit(actorId, role.Value, "FEE_CANCEL", "PhuPhi", fee.Id, request.RequestId, fingerprint, traceId, now, dto);
            await context.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);

            return new ExtraChargeResult<FeeDto>(ApiResponse<FeeDto>.Ok(dto, "Hủy phụ phí thành công."), IsReplay: false);
        }
        catch (DbUpdateException ex) when (UniqueIndex(ex) is AuditRequestIndex)
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            context.ChangeTracker.Clear();
            return await ReadReplayAsync<FeeDto>(actorId, "FEE_CANCEL", request.RequestId, fingerprint, "Hủy phụ phí thành công.", ct)
                ?? FailCmd<FeeDto>("RequestId đã được sử dụng với dữ liệu khác.", "IDEMPOTENCY_CONFLICT");
        }
    }

    // =========================================================================
    // Helpers nội bộ
    // =========================================================================
    private async Task LockBookingOnlyAsync(int bookingId, CancellationToken ct)
    {
        if (!context.Database.IsRelational()) return;
        await context.Database.SqlQuery<int>(
            $"SELECT id AS \"Value\" FROM don_dat_lich WHERE id = {bookingId} FOR UPDATE").ToListAsync(ct);
    }

    private async Task LockBookingAndSlotAsync(int bookingId, int slotId, CancellationToken ct)
    {
        if (!context.Database.IsRelational()) return;

        await context.Database.SqlQuery<int>(
            $"SELECT id AS \"Value\" FROM don_dat_lich WHERE id = {bookingId} FOR UPDATE").ToListAsync(ct);

        await context.Database.SqlQuery<int>(
            $"SELECT id AS \"Value\" FROM khung_gio_dat_lich WHERE id = {slotId} FOR UPDATE").ToListAsync(ct);
    }

    private async Task<Dictionary<SizeBand, decimal>> LoadServicePriceMapAsync(int? dichVuId, int? comboId, CancellationToken ct)
    {
        var prices = await context.BangGiaSizeSet.AsNoTracking()
            .Where(p => p.DichVuId == dichVuId && p.ComboId == comboId)
            .ToListAsync(ct);

        return prices.ToDictionary(p => (SizeBand)p.SizeId, p => p.DonGia);
    }

    private async Task<HashSet<string>> LoadAllowedDetanglingFurCodesAsync(CancellationToken ct)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ROI_NANG" };
        var setting = await context.CauHinhHeThongSet.AsNoTracking()
            .Where(c => c.Khoa == "FUR_CONDITIONS")
            .Select(c => c.GiaTri)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(setting)) return allowed;

        try
        {
            using var doc = JsonDocument.Parse(setting);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (item.TryGetProperty("ma", out var codeProp) &&
                        item.TryGetProperty("choPhepPhuPhiGoRoi", out var allowProp) &&
                        allowProp.ValueKind == JsonValueKind.True)
                    {
                        var code = codeProp.GetString();
                        if (!string.IsNullOrWhiteSpace(code))
                            allowed.Add(code.Trim());
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Fallback về mặc định ROI_NANG nếu cấu hình JSON không đọc được
        }

        return allowed;
    }

    private static List<string> ReadFurConditions(string? conditions)
    {
        if (string.IsNullOrWhiteSpace(conditions)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(conditions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private Task<Role?> GetActorRoleAsync(int actorId, string permission, CancellationToken ct) =>
        context.NhanVienSet.AsNoTracking()
            .Where(n => n.Id == actorId && n.IsActive && n.NhanVienQuyenNhanVienItems.Any(q => q.MaQuyen == permission))
            .Select(n => (Role?)n.VaiTro)
            .SingleOrDefaultAsync(ct);

    private async Task<ExtraChargeResult<T>?> ReadReplayAsync<T>(
        int actorId, string operation, Guid requestId, string fingerprint, string successMessage, CancellationToken ct)
    {
        var audit = await context.AuditLogSet.AsNoTracking().SingleOrDefaultAsync(
            a => a.ActorId == actorId && a.HanhDong == operation && a.RequestId == requestId, ct);
        if (audit is null)
            return null;

        if (audit.Fingerprint != fingerprint)
            return FailCmd<T>("RequestId đã được sử dụng với dữ liệu khác.", "IDEMPOTENCY_CONFLICT");

        var dto = JsonSerializer.Deserialize<T>(audit.KetQua!, JsonOptions)!;
        return new ExtraChargeResult<T>(ApiResponse<T>.Ok(dto, successMessage), IsReplay: true);
    }

    private void RecordAudit<T>(
        int actorId, Role role, string operation, string targetType, int targetId, Guid requestId,
        string fingerprint, string traceId, DateTimeOffset now, T responseDto)
    {
        var safeTraceId = traceId[..Math.Min(40, traceId.Length)];
        context.AuditLogSet.Add(new AuditLog
        {
            ActorId = actorId,
            ActorLoai = role.ToString(),
            HanhDong = operation,
            LoaiDoiTuong = targetType,
            DoiTuongId = targetId,
            RequestId = requestId,
            Fingerprint = fingerprint,
            TraceId = safeTraceId,
            KetQua = JsonSerializer.Serialize(responseDto, JsonOptions),
            CreatedAt = now
        });
    }

    private static OrderService MapOrderService(DichVuTrongDon s) =>
        new()
        {
            MaDichVuTrongDon = s.Id,
            MaDichVu = s.DichVuId,
            MaCombo = s.ComboId,
            MaKhungGio = s.KhungGioId,
            TenDichVu = s.TenSnapshot,
            TrangThai = s.TrangThai.ToString(),
            SizeChot = s.SizeChot?.ToString(),
            GiaChot = s.GiaChot,
            ThanhPhan = s.ThanhPhanDichVuTrongDonDichVuTrongDonItems
                .OrderBy(p => p.Id)
                .Select(p => new Component
                {
                    MaThanhPhan = p.Id,
                    MaDichVu = p.DichVuId,
                    TenDichVu = p.TenSnapshot,
                    TrangThai = p.TrangThai.ToString(),
                    HoanTatAt = p.HoanTatAt
                }).ToList()
        };

    private static FeeDto MapFeeDto(PhuPhi f) =>
        new(f.Id, f.DonDatLichId, f.ChiTietDonId, f.TenPhuPhi, f.SoTien, f.LyDo, f.HuyAt);

    private static string ComputeFingerprint<T>(T payload) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions)));

    private static bool IsMoney(decimal value) => value is >= 0 and <= MaximumMoney && value == decimal.Truncate(value);

    private static string? UniqueIndex(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
            ? postgres.ConstraintName : null;

    private static ExtraChargeResult<T> FailCmd<T>(string message, string errorCode) =>
        new(ApiResponse<T>.Fail(message, errorCode, []), IsReplay: false);
}