using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PetSpa.Api.Data;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Billing;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Booking;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Enums;
using PetSpa.Core.Interfaces.Billing;

namespace PetSpa.Api.Services.Billing;

public interface IInvoiceService
{
    Task<(ApiResponse<InvoiceResponseDto> Response, bool IsReplay)> CreateInvoiceAsync(
        int bookingId, CreateInvoiceCommand request, int actorId, string traceId, CancellationToken cancellationToken);
    Task<ApiResponse<InvoiceResponseDto>> GetInvoiceByIdAsync(int invoiceId, int actorId, CancellationToken cancellationToken);
    Task<ApiResponse<InvoiceResponseDto>> CheckoutInvoiceAsync(
        int invoiceId, CheckoutRequestDto request, int actorId, string traceId, CancellationToken cancellationToken);
    Task<ApiResponse<InvoiceResponseDto>> WithdrawInvoiceAsync(
        int invoiceId, ReasonCommand request, int actorId, string traceId, CancellationToken cancellationToken);
}

public class InvoiceService(
    PetSpaDbContext context,
    IConfiguration configuration,
    IBillingSettlementHelper billingSettlement,
    TimeProvider clock) : IInvoiceService
{
    private const string CreatePermission = "INVOICE_CREATE";
    private const string CollectPermission = "INVOICE_COLLECT";
    private const string RevokePermission = "INVOICE_REVOKE";
    private const string AuditRequestIndex = "ix_audit_log_actor_id_hanh_dong_request_id";
    private const string ReceiptRequestIndex = "ix_giao_dich_thu_request_id";
    private const decimal MaximumMoney = 99_999_999_999_999m;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // =========================================================================
    // E23: POST /api/internal/bookings/{id}/invoices (Mở rộng G5)
    // =========================================================================
    public async Task<(ApiResponse<InvoiceResponseDto> Response, bool IsReplay)> CreateInvoiceAsync(
        int bookingId, CreateInvoiceCommand request, int actorId, string traceId, CancellationToken cancellationToken)
    {
        if (bookingId <= 0 || request.RequestId == Guid.Empty)
            return (Failure("Dữ liệu lập hóa đơn không hợp lệ.", "VALIDATION_FAILED"), false);

        if (await GetActorRoleAsync(actorId, CreatePermission, cancellationToken) is null)
            return (Forbidden(), false);

        var fingerprint = Fingerprint(new { bookingId });
        await using var transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;
        await LockBookingGraphAsync(bookingId, lockDeposit: true, lockInvoices: true, cancellationToken);

        var role = await GetActorRoleAsync(actorId, CreatePermission, cancellationToken);
        if (role is null)
            return (Forbidden(), false);

        var replay = await ReadReplayAsync(actorId, CreatePermission, request.RequestId, fingerprint, cancellationToken);
        if (replay is not null)
            return (replay, true);

        var booking = await context.DonDatLichSet.AsSplitQuery()
            .Include(b => b.KhungGio)
            .Include(b => b.ChiTietDonDonDatLichItems).ThenInclude(p => p.LoaiPhong)
            .Include(b => b.ChiTietDonDonDatLichItems).ThenInclude(p => p.DichVuTrongDonChiTietDonItems)
                .ThenInclude(s => s.ThanhPhanDichVuTrongDonDichVuTrongDonItems)
            .Include(b => b.PhuPhiDonDatLichItems)
            .Include(b => b.TienCocDonDatLich).ThenInclude(d => d!.GiaoDichThuTienCocItems)
            .Include(b => b.TienCocDonDatLich).ThenInclude(d => d!.HoanCocTienCocItems)
            .Include(b => b.HoaDonDonDatLichItems)
            .FirstOrDefaultAsync(b => b.Id == bookingId, cancellationToken);

        if (booking is null)
            return (Failure("Không tìm thấy đơn đặt lịch.", "NOT_FOUND"), false);

        if (booking.HoaDonDonDatLichItems.Any(i => i.TrangThai != InvoiceState.REVOKED))
            return (Failure("Đơn đặt lịch đã có hóa đơn hiệu lực.", "INVALID_TRANSITION"), false);

        // Guard: Mọi pet phải ở READY, DONE hoặc CANCELLED
        if (booking.ChiTietDonDonDatLichItems.Any(p =>
                p.TrangThaiChamSoc is not (PetCareState.READY or PetCareState.DONE or PetCareState.CANCELLED)))
        {
            return (Failure("Đơn đặt lịch còn thú cưng chưa hoàn tất chăm sóc để lập hóa đơn.", "INVALID_TRANSITION"), false);
        }

        var now = clock.GetUtcNow();
        var lines = new List<CtHoaDon>();

        foreach (var pet in booking.ChiTietDonDonDatLichItems.OrderBy(p => p.Id))
        {
            var isCancelledPet = pet.TrangThaiChamSoc == PetCareState.CANCELLED;

            // 1. Các dòng dịch vụ SPA / COMBO
            foreach (var service in pet.DichVuTrongDonChiTietDonItems.OrderBy(s => s.Id))
            {
                if (!isCancelledPet)
                {
                    if (service.TrangThai == OrderServiceState.HUY)
                        continue;

                    if (service.TrangThai != OrderServiceState.HOAN_TAT || !service.GiaChot.HasValue)
                        return (Failure("Dịch vụ chưa hoàn tất hoặc chưa có giá chốt.", "INVALID_TRANSITION"), false);

                    lines.Add(new CtHoaDon
                    {
                        LoaiKhoan = "SPA",
                        TenPetSnapshot = pet.TenPetSnapshot,
                        TenKhoanMuc = service.TenSnapshot,
                        SizeSnapshot = service.SizeChot ?? pet.SizeChot,
                        SoLuong = 1,
                        DonGiaSnapshot = service.GiaChot.Value,
                        ThanhTien = service.GiaChot.Value,
                        DienGiai = service.LyDo,
                        ChiTietDonId = pet.Id,
                        DichVuTrongDonId = service.Id,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                }
                else
                {
                    // Pet bị hủy riêng (E47): chỉ lập dòng cho phần dịch vụ/thành phần đã HOAN_TAT và có GiaChot > 0
                    if (service.TrangThai != OrderServiceState.HOAN_TAT || !service.GiaChot.HasValue || service.GiaChot.Value <= 0)
                        continue;

                    var note = service.LyDo;
                    if (service.ComboId.HasValue)
                    {
                        var doneParts = service.ThanhPhanDichVuTrongDonDichVuTrongDonItems
                            .Where(part => part.TrangThai == OrderServiceState.HOAN_TAT)
                            .OrderBy(part => part.Id)
                            .Select(part => part.TenSnapshot)
                            .ToList();
                        if (doneParts.Count > 0)
                        {
                            note = $"Thành phần combo đã làm: {string.Join(", ", doneParts)}";
                        }
                    }

                    lines.Add(new CtHoaDon
                    {
                        LoaiKhoan = "SPA",
                        TenPetSnapshot = pet.TenPetSnapshot,
                        TenKhoanMuc = service.TenSnapshot,
                        SizeSnapshot = service.SizeChot ?? pet.SizeChot,
                        SoLuong = 1,
                        DonGiaSnapshot = service.GiaChot.Value,
                        ThanhTien = service.GiaChot.Value,
                        DienGiai = note,
                        ChiTietDonId = pet.Id,
                        DichVuTrongDonId = service.Id,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                }
            }

            // 2. Dòng lưu trú HOTEL (M2-L01) - không tạo dòng có SoLuong == 0 để bảo toàn ck_ct_hoa_don_so_luong
            if (pet.LoaiPhongId.HasValue)
            {
                var roomPrice = pet.GiaPhongChot ?? pet.GiaPhongCamKet ?? pet.LoaiPhong?.GiaMoiDem ?? 0m;
                if (!isCancelledPet)
                {
                    var nights = pet.SoDemChot ?? 0;
                    if (nights <= 0 || roomPrice <= 0)
                        return (Failure("Thông tin số đêm hoặc đơn giá phòng lưu trú chưa hợp lệ.", "INVALID_TRANSITION"), false);

                    var roomName = pet.LoaiPhong is not null ? $"Lưu trú {pet.LoaiPhong.TenPhong}" : "Lưu trú Hotel";
                    lines.Add(new CtHoaDon
                    {
                        LoaiKhoan = "HOTEL",
                        TenPetSnapshot = pet.TenPetSnapshot,
                        TenKhoanMuc = roomName,
                        SoLuong = nights,
                        DonGiaSnapshot = roomPrice,
                        ThanhTien = roomPrice * nights,
                        ChiTietDonId = pet.Id,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                }
                else if (pet.SoDemDaO is > 0)
                {
                    var nightsStayed = pet.SoDemDaO.Value;
                    if (roomPrice <= 0)
                        return (Failure("Đơn giá phòng lưu trú chưa hợp lệ.", "INVALID_TRANSITION"), false);

                    var roomName = pet.LoaiPhong is not null ? $"Lưu trú {pet.LoaiPhong.TenPhong}" : "Lưu trú Hotel";
                    lines.Add(new CtHoaDon
                    {
                        LoaiKhoan = "HOTEL",
                        TenPetSnapshot = pet.TenPetSnapshot,
                        TenKhoanMuc = roomName,
                        SoLuong = nightsStayed,
                        DonGiaSnapshot = roomPrice,
                        ThanhTien = roomPrice * nightsStayed,
                        DienGiai = $"Lưu trú thực tế {nightsStayed} đêm trước khi hủy",
                        ChiTietDonId = pet.Id,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                }
            }
        }

        // 3. Các dòng PHU_PHI (M2-L09: phân biệt phụ phí riêng từng pet vs phí chung toàn đơn)
        var petLookup = booking.ChiTietDonDonDatLichItems.ToDictionary(p => p.Id, p => p.TenPetSnapshot);
        foreach (var fee in booking.PhuPhiDonDatLichItems.Where(p => p.HuyAt is null).OrderBy(p => p.Id))
        {
            string? petNameSnapshot = null;
            var description = fee.LyDo;
            if (fee.ChiTietDonId.HasValue && petLookup.TryGetValue(fee.ChiTietDonId.Value, out var name))
            {
                petNameSnapshot = name;
            }
            else if (!fee.ChiTietDonId.HasValue)
            {
                description = $"Phí chung đơn: {fee.LyDo}";
            }

            lines.Add(new CtHoaDon
            {
                LoaiKhoan = "PHU_PHI",
                TenPetSnapshot = petNameSnapshot,
                TenKhoanMuc = fee.TenPhuPhi,
                SoLuong = 1,
                DonGiaSnapshot = fee.SoTien,
                ThanhTien = fee.SoTien,
                DienGiai = description,
                ChiTietDonId = fee.ChiTietDonId,
                PhuPhiId = fee.Id,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        var total = lines.Sum(line => line.ThanhTien);
        if (lines.Count == 0 || total is <= 0 or > MaximumMoney)
            return (Failure("Đơn đặt lịch chưa có khoản thu hợp lệ để lập hóa đơn.", "INVALID_TRANSITION"), false);

        // 4. Khấu trừ tiền cọc khả dụng & xử lý Zero-due
        var depositDeduction = 0m;
        if (booking.TienCocDonDatLich is { } deposit)
        {
            var received = deposit.GiaoDichThuTienCocItems.Sum(x => x.SoTien);
            var reservedRefunds = deposit.HoanCocTienCocItems.Sum(x => x.SoTien);
            var availableDeposit = Math.Max(0m, received - reservedRefunds);
            depositDeduction = Math.Min(total, availableDeposit);
        }
        var remainingDue = total - depositDeduction;

        // 5. Liên kết hóa đơn thay thế (M2-L04) nếu có bản REVOKED chưa bị thay thế
        var replacedIds = booking.HoaDonDonDatLichItems
            .Where(h => h.ThayTheHoaDonId.HasValue)
            .Select(h => h.ThayTheHoaDonId!.Value)
            .ToHashSet();

        var latestUnreplacedRevoked = booking.HoaDonDonDatLichItems
            .Where(h => h.TrangThai == InvoiceState.REVOKED && !replacedIds.Contains(h.Id))
            .OrderByDescending(h => h.Id)
            .FirstOrDefault();

        var isZeroDue = remainingDue == 0m;
        var invoice = new HoaDon
        {
            MaHoaDon = $"INV-{now:yyyyMMdd}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(2))}",
            TrangThai = isZeroDue ? InvoiceState.PAID : InvoiceState.UNPAID,
            TongTien = total,
            CocKhauTru = depositDeduction,
            ConPhaiTra = remainingDue,
            DonDatLichId = bookingId,
            ThayTheHoaDonId = latestUnreplacedRevoked?.Id,
            TaoBoiId = actorId,
            PaidAt = isZeroDue ? now : null,
            ThuBoiId = isZeroDue ? actorId : null,
            KhachSnapshot = JsonSerializer.Serialize(new InvoiceContact(
                booking.TenNguoiDat, booking.SoDienThoaiSnapshot, booking.EmailSnapshot), JsonOptions),
            CreatedAt = now,
            UpdatedAt = now,
            CtHoaDonHoaDonItems = lines
        };
        context.HoaDonSet.Add(invoice);

        // Nếu cọc bao phủ 100% (conPhaiTra == 0), kiểm tra khép vòng đời đơn ngay
        if (isZeroDue)
        {
            billingSettlement.TryTransitionBookingTerminalState(booking, now);
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            var dto = (await LoadInvoiceAsync(invoice.Id, cancellationToken))!;
            RecordAudit(actorId, role.Value, CreatePermission, invoice.Id, request.RequestId, fingerprint, traceId, now, dto);
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);

            return (ApiResponse<InvoiceResponseDto>.Ok(dto, "Lập hóa đơn thành công."), false);
        }
        catch (DbUpdateException ex) when (UniqueIndex(ex) is
            AuditRequestIndex or "ix_hoa_don_don_dat_lich_id" or "ix_hoa_don_ma_hoa_don")
        {
            if (transaction is not null)
                await transaction.RollbackAsync(cancellationToken);
            context.ChangeTracker.Clear();
            return UniqueIndex(ex) switch
            {
                AuditRequestIndex => (await ReadReplayAsync(actorId, CreatePermission, request.RequestId,
                    fingerprint, cancellationToken) ?? IdempotencyConflict(), true),
                "ix_hoa_don_don_dat_lich_id" => (Failure("Đơn đặt lịch đã có hóa đơn hiệu lực.", "INVALID_TRANSITION"), false),
                _ => (Failure("Tạm thời không thể cấp số hóa đơn.", "TEMPORARY_FAILURE"), false)
            };
        }
    }

    // =========================================================================
    // E24: GET /api/internal/invoices/{id}
    // =========================================================================
    public async Task<ApiResponse<InvoiceResponseDto>> GetInvoiceByIdAsync(
        int invoiceId, int actorId, CancellationToken cancellationToken)
    {
        if (invoiceId <= 0)
            return Failure("ID hóa đơn không hợp lệ.", "VALIDATION_FAILED");
        if (await GetActorRoleAsync(actorId, CreatePermission, cancellationToken) is null)
            return Forbidden();

        var invoice = await LoadInvoiceAsync(invoiceId, cancellationToken);
        return invoice is null
            ? Failure("Không tìm thấy hóa đơn.", "NOT_FOUND")
            : ApiResponse<InvoiceResponseDto>.Ok(invoice);
    }

    // =========================================================================
    // E26: POST /api/internal/invoices/{id}/checkout (Cập nhật khép đơn M2-05)
    // =========================================================================
    public async Task<ApiResponse<InvoiceResponseDto>> CheckoutInvoiceAsync(
        int invoiceId, CheckoutRequestDto request, int actorId, string traceId, CancellationToken cancellationToken)
    {
        var received = request.SoTienThucNhan;
        var change = request.SoTienDaTraLai;
        var reference = request.ThamChieu?.Trim();
        var evidence = request.BangChungTraThua?.Trim();
        if (invoiceId <= 0 || request.RequestId == Guid.Empty || !Enum.IsDefined(request.PhuongThuc)
            || !IsMoney(received) || !IsMoney(change)
            || reference is { Length: 0 or > 150 } || evidence is { Length: 0 or > 2000 }
            || (change > 0 && evidence is null))
            return Failure("Dữ liệu thu tiền hoặc bằng chứng trả lại không hợp lệ.", "VALIDATION_FAILED");

        if (await GetActorRoleAsync(actorId, CollectPermission, cancellationToken) is null)
            return Forbidden();

        var cashAudit = new
        {
            invoiceId,
            phuongThuc = request.PhuongThuc.ToString(),
            soTienThucNhan = (long)received,
            soTienDaTraLai = (long)change,
            thamChieu = reference,
            bangChungTraThua = evidence
        };
        var fingerprint = Fingerprint(cashAudit);
        var bookingId = await context.HoaDonSet.AsNoTracking().Where(i => i.Id == invoiceId)
            .Select(i => (int?)i.DonDatLichId).SingleOrDefaultAsync(cancellationToken);

        await using var transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;
        if (bookingId is not null)
            await LockBookingGraphAsync(bookingId.Value, lockDeposit: true, lockInvoices: true, cancellationToken);

        var role = await GetActorRoleAsync(actorId, CollectPermission, cancellationToken);
        if (role is null)
            return Forbidden();

        var replay = await ReadReplayAsync(actorId, CollectPermission, request.RequestId, fingerprint, cancellationToken);
        if (replay is not null)
            return replay;

        if (bookingId is null)
            return Failure("Không tìm thấy hóa đơn.", "NOT_FOUND");

        var booking = await context.DonDatLichSet.AsSplitQuery()
            .Include(b => b.ChiTietDonDonDatLichItems)
            .Include(b => b.TienCocDonDatLich).ThenInclude(d => d!.GiaoDichThuTienCocItems)
            .Include(b => b.TienCocDonDatLich).ThenInclude(d => d!.HoanCocTienCocItems)
            .Include(b => b.HoaDonDonDatLichItems)
            .FirstOrDefaultAsync(b => b.Id == bookingId.Value, cancellationToken);

        var invoice = booking?.HoaDonDonDatLichItems.FirstOrDefault(i => i.Id == invoiceId);
        if (booking is null || invoice is null)
            return Failure("Không tìm thấy hóa đơn.", "NOT_FOUND");
        if (invoice.TrangThai != InvoiceState.UNPAID || invoice.ConPhaiTra <= 0)
            return Failure("Hóa đơn không còn khoản phải thu hợp lệ.", "INVALID_TRANSITION");

        var netAmount = received - change;
        if (netAmount != invoice.ConPhaiTra)
            return Failure("Số tiền thu không khớp với số tiền cần thanh toán.", "AMOUNT_MISMATCH");

        var now = clock.GetUtcNow();
        invoice.TrangThai = InvoiceState.PAID;
        invoice.PaidAt = now;
        invoice.ThuBoiId = actorId;
        invoice.UpdatedAt = now;

        context.GiaoDichThuSet.Add(new GiaoDichThu
        {
            RequestId = request.RequestId,
            SoTien = netAmount,
            PhuongThuc = request.PhuongThuc,
            MaGiaoDich = reference,
            GiaoDichAt = now,
            XacNhanAt = now,
            HoaDonId = invoiceId,
            XacNhanBoiId = actorId,
            CreatedAt = now,
            UpdatedAt = now
        });

        // Khép đơn M2-05 nếu toàn bộ pet đã CANCELLED và đã trả khách
        billingSettlement.TryTransitionBookingTerminalState(booking, now);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            var dto = (await LoadInvoiceAsync(invoiceId, cancellationToken))!;
            RecordAudit(actorId, role.Value, CollectPermission, invoiceId, request.RequestId, fingerprint, traceId,
                now, dto, JsonSerializer.Serialize(cashAudit, JsonOptions));
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);

            return ApiResponse<InvoiceResponseDto>.Ok(dto, "Thanh toán hóa đơn thành công.");
        }
        catch (DbUpdateException ex) when (UniqueIndex(ex) is AuditRequestIndex or ReceiptRequestIndex)
        {
            if (transaction is not null)
                await transaction.RollbackAsync(cancellationToken);
            context.ChangeTracker.Clear();
            return await ReadReplayAsync(actorId, CollectPermission, request.RequestId, fingerprint, cancellationToken)
                ?? IdempotencyConflict();
        }
    }

    // =========================================================================
    // E25: POST /api/internal/invoices/{id}/withdraw (Thu hồi hóa đơn UNPAID)
    // =========================================================================
    public async Task<ApiResponse<InvoiceResponseDto>> WithdrawInvoiceAsync(
        int invoiceId, ReasonCommand request, int actorId, string traceId, CancellationToken cancellationToken)
    {
        if (invoiceId <= 0 || request.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(request.LyDo))
            return Failure("Dữ liệu thu hồi hóa đơn không hợp lệ.", "VALIDATION_FAILED");

        if (await GetActorRoleAsync(actorId, RevokePermission, cancellationToken) is null)
            return Forbidden();

        var reasonNorm = request.LyDo.Trim();
        var fingerprint = Fingerprint(new { invoiceId, lyDo = reasonNorm });

        // Đọc DonDatLichId trước khi khóa để tránh AB-BA deadlock
        var bookingId = await context.HoaDonSet.AsNoTracking()
            .Where(i => i.Id == invoiceId)
            .Select(i => (int?)i.DonDatLichId)
            .SingleOrDefaultAsync(cancellationToken);

        if (bookingId is null)
            return Failure("Không tìm thấy hóa đơn.", "NOT_FOUND");

        await using var transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        // Khóa đúng thứ tự: don_dat_lich -> tien_coc -> hoa_don
        await LockBookingGraphAsync(bookingId.Value, lockDeposit: true, lockInvoices: true, cancellationToken);

        var role = await GetActorRoleAsync(actorId, RevokePermission, cancellationToken);
        if (role is null)
            return Forbidden();

        var replay = await ReadReplayAsync(actorId, RevokePermission, request.RequestId, fingerprint, cancellationToken);
        if (replay is not null)
            return replay;

        var invoice = await context.HoaDonSet
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.DonDatLichId == bookingId.Value, cancellationToken);

        if (invoice is null)
            return Failure("Không tìm thấy hóa đơn.", "NOT_FOUND");

        if (invoice.TrangThai != InvoiceState.UNPAID)
            return Failure("Chỉ có thể thu hồi hóa đơn đang ở trạng thái UNPAID.", "INVALID_TRANSITION");

        var now = clock.GetUtcNow();
        // Giữ nguyên TongTien, CocKhauTru, ConPhaiTra & CtHoaDon để tuân thủ ck_hoa_don_tong_tien và bất biến lịch sử
        invoice.TrangThai = InvoiceState.REVOKED;
        invoice.ThuHoiBoiId = actorId;
        invoice.ThuHoiAt = now;
        invoice.LyDoThuHoi = reasonNorm;
        invoice.UpdatedAt = now;

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            var dto = (await LoadInvoiceAsync(invoiceId, cancellationToken))!;
            RecordAudit(actorId, role.Value, RevokePermission, invoiceId, request.RequestId, fingerprint, traceId, now, dto);
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);

            return ApiResponse<InvoiceResponseDto>.Ok(dto, "Thu hồi hóa đơn thành công.");
        }
        catch (DbUpdateException ex) when (UniqueIndex(ex) is AuditRequestIndex)
        {
            if (transaction is not null)
                await transaction.RollbackAsync(cancellationToken);
            context.ChangeTracker.Clear();
            return await ReadReplayAsync(actorId, RevokePermission, request.RequestId, fingerprint, cancellationToken)
                ?? IdempotencyConflict();
        }
    }

    // =========================================================================
    // Helpers nội bộ
    // =========================================================================
    private async Task LockBookingGraphAsync(int bookingId, bool lockDeposit, bool lockInvoices, CancellationToken cancellationToken)
    {
        if (!context.Database.IsRelational()) return;

        await context.Database.SqlQuery<int>(
            $"SELECT id AS \"Value\" FROM don_dat_lich WHERE id = {bookingId} FOR UPDATE").ToListAsync(cancellationToken);

        if (lockDeposit)
        {
            await context.Database.SqlQuery<int>(
                $"SELECT id AS \"Value\" FROM tien_coc WHERE don_dat_lich_id = {bookingId} ORDER BY id FOR UPDATE").ToListAsync(cancellationToken);
        }

        if (lockInvoices)
        {
            await context.Database.SqlQuery<int>(
                $"SELECT id AS \"Value\" FROM hoa_don WHERE don_dat_lich_id = {bookingId} ORDER BY id FOR UPDATE").ToListAsync(cancellationToken);
        }
    }

    private Task<Role?> GetActorRoleAsync(int actorId, string permission, CancellationToken cancellationToken) =>
        context.NhanVienSet.AsNoTracking()
            .Where(n => n.Id == actorId && n.IsActive && n.NhanVienQuyenNhanVienItems.Any(q => q.MaQuyen == permission))
            .Select(n => (Role?)n.VaiTro).SingleOrDefaultAsync(cancellationToken);

    private async Task<InvoiceResponseDto?> LoadInvoiceAsync(int invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await context.HoaDonSet.AsNoTracking()
            .Include(i => i.TaoBoi)
            .Include(i => i.DonDatLich).ThenInclude(b => b.KhungGio)
            .Include(i => i.CtHoaDonHoaDonItems)
            .SingleOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
        return invoice is null ? null : InvoiceProjection.Project(invoice, configuration, includeInternalDetails: true);
    }

    private async Task<ApiResponse<InvoiceResponseDto>?> ReadReplayAsync(
        int actorId, string operation, Guid requestId, string fingerprint, CancellationToken cancellationToken)
    {
        var audit = await context.AuditLogSet.AsNoTracking().SingleOrDefaultAsync(
            a => a.ActorId == actorId && a.HanhDong == operation && a.RequestId == requestId, cancellationToken);
        if (audit is null)
            return null;
        if (audit.Fingerprint != fingerprint)
            return IdempotencyConflict();

        var invoice = JsonSerializer.Deserialize<InvoiceResponseDto>(audit.KetQua!, JsonOptions)!;
        if (operation == CreatePermission && configuration["Billing:VietQrImageUrl"] is not null)
            invoice = invoice with { Qr = (await LoadInvoiceAsync(invoice.MaHoaDon, cancellationToken))?.Qr };

        var message = operation switch
        {
            CreatePermission => "Lập hóa đơn thành công.",
            CollectPermission => "Thanh toán hóa đơn thành công.",
            RevokePermission => "Thu hồi hóa đơn thành công.",
            _ => "Thành công"
        };
        return ApiResponse<InvoiceResponseDto>.Ok(invoice, message);
    }

    private void RecordAudit(
        int actorId, Role role, string operation, int invoiceId, Guid requestId, string fingerprint, string traceId,
        DateTimeOffset now, InvoiceResponseDto response, string? details = null)
    {
        var safeTraceId = traceId[..Math.Min(40, traceId.Length)];
        context.AuditLogSet.Add(new AuditLog
        {
            ActorId = actorId,
            ActorLoai = role.ToString(),
            HanhDong = operation,
            LoaiDoiTuong = "HoaDon",
            DoiTuongId = invoiceId,
            RequestId = requestId,
            Fingerprint = fingerprint,
            TraceId = safeTraceId,
            KetQua = JsonSerializer.Serialize(response with { Qr = null }, JsonOptions),
            Sau = details,
            CreatedAt = now
        });
    }

    private static string Fingerprint<T>(T payload) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions)));

    private static bool IsMoney(decimal value) => value is >= 0 and <= MaximumMoney && value == decimal.Truncate(value);

    private static string? UniqueIndex(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
            ? postgres.ConstraintName : null;

    private static ApiResponse<InvoiceResponseDto> Forbidden() =>
        Failure("Nhân viên không có quyền thực hiện thao tác hóa đơn.", "FORBIDDEN");

    private static ApiResponse<InvoiceResponseDto> IdempotencyConflict() =>
        Failure("RequestId đã được sử dụng với dữ liệu khác.", "IDEMPOTENCY_CONFLICT");

    private static ApiResponse<InvoiceResponseDto> Failure(string message, string code) =>
        ApiResponse<InvoiceResponseDto>.Fail(message, code, []);
}