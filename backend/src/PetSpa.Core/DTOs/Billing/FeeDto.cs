using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PetSpa.Core.DTOs.Billing;

public sealed record FeeDto(int MaPhuPhi, int MaDon, int? MaChiTietDon, string TenPhuPhi, decimal SoTien, string LyDo, DateTimeOffset? HuyAt);

public sealed class FeeCreate : ContractCommand
{
    [JsonRequired, Required, StringLength(150, MinimumLength = 1)]
    public string TenPhuPhi { get; init; } = string.Empty;
    [JsonRequired, VndAmount]
    public decimal SoTien { get; init; }
    [JsonRequired, Required, StringLength(1000, MinimumLength = 1)]
    public string LyDo { get; init; } = string.Empty;
    [Range(1, int.MaxValue)]
    public int? MaChiTietDon { get; init { field = value ?? throw new JsonException("MaChiTietDon không được null."); } }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class FeeEdit : ContractCommand
{
    [StringLength(150, MinimumLength = 1)]
    public string? TenPhuPhi { get; init { field = value ?? throw new JsonException("TenPhuPhi không được null."); } }
    [VndAmount]
    public decimal? SoTien { get; init { field = value ?? throw new JsonException("SoTien không được null."); } }
    [JsonRequired, Required, StringLength(1000, MinimumLength = 1)]
    public string LyDo { get; init; } = string.Empty;
    protected override IEnumerable<ValidationResult> ValidateBusiness()
    {
        if (TenPhuPhi is null && SoTien is null)
            yield return new ValidationResult("Cần ít nhất một trường thay đổi.", [nameof(TenPhuPhi), nameof(SoTien)]);
    }
}

public sealed class ExtraServiceRequest : ContractCommand
{
    [Range(1, int.MaxValue)]
    public int? MaDichVu { get; init { field = value ?? throw new JsonException("MaDichVu không được null."); } }
    [Range(1, int.MaxValue)]
    public int? MaCombo { get; init { field = value ?? throw new JsonException("MaCombo không được null."); } }
    [JsonRequired, Range(1, int.MaxValue)]
    public int MaKhungGio { get; init; }
    [JsonRequired, Required, StringLength(1000, MinimumLength = 1)]
    public string LyDo { get; init; } = string.Empty;
    protected override IEnumerable<ValidationResult> ValidateBusiness()
    {
        if (MaDichVu.HasValue == MaCombo.HasValue)
            yield return new ValidationResult("Chọn đúng một dịch vụ hoặc combo.", [nameof(MaDichVu), nameof(MaCombo)]);
    }
}

public sealed class ServiceActionRequest : ContractCommand
{
    [JsonRequired, Required, RegularExpression("COMPLETE|CANCEL")]
    public string HanhDong { get; init; } = string.Empty;
    [StringLength(1000, MinimumLength = 1)]
    public string? LyDo { get; init { field = value ?? throw new JsonException("LyDo không được null."); } }
    protected override IEnumerable<ValidationResult> ValidateBusiness()
    {
        if (HanhDong == "CANCEL" && string.IsNullOrWhiteSpace(LyDo))
            yield return new ValidationResult("Hủy dịch vụ cần lý do.", [nameof(LyDo)]);
    }
}

public sealed class ReasonCommand : ContractCommand
{
    [JsonRequired, Required, StringLength(1000, MinimumLength = 1)]
    public string LyDo { get; init; } = string.Empty;
}

