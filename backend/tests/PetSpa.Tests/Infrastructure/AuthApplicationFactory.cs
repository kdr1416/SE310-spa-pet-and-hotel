using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PetSpa.Api.Data;

namespace PetSpa.Tests.Infrastructure;

public static class AuthApplicationFactory
{
    public const string TestJwtSecret = "petspa_integration_test_secret_key_entropy_256_bits_length_secure!";

    public static WebApplicationFactory<Program> Create(Action<PetSpaDbContext>? seedDatabase = null)
    {
        var dbName = "PetSpa_AuthTest_" + Guid.NewGuid().ToString("N");

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Jwt:SecretKey"] = TestJwtSecret,
                    ["Jwt:Issuer"] = "PetSpaApi",
                    ["Jwt:Audience"] = "PetSpaClient",
                    ["Jwt:ExpiryMinutes"] = "480",
                    ["SeedData:Enabled"] = "false"
                }));

            builder.ConfigureServices(services =>
            {
                var descriptors = services.Where(d =>
                    d.ServiceType.Namespace?.StartsWith("Microsoft.EntityFrameworkCore") == true ||
                    d.ServiceType == typeof(DbContextOptions<PetSpaDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    d.ServiceType == typeof(PetSpaDbContext)).ToList();

                foreach (var descriptor in descriptors)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<PetSpaDbContext>(options =>
                {
                    options.UseInMemoryDatabase(dbName);
                });

                var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
                db.Database.EnsureCreated();
                seedDatabase?.Invoke(db);
            });
        });
    }
}
