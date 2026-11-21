using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace PetSpa.Api.Services.Storage;

public sealed class SupabaseFileStorage(HttpClient client, IConfiguration configuration) : IFileStorageService
{
    private const int MaximumPhotoBytes = 5 * 1024 * 1024;
    private const int ReadUrlSeconds = 300;

    public async Task<string> UploadPhotoAsync(byte[] content, CancellationToken cancellationToken = default)
    {
        if (content.Length > MaximumPhotoBytes)
            throw new FileStorageException("Ảnh vượt quá 5 MiB.", "FILE_TOO_LARGE");

        var (extension, mediaType) = PhotoType(content);
        var objectPath = $"{Guid.NewGuid():N}.{extension}";
        using var request = CreateRequest(HttpMethod.Post, "object", objectPath);
        request.Content = new ByteArrayContent(content);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        using var response = await SendAsync(request, cancellationToken);
        return objectPath;
    }

    public async Task DeleteAsync(string objectPath, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Delete, "object", objectPath);
        using var response = await SendAsync(request, cancellationToken);
    }

    // Callers must authorize access to the persisted object path before asking for a URL.
    public async Task<Uri> CreateReadUrlAsync(string objectPath, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, "object/sign", objectPath);
        request.Content = JsonContent.Create(new { expiresIn = ReadUrlSeconds });
        using var response = await SendAsync(request, cancellationToken);

        try
        {
            var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            if (payload.TryGetProperty("signedURL", out var value) && value.ValueKind == JsonValueKind.String)
            {
                var signed = value.GetString()!;
                if (signed.StartsWith("/object/sign/", StringComparison.Ordinal))
                    signed = "/storage/v1" + signed;
                if (signed.StartsWith("/storage/v1/object/sign/", StringComparison.Ordinal)
                    && Uri.TryCreate(request.RequestUri!, signed, out var url)
                    && url.Host == request.RequestUri!.Host
                    && url.AbsolutePath.StartsWith("/storage/v1/object/sign/", StringComparison.Ordinal))
                    return url;
            }
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or HttpRequestException or IOException
            || (error is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw Unavailable(error);
        }

        throw Unavailable();
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string operation, string objectPath)
    {
        if (!Uri.TryCreate(configuration["Supabase:Storage:Url"], UriKind.Absolute, out var project)
            || project.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(project.UserInfo)
            || configuration["Supabase:Storage:Bucket"] is not { Length: > 0 } bucket
            || configuration["Supabase:Storage:ServiceRoleKey"] is not { Length: > 0 } key)
            throw Unavailable();

        var segments = objectPath.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".." || segment.Contains('\\'))
            || bucket is "." or ".." || bucket.Contains('/') || bucket.Contains('\\'))
            throw new ArgumentException("Invalid storage object path or bucket.", nameof(objectPath));

        var path = string.Join('/', segments.Select(Uri.EscapeDataString));
        var url = new Uri(project, $"/storage/v1/{operation}/{Uri.EscapeDataString(bucket)}/{path}");
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.Add("apikey", key);
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (Exception error) when (error is HttpRequestException or IOException
            || (error is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw Unavailable(error);
        }

        if (response.IsSuccessStatusCode
            || (request.Method == HttpMethod.Delete && response.StatusCode == HttpStatusCode.NotFound))
            return response;

        response.Dispose();
        throw Unavailable();
    }

    // Header validation does not decode the image. The upload workflow must also verify decode before use.
    private static (string Extension, string MediaType) PhotoType(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith(new byte[] { 0xff, 0xd8, 0xff }))
            return ("jpg", "image/jpeg");
        if (content.StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }))
            return ("png", "image/png");
        if (content.Length >= 12 && content[..4].SequenceEqual("RIFF"u8) && content[8..12].SequenceEqual("WEBP"u8))
            return ("webp", "image/webp");

        throw new FileStorageException("Chỉ chấp nhận ảnh JPEG, PNG hoặc WebP.", "UNSUPPORTED_FILE");
    }

    private static FileStorageException Unavailable(Exception? cause = null) =>
        new("Không thể truy cập kho lưu trữ ảnh.", "STORAGE_UNAVAILABLE", cause);
}
