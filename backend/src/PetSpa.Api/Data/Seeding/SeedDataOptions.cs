namespace PetSpa.Api.Data.Seeding;

public sealed class SeedDataOptions
{
    public DateOnly? Date { get; set; }
    public string AdminPassword { get; set; } = string.Empty;
    public string Staff1Password { get; set; } = string.Empty;
    public string Staff2Password { get; set; } = string.Empty;

    public void Validate()
    {
        foreach (var (key, value) in new[]
        {
            (nameof(AdminPassword), AdminPassword),
            (nameof(Staff1Password), Staff1Password),
            (nameof(Staff2Password), Staff2Password)
        })
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length is < 12 or > 128)
                throw new InvalidOperationException($"Configure SeedData:{key} with 12..128 characters outside source control.");
        }
    }
}
