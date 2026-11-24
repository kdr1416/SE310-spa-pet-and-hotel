using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MimeKit;
using Npgsql;
using PetSpa.Api.Data;
using PetSpa.Api.Services.Booking;
using PetSpa.Api.Services.Storage;
using Xunit.Abstractions;

namespace PetSpa.Tests.Infrastructure;

[Collection("LiveIntegrationTests")]
public sealed class LiveIntegrationSpikeTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "LiveIntegrationSpike")]
    public Task Neon_PooledConnection_HasApplicationTablesAndSeedConfiguration() =>
        RunIfEnabledAsync(Environment.GetEnvironmentVariable("PETSPA_LIVE_SPIKE_ENABLED"), output.WriteLine,
            async () =>
            {
                var configuration = EnvironmentConfiguration();
                var settings = new NpgsqlConnectionStringBuilder(RequireSetting(configuration, "ConnectionStrings:DefaultConnection"));
                var host = settings.Host ?? string.Empty;
                if (!host.EndsWith(".neon.tech", StringComparison.OrdinalIgnoreCase)
                    || !host.Contains("-pooler.", StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(settings.Database) || string.IsNullOrWhiteSpace(settings.Username)
                    || string.IsNullOrWhiteSpace(settings.Password)
                    || settings.SslMode is not (SslMode.Require or SslMode.VerifyCA or SslMode.VerifyFull))
                    throw new LiveSpikeException("CONFIGURATION_INVALID: ConnectionStrings:DefaultConnection requires a Neon pooled host, database, credentials and TLS.");
                settings.Timeout = Math.Max(30, settings.Timeout);
                settings.CommandTimeout = 30;
                settings.IncludeErrorDetail = false;
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(settings.Timeout + 60));
                await using var context = new PetSpaDbContext(new DbContextOptionsBuilder<PetSpaDbContext>().UseNpgsql().Options);
                var tables = context.Model.GetRelationalModel().Tables
                    .Select(table => $"{table.Schema ?? "public"}.{table.Name}").ToArray();
                await using var connection = new NpgsqlConnection(settings.ConnectionString);
                await connection.OpenAsync(timeout.Token);
                var actual = new List<string>();
                await using (var command = new NpgsqlCommand("SELECT schemaname || '.' || tablename FROM pg_catalog.pg_tables", connection))
                await using (var reader = await command.ExecuteReaderAsync(timeout.Token))
                    while (await reader.ReadAsync(timeout.Token)) actual.Add(reader.GetString(0));
                var values = new Dictionary<string, string>();
                await using (var command = new NpgsqlCommand(
                    "SELECT khoa, gia_tri::text FROM public.cau_hinh_he_thong WHERE khoa = ANY(@keys)", connection))
                {
                    command.Parameters.AddWithValue("keys", new[] { "FUR_CONDITIONS", "EXTENSION_SETTINGS" });
                    await using var reader = await command.ExecuteReaderAsync(timeout.Token);
                    while (await reader.ReadAsync(timeout.Token)) values.Add(reader.GetString(0), reader.GetString(1));
                }
                if (!values.TryGetValue("FUR_CONDITIONS", out var fur) || !values.TryGetValue("EXTENSION_SETTINGS", out var extensions))
                    throw new LiveSpikeException("NEON_SEED_MISSING: FUR_CONDITIONS or EXTENSION_SETTINGS; bootstrap Staging first.");
                VerifyNeonState(tables, actual, fur, extensions);
                output.WriteLine("PASS: Neon pooled connection, 33 application tables and seed configuration verified (read-only).");
            });

    [Fact]
    [Trait("Category", "LiveIntegrationSpike")]
    public Task Supabase_PrivateStorage_RoundTripsAndCleansUpPng() =>
        RunIfEnabledAsync(Environment.GetEnvironmentVariable("PETSPA_LIVE_SPIKE_ENABLED"), output.WriteLine,
            async () =>
            {
                var configuration = EnvironmentConfiguration();
                var project = RequireSetting(configuration, "Supabase:Storage:Url");
                var bucket = RequireSetting(configuration, "Supabase:Storage:Bucket");
                RequireSetting(configuration, "Supabase:Storage:ServiceRoleKey");
                if (!Uri.TryCreate(project, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps
                    || !string.IsNullOrEmpty(url.UserInfo) || !string.IsNullOrEmpty(url.Query)
                    || !string.IsNullOrEmpty(url.Fragment) || url.AbsolutePath != "/"
                    || !System.Text.RegularExpressions.Regex.IsMatch(bucket, "^[A-Za-z0-9][A-Za-z0-9._-]*$"))
                    throw new LiveSpikeException("CONFIGURATION_INVALID: Supabase:Storage:Url or Supabase:Storage:Bucket.");
                using var adapterClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
                using var anonymousClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
                IFileStorageService storage = new SupabaseFileStorage(adapterClient, configuration);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                await VerifyStorageRoundTripAsync(storage, anonymousClient, url, bucket, output.WriteLine, timeout.Token);
                output.WriteLine("PASS: private Storage upload, signed download byte equality, public denial and cleanup verified.");
            });

    [Fact]
    [Trait("Category", "LiveIntegrationSpike")]
    public Task Smtp_AcceptsSmokeMessageForConfiguredRecipient() =>
        RunIfEnabledAsync(Environment.GetEnvironmentVariable("PETSPA_LIVE_SPIKE_ENABLED"), output.WriteLine,
            async () =>
            {
                var configuration = EnvironmentConfiguration();
                ValidateSmtpConfiguration(configuration);
                IEmailSender sender = new SmtpEmailSender(configuration);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var trackingCode = "SMOKE-TEST-" + Guid.NewGuid().ToString("N")[..8];
                await sender.SendTrackingCodeAsync(RequireSetting(configuration, "LiveSpike:EmailRecipient"), trackingCode, timeout.Token);
                output.WriteLine($"PASS: SMTP accepted smoke message {trackingCode}; verify inbox/spam separately.");
            });

    internal static async Task RunIfEnabledAsync(string? flag, Action<string> write, Func<Task> run)
    {
        if (!string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase))
        {
            write("NO-OP: Live integration tests are disabled. Set PETSPA_LIVE_SPIKE_ENABLED=true to run.");
            return;
        }
        try { await run(); }
        catch (LiveSpikeException) { throw; }
        catch (Exception error)
        {
            // Upstream Message/ToString/InnerException may contain credentials or a signed URL.
            throw new LiveSpikeException($"LIVE_SPIKE_FAILED: {error.GetType().Name}; check this test's service configuration/connectivity.");
        }
    }

    internal static string RequireSetting(IConfiguration configuration, string key) =>
        configuration[key] is { } value && !string.IsNullOrWhiteSpace(value)
            ? value : throw new LiveSpikeException($"CONFIGURATION_MISSING: {key}");

    internal static void VerifyNeonState(IReadOnlyCollection<string> expectedTables,
        IReadOnlyCollection<string> actualTables, string furConditionsJson, string extensionSettingsJson)
    {
        if (expectedTables.Count != 33 || expectedTables.Distinct(StringComparer.Ordinal).Count() != 33)
            throw new LiveSpikeException("NEON_MODEL_INVALID: expected 33 distinct application tables.");
        var missing = expectedTables.Except(actualTables, StringComparer.Ordinal).ToArray();
        if (missing.Length != 0)
            throw new LiveSpikeException($"NEON_TABLES_MISSING: {string.Join(", ", missing)}");
        try
        {
            using var fur = JsonDocument.Parse(furConditionsJson);
            if (fur.RootElement.ValueKind != JsonValueKind.Array)
                throw new LiveSpikeException("NEON_SEED_INVALID: FUR_CONDITIONS must be an array.");
            var codes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in fur.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("ma", out var code)
                    || code.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(code.GetString())
                    || !codes.Add(code.GetString()!))
                    throw new LiveSpikeException("NEON_SEED_INVALID: FUR_CONDITIONS has invalid or duplicate codes.");
            }
            string[] required = ["BINH_THUONG", "ROI_NHE", "ROI_NANG", "VE_RAN", "LONG_NGAN", "LONG_DAI"];
            if (required.Any(code => !codes.Contains(code)))
                throw new LiveSpikeException("NEON_SEED_INVALID: FUR_CONDITIONS is missing default codes.");
            using var settings = JsonDocument.Parse(extensionSettingsJson);
            foreach (var key in new[] { "otpEmail", "cocHotel", "giuCho" })
                if (settings.RootElement.ValueKind != JsonValueKind.Object || !settings.RootElement.TryGetProperty(key, out var value)
                    || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new LiveSpikeException("NEON_SEED_INVALID: EXTENSION_SETTINGS requires three boolean flags.");
            if (settings.RootElement.GetProperty("cocHotel").GetBoolean() && !settings.RootElement.GetProperty("giuCho").GetBoolean())
                throw new LiveSpikeException("NEON_SEED_INVALID: cocHotel requires giuCho.");
        }
        catch (JsonException)
        {
            throw new LiveSpikeException("NEON_SEED_INVALID: configuration is not valid JSON.");
        }
    }

    internal static async Task VerifyStorageRoundTripAsync(IFileStorageService storage, HttpClient anonymousClient,
        Uri projectUrl, string bucket, Action<string> write, CancellationToken ct)
    {
        var image = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNgYGD4DwABBAEAX+XDSwAAAABJRU5ErkJggg==");
        string? objectPath = null;
        string? failure = null;
        var stage = "UPLOAD";
        try
        {
            objectPath = await storage.UploadPhotoAsync(image, ct);
            stage = "SIGN";
            var signed = await storage.CreateReadUrlAsync(objectPath, ct);
            stage = "DOWNLOAD";
            using (var response = await anonymousClient.GetAsync(signed, ct))
            {
                if (response.StatusCode != HttpStatusCode.OK)
                    throw new LiveSpikeException($"STORAGE_DOWNLOAD_FAILED: HTTP {(int)response.StatusCode}.");
                if (!(await response.Content.ReadAsByteArrayAsync(ct)).AsSpan().SequenceEqual(image))
                    throw new LiveSpikeException("STORAGE_DOWNLOAD_FAILED: PNG bytes differ.");
            }
            stage = "PRIVATE_CHECK";
            var publicUrl = new Uri(projectUrl, $"/storage/v1/object/public/{Uri.EscapeDataString(bucket)}/{Uri.EscapeDataString(objectPath)}");
            using var denied = await anonymousClient.GetAsync(publicUrl, ct);
            if (denied.StatusCode is not (HttpStatusCode.BadRequest or HttpStatusCode.Forbidden or HttpStatusCode.NotFound))
                throw new LiveSpikeException($"STORAGE_PRIVATE_CHECK_FAILED: HTTP {(int)denied.StatusCode}; expected 400/403/404.");
        }
        catch (Exception error) { failure = SafeFailure(stage, error); }
        finally
        {
            if (objectPath is not null)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                try { await storage.DeleteAsync(objectPath, cleanup.Token); }
                catch (Exception error)
                {
                    var cleanupFailure = $"CLEANUP_FAILED: objectPath={objectPath}; delete manually from the Staging bucket. {error.GetType().Name}.";
                    write(cleanupFailure);
                    failure = failure is null ? cleanupFailure : $"{failure} {cleanupFailure}";
                }
            }
        }
        if (failure is not null) throw new LiveSpikeException(failure);
    }

    internal static void ValidateSmtpConfiguration(IConfiguration configuration)
    {
        RequireSetting(configuration, "Smtp:Host");
        foreach (var key in new[] { "Smtp:From", "LiveSpike:EmailRecipient" })
            if (!MailboxAddress.TryParse(RequireSetting(configuration, key), out _))
                throw new LiveSpikeException($"CONFIGURATION_INVALID: {key}");
        if (!int.TryParse(RequireSetting(configuration, "Smtp:Port"), out var port) || port is not (465 or 587))
            throw new LiveSpikeException("CONFIGURATION_INVALID: Smtp:Port must be 465 or 587.");
        if (!bool.TryParse(RequireSetting(configuration, "Smtp:EnableSsl"), out var tls) || !tls)
            throw new LiveSpikeException("CONFIGURATION_INVALID: Smtp:EnableSsl must be true.");
        if (!string.IsNullOrEmpty(configuration["Smtp:Username"])) RequireSetting(configuration, "Smtp:Password");
    }

    private static IConfiguration EnvironmentConfiguration() => new ConfigurationBuilder().AddEnvironmentVariables().Build();

    private static string SafeFailure(string stage, Exception error) => error is LiveSpikeException
        ? error.Message : $"STORAGE_{stage}_FAILED: {error.GetType().Name}.";
}

internal sealed class LiveSpikeException(string message) : Exception(message);
