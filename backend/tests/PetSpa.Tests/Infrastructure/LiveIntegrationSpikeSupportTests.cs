using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using PetSpa.Api.Services.Storage;
using static PetSpa.Tests.Infrastructure.LiveIntegrationSpikeTests;

namespace PetSpa.Tests.Infrastructure;

public sealed class LiveIntegrationSpikeSupportTests
{
    private const string Path = "00112233445566778899aabbccddeeff.png";
    private const string Secret = "DO_NOT_DISCLOSE_SENTINEL";
    private const string Settings = "{\"otpEmail\":false,\"cocHotel\":true,\"giuCho\":true}";
    private static readonly string[] Tables = Enumerable.Range(1, 33).Select(i => $"public.table_{i}").ToArray();
    private static readonly string[] Codes = ["BINH_THUONG", "ROI_NHE", "ROI_NANG", "VE_RAN", "LONG_NGAN", "LONG_DAI"];
    private static string FurJson => JsonSerializer.Serialize(Codes.Select(code => new { ma = code }));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("false")]
    [InlineData("1")]
    [InlineData(" true ")]
    public async Task DisabledGuard_DoesNotReadConfigurationOrInvokeService(string? flag)
    {
        var messages = new List<string>();
        await RunIfEnabledAsync(flag, messages.Add, () => throw new InvalidOperationException(Secret));
        Assert.Contains(messages, message => message.Contains("NO-OP", StringComparison.Ordinal));
        Assert.DoesNotContain(Secret, string.Join("", messages));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    public async Task EnabledGuard_RunsServiceExactlyOnce(string flag)
    {
        var calls = 0;
        await RunIfEnabledAsync(flag, _ => { }, () => { calls++; return Task.CompletedTask; });
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task EnabledGuard_PropagatesSanitizedFailureWithoutRetryOrInnerException()
    {
        var calls = 0;
        var error = await Assert.ThrowsAsync<LiveSpikeException>(() => RunIfEnabledAsync("true", _ => { }, () =>
        {
            calls++;
            throw new IOException($"https://example.test/?token={Secret}");
        }));
        Assert.Equal(1, calls);
        Assert.Contains("IOException", error.Message);
        Assert.DoesNotContain(Secret, error.ToString());
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void MissingSetting_IdentifiesKeyWithoutConfigurationDump()
    {
        var config = Configuration(new() { ["OtherSecret"] = Secret });
        var error = Assert.Throws<LiveSpikeException>(() => RequireSetting(config, "Smtp:Host"));
        Assert.Contains("Smtp:Host", error.Message);
        Assert.DoesNotContain(Secret, error.ToString());
    }

    [Fact]
    public void NeonState_AllExpectedTablesWithExtraHistoryAndValidConfigurationPass()
    {
        VerifyNeonState(Tables, [.. Tables, "public.__EFMigrationsHistory"], FurJson, Settings);
    }

    [Fact]
    public void NeonState_EqualTableCountDoesNotHideMissingApplicationTable()
    {
        var error = Assert.Throws<LiveSpikeException>(() =>
            VerifyNeonState(Tables, [.. Tables.Skip(1), "public.unrelated"], FurJson, Settings));
        Assert.Contains("public.table_1", error.Message);
    }

    [Fact]
    public void NeonState_MissingOrDuplicatedFurCodeFails()
    {
        var missing = JsonSerializer.Serialize(Codes.Skip(1).Select(code => new { ma = code }));
        var duplicate = JsonSerializer.Serialize(Codes.Append(Codes[0]).Select(code => new { ma = code }));
        Assert.Throws<LiveSpikeException>(() => VerifyNeonState(Tables, Tables, missing, Settings));
        Assert.Throws<LiveSpikeException>(() => VerifyNeonState(Tables, Tables, duplicate, Settings));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"otpEmail\":false,\"cocHotel\":true,\"giuCho\":false}")]
    [InlineData("{\"otpEmail\":\"false\",\"cocHotel\":false,\"giuCho\":false}")]
    [InlineData("not-json")]
    public void NeonState_InvalidExtensionConfigurationFails(string settings) =>
        Assert.Throws<LiveSpikeException>(() => VerifyNeonState(Tables, Tables, FurJson, settings));

    [Theory]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(404)]
    public async Task Storage_RoundTripUsesAnonymousDownloadAndDeletesOwnObject(int deniedStatus)
    {
        var storage = new StorageStub();
        using var client = new HttpClient(new StorageHandler(storage, deniedStatus));
        await VerifyStorageRoundTripAsync(storage, client, new Uri("https://storage.example.test"), "staging", _ => { }, default);
        Assert.Equal(new[] { "upload", "sign", "signed-get", "public-get", "delete" }, storage.Events);
        Assert.Equal(Path, storage.DeletedPath);
    }

    [Fact]
    public async Task Storage_UploadsAValidOnePixelPngWithCorrectChunkChecksums()
    {
        var storage = new StorageStub();
        using var client = new HttpClient(new StorageHandler(storage, 404));
        await VerifyStorageRoundTripAsync(storage, client, new Uri("https://storage.example.test"), "staging", _ => { }, default);
        var png = storage.Uploaded;
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
        using var compressed = new MemoryStream();
        var chunks = new List<string>();
        for (var offset = 8; offset < png.Length;)
        {
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset, 4)));
            var type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            chunks.Add(type);
            Assert.InRange(length, 0, png.Length - offset - 12);
            // PNG CRC covers the chunk type and data, independently of the uploader.
            uint crc = uint.MaxValue;
            for (var i = offset + 4; i < offset + 8 + length; i++)
            {
                crc ^= png[i];
                for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320u : crc >> 1;
            }
            Assert.Equal(BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8 + length, 4)), ~crc);
            if (type == "IHDR")
            {
                Assert.Equal(1u, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8, 4)));
                Assert.Equal(1u, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 12, 4)));
            }
            if (type == "IDAT") compressed.Write(png, offset + 8, length);
            offset += length + 12;
        }
        Assert.Equal(new[] { "IHDR", "IDAT", "IEND" }, chunks);
        Assert.Equal(new byte[] { 8, 6, 0, 0, 0 }, png[24..29]);
        compressed.Position = 0;
        using var inflate = new ZLibStream(compressed, CompressionMode.Decompress);
        using var pixels = new MemoryStream();
        await inflate.CopyToAsync(pixels);
        Assert.Equal(new byte[] { 0, 0, 0, 0, 255 }, pixels.ToArray());
    }

    [Theory]
    [InlineData(200)]
    [InlineData(302)]
    [InlineData(500)]
    public async Task Storage_PublicSuccessRedirectOrServerErrorIsNotPrivateEvidence(int publicStatus)
    {
        var storage = new StorageStub();
        using var client = new HttpClient(new StorageHandler(storage, publicStatus));
        await Assert.ThrowsAsync<LiveSpikeException>(() =>
            VerifyStorageRoundTripAsync(storage, client, new Uri("https://storage.example.test"), "staging", _ => { }, default));
        Assert.Equal(Path, storage.DeletedPath);
    }

    [Theory]
    [InlineData(true, 200)]
    [InlineData(false, 503)]
    public async Task Storage_CorruptOrFailedDownloadFailsAndStillCleansUp(bool corrupt, int signedStatus)
    {
        var storage = new StorageStub();
        using var client = new HttpClient(new StorageHandler(storage, 404, signedStatus, corrupt));
        await Assert.ThrowsAsync<LiveSpikeException>(() =>
            VerifyStorageRoundTripAsync(storage, client, new Uri("https://storage.example.test"), "staging", _ => { }, default));
        Assert.Equal(Path, storage.DeletedPath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Storage_CleanupFailureReportsObjectPathAndPreservesMainFailureWithoutLeaking(bool mainFails)
    {
        using var cancellation = new CancellationTokenSource();
        var storage = new StorageStub { FailDelete = true, CancelAfterSign = mainFails ? cancellation : null };
        var output = new List<string>();
        using var client = new HttpClient(new StorageHandler(storage, 404));
        var error = await Assert.ThrowsAsync<LiveSpikeException>(() =>
            VerifyStorageRoundTripAsync(storage, client, new Uri("https://storage.example.test"), "staging", output.Add, cancellation.Token));
        Assert.Contains("CLEANUP_FAILED", error.Message);
        Assert.Contains(Path, error.Message);
        if (mainFails) Assert.Contains("DOWNLOAD", error.Message);
        Assert.False(storage.DeleteTokenWasCanceled);
        Assert.DoesNotContain(Secret, error.ToString() + string.Join("", output));
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task Storage_UploadFailureDoesNotDeleteAnUnknownObject()
    {
        var storage = new StorageStub { FailUpload = true };
        using var client = new HttpClient(new StorageHandler(storage, 404));
        var error = await Assert.ThrowsAsync<LiveSpikeException>(() =>
            VerifyStorageRoundTripAsync(storage, client, new Uri("https://storage.example.test"), "staging", _ => { }, default));
        Assert.Null(storage.DeletedPath);
        Assert.DoesNotContain(Secret, error.ToString());
    }

    [Theory]
    [InlineData("Smtp:Host")]
    [InlineData("Smtp:From")]
    [InlineData("Smtp:Password")]
    [InlineData("LiveSpike:EmailRecipient")]
    public void Smtp_MissingConfigurationFailsWithoutLeaking(string missing)
    {
        var settings = SmtpSettings();
        settings.Remove(missing);
        var error = Assert.Throws<LiveSpikeException>(() => ValidateSmtpConfiguration(Configuration(settings)));
        Assert.Contains(missing, error.Message);
        Assert.DoesNotContain(Secret, error.ToString());
    }

    [Theory]
    [InlineData("25", "true")]
    [InlineData("587", "false")]
    [InlineData("not-a-port", "true")]
    public void Smtp_InvalidPortOrTlsFails(string port, string ssl)
    {
        var settings = SmtpSettings();
        settings["Smtp:Port"] = port;
        settings["Smtp:EnableSsl"] = ssl;
        Assert.Throws<LiveSpikeException>(() => ValidateSmtpConfiguration(Configuration(settings)));
    }

    [Theory]
    [InlineData("465")]
    [InlineData("587")]
    public void Smtp_ValidTlsAllowsRelayWithoutAuthentication(string port)
    {
        var settings = SmtpSettings();
        settings["Smtp:Port"] = port;
        settings.Remove("Smtp:Username");
        settings.Remove("Smtp:Password");
        ValidateSmtpConfiguration(Configuration(settings));
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static Dictionary<string, string?> SmtpSettings() => new()
    {
        ["Smtp:Host"] = "smtp.example.test", ["Smtp:Port"] = "587", ["Smtp:EnableSsl"] = "true",
        ["Smtp:From"] = "sender@example.test", ["Smtp:Username"] = "sender@example.test",
        ["Smtp:Password"] = Secret, ["LiveSpike:EmailRecipient"] = "recipient@example.test"
    };

    private sealed class StorageStub : IFileStorageService
    {
        public List<string> Events { get; } = [];
        public byte[] Uploaded { get; private set; } = [];
        public string? DeletedPath { get; private set; }
        public bool DeleteTokenWasCanceled { get; private set; }
        public bool FailDelete { get; init; }
        public bool FailUpload { get; init; }
        public CancellationTokenSource? CancelAfterSign { get; init; }
        public Task<string> UploadPhotoAsync(byte[] content, CancellationToken cancellationToken = default)
        {
            Events.Add("upload");
            if (FailUpload) throw new IOException(Secret);
            Uploaded = content;
            return Task.FromResult(Path);
        }
        public Task<Uri> CreateReadUrlAsync(string objectPath, CancellationToken cancellationToken = default)
        {
            Assert.Equal(Path, objectPath);
            Events.Add("sign");
            CancelAfterSign?.Cancel();
            return Task.FromResult(new Uri($"https://storage.example.test/storage/v1/object/sign/staging/{Path}?token={Secret}"));
        }
        public Task DeleteAsync(string objectPath, CancellationToken cancellationToken = default)
        {
            Events.Add("delete");
            DeletedPath = objectPath;
            DeleteTokenWasCanceled = cancellationToken.IsCancellationRequested;
            if (FailDelete) throw new IOException(Secret);
            return Task.CompletedTask;
        }
    }

    private sealed class StorageHandler(StorageStub storage, int publicStatus, int signedStatus = 200, bool corrupt = false) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("apikey"));
            var isSigned = request.RequestUri!.AbsolutePath.Contains("/object/sign/", StringComparison.Ordinal);
            storage.Events.Add(isSigned ? "signed-get" : "public-get");
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)(isSigned ? signedStatus : publicStatus))
            {
                Content = new ByteArrayContent(corrupt ? [1, 2, 3] : storage.Uploaded)
            });
        }
    }
}
