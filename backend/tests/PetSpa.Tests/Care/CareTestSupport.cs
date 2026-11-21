using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using PetSpa.Api.Data;
using PetSpa.Api.Services.Storage;
using PetSpa.Tests.Infrastructure;

namespace PetSpa.Tests.Care;

internal static class CareTestSupport
{
    // 08:30 at the clinic in Asia/Ho_Chi_Minh.
    public static DateTimeOffset Now { get; } = new(2026, 11, 20, 1, 30, 0, TimeSpan.Zero);

    public static WebApplicationFactory<Program> Create(Action<PetSpaDbContext> seed, Storage? storage = null, Clock? clock = null) =>
        Configure(AuthApplicationFactory.Create(seed), storage, clock);

    public static WebApplicationFactory<Program> Configure(WebApplicationFactory<Program> factory, Storage? storage = null, Clock? clock = null) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock ?? new Clock(Now)));
            services.Replace(ServiceDescriptor.Singleton<IFileStorageService>(storage ?? new Storage()));
        }));

    public static async Task WaitForLockWaitersAsync(
        string connectionString, string sqlPattern, int expected, CancellationToken cancellationToken)
    {
        await using var observer = new NpgsqlConnection(connectionString);
        await observer.OpenAsync(cancellationToken);
        await using var waiters = new NpgsqlCommand("""
            SELECT count(*) FROM pg_stat_activity
            WHERE datname = current_database() AND wait_event_type = 'Lock' AND query LIKE $1
            """, observer);
        waiters.Parameters.AddWithValue(sqlPattern);
        while ((long)(await waiters.ExecuteScalarAsync(cancellationToken))! < expected)
            await Task.Delay(25, cancellationToken);
    }

    public sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    public sealed class Storage : IFileStorageService
    {
        public ConcurrentQueue<string> Uploads { get; } = new();
        public ConcurrentQueue<string> Reads { get; } = new();
        public ConcurrentQueue<string> Deletes { get; } = new();
        public Func<CancellationToken, Task>? BeforeUpload { get; set; }
        public Func<string, CancellationToken, Task>? BeforeRead { get; set; }

        public async Task<string> UploadPhotoAsync(byte[] content, CancellationToken cancellationToken = default)
        {
            if (BeforeUpload is not null)
                await BeforeUpload(cancellationToken);
            var path = $"{Guid.NewGuid():N}.png";
            Uploads.Enqueue(path);
            return path;
        }

        public Task DeleteAsync(string objectPath, CancellationToken cancellationToken = default)
        {
            Deletes.Enqueue(objectPath);
            return Task.CompletedTask;
        }

        public async Task<Uri> CreateReadUrlAsync(string objectPath, CancellationToken cancellationToken = default)
        {
            if (BeforeRead is not null)
                await BeforeRead(objectPath, cancellationToken);
            Reads.Enqueue(objectPath);
            return new Uri($"https://photo.test/sign/{Reads.Count}");
        }
    }
}
