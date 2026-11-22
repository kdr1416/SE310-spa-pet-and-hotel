using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PetSpa.Core.DTOs.Booking;

public sealed record CustomerDto(int MaKhachHang, string HoTen, string SoDienThoai, string Email, IReadOnlyList<PetDto> ThuCung);
public sealed record PetDto(int MaThuCung, string TenThuCung, string LoaiPet, string? Giong, decimal CanNang,
    DateOnly? NgaySinh, string? LoaiLong, string? GhiChuDiUng);
public sealed record AcknowledgedResponse(bool DaTiepNhan, string ThongBao);

public sealed class CustomerQuery
{
    [JsonRequired, Required, RegularExpression("[0-9]{10}")]
    public string SoDienThoai { get; init; } = string.Empty;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PetEdit : ContractCommand
{
    [StringLength(150, MinimumLength = 1)]
    public string? TenThuCung { get; init { field = value ?? throw new JsonException("TenThuCung không được null."); } }
    [StringLength(150, MinimumLength = 0)]
    public string? Giong { get; init { field = value ?? throw new JsonException("Giong không được null."); } }
    [PetWeight]
    public decimal? CanNang { get; init { field = value ?? throw new JsonException("CanNang không được null."); } }
    [StringLength(2000, MinimumLength = 0)]
    public string? GhiChuDiUng { get; init { field = value ?? throw new JsonException("GhiChuDiUng không được null."); } }
    [StringLength(100, MinimumLength = 0)]
    public string? LoaiLong { get; init { field = value ?? throw new JsonException("LoaiLong không được null."); } }
    public DateOnly? NgaySinh { get; init { field = value ?? throw new JsonException("NgaySinh không được null."); } }
    protected override IEnumerable<ValidationResult> ValidateBusiness()
    {
        if (TenThuCung is null && Giong is null && CanNang is null && GhiChuDiUng is null && LoaiLong is null && NgaySinh is null)
            yield return new ValidationResult("Cần ít nhất một trường thay đổi.");
    }
}

public sealed class RescheduleRequest : ContractCommand
{
    [JsonRequired, Range(1, int.MaxValue)]
    public int MaKhungGio { get; init; }
    [JsonRequired]
    public DateOnly NgayTra { get; init; }
    [JsonRequired, Required, StringLength(1000, MinimumLength = 1)]
    public string LyDo { get; init; } = string.Empty;
}

public sealed class TrackingCancel : ContractCommand
{
    [JsonRequired, Required, RegularExpression("[0-9]{10}")]
    public string SoDienThoai { get; init; } = string.Empty;
    [JsonRequired, Required, StringLength(8, MinimumLength = 8)]
    public string TrackingCode { get; init; } = string.Empty;
    [JsonRequired, Required, StringLength(1000, MinimumLength = 1)]
    public string LyDo { get; init; } = string.Empty;
}

public sealed class TrackingResend
{
    [JsonRequired, Required, RegularExpression("[0-9]{10}")]
    public string SoDienThoai { get; init; } = string.Empty;
    [JsonRequired, Required, EmailAddress, StringLength(2000)]
    public string Email { get; init; } = string.Empty;
    public DateOnly? NgayHen { get; init { field = value ?? throw new JsonException("NgayHen không được null."); } }
}

public sealed class AssignmentRequest : ContractCommand
{
    [JsonRequired, Range(1, int.MaxValue)]
    public int MaNhanVien { get; init; }
    [JsonRequired, Required, StringLength(1000, MinimumLength = 1)]
    public string LyDo { get; init; } = string.Empty;
}

public sealed class ShiftQuery
{
    [JsonRequired]
    public DateOnly Ngay { get; init; }
    [JsonRequired, Range(1, int.MaxValue)]
    public int MaCa { get; init; }
}

public sealed class PetCancelRequest : ContractCommand
{
    [JsonRequired, Required, StringLength(1000, MinimumLength = 1)]
    public string LyDo { get; init; } = string.Empty;
    [JsonRequired, Range(0, int.MaxValue)]
    public int SoDemDaO { get; init; }
    [JsonRequired, Required]
    public int[] MaThanhPhanDaLam { get; init; } = [];
    protected override IEnumerable<ValidationResult> ValidateBusiness()
    {
        if (MaThanhPhanDaLam.Any(id => id <= 0) || MaThanhPhanDaLam.Distinct().Count() != MaThanhPhanDaLam.Length)
            yield return new ValidationResult("Mã thành phần phải dương và không trùng.", [nameof(MaThanhPhanDaLam)]);
    }
}

