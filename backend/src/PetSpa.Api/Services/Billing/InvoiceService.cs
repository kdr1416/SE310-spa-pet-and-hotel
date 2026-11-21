using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PetSpa.Api.Data;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Billing;
using PetSpa.Core.Entities.Billing;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Enums;

namespace PetSpa.Api.Services.Billing;

public interface IInvoiceService
{
    Task<(ApiResponse<InvoiceResponseDto> Response, bool IsReplay)> CreateInvoiceAsync(
        int bookingId, CreateInvoiceCommand request, int actorId, string traceId, CancellationToken cancellationToken);
    Task<ApiResponse<InvoiceResponseDto>> GetInvoiceByIdAsync(int invoiceId, int actorId, CancellationToken cancellationToken);
    Task<ApiResponse<InvoiceResponseDto>> CheckoutInvoiceAsync(
        int invoiceId, CheckoutRequestDto request, int actorId, string traceId, CancellationToken cancellationToken);
}

public class InvoiceService(PetSpaDbContext context, IConfiguration configuration) : IInvoiceService
{
    private const string CreatePermission = "INVOICE_CREATE";
    private const string CollectPermission = "INVOICE_COLLECT";
    private const string AuditRequestIndex = "ix_audit_log_actor_id_hanh_dong_request_id";
    private const string ReceiptRequestIndex = "ix_giao_dich_thu_request_id";
    private const decimal MaximumMoney = 99_999_999_999_999m;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
        await LockBookingAsync(bookingId, cancellationToken);

        var role = await GetActorRoleAsync(actorId, CreatePermission, cancellationToken);
        if (role is null)
            return (Forbidden(), false);

        var replay = await ReadReplayAsync(actorId, CreatePermission, request.RequestId, fingerprint, cancellationToken);
        if (replay is not null)
            return (replay, true);

        var booking = await context.DonDatLichSet.AsNoTracking().AsSplitQuery()
            .Include(b => b.ChiTietDonDonDatLichItems).ThenInclude(p => p.DichVuTrongDonChiTietDonItems)
            .Include(b => b.PhuPhiDonDatLichItems)
            .Include(b => b.TienCocDonDatLich)
            .Include(b => b.HoaDonDonDatLichItems)
            .FirstOrDefaultAsync(b => b.Id == bookingId, cancellationToken);
        if (booking is null)
            return (Failure("Không tìm thấy đơn đặt lịch.", "NOT_FOUND"), false);

        if (booking.HoaDonDonDatLichItems.Any(i => i.TrangThai != InvoiceState.REVOKED))
            return (Failure("Đơn đặt lịch đã có hóa đơn hiệu lực.", "INVALID_TRANSITION"), false);

        if (booking.ChiTietDonDonDatLichItems.Any(p =>
                p.TrangThaiChamSoc is not (PetCareState.READY or PetCareState.DONE or PetCareState.CANCELLED)
                || p.LoaiPhongId.HasValue))
            return (Failure("Đơn Spa chưa đủ điều kiện lập hóa đơn.", "INVALID_TRANSITION"), false);

