using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PetSpa.Api.Data;
using PetSpa.Api.Services.Booking;
using PetSpa.Api.Services.Storage;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Billing;
using PetSpa.Core.DTOs.Booking;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Entities.Scheduling;
using PetSpa.Core.Enums;
using PetSpa.Core.Interfaces.Billing;

namespace PetSpa.Api.Services.Billing;

public sealed record CommandResult<T>(ApiResponse<T> Response, bool IsReplay = false);

public interface IDepositService
{
    Task<CommandResult<DepositDto>> ConfirmDepositReceiptAsync(
        int depositId, ReceiptCreate request, int actorId, string traceId, CancellationToken ct);
    Task<ApiResponse<PageDeposit>> GetDepositsAsync(
        BookingQuery query, int actorId, CancellationToken ct);
    Task<CommandResult<RefundDto>> CreateRefundAsync(
        int depositId, RefundCreate request, int actorId, string traceId, CancellationToken ct);
    Task<CommandResult<RefundDto>> ConfirmRefundAsync(
        int refundId, RefundConfirm request, byte[]? evidenceContent, int actorId, string traceId, CancellationToken ct);
    Task<ApiResponse<PageRefund>> GetRefundsAsync(
        BookingQuery query, int actorId, CancellationToken ct);
}

public sealed class DepositService(
    PetSpaDbContext context,
    IDepositExpiryService depositExpiry,
    IBillingSettlementHelper billingSettlement,
    IFileStorageService fileStorage,
    IEmailSender emailSender,
    TimeProvider clock) : IDepositService
{
    private const string ConfirmPermission = "DEPOSIT_CONFIRM";
    private const string RefundPermission = "DEPOSIT_REFUND";
    private const string AuditRequestIndex = "ix_audit_log_actor_id_hanh_dong_request_id";
    private const string ReceiptRequestIndex = "ix_giao_dich_thu_request_id";
    private const string BankReceiptIndex = "ix_giao_dich_thu_ngan_hang_ma_giao_dich";
    private const string RefundRequestIndex = "ix_hoan_coc_request_id";
    private const decimal MaximumMoney = 99_999_999_999_999m;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // =========================================================================
    // E42: POST /api/internal/deposits/{id}/receipts
    // =========================================================================
    public async Task<CommandResult<DepositDto>> ConfirmDepositReceiptAsync(
        int depositId, ReceiptCreate request, int actorId, string traceId, CancellationToken ct)
    {
        if (depositId <= 0 || request.RequestId == Guid.Empty || !IsMoney(request.SoTien) || request.SoTien <= 0)
            return FailCmd<DepositDto>("Dữ liệu thu cọc không hợp lệ.", "VALIDATION_FAILED");

        if (request.PhuongThuc == "BANK_TRANSFER" &&
            (string.IsNullOrWhiteSpace(request.NganHang) || string.IsNullOrWhiteSpace(request.MaGiaoDich)))
        {
            return FailCmd<DepositDto>("Chuyển khoản bắt buộc cung cấp ngân hàng và mã giao dịch.", "VALIDATION_FAILED");
        }

        if (await GetActorRoleAsync(actorId, ConfirmPermission, ct) is null)
            return FailCmd<DepositDto>("Không có quyền xác nhận thu cọc.", "FORBIDDEN");

        var bookingId = await context.TienCocSet.AsNoTracking()
            .Where(d => d.Id == depositId)
            .Select(d => (int?)d.DonDatLichId)
            .FirstOrDefaultAsync(ct);

        if (!bookingId.HasValue)
            return FailCmd<DepositDto>("Không tìm thấy yêu cầu thu cọc.", "NOT_FOUND");

        var fingerprint = ComputeFingerprint(new
        {
            depositId,
            soTien = (long)request.SoTien,
            request.PhuongThuc,
            giaoDichAt = request.GiaoDichAt.ToUniversalTime(),
            maGiaoDich = request.MaGiaoDich?.Trim(),
            nganHang = request.NganHang?.Trim(),
            ghiChu = request.GhiChu?.Trim()
        });

        await using var tx = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(ct)
            : null;

        await LockBookingGraphForDepositAsync(bookingId.Value, lockRefunds: false, lockInvoices: true, ct);

        var role = await GetActorRoleAsync(actorId, ConfirmPermission, ct);
        if (role is null)
            return FailCmd<DepositDto>("Không có quyền xác nhận thu cọc.", "FORBIDDEN");

        var replay = await ReadReplayAsync<DepositDto>(actorId, ConfirmPermission, request.RequestId, fingerprint, "Ghi nhận thu cọc thành công.", ct);
        if (replay is not null)
        {
            if (tx is not null) await tx.CommitAsync(ct);
            return replay;
        }

        var booking = await LoadFullBookingForDepositAsync(bookingId.Value, ct);
        var deposit = booking?.TienCocDonDatLich;
        if (booking is null || deposit is null || deposit.Id != depositId)
            return FailCmd<DepositDto>("Không tìm thấy yêu cầu thu cọc.", "NOT_FOUND");

        // Chống trùng (nganHang, maGiaoDich) theo M2-L05
        if (request.PhuongThuc == "BANK_TRANSFER" && !string.IsNullOrWhiteSpace(request.MaGiaoDich))
        {
            var bankNorm = request.NganHang!.Trim();
            var txCodeNorm = request.MaGiaoDich.Trim();
            var duplicateTx = await context.GiaoDichThuSet.AsNoTracking()
                .AnyAsync(x => x.NganHang == bankNorm && x.MaGiaoDich == txCodeNorm, ct);
            if (duplicateTx)
                return FailCmd<DepositDto>("Mã giao dịch ngân hàng đã tồn tại trong hệ thống.", "INVALID_TRANSITION");
        }

        var now = clock.GetUtcNow();

        // Kiểm tra quá hạn 30 phút dưới khóa (M2-11 / M2-L06)
        if (deposit.TrangThaiYeuCau == DepositRequestState.CHO_THU && now >= deposit.HetHanAt)
        {
            if (context.Database.IsRelational())
            {
                await depositExpiry.ExpireLockedBookingDepositAsync(booking, now, ct);
            }
            else
            {
                ExpireInMemoryBookingDeposit(booking, deposit, now);
            }
        }

        var paymentMethod = Enum.Parse<PaymentMethod>(request.PhuongThuc);
        deposit.GiaoDichThuTienCocItems.Add(new GiaoDichThu
        {
            RequestId = request.RequestId,
            SoTien = request.SoTien,
            PhuongThuc = paymentMethod,
            NganHang = request.NganHang?.Trim(),
            MaGiaoDich = request.MaGiaoDich?.Trim(),
            GiaoDichAt = request.GiaoDichAt,
            XacNhanAt = now,
            GhiChu = request.GhiChu?.Trim(),
            TienCocId = deposit.Id,
            XacNhanBoiId = actorId,
            CreatedAt = now,
            UpdatedAt = now
        });

        if (deposit.TrangThaiYeuCau is DepositRequestState.HET_HAN or DepositRequestState.HUY)
        {
            // Tiền đến muộn sau hạn/hủy: gom toàn bộ phần thực nhận chưa dành hoàn vào lệnh CHO_HOAN
            var totalReceived = deposit.GiaoDichThuTienCocItems.Sum(x => x.SoTien);
            var totalReserved = deposit.HoanCocTienCocItems.Sum(x => x.SoTien);
            var unreserved = totalReceived - totalReserved;
            if (unreserved > 0)
            {
                var pendingRefund = deposit.HoanCocTienCocItems
                    .FirstOrDefault(r => r.TrangThai == RefundState.CHO_HOAN);
                if (pendingRefund is not null)
                {
                    pendingRefund.SoTien += unreserved;
                    pendingRefund.UpdatedAt = now;
                }
                else
                {
                    deposit.HoanCocTienCocItems.Add(new HoanCoc
                    {
                        RequestId = Guid.NewGuid(),
                        SoTien = unreserved,
                        TrangThai = RefundState.CHO_HOAN,
                        LyDo = "Hoàn tiền cọc nhận trên đơn đã hết hạn/hủy",
                        TaoBoiId = actorId,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                }
            }
            deposit.UpdatedAt = now;
        }
        else if (deposit.TrangThaiYeuCau == DepositRequestState.CHO_THU)
        {
            var totalReceived = deposit.GiaoDichThuTienCocItems.Sum(x => x.SoTien);
            if (totalReceived >= deposit.SoTienYeuCau)
            {
                deposit.TrangThaiYeuCau = DepositRequestState.DA_DAP_UNG;
                deposit.KetThucAt = now;
            }
            deposit.UpdatedAt = now;
        }
        else
        {
            return FailCmd<DepositDto>("Yêu cầu cọc này đã được đáp ứng đủ.", "INVALID_TRANSITION");
        }

        try
        {
            await context.SaveChangesAsync(ct);
            var dto = MapDepositDto(booking, deposit);
            RecordAudit(actorId, role.Value, ConfirmPermission, "TienCoc", deposit.Id, request.RequestId, fingerprint, traceId, now, dto);
            await context.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);

            return new CommandResult<DepositDto>(ApiResponse<DepositDto>.Ok(dto, "Ghi nhận thu cọc thành công."), IsReplay: false);
        }
        catch (DbUpdateException ex) when (UniqueIndex(ex) is AuditRequestIndex or ReceiptRequestIndex)
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            context.ChangeTracker.Clear();
            return await ReadReplayAsync<DepositDto>(actorId, ConfirmPermission, request.RequestId, fingerprint, "Ghi nhận thu cọc thành công.", ct)
                ?? FailCmd<DepositDto>("RequestId đã được sử dụng với dữ liệu khác.", "IDEMPOTENCY_CONFLICT");
        }
        catch (DbUpdateException ex) when (UniqueIndex(ex) is BankReceiptIndex)
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            context.ChangeTracker.Clear();
            return await ReadReplayAsync<DepositDto>(actorId, ConfirmPermission, request.RequestId, fingerprint, "Ghi nhận thu cọc thành công.", ct)
                ?? FailCmd<DepositDto>("Mã giao dịch ngân hàng đã được ghi nhận.", "INVALID_TRANSITION");
        }
    }

    // =========================================================================
    // E43: GET /api/internal/deposits
    // =========================================================================
    public async Task<ApiResponse<PageDeposit>> GetDepositsAsync(
        BookingQuery query, int actorId, CancellationToken ct)
    {
        if (await GetActorRoleAsync(actorId, ConfirmPermission, ct) is null)
            return ApiResponse<PageDeposit>.Fail("Không có quyền xem danh sách thu cọc.", "FORBIDDEN");

        var q = context.TienCocSet.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.TrangThai))
        {
            if (!Enum.TryParse<DepositRequestState>(query.TrangThai.Trim(), ignoreCase: false, out var state))
                return ApiResponse<PageDeposit>.Fail("Trạng thái yêu cầu cọc không hợp lệ.", "VALIDATION_FAILED");
            q = q.Where(d => d.TrangThaiYeuCau == state);
        }

        var hcmOffset = TimeSpan.FromHours(7);
        if (query.TuNgay.HasValue)
        {
            var start = new DateTimeOffset(query.TuNgay.Value.ToDateTime(TimeOnly.MinValue), hcmOffset).ToUniversalTime();
            q = q.Where(d => d.CreatedAt >= start);
        }
        if (query.DenNgay.HasValue)
        {
            var endExclusive = new DateTimeOffset(query.DenNgay.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), hcmOffset).ToUniversalTime();
            q = q.Where(d => d.CreatedAt < endExclusive);
        }

        var total = await q.CountAsync(ct);
        var page = Math.Max(1, query.Trang);
        var pageSize = Math.Clamp(query.KichThuocTrang, 1, 100);

        var items = await q.OrderByDescending(d => d.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(d => d.GiaoDichThuTienCocItems)
            .Include(d => d.HoanCocTienCocItems)
            .Include(d => d.DonDatLich).ThenInclude(b => b.HoaDonDonDatLichItems)
            .Include(d => d.DonDatLich).ThenInclude(b => b.ChiTietDonDonDatLichItems)
            .AsSplitQuery()
            .ToListAsync(ct);

        var dtos = items.Select(d => MapDepositDto(d.DonDatLich, d)).ToList();
        return ApiResponse<PageDeposit>.Ok(new PageDeposit(dtos, page, pageSize, total), "Lấy danh sách thu cọc thành công.");
    }

    // =========================================================================
    // E44: POST /api/admin/deposits/{id}/refunds
    // =========================================================================
    public async Task<CommandResult<RefundDto>> CreateRefundAsync(
        int depositId, RefundCreate request, int actorId, string traceId, CancellationToken ct)
    {
        if (depositId <= 0 || request.RequestId == Guid.Empty || !IsMoney(request.SoTien) || request.SoTien <= 0)
            return FailCmd<RefundDto>("Dữ liệu tạo lệnh hoàn cọc không hợp lệ.", "VALIDATION_FAILED");

        if (await GetActorRoleAsync(actorId, RefundPermission, ct) is null)
            return FailCmd<RefundDto>("Không có quyền tạo lệnh hoàn cọc.", "FORBIDDEN");

        var bookingId = await context.TienCocSet.AsNoTracking()
            .Where(d => d.Id == depositId)
            .Select(d => (int?)d.DonDatLichId)
            .FirstOrDefaultAsync(ct);

        if (!bookingId.HasValue)
            return FailCmd<RefundDto>("Không tìm thấy yêu cầu thu cọc.", "NOT_FOUND");

        var fingerprint = ComputeFingerprint(new
        {
            depositId,
            soTien = (long)request.SoTien,
            lyDo = request.LyDo.Trim()
        });

        await using var tx = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(ct)
            : null;

        await LockBookingGraphForDepositAsync(bookingId.Value, lockRefunds: true, lockInvoices: true, ct);

        var role = await GetActorRoleAsync(actorId, RefundPermission, ct);
        if (role is null)
            return FailCmd<RefundDto>("Không có quyền tạo lệnh hoàn cọc.", "FORBIDDEN");

        var replay = await ReadReplayAsync<RefundDto>(actorId, "DEPOSIT_REFUND_CREATE", request.RequestId, fingerprint, "Tạo lệnh hoàn cọc thành công.", ct);
        if (replay is not null)
        {
            if (tx is not null) await tx.CommitAsync(ct);
            return replay;
        }

        var booking = await LoadFullBookingForDepositAsync(bookingId.Value, ct);
        var deposit = booking?.TienCocDonDatLich;
        if (booking is null || deposit is null || deposit.Id != depositId)
            return FailCmd<RefundDto>("Không tìm thấy yêu cầu thu cọc.", "NOT_FOUND");

        var thucNhan = deposit.GiaoDichThuTienCocItems.Sum(x => x.SoTien);
        var danhHoan = deposit.HoanCocTienCocItems.Sum(x => x.SoTien);
        var obligatedAmount = billingSettlement.CalculateObligatedDepositAmount(booking);

        if (obligatedAmount + danhHoan + request.SoTien > thucNhan)
        {
            return FailCmd<RefundDto>(
                "Số tiền yêu cầu hoàn vượt quá phần cọc khả dụng chưa ràng buộc nghĩa vụ.",
                "AMOUNT_MISMATCH");
        }

        var now = clock.GetUtcNow();
        var refund = new HoanCoc
        {
            RequestId = request.RequestId,
            SoTien = request.SoTien,
            LyDo = request.LyDo.Trim(),
            TrangThai = RefundState.CHO_HOAN,
            TienCocId = deposit.Id,
            TaoBoiId = actorId,
            CreatedAt = now,
            UpdatedAt = now
        };
        deposit.HoanCocTienCocItems.Add(refund);

        try
        {
            await context.SaveChangesAsync(ct);
            var dto = MapRefundDto(refund, includeInternalDetails: true);
            RecordAudit(actorId, role.Value, "DEPOSIT_REFUND_CREATE", "HoanCoc", refund.Id, request.RequestId, fingerprint, traceId, now, dto);
            await context.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);

            return new CommandResult<RefundDto>(ApiResponse<RefundDto>.Ok(dto, "Tạo lệnh hoàn cọc thành công."), IsReplay: false);
        }
        catch (DbUpdateException ex) when (UniqueIndex(ex) is AuditRequestIndex or RefundRequestIndex)
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            context.ChangeTracker.Clear();
            return await ReadReplayAsync<RefundDto>(actorId, "DEPOSIT_REFUND_CREATE", request.RequestId, fingerprint, "Tạo lệnh hoàn cọc thành công.", ct)
                ?? FailCmd<RefundDto>("RequestId đã được sử dụng với dữ liệu khác.", "IDEMPOTENCY_CONFLICT");
        }
    }

    // =========================================================================
    // E45: POST /api/admin/refunds/{id}/confirm
    // =========================================================================
    public async Task<CommandResult<RefundDto>> ConfirmRefundAsync(
        int refundId, RefundConfirm request, byte[]? evidenceContent, int actorId, string traceId, CancellationToken ct)
    {
        if (refundId <= 0 || request.RequestId == Guid.Empty)
            return FailCmd<RefundDto>("Dữ liệu xác nhận hoàn cọc không hợp lệ.", "VALIDATION_FAILED");

        if (await GetActorRoleAsync(actorId, RefundPermission, ct) is null)
            return FailCmd<RefundDto>("Không có quyền xác nhận hoàn cọc.", "FORBIDDEN");

        var bookingId = await context.HoanCocSet.AsNoTracking()
            .Where(r => r.Id == refundId)
            .Select(r => (int?)r.TienCoc.DonDatLichId)
            .FirstOrDefaultAsync(ct);

        if (!bookingId.HasValue)
            return FailCmd<RefundDto>("Không tìm thấy lệnh hoàn cọc.", "NOT_FOUND");

        var evidenceHash = evidenceContent is { Length: > 0 }
            ? Convert.ToHexString(SHA256.HashData(evidenceContent))
            : null;

        var fingerprint = ComputeFingerprint(new
        {
            refundId,
            request.PhuongThuc,
            hoanAt = request.HoanAt.ToUniversalTime(),
            thamChieu = request.ThamChieu.Trim(),
            request.BangChungFileKey,
            evidenceHash
        });

        string? uploadedObjectPath = null;
        if (evidenceContent is { Length: > 0 })
        {
            uploadedObjectPath = await fileStorage.UploadPhotoAsync(evidenceContent, ct);
        }

        try
        {
            await using var tx = context.Database.IsRelational()
                ? await context.Database.BeginTransactionAsync(ct)
                : null;

            await LockBookingGraphForDepositAsync(bookingId.Value, lockRefunds: true, lockInvoices: true, ct);

            var role = await GetActorRoleAsync(actorId, RefundPermission, ct);
            if (role is null)
            {
                if (uploadedObjectPath is not null) await SafeDeleteStorageAsync(uploadedObjectPath);
                return FailCmd<RefundDto>("Không có quyền xác nhận hoàn cọc.", "FORBIDDEN");
            }

            var replay = await ReadReplayAsync<RefundDto>(actorId, "DEPOSIT_REFUND_CONFIRM", request.RequestId, fingerprint, "Xác nhận đã hoàn tiền cọc thành công.", ct);
            if (replay is not null)
            {
                if (tx is not null) await tx.CommitAsync(ct);
                if (uploadedObjectPath is not null) await SafeDeleteStorageAsync(uploadedObjectPath);
                return replay;
            }

            var booking = await LoadFullBookingForDepositAsync(bookingId.Value, ct);
            var deposit = booking?.TienCocDonDatLich;
            var refund = deposit?.HoanCocTienCocItems.FirstOrDefault(r => r.Id == refundId);

            if (booking is null || deposit is null || refund is null)
            {
                if (uploadedObjectPath is not null) await SafeDeleteStorageAsync(uploadedObjectPath);
                return FailCmd<RefundDto>("Không tìm thấy lệnh hoàn cọc.", "NOT_FOUND");
            }

            if (refund.TrangThai != RefundState.CHO_HOAN)
            {
                if (uploadedObjectPath is not null) await SafeDeleteStorageAsync(uploadedObjectPath);
                return FailCmd<RefundDto>("Chỉ có thể xác nhận lệnh hoàn cọc đang ở trạng thái CHO_HOAN.", "INVALID_TRANSITION");
            }

            var now = clock.GetUtcNow();
            refund.TrangThai = RefundState.DA_HOAN;
            refund.HoanBoiId = actorId;
            refund.HoanAt = request.HoanAt;
            refund.PhuongThuc = Enum.Parse<PaymentMethod>(request.PhuongThuc);
            refund.ThamChieu = request.ThamChieu.Trim();
            refund.BangChungPath = uploadedObjectPath;
            refund.UpdatedAt = now;

            billingSettlement.TryTransitionBookingTerminalState(booking, now);

            var dto = MapRefundDto(refund, includeInternalDetails: true);
            RecordAudit(actorId, role.Value, "DEPOSIT_REFUND_CONFIRM", "HoanCoc", refund.Id, request.RequestId, fingerprint, traceId, now, dto);

            await context.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);

            if (!string.IsNullOrWhiteSpace(booking.EmailSnapshot))
            {
                try
                {
                    await emailSender.SendRefundConfirmedAsync(
                        booking.EmailSnapshot, booking.TrackingCode, refund.Id, refund.SoTien, refund.HoanAt.Value, ct);
                }
                catch
                {
                    // Bỏ qua lỗi SMTP tạm thời sau khi đã commit giao dịch hoàn tiền
                }
            }

            return new CommandResult<RefundDto>(ApiResponse<RefundDto>.Ok(dto, "Xác nhận đã hoàn tiền cọc thành công."), IsReplay: false);
        }
        catch (DbUpdateException ex) when (UniqueIndex(ex) is AuditRequestIndex)
        {
            if (uploadedObjectPath is not null) await SafeDeleteStorageAsync(uploadedObjectPath);
            context.ChangeTracker.Clear();
            return await ReadReplayAsync<RefundDto>(actorId, "DEPOSIT_REFUND_CONFIRM", request.RequestId, fingerprint, "Xác nhận đã hoàn tiền cọc thành công.", ct)
                ?? FailCmd<RefundDto>("RequestId đã được sử dụng với dữ liệu khác.", "IDEMPOTENCY_CONFLICT");
        }
        catch
        {
            if (uploadedObjectPath is not null) await SafeDeleteStorageAsync(uploadedObjectPath);
            throw;
        }
    }

    // =========================================================================
    // E46: GET /api/admin/refunds
    // =========================================================================
    public async Task<ApiResponse<PageRefund>> GetRefundsAsync(
        BookingQuery query, int actorId, CancellationToken ct)
    {
        if (await GetActorRoleAsync(actorId, RefundPermission, ct) is null)
            return ApiResponse<PageRefund>.Fail("Không có quyền xem danh sách hoàn cọc.", "FORBIDDEN");

        var q = context.HoanCocSet.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.TrangThai))
        {
            if (!Enum.TryParse<RefundState>(query.TrangThai.Trim(), ignoreCase: false, out var state))
                return ApiResponse<PageRefund>.Fail("Trạng thái hoàn cọc không hợp lệ.", "VALIDATION_FAILED");
            q = q.Where(r => r.TrangThai == state);
        }

        var hcmOffset = TimeSpan.FromHours(7);
        if (query.TuNgay.HasValue)
        {
            var start = new DateTimeOffset(query.TuNgay.Value.ToDateTime(TimeOnly.MinValue), hcmOffset).ToUniversalTime();
            q = q.Where(r => r.CreatedAt >= start);
        }
        if (query.DenNgay.HasValue)
        {
            var endExclusive = new DateTimeOffset(query.DenNgay.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), hcmOffset).ToUniversalTime();
            q = q.Where(r => r.CreatedAt < endExclusive);
        }

        var total = await q.CountAsync(ct);
        var page = Math.Max(1, query.Trang);
        var pageSize = Math.Clamp(query.KichThuocTrang, 1, 100);

        var items = await q.OrderByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var dtos = items.Select(r => MapRefundDto(r, includeInternalDetails: true)).ToList();
        return ApiResponse<PageRefund>.Ok(new PageRefund(dtos, page, pageSize, total), "Lấy danh sách hoàn cọc thành công.");
    }

    // =========================================================================
    // Helpers nội bộ: Khóa thứ tự chuẩn, Idempotency (AuditLog), Permission & Mapping
    // =========================================================================
    private void ExpireInMemoryBookingDeposit(DonDatLich booking, TienCoc deposit, DateTimeOffset now)
    {
        foreach (var slot in context.ChangeTracker.Entries<KhungGioDatLich>().Select(e => e.Entity))
        {
            var petsInSlot = booking.ChiTietDonDonDatLichItems
                .Count(p => p.DichVuTrongDonChiTietDonItems.Any(s => s.TrangThai != OrderServiceState.HUY && s.KhungGioId == slot.Id));
            var minsInSlot = booking.ChiTietDonDonDatLichItems
                .SelectMany(p => p.DichVuTrongDonChiTietDonItems)
                .Where(s => s.TrangThai != OrderServiceState.HUY && s.KhungGioId == slot.Id)
                .Sum(s => s.ThoiLuongSnapshot);
            slot.SlotDaDat = Math.Max(0, slot.SlotDaDat - petsInSlot);
            slot.PhutDaGiu = Math.Max(0, slot.PhutDaGiu - minsInSlot);
            slot.UpdatedAt = now;
        }

        booking.TrangThai = BookingState.CANCELLED;
        booking.LoaiHuy = CancellationType.NO_SHOW;
        booking.LyDoHuy = "Quá hạn thanh toán tiền cọc 30 phút";
        booking.HuyAt = now;
        booking.HuyBoiId = null;
        booking.UpdatedAt = now;

        deposit.TrangThaiYeuCau = DepositRequestState.HET_HAN;
        deposit.KetThucAt = now;
        deposit.UpdatedAt = now;
    }

    private async Task LockBookingGraphForDepositAsync(int bookingId, bool lockRefunds, bool lockInvoices, CancellationToken ct)
    {
        var isRelational = context.Database.IsRelational();

        if (isRelational)
        {
            await context.Database.SqlQuery<int>(
                $"SELECT id AS \"Value\" FROM don_dat_lich WHERE id = {bookingId} FOR UPDATE").ToListAsync(ct);
        }

        var intakeSlots = await context.DonDatLichSet.AsNoTracking()
            .Where(b => b.Id == bookingId).Select(b => b.KhungGioId).ToArrayAsync(ct);
        var serviceSlots = await context.DichVuTrongDonSet.AsNoTracking()
            .Where(s => s.ChiTietDon.DonDatLichId == bookingId).Select(s => s.KhungGioId).ToArrayAsync(ct);
        var slots = intakeSlots.Concat(serviceSlots).Distinct().Order().ToArray();

        if (slots.Length > 0)
        {
            if (isRelational)
            {
                await context.Database.SqlQuery<int>(
                    $"SELECT id AS \"Value\" FROM khung_gio_dat_lich WHERE id = ANY({slots}) ORDER BY id FOR UPDATE").ToListAsync(ct);
            }
            await context.KhungGioDatLichSet.Where(s => slots.Contains(s.Id)).LoadAsync(ct);
        }

        if (!isRelational) return;

        var rooms = await context.ChiTietDonSet.AsNoTracking()
            .Where(p => p.DonDatLichId == bookingId && p.LoaiPhongId.HasValue)
            .Select(p => p.LoaiPhongId!.Value).Distinct().Order().ToArrayAsync(ct);

        if (rooms.Length > 0)
        {
            await new HotelCapacityCalculator(context).LockRoomTypesAsync(rooms, ct);
        }

        await context.Database.SqlQuery<int>(
            $"SELECT id AS \"Value\" FROM tien_coc WHERE don_dat_lich_id = {bookingId} ORDER BY id FOR UPDATE").ToListAsync(ct);

        if (lockRefunds)
        {
            await context.Database.SqlQuery<int>(
                $"SELECT id AS \"Value\" FROM hoan_coc WHERE tien_coc_id IN (SELECT id FROM tien_coc WHERE don_dat_lich_id = {bookingId}) ORDER BY id FOR UPDATE").ToListAsync(ct);
        }

        if (lockInvoices)
        {
            await context.Database.SqlQuery<int>(
                $"SELECT id AS \"Value\" FROM hoa_don WHERE don_dat_lich_id = {bookingId} ORDER BY id FOR UPDATE").ToListAsync(ct);
        }
    }

    private Task<DonDatLich?> LoadFullBookingForDepositAsync(int bookingId, CancellationToken ct) =>
        BookingProjection.IncludeDetails(context.DonDatLichSet)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

    private Task<Role?> GetActorRoleAsync(int actorId, string permission, CancellationToken ct) =>
        context.NhanVienSet.AsNoTracking()
            .Where(n => n.Id == actorId && n.IsActive
                && (permission != RefundPermission || n.VaiTro == Role.ADMIN)
                && n.NhanVienQuyenNhanVienItems.Any(q => q.MaQuyen == permission))
            .Select(n => (Role?)n.VaiTro)
            .SingleOrDefaultAsync(ct);

    private async Task<CommandResult<T>?> ReadReplayAsync<T>(
        int actorId, string operation, Guid requestId, string fingerprint, string successMessage, CancellationToken ct)
    {
        var audit = await context.AuditLogSet.AsNoTracking().SingleOrDefaultAsync(
            a => a.ActorId == actorId && a.HanhDong == operation && a.RequestId == requestId, ct);
        if (audit is null)
            return null;

        if (audit.Fingerprint != fingerprint)
            return FailCmd<T>("RequestId đã được sử dụng với dữ liệu khác.", "IDEMPOTENCY_CONFLICT");

        var dto = JsonSerializer.Deserialize<T>(audit.KetQua!, JsonOptions)!;
        return new CommandResult<T>(ApiResponse<T>.Ok(dto, successMessage), IsReplay: true);
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

    private static string ComputeFingerprint<T>(T payload) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions)));

    private static bool IsMoney(decimal value) => value is >= 0 and <= MaximumMoney && value == decimal.Truncate(value);

    private static string? UniqueIndex(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
            ? postgres.ConstraintName : null;

    private static DepositDto MapDepositDto(DonDatLich booking, TienCoc deposit)
    {
        var amounts = DepositAccounting.Calculate(booking);
        var refunds = deposit.HoanCocTienCocItems
            .OrderBy(x => x.Id)
            .Select(x => MapRefundDto(x, includeInternalDetails: true))
            .ToArray();

        return new DepositDto(
            deposit.Id,
            booking.Id,
            deposit.TrangThaiYeuCau.ToString(),
            deposit.CoSoTinhSnapshot,
            deposit.TyLe,
            deposit.SoTienYeuCau,
            deposit.HetHanAt,
            amounts.ThucNhan,
            amounts.KhauTru,
            amounts.GiuLai,
            amounts.DanhHoan,
            amounts.DaHoan,
            refunds);
    }

    private static RefundDto MapRefundDto(HoanCoc refund, bool includeInternalDetails) =>
        new(
            refund.Id,
            refund.TienCocId,
            refund.SoTien,
            refund.LyDo,
            refund.TrangThai.ToString(),
            refund.HoanAt,
            includeInternalDetails ? MaskReference(refund.ThamChieu) : null);

    private static string? MaskReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;
        if (reference.Length <= 4) return "***";
        var tail = reference[^4..];
        return tail.All(char.IsAsciiLetterOrDigit) ? "***" + tail : "***";
    }

    private async Task SafeDeleteStorageAsync(string objectPath)
    {
        try { await fileStorage.DeleteAsync(objectPath); } catch { /* ignore cleanup error */ }
    }

    private static CommandResult<T> FailCmd<T>(string message, string errorCode) =>
        new(ApiResponse<T>.Fail(message, errorCode, []), IsReplay: false);
}
