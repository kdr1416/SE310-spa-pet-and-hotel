using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PetSpa.Api.Data;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Booking;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Enums;

namespace PetSpa.Api.Services.Booking;

public sealed class CustomerLookupService(
    PetSpaDbContext db,
    TimeProvider clock,
    ILogger<CustomerLookupService> logger)
{
    private const string Permission = "CUSTOMER_LOOKUP";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ApiResponse<IReadOnlyList<CustomerDto>>> LookupByPhoneAsync(
        CustomerQuery query, int actorId, CancellationToken cancellationToken)
    {
        if (query is null || string.IsNullOrWhiteSpace(query.SoDienThoai) ||
            query.SoDienThoai.Length != 10 || !query.SoDienThoai.All(char.IsAsciiDigit))
        {
            return ApiResponse<IReadOnlyList<CustomerDto>>.Fail(
                "Số điện thoại phải gồm đúng 10 chữ số.", "VALIDATION_FAILED");
        }

        if (await GetRoleAsync(actorId, cancellationToken) is null)
        {
            return ApiResponse<IReadOnlyList<CustomerDto>>.Fail(
                "Nhân viên không có quyền tra cứu thông tin khách hàng.", "FORBIDDEN");
        }

        try
        {
            // Do not log the raw phone number for privacy/PII compliance
            logger.LogInformation("Customer lookup requested by actor {ActorId}", actorId);

            var customers = await db.KhachHangSet.AsNoTracking()
                .Where(k => k.SoDienThoai == query.SoDienThoai && k.DeletedAt == null)
                .Include(k => k.ThuCungKhachHangItems)
                .OrderBy(k => k.Id)
                .ToListAsync(cancellationToken);

            var result = customers.Select(k => new CustomerDto(
                k.Id,
                k.HoTen,
                k.SoDienThoai,
                k.Email,
                k.ThuCungKhachHangItems
                    .Where(p => p.DeletedAt == null)
                    .OrderBy(p => p.Id)
                    .Select(p => new PetDto(
                        p.Id,
                        p.TenThuCung,
                        p.LoaiPet.ToString(),
                        p.Giong,
                        p.CanNang,
                        p.NgaySinh,
                        p.LoaiLong,
                        p.GhiChuDiUng))
                    .ToList()
            )).ToList();

            return ApiResponse<IReadOnlyList<CustomerDto>>.Ok(result);
        }
        catch (NpgsqlException ex) when (ex.IsTransient)
        {
            return ApiResponse<IReadOnlyList<CustomerDto>>.Fail(
                "Chưa thể tra cứu thông tin khách hàng. Vui lòng thử lại.", "TEMPORARY_FAILURE");
        }
    }

    public async Task<ApiResponse<PetDto>> UpdatePetAsync(
        int petId, PetEdit request, int actorId, string traceId, CancellationToken cancellationToken)
    {
        if (petId <= 0 || request.RequestId == Guid.Empty)
            return ApiResponse<PetDto>.Fail("Mã thú cưng hoặc RequestId không hợp lệ.", "VALIDATION_FAILED");

        if (request.TenThuCung is null && request.Giong is null && request.CanNang is null &&
            request.GhiChuDiUng is null && request.LoaiLong is null && request.NgaySinh is null)
        {
            return ApiResponse<PetDto>.Fail("Cần ít nhất một trường thay đổi.", "VALIDATION_FAILED");
        }

        var role = await GetRoleAsync(actorId, cancellationToken);
        if (role is null)
        {
            return ApiResponse<PetDto>.Fail("Nhân viên không có quyền cập nhật thông tin thú cưng.", "FORBIDDEN");
        }

        try
        {
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(cancellationToken)
                : null;

            if (db.Database.IsRelational())
            {
                var key = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(
                    Encoding.UTF8.GetBytes($"pet-management:{actorId}:PET_EDIT:{request.RequestId:N}")));
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", cancellationToken);
            }

            role = await GetRoleAsync(actorId, cancellationToken);
            if (role is null)
                return ApiResponse<PetDto>.Fail("Nhân viên không có quyền cập nhật thông tin thú cưng.", "FORBIDDEN");

            var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
                new
                {
                    petId,
                    request.TenThuCung,
                    request.Giong,
                    request.CanNang,
                    request.GhiChuDiUng,
                    request.LoaiLong,
                    request.NgaySinh
                }, JsonOptions)));

            var replay = await db.AuditLogSet.AsNoTracking().SingleOrDefaultAsync(
                audit => audit.ActorId == actorId && audit.HanhDong == "PET_EDIT" && audit.RequestId == request.RequestId,
                cancellationToken);

            if (replay is not null)
            {
                return replay.Fingerprint == fingerprint
                    ? ApiResponse<PetDto>.Ok(JsonSerializer.Deserialize<PetDto>(replay.KetQua!, JsonOptions)!)
                    : ApiResponse<PetDto>.Fail("RequestId đã được sử dụng với dữ liệu khác.", "IDEMPOTENCY_CONFLICT");
            }

            var pet = await db.ThuCungSet.SingleOrDefaultAsync(
                p => p.Id == petId && p.DeletedAt == null, cancellationToken);

            if (pet is null)
                return ApiResponse<PetDto>.Fail("Không tìm thấy thú cưng.", "NOT_FOUND");

            var now = clock.GetUtcNow();

            var oldValues = new
            {
                tenThuCung = pet.TenThuCung,
                giong = pet.Giong,
                canNang = pet.CanNang,
                ghiChuDiUng = pet.GhiChuDiUng,
                loaiLong = pet.LoaiLong,
                ngaySinh = pet.NgaySinh
            };

            // Only update master ThuCung. Do NOT touch ChiTietDon snapshot fields (D10 / Q05).
            if (request.TenThuCung is not null) pet.TenThuCung = request.TenThuCung.Trim();
            if (request.Giong is not null) pet.Giong = request.Giong.Trim();
            if (request.CanNang is not null) pet.CanNang = request.CanNang.Value;
            if (request.GhiChuDiUng is not null) pet.GhiChuDiUng = request.GhiChuDiUng.Trim();
            if (request.LoaiLong is not null) pet.LoaiLong = request.LoaiLong.Trim();
            if (request.NgaySinh is not null) pet.NgaySinh = request.NgaySinh;
            pet.UpdatedAt = now;

            var newValues = new
            {
                tenThuCung = pet.TenThuCung,
                giong = pet.Giong,
                canNang = pet.CanNang,
                ghiChuDiUng = pet.GhiChuDiUng,
                loaiLong = pet.LoaiLong,
                ngaySinh = pet.NgaySinh
            };

            var result = new PetDto(
                pet.Id,
                pet.TenThuCung,
                pet.LoaiPet.ToString(),
                pet.Giong,
                pet.CanNang,
                pet.NgaySinh,
                pet.LoaiLong,
                pet.GhiChuDiUng
            );

            db.AuditLogSet.Add(new AuditLog
            {
                ActorId = actorId,
                ActorLoai = role.Value.ToString(),
                HanhDong = "PET_EDIT",
                LoaiDoiTuong = "ThuCung",
                DoiTuongId = pet.Id,
                RequestId = request.RequestId,
                Fingerprint = fingerprint,
                TraceId = traceId[..Math.Min(40, traceId.Length)],
                Truoc = JsonSerializer.Serialize(oldValues, JsonOptions),
                Sau = JsonSerializer.Serialize(newValues, JsonOptions),
                KetQua = JsonSerializer.Serialize(result, JsonOptions),
                CreatedAt = now
            });

            await db.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);

            return ApiResponse<PetDto>.Ok(result, "Cập nhật thông tin thú cưng thành công.");
        }
        catch (NpgsqlException ex) when (ex.IsTransient)
        {
            return ApiResponse<PetDto>.Fail(
                "Chưa thể cập nhật thông tin thú cưng. Vui lòng thử lại.", "TEMPORARY_FAILURE");
        }
    }

    private Task<Role?> GetRoleAsync(int actorId, CancellationToken cancellationToken) =>
        db.NhanVienSet.AsNoTracking().Where(employee => employee.Id == actorId && employee.IsActive &&
                employee.NhanVienQuyenNhanVienItems.Any(permission => permission.MaQuyen == Permission))
            .Select(employee => (Role?)employee.VaiTro).SingleOrDefaultAsync(cancellationToken);
}
