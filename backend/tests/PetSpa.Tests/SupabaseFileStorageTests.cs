using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http.Json;
using PetSpa.Api.Services.Storage;

namespace PetSpa.Tests;

public class SupabaseFileStorageTests
{
    [Fact]
    public async Task Storage_UploadsAFileAndSignsItForFiveMinutes()
    {
        var handler = new StorageHandler();
        using var client = new HttpClient(handler);
        var storage = CreateStorage(client);
        var image = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jF0sAAAAASUVORK5CYII=");

        var path = await storage.UploadPhotoAsync(image);
        Assert.Matches("^[a-f0-9]{32}\\.png$", path);
        var signed = await storage.CreateReadUrlAsync(path);
        await storage.DeleteAsync(path);

        Assert.Equal($"https://storage.example.test/storage/v1/object/sign/pets/{path}?token=fake", signed.AbsoluteUri);
        Assert.Collection(handler.Requests,
            request => Assert.Equal((HttpMethod.Post, $"/storage/v1/object/pets/{path}", "image/png"), request),
            request => Assert.Equal((HttpMethod.Post, $"/storage/v1/object/sign/pets/{path}", "300"), request),
            request => Assert.Equal((HttpMethod.Delete, $"/storage/v1/object/pets/{path}", (string?)null), request));
    }

    [Fact]
    public async Task Storage_RejectsOversizedAndUnsupportedFilesBeforeSendingRequests()
    {
        var handler = new StorageHandler();
        using var client = new HttpClient(handler);
        var storage = CreateStorage(client);
        var oversized = await Assert.ThrowsAsync<FileStorageException>(() => storage.UploadPhotoAsync(new byte[5 * 1024 * 1024 + 1]));
        var unsupported = await Assert.ThrowsAsync<FileStorageException>(() => storage.UploadPhotoAsync("not an image"u8.ToArray()));
        Assert.Equal("FILE_TOO_LARGE", oversized.ErrorCode);
        Assert.Equal("UNSUPPORTED_FILE", unsupported.ErrorCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Storage_WhenUpstreamFails_ReturnsStorageUnavailable()
    {
        using var client = new HttpClient(new StorageHandler { IsUnavailable = true });
        var storage = CreateStorage(client);
        var exception = await Assert.ThrowsAsync<FileStorageException>(
            () => storage.CreateReadUrlAsync("00112233445566778899aabbccddeeff.png"));
        Assert.Equal("STORAGE_UNAVAILABLE", exception.ErrorCode);
    }

    private static SupabaseFileStorage CreateStorage(HttpClient client)
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Supabase:Storage:Url"] = "https://storage.example.test",
                ["Supabase:Storage:Bucket"] = "pets",
                ["Supabase:Storage:ServiceRoleKey"] = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16))
            }).Build();
        return new SupabaseFileStorage(client, config);
    }

    private sealed class StorageHandler : HttpMessageHandler
    {
        public bool IsUnavailable { get; init; }
        public List<(HttpMethod Method, string Path, string? Detail)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (IsUnavailable)
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.True(request.Headers.Contains("apikey"));
            if (path.Contains("/object/sign/"))
            {
                var payload = await request.Content!.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken);
                var ttl = payload.GetProperty("expiresIn").GetInt32().ToString();
                Requests.Add((request.Method, path, ttl));
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { signedURL = path["/storage/v1".Length..] + "?token=fake" })
                };
            }

            Requests.Add((request.Method, path, request.Content?.Headers.ContentType?.MediaType));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
