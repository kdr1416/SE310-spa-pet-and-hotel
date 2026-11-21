using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using PetSpa.Core.Common;
using PetSpa.Api.Services.Storage;

namespace PetSpa.Tests;

public class CoreEnvelopeTests
{
    [Fact]
    public void Ok_SerializesWithWebCamelCase_AndSuccessfulData()
    {
        var response = ApiResponse<object>.Ok(new { maDon = 7 });

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var root = json.RootElement;

        Assert.Equal(new[] { "success", "data", "message" },
            root.EnumerateObject().Select(property => property.Name));
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal(7, root.GetProperty("data").GetProperty("maDon").GetInt32());
        Assert.Equal("Thành công", root.GetProperty("message").GetString());
    }

    [Fact]
    public void Fail_SerializesNullData_AndFieldErrorsWithTraceId()
    {
        var response = ApiResponse<object>.Fail("Dữ liệu không hợp lệ.", "VALIDATION_FAILED",
            [new ApiErrorItem { Field = "tenDangNhap", Message = "Bắt buộc nhập." }], "validation-trace");

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var root = json.RootElement;

        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("data").ValueKind);
        Assert.Equal("Dữ liệu không hợp lệ.", root.GetProperty("message").GetString());
        Assert.Equal("VALIDATION_FAILED", root.GetProperty("errorCode").GetString());
        Assert.Equal("tenDangNhap", root.GetProperty("errors")[0].GetProperty("field").GetString());
        Assert.Equal("Bắt buộc nhập.", root.GetProperty("errors")[0].GetProperty("message").GetString());
        Assert.Equal("validation-trace", root.GetProperty("traceId").GetString());
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task UnhandledException_ReturnsInternalServerErrorEnvelope_WithContextTraceId(string environment)
    {
        using var factory = CreateFactory(environment);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/_tests/core-envelope/throw");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal(new[] { "success", "data", "message", "errorCode", "errors", "traceId" },
            root.EnumerateObject().Select(property => property.Name));
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("data").ValueKind);
        Assert.Equal("Đã xảy ra lỗi hệ thống nội bộ.", root.GetProperty("message").GetString());
        Assert.Equal("INTERNAL_SERVER_ERROR", root.GetProperty("errorCode").GetString());
        Assert.Empty(root.GetProperty("errors").EnumerateArray());
        Assert.Equal("core-envelope-test-trace", root.GetProperty("traceId").GetString());
        Assert.DoesNotContain("Test exception detail", body);
        Assert.DoesNotContain("InvalidOperationException", body);
    }

    [Fact]
    public async Task SuccessfulController_ReturnsOkEnvelope_ThroughThePipeline()
    {
        using var factory = CreateFactory("Production");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/_tests/core-envelope/success");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(7, json.RootElement.GetProperty("data").GetProperty("maDon").GetInt32());
        Assert.Equal("Thành công", json.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task ProtectedRoute_WithoutJwt_ReturnsContractError()
    {
        using var factory = CreateFactory("Testing");
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/internal/invoices/1");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("UNAUTHENTICATED", json.RootElement.GetProperty("errorCode").GetString());
        Assert.Empty(json.RootElement.GetProperty("errors").EnumerateArray());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("traceId").GetString()));
        Assert.NotEmpty(response.Headers.WwwAuthenticate);
    }

    [Fact]
    public async Task Login_LimitsRequestsPerClientIp_AndReturnsRetryAfter()
    {
        using var factory = CreateFactory("Testing").WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["RateLimiting:Login:PermitLimit"] = "1" })));
        using var client = factory.CreateClient();
        var first = await SendLoginAsync("192.0.2.1");
        var blocked = await SendLoginAsync("192.0.2.1");
        var other = await SendLoginAsync("192.0.2.2");
        Assert.Equal(400, first.Response.StatusCode);
        Assert.Equal(429, blocked.Response.StatusCode);
        Assert.InRange(int.Parse(blocked.Response.Headers["Retry-After"].ToString()), 1, 60);
        using var json = await JsonDocument.ParseAsync(blocked.Response.Body);
        Assert.Equal("RATE_LIMITED", json.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal(400, other.Response.StatusCode);

        Task<Microsoft.AspNetCore.Http.HttpContext> SendLoginAsync(string ip) =>
            factory.Server.SendAsync(context =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
                context.Request.Scheme = "https";
                context.Request.Method = "POST";
                context.Request.Path = "/api/auth/login";
                context.Request.ContentType = "application/json";
                context.Request.Body = new MemoryStream("{}"u8.ToArray());
            });
    }

    [Fact]
    public async Task UnavailableStorage_Returns503WithContractEnvelope()
    {
        using var factory = CreateFactory("Testing");
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/_tests/core-envelope/storage");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("STORAGE_UNAVAILABLE", json.RootElement.GetProperty("errorCode").GetString());
        Assert.Empty(json.RootElement.GetProperty("errors").EnumerateArray());
    }

    private static WebApplicationFactory<Program> CreateFactory(string environment) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureTestServices(services =>
                services.AddControllers().AddApplicationPart(typeof(CoreEnvelopeTestController).Assembly));
        });
}

// Registered only by the test factory; no diagnostic endpoint ships in the API.
[ApiController]
[Route("_tests/core-envelope")]
public class CoreEnvelopeTestController : ControllerBase
{
    [HttpGet("throw")]
    public IActionResult Throw()
    {
        HttpContext.TraceIdentifier = "core-envelope-test-trace";
        Response.StatusCode = 202;
        Response.ContentType = "text/plain";
        throw new InvalidOperationException("Test exception detail must stay internal.");
    }

    [HttpGet("storage")]
    public IActionResult Storage() =>
        throw new FileStorageException("Không thể truy cập kho lưu trữ ảnh.", "STORAGE_UNAVAILABLE");

    [HttpGet("success")]
    public IActionResult Success() => Ok(ApiResponse<object>.Ok(new { maDon = 7 }));
}
