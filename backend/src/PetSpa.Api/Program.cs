using Microsoft.EntityFrameworkCore;
using PetSpa.Api.Data;
using PetSpa.Api.Data.Seeding;
using PetSpa.Api.Middleware;
using PetSpa.Api.Services.Billing;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddDbContext<PetSpaDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Cơ chế chuyển đổi linh hoạt: Mock Data vs Real Database
var useMockData = builder.Configuration.GetValue<bool>("UseMockData", false);
if (useMockData)
{
    builder.Services.AddScoped<ICatalogService, MockCatalogService>();
}
else
{
    builder.Services.AddScoped<ICatalogService, CatalogService>();
}

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