        var now = DateTimeOffset.UtcNow;
        var lines = new List<CtHoaDon>();
        foreach (var pet in booking.ChiTietDonDonDatLichItems)
        {
            foreach (var service in pet.DichVuTrongDonChiTietDonItems.Where(s => s.TrangThai != OrderServiceState.HUY))
            {
                if (service.TrangThai != OrderServiceState.HOAN_TAT || !service.GiaChot.HasValue
                    || service.ComboId.HasValue)
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
        }

        foreach (var fee in booking.PhuPhiDonDatLichItems.Where(p => p.HuyAt is null))
        {
            lines.Add(new CtHoaDon
            {
                LoaiKhoan = "PHU_PHI",
                TenKhoanMuc = fee.TenPhuPhi,
                SoLuong = 1,
                DonGiaSnapshot = fee.SoTien,
                ThanhTien = fee.SoTien,
                DienGiai = fee.LyDo,
                PhuPhiId = fee.Id,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        var total = lines.Sum(line => line.ThanhTien);
        if (lines.Count == 0 || total is <= 0 or > MaximumMoney || booking.TienCocDonDatLich is not null)
            return (Failure("Đơn Spa chưa có khoản thu hợp lệ.", "INVALID_TRANSITION"), false);

        var invoice = new HoaDon
        {
            MaHoaDon = $"INV-{now:yyyyMMdd}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(2))}",
            TrangThai = InvoiceState.UNPAID,
            TongTien = total,
            ConPhaiTra = total,
            DonDatLichId = bookingId,
            TaoBoiId = actorId,
            KhachSnapshot = JsonSerializer.Serialize(new InvoiceContact(
                booking.TenNguoiDat, booking.SoDienThoaiSnapshot, booking.EmailSnapshot), JsonOptions),
            CreatedAt = now,
            UpdatedAt = now,
            CtHoaDonHoaDonItems = lines
        };
        context.HoaDonSet.Add(invoice);

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
            await transaction!.RollbackAsync(cancellationToken);
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
            await LockBookingAsync(bookingId.Value, cancellationToken);

        var role = await GetActorRoleAsync(actorId, CollectPermission, cancellationToken);
        if (role is null)
            return Forbidden();

        var replay = await ReadReplayAsync(actorId, CollectPermission, request.RequestId, fingerprint, cancellationToken);
        if (replay is not null)
            return replay;

        HoaDon? invoice = null;
        if (bookingId is not null)
        {
            invoice = transaction is not null
                ? (await context.HoaDonSet.FromSqlInterpolated($"SELECT * FROM hoa_don WHERE id = {invoiceId} FOR UPDATE")
                    .ToListAsync(cancellationToken)).SingleOrDefault()
                : await context.HoaDonSet.FindAsync([invoiceId], cancellationToken);
        }
        if (invoice is null)
            return Failure("Không tìm thấy hóa đơn.", "NOT_FOUND");
        if (invoice.TrangThai != InvoiceState.UNPAID || invoice.ConPhaiTra <= 0)
            return Failure("Hóa đơn không còn khoản phải thu hợp lệ.", "INVALID_TRANSITION");

        var netAmount = received - change;
        if (netAmount != invoice.ConPhaiTra)
            return Failure("Số tiền thu không khớp với số tiền cần thanh toán.", "AMOUNT_MISMATCH");

        var now = DateTimeOffset.UtcNow;
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
            await transaction!.RollbackAsync(cancellationToken);
            context.ChangeTracker.Clear();
            return await ReadReplayAsync(actorId, CollectPermission, request.RequestId, fingerprint, cancellationToken)
                ?? IdempotencyConflict();
        }
    }

    private async Task LockBookingAsync(int bookingId, CancellationToken cancellationToken)
    {
        if (context.Database.IsRelational())
            await context.Database.SqlQuery<int>($"SELECT id AS \"Value\" FROM don_dat_lich WHERE id = {bookingId} FOR UPDATE")
                .ToListAsync(cancellationToken);
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
        return ApiResponse<InvoiceResponseDto>.Ok(invoice,
            operation == CreatePermission ? "Lập hóa đơn thành công." : "Thanh toán hóa đơn thành công.");
    }

    private void RecordAudit(
        int actorId, Role role, string operation, int invoiceId, Guid requestId, string fingerprint, string traceId,
        DateTimeOffset now, InvoiceResponseDto response, string? details = null)
    {
        context.AuditLogSet.Add(new AuditLog
        {
            ActorId = actorId,
            ActorLoai = role.ToString(),
            HanhDong = operation,
            LoaiDoiTuong = "HoaDon",
            DoiTuongId = invoiceId,
            RequestId = requestId,
            Fingerprint = fingerprint,
            TraceId = traceId,
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
