using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PetSpa.Api.Data;
using PetSpa.Api.Data.Seeding;
using PetSpa.Api.Middleware;
using PetSpa.Api.Services.Identity;
using PetSpa.Core.Common;

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

builder.Services.AddScoped<ITokenService, TokenService>();

var jwtSecretKey = builder.Configuration["Jwt:SecretKey"];
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        if (!string.IsNullOrWhiteSpace(jwtSecretKey))
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "PetSpaApi",
                ValidateAudience = true,
                ValidAudience = builder.Configuration["Jwt:Audience"] ?? "PetSpaClient",
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey)),
                NameClaimType = "username",
                RoleClaimType = "role",
                ClockSkew = TimeSpan.Zero
            };
        }
    });

builder.Services.AddOpenApi();
builder.Services.AddDbContext<PetSpaDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

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

app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors("AllowFrontend");
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
