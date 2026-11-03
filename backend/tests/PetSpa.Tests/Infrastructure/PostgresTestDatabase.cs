using Microsoft.EntityFrameworkCore;
using Npgsql;
using PetSpa.Api.Data;

namespace PetSpa.Tests.Infrastructure;

public sealed class PostgresTestDatabase : IAsyncDisposable
{
    private readonly string _adminConnection;
    private readonly string _databaseName = "petspa_test_" + Guid.NewGuid().ToString("N");
    public string ConnectionString { get; }

    private PostgresTestDatabase(string adminConnection)
    {
        _adminConnection = adminConnection;
        ConnectionString = new NpgsqlConnectionStringBuilder(adminConnection)
        {
            Database = _databaseName,
            Pooling = false,
            IncludeErrorDetail = false
        }.ConnectionString;
    }

    public static async Task<PostgresTestDatabase> CreateAsync()
    {
        var connection = Environment.GetEnvironmentVariable("PETSPA_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("Set PETSPA_TEST_POSTGRES to a local PostgreSQL test server; see backend/README.md.");
        var settings = new NpgsqlConnectionStringBuilder(connection);
        if (settings.Host is not ("localhost" or "127.0.0.1" or "::1"))
            throw new InvalidOperationException("PostgreSQL tests require a loopback host; remote databases are not allowed.");

        var database = new PostgresTestDatabase(connection);
        await using var admin = new NpgsqlConnection(connection);
        await admin.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{database._databaseName}\"", admin);
        await command.ExecuteNonQueryAsync();
        try
        {
            await using var context = database.CreateContext();
            // Apply existing migrations only to this newly created, isolated test database.
            await context.Database.MigrateAsync();
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public PetSpaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PetSpaDbContext>().UseNpgsql(ConnectionString).Options);

    public async ValueTask DisposeAsync()
    {
        await using var admin = new NpgsqlConnection(_adminConnection);
        await admin.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", admin);
        await command.ExecuteNonQueryAsync();
    }
}
