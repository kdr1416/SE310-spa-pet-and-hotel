using System.Data;
using Microsoft.EntityFrameworkCore;
using PetSpa.Api.Data;

namespace PetSpa.Tests.Infrastructure;

[CollectionDefinition("LiveIntegrationTests", DisableParallelization = true)]
public sealed class LiveIntegrationTestCollection;

[Collection("LiveIntegrationTests")]
public sealed class DbContextFactoryTests
{
    [Fact]
    public void Factory_UsesMigrationEnvironmentWithoutOpeningDatabase()
    {
        const string migration = "Host=127.0.0.1;Port=1;Database=migration_only;Username=test";
        WithConnections(migration, "Host=127.0.0.1;Database=runtime_only", () =>
        {
            using var context = new PetSpaDbContextFactory().CreateDbContext([]);
            Assert.Equal(migration, context.Database.GetConnectionString());
            Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
            Assert.Equal(33, context.Model.GetEntityTypes().Count());
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Factory_AbsentOrWhitespaceMigrationDoesNotUseRuntimeConnection(string? migration)
    {
        WithConnections(migration, "Host=127.0.0.1;Database=runtime_only", () =>
        {
            using var context = new PetSpaDbContextFactory().CreateDbContext([]);
            Assert.Null(context.Database.GetConnectionString());
            Assert.Equal(33, context.Model.GetEntityTypes().Count());
        });
    }

    private static void WithConnections(string? migration, string runtime, Action test)
    {
        const string migrationKey = "ConnectionStrings__MigrationConnection";
        const string runtimeKey = "ConnectionStrings__DefaultConnection";
        var previousMigration = Environment.GetEnvironmentVariable(migrationKey);
        var previousRuntime = Environment.GetEnvironmentVariable(runtimeKey);
        try
        {
            Environment.SetEnvironmentVariable(migrationKey, migration);
            Environment.SetEnvironmentVariable(runtimeKey, runtime);
            test();
        }
        finally
        {
            Environment.SetEnvironmentVariable(migrationKey, previousMigration);
            Environment.SetEnvironmentVariable(runtimeKey, previousRuntime);
        }
    }
}
