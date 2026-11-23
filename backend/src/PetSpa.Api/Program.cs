using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PetSpa.Api.Data;
using PetSpa.Api.Data.Seeding;
using PetSpa.Api.Middleware;
using PetSpa.Api.Services.Billing;
using PetSpa.Api.Services.Booking;
using PetSpa.Api.Services.Care;
using PetSpa.Api.Services.Identity;
using PetSpa.Api.Services.Storage;
using PetSpa.Api.Services;
using PetSpa.Core.Common;
using PetSpa.Core.Interfaces.Care;
using PetSpa.Core.Interfaces.Billing;
using PetSpa.Core.Services;

JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(e => e.Value?.Errors.Count > 0)
                .SelectMany(e => e.Value!.Errors.Select(err => new ApiErrorItem
                {
                    Field = JsonNamingPolicy.CamelCase.ConvertName(e.Key),
                    Message = err.ErrorMessage
                }))
                .ToList();

            var response = ApiResponse<object>.Fail(
                "Dữ liệu không hợp lệ.",
                "VALIDATION_FAILED",
                errors,
                context.HttpContext.TraceIdentifier);

            return new BadRequestObjectResult(response);
        };
    });

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
    foreach (var proxy in builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
        options.KnownProxies.Add(IPAddress.Parse(proxy));
});

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown", _ =>
        {
            var config = context.RequestServices.GetRequiredService<IConfiguration>();
            return new FixedWindowRateLimiterOptions
            {
                PermitLimit = config.GetValue("RateLimiting:Login:PermitLimit", 20),
                Window = TimeSpan.FromSeconds(config.GetValue("RateLimiting:Login:WindowSeconds", 60)),
                QueueLimit = 0,
                AutoReplenishment = true
            };
        }));
    options.OnRejected = async (rejection, cancellationToken) =>
    {
        var context = rejection.HttpContext;
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        var retryAfter = rejection.Lease.TryGetMetadata(MetadataName.RetryAfter, out var estimate)
            ? estimate.TotalSeconds
            : context.RequestServices.GetRequiredService<IConfiguration>().GetValue("RateLimiting:Login:WindowSeconds", 60);
        context.Response.Headers.RetryAfter = Math.Max(1, Math.Ceiling(retryAfter)).ToString(CultureInfo.InvariantCulture);
        await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail(
            "Vui lòng thử đăng nhập lại sau.", "RATE_LIMITED", traceId: context.TraceIdentifier), cancellationToken);
    };
});

builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAvailabilityService, AvailabilityService>();
builder.Services.AddScoped<SpaCapacityCalculator>();
builder.Services.AddScoped<HotelCapacityCalculator>();
builder.Services.AddScoped<IDepositExpiryService, DepositExpiryService>();
builder.Services.AddScoped<IBillingSettlementHelper, BillingSettlementHelper>();
builder.Services.AddHostedService<DepositExpiryBackgroundService>();
builder.Services.AddScoped<BookingCreateService>();
builder.Services.AddScoped<BookingProofService>();
builder.Services.AddScoped<BookingManagementService>();
builder.Services.AddScoped<CustomerLookupService>();
builder.Services.AddScoped<BookingOptionsService>();
builder.Services.AddScoped<FurConditionsService>();
builder.Services.AddScoped<TrackingService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<CareOperation>();
builder.Services.AddScoped<ICheckinService, CheckinService>();
builder.Services.AddScoped<ICareService, CareService>();
builder.Services.AddScoped<IHandoverService, HandoverService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async challenge =>
            {
                challenge.HandleResponse();
                var context = challenge.HttpContext;
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.WWWAuthenticate = JwtBearerDefaults.AuthenticationScheme;
                await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail(
                    "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", "UNAUTHENTICATED",
                    traceId: context.TraceIdentifier), context.RequestAborted);
            }
        };
    });

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IConfiguration>((options, config) =>
    {
        var jwtSecretKey = config["Jwt:SecretKey"];
        if (!string.IsNullOrWhiteSpace(jwtSecretKey))
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = config["Jwt:Issuer"] ?? "PetSpaApi",
                ValidateAudience = true,
                ValidAudience = config["Jwt:Audience"] ?? "PetSpaClient",
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey)),
                NameClaimType = "username",
                RoleClaimType = "role",
                ClockSkew = TimeSpan.Zero
            };
        }
    });

builder.Services.AddHttpClient<IFileStorageService, SupabaseFileStorage>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

builder.Services.AddOpenApi();
builder.Services.AddDbContext<PetSpaDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<ICatalogService, CatalogService>();

// CORS configuration for local frontend development
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

if (app.Configuration.GetValue<bool>("SeedData:Enabled"))
{
    var options = app.Configuration.GetSection("SeedData").Get<SeedDataOptions>() ?? new SeedDataOptions();
    using var scope = app.Services.CreateScope();
    await DbInitializer.InitializeAsync(scope.ServiceProvider.GetRequiredService<PetSpaDbContext>(), options, app.Environment);
}

if (app.Configuration.GetValue<bool>("CatalogSeed:Enabled"))
{
    using var scope = app.Services.CreateScope();
    await CatalogSeed.InitializeAsync(scope.ServiceProvider.GetRequiredService<PetSpaDbContext>(),
        app.Configuration.GetValue<int>("CatalogSeed:AdminId"));
}

app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.UseRouting();
app.UseCors("AllowFrontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Health check endpoint for integration testing and monitoring
app.MapGet("/api/health", () => Results.Ok(new
{
    status = "Healthy",
    timestamp = DateTime.UtcNow,
    environment = app.Environment.EnvironmentName
})).WithName("HealthCheck");

app.MapControllers();

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }
