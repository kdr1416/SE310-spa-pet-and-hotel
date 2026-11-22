using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PetSpa.Core.DTOs;

public abstract class ContractCommand : IValidatableObject
{
    [JsonRequired]
    public Guid RequestId { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RequestId == Guid.Empty)
            yield return new ValidationResult("RequestId phải khác rỗng.", [nameof(RequestId)]);
        foreach (var error in ValidateBusiness()) yield return error;
    }

    protected virtual IEnumerable<ValidationResult> ValidateBusiness() => [];
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class VndAmountAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => value is null ||
        value is decimal amount && amount > 0m && amount <= 99_999_999_999_999m && decimal.Truncate(amount) == amount;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class PetWeightAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => value is null ||
        value is decimal weight && weight > 0m && decimal.Round(weight, 2) == weight;
}
