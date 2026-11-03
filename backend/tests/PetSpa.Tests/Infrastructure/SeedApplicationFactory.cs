using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace PetSpa.Tests.Infrastructure;

public static class SeedApplicationFactory
{
    public static string NewPassword() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public static WebApplicationFactory<Program> Create(string connection, string password,
        string environment = "Testing", bool enabled = true, bool missingStaffPassword = false,
        string? date = "2026-11-04") =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = connection,
                    ["SeedData:Enabled"] = enabled.ToString(),
                    ["SeedData:Date"] = date,
                    ["SeedData:AdminPassword"] = password,
                    ["SeedData:Staff1Password"] = password,
                    ["SeedData:Staff2Password"] = missingStaffPassword ? null : password
                }));
        });
}
