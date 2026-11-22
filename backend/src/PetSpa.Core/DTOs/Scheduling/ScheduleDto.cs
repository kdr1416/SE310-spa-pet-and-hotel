using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PetSpa.Core.DTOs.Scheduling;

public sealed record ShiftRegistrationDto(int MaDangKy, int MaNhanVien, DateOnly Ngay, int MaCa, string TrangThai, string? LyDoDieuChuyen);
public sealed record ShiftWindowDto(int MaDot, DateOnly TuNgay, DateOnly DenNgay, DateTimeOffset MoAt, DateTimeOffset DongAt);

public sealed class ScheduleQuery : IValidatableObject
{
    [JsonRequired]
    public DateOnly TuNgay { get; init; }
    [JsonRequired]
    public DateOnly DenNgay { get; init; }
    [Range(1, int.MaxValue)]
    public int? MaNhanVien { get; init { field = value ?? throw new JsonException("MaNhanVien không được null."); } }
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (TuNgay == default || DenNgay == default || TuNgay > DenNgay)
            yield return new ValidationResult("Khoảng ngày không hợp lệ.", [nameof(TuNgay), nameof(DenNgay)]);
    }
}

public sealed class ShiftRegisterRequest : ContractCommand
{
    [JsonRequired]
    public DateOnly Ngay { get; init; }
    [JsonRequired, Range(1, int.MaxValue)]
    public int MaCa { get; init; }
}

public sealed class ShiftDecisionRequest : ContractCommand
{
    [JsonRequired, Required, RegularExpression("APPROVE|REJECT")]
    public string QuyetDinh { get; init; } = string.Empty;
    [StringLength(1000, MinimumLength = 1)]
    public string? LyDo { get; init { field = value ?? throw new JsonException("LyDo không được null."); } }
}

public sealed class ShiftWindowWriteRequest : ContractCommand
{
    [JsonRequired]
    public DateOnly TuNgay { get; init; }
    [JsonRequired]
    public DateOnly DenNgay { get; init; }
    [JsonRequired]
    public DateTimeOffset MoAt { get; init; }
    [JsonRequired]
    public DateTimeOffset DongAt { get; init; }
    protected override IEnumerable<ValidationResult> ValidateBusiness()
    {
        if (TuNgay == default || DenNgay == default || TuNgay > DenNgay || MoAt >= DongAt)
            yield return new ValidationResult("Khoảng ngày hoặc thời gian mở đăng ký không hợp lệ.");
    }
}

public sealed class ShiftTransferRequest : ContractCommand
{
    [JsonRequired]
    public DateOnly Ngay { get; init; }
    [JsonRequired, Range(1, int.MaxValue)]
    public int MaCa { get; init; }
    [JsonRequired, Required, StringLength(1000, MinimumLength = 1)]
    public string LyDo { get; init; } = string.Empty;
}

