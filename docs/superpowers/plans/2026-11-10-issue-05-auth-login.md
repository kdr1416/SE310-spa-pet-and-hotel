# Issue #5: API Đăng Nhập Nhân Viên & Cấp JWT Token (E01) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Triển khai endpoint `POST /api/auth/login` (E01) cấp JWT token và trả về thông tin nhân viên kèm danh mục quyền hạn (FR01) theo chuẩn envelope `ApiResponse<Session>` bằng phương pháp TDD.

**Architecture:** Inject trực tiếp `PetSpaDbContext` và `ITokenService` vào `AuthController`. `TokenService` chịu trách nhiệm đọc cấu hình và tạo token JWT chuẩn HMAC-SHA256 (claims `id`, `username`, `role`, `jti`). Controller xác thực mật khẩu qua `PasswordHasher<NhanVien>`. Toàn bộ lỗi validation và sai mật khẩu được framework/controller chuẩn hóa thành HTTP 400 `VALIDATION_FAILED` bọc trong `ApiResponse<T>`.

**Tech Stack:** .NET 10 Web API, Entity Framework Core, PostgreSQL, `Microsoft.AspNetCore.Authentication.JwtBearer`, xUnit, FluentAssertions, `WebApplicationFactory<Program>`.

**Spec:** [`docs/superpowers/specs/2026-11-09-issue-05-auth-login-design.md`](../specs/2026-11-09-issue-05-auth-login-design.md)

---

## Global Constraints
- Schema Freeze: 33 bảng Entity, DbContext và Migrations đã đóng băng — tuyệt đối không sửa đổi hay tạo migration mới.
- Kiến trúc tối giản: Tuyệt đối không tạo Repository Pattern, UnitOfWork, MediatR hay CQRS.
- Bảo mật thông tin: Không commit Secret Key thật lên Git; chỉ dùng User Secrets / biến môi trường hoặc in-memory test configuration.
- Single Source of Truth: Toàn bộ DTOs và Response Schema phải khớp chính xác với `docs/design/api-contract.md` (mục 2 E01 và mục 3.2 DTOs `Login`, `Session`, `Employee`).
- Chống User Enumeration: Mọi lỗi sai tài khoản, mật khẩu, tài khoản vô hiệu hóa đều trả về cùng một thông báo chung: `"Tên đăng nhập hoặc mật khẩu không chính xác."`.

---

## Review Focus
- Input rỗng hoặc thiếu trường bắt buộc $\rightarrow$ HTTP 400 `VALIDATION_FAILED` kèm mảng `errors` đúng format envelope.
- Tên đăng nhập không tồn tại $\rightarrow$ HTTP 400 `VALIDATION_FAILED` với message `"Tên đăng nhập hoặc mật khẩu không chính xác."`.
- Tài khoản có `IsActive = false` $\rightarrow$ HTTP 400 `VALIDATION_FAILED` với message `"Tên đăng nhập hoặc mật khẩu không chính xác."`.
- Nhập sai mật khẩu $\rightarrow$ HTTP 400 `VALIDATION_FAILED` với message `"Tên đăng nhập hoặc mật khẩu không chính xác."`.
- Token JWT cấp ra phải hợp lệ về mặt mật mã học $\rightarrow$ `JwtSecurityTokenHandler.ValidateToken` thành công với SecretKey, Issuer, Audience và Lifetime hợp lệ.

---

## File Structure Map
- **Modify:** `backend/src/PetSpa.Api/PetSpa.Api.csproj` (thêm package `Microsoft.AspNetCore.Authentication.JwtBearer`)
- **Modify:** `backend/src/PetSpa.Api/Program.cs` (cấu hình `InvalidModelStateResponseFactory`, JWT Auth, DI cho `ITokenService`)
- **Create:** `backend/src/PetSpa.Core/DTOs/Identity/Login.cs`
- **Create:** `backend/src/PetSpa.Core/DTOs/Identity/Employee.cs`
- **Create:** `backend/src/PetSpa.Core/DTOs/Identity/Session.cs`
- **Create:** `backend/src/PetSpa.Api/Services/Identity/ITokenService.cs`
- **Create:** `backend/src/PetSpa.Api/Services/Identity/TokenService.cs`
- **Create:** `backend/src/PetSpa.Api/Controllers/Identity/AuthController.cs`
- **Create:** `backend/tests/PetSpa.Tests/Identity/TokenServiceTests.cs`
- **Create:** `backend/tests/PetSpa.Tests/Identity/AuthLoginTests.cs`
- **Create:** `backend/tests/PetSpa.Tests/Infrastructure/AuthApplicationFactory.cs`

---

## Tasks

### Task 1: Git Branch & Package Dependency Setup

**Files:**
- Modify: `backend/src/PetSpa.Api/PetSpa.Api.csproj`

**Interfaces:**
- Consumes: None
- Produces: Nhánh `feat/issue-05-auth-login` từ `origin/develop`, package `Microsoft.AspNetCore.Authentication.JwtBearer` sẵn sàng trong solution.

- [ ] **Step 1: Checkout develop mới nhất và tạo nhánh tính năng**
```powershell
git checkout develop
git pull origin develop
git checkout -b feat/issue-05-auth-login
```

- [ ] **Step 2: Thêm package `Microsoft.AspNetCore.Authentication.JwtBearer` vào `PetSpa.Api.csproj`**
Thêm PackageReference:
```xml
<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.3" />
```

- [ ] **Step 3: Chạy build để xác nhận dependencies hợp lệ**
Run: `dotnet build backend/PetSpa.sln -m:1`
Expected: Build succeeded with 0 errors.

- [ ] **Step 4: Commit**
```powershell
git add backend/src/PetSpa.Api/PetSpa.Api.csproj
git commit -m "chore(identity): add Microsoft.AspNetCore.Authentication.JwtBearer dependency"
```

---

### Task 2: Core Identity DTOs (`Login`, `Employee`, `Session`)

**Files:**
- Create: `backend/src/PetSpa.Core/DTOs/Identity/Login.cs`
- Create: `backend/src/PetSpa.Core/DTOs/Identity/Employee.cs`
- Create: `backend/src/PetSpa.Core/DTOs/Identity/Session.cs`

**Interfaces:**
- Consumes: None
- Produces: 
  - `PetSpa.Core.DTOs.Identity.Login`
  - `PetSpa.Core.DTOs.Identity.Employee`
  - `PetSpa.Core.DTOs.Identity.Session`

- [ ] **Step 1: Tạo `Login.cs`**
Tạo file `backend/src/PetSpa.Core/DTOs/Identity/Login.cs`:
```csharp
using System.ComponentModel.DataAnnotations;

namespace PetSpa.Core.DTOs.Identity;

public class Login
{
    [Required(ErrorMessage = "Tên đăng nhập không được để trống.")]
    [MaxLength(50, ErrorMessage = "Tên đăng nhập tối đa 50 ký tự.")]
    public string TenDangNhap { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mật khẩu không được để trống.")]
    [MaxLength(128, ErrorMessage = "Mật khẩu tối đa 128 ký tự.")]
    public string MatKhau { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Tạo `Employee.cs`**
Tạo file `backend/src/PetSpa.Core/DTOs/Identity/Employee.cs`:
```csharp
namespace PetSpa.Core.DTOs.Identity;

public class Employee
{
    public int MaNhanVien { get; set; }
    public string TenNhanVien { get; set; } = string.Empty;
    public string TenDangNhap { get; set; } = string.Empty;
    public string VaiTro { get; set; } = string.Empty;
    public List<string> Quyen { get; set; } = new();
    public bool DangHoatDong { get; set; }
}
```

- [ ] **Step 3: Tạo `Session.cs`**
Tạo file `backend/src/PetSpa.Core/DTOs/Identity/Session.cs`:
```csharp
namespace PetSpa.Core.DTOs.Identity;

public class Session
{
    public string AccessToken { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
    public Employee NhanVien { get; set; } = new();
}
```

- [ ] **Step 4: Build project PetSpa.Core**
Run: `dotnet build backend/src/PetSpa.Core -m:1`
Expected: Build succeeded with 0 errors.

- [ ] **Step 5: Commit**
```powershell
git add backend/src/PetSpa.Core/DTOs/Identity/
git commit -m "feat(identity): define Login, Employee, and Session DTOs"
```

---

### Task 3: TokenService & Cryptographic Verification (TDD)

**Files:**
- Create: `backend/src/PetSpa.Api/Services/Identity/ITokenService.cs`
- Create: `backend/src/PetSpa.Api/Services/Identity/TokenService.cs`
- Test: `backend/tests/PetSpa.Tests/Identity/TokenServiceTests.cs`

**Interfaces:**
- Consumes: `PetSpa.Core.Entities.Identity.NhanVien`, `Microsoft.Extensions.Configuration.IConfiguration`
- Produces: `ITokenService.GenerateToken(NhanVien nhanVien) -> (string Token, int ExpiresInSeconds)`

- [ ] **Step 1: Viết failing test `TokenServiceTests`**
Tạo file `backend/tests/PetSpa.Tests/Identity/TokenServiceTests.cs`:
```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using PetSpa.Api.Services.Identity;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Enums;
using Xunit;

namespace PetSpa.Tests.Identity;

public class TokenServiceTests
{
    [Fact]
    public void GenerateToken_ProducesValidCryptographicJwt_WithExpectedClaimsAndExpiry()
    {
        // Arrange
        const string secretKey = "test_secret_key_at_least_32_bytes_long_entropy_123456789!";
        const string issuer = "PetSpaTestIssuer";
        const string audience = "PetSpaTestAudience";
        const int expiryMinutes = 480;

        var inMemoryConfig = new Dictionary<string, string?>
        {
            ["Jwt:SecretKey"] = secretKey,
            ["Jwt:Issuer"] = issuer,
            ["Jwt:Audience"] = audience,
            ["Jwt:ExpiryMinutes"] = expiryMinutes.ToString()
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();
        ITokenService tokenService = new TokenService(configuration);

        var nhanVien = new NhanVien
        {
            Id = 42,
            TenDangNhap = "staff_test",
            TenNhanVien = "Nguyễn Văn Test",
            VaiTro = Role.STAFF,
            IsActive = true
        };

        // Act
        var (token, expiresIn) = tokenService.GenerateToken(nhanVien);

        // Assert
        token.Should().NotBeNullOrWhiteSpace();
        expiresIn.Should().Be(expiryMinutes * 60);

        var tokenHandler = new JwtSecurityTokenHandler();
        tokenHandler.CanReadToken(token).Should().BeTrue();

        var validationParams = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
            ClockSkew = TimeSpan.Zero
        };

        var principal = tokenHandler.ValidateToken(token, validationParams, out var validatedToken);
        principal.FindFirst("id")?.Value.Should().Be("42");
        principal.FindFirst("username")?.Value.Should().Be("staff_test");
        principal.FindFirst("role")?.Value.Should().Be("STAFF");
        principal.FindFirst("jti")?.Value.Should().NotBeNullOrWhiteSpace();
    }
}
```

- [ ] **Step 2: Chạy test để xác nhận FAIL**
Run: `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~TokenServiceTests" -m:1`
Expected: FAIL vì `ITokenService` và `TokenService` chưa tồn tại.

- [ ] **Step 3: Tạo `ITokenService.cs`**
Tạo file `backend/src/PetSpa.Api/Services/Identity/ITokenService.cs`:
```csharp
using PetSpa.Core.Entities.Identity;

namespace PetSpa.Api.Services.Identity;

public interface ITokenService
{
    (string Token, int ExpiresInSeconds) GenerateToken(NhanVien nhanVien);
}
```

- [ ] **Step 4: Triển khai `TokenService.cs`**
Tạo file `backend/src/PetSpa.Api/Services/Identity/TokenService.cs`:
```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using PetSpa.Core.Entities.Identity;

namespace PetSpa.Api.Services.Identity;

public class TokenService(IConfiguration configuration) : ITokenService
{
    public (string Token, int ExpiresInSeconds) GenerateToken(NhanVien nhanVien)
    {
        var secretKey = configuration["Jwt:SecretKey"]
            ?? throw new InvalidOperationException("Jwt:SecretKey is not configured.");
        var issuer = configuration["Jwt:Issuer"] ?? "PetSpaApi";
        var audience = configuration["Jwt:Audience"] ?? "PetSpaClient";
        var expiryMinutesStr = configuration["Jwt:ExpiryMinutes"];
        var expiryMinutes = int.TryParse(expiryMinutesStr, out var m) ? m : 480;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim("id", nhanVien.Id.ToString()),
            new Claim("username", nhanVien.TenDangNhap),
            new Claim("role", nhanVien.VaiTro.ToString()),
            new Claim("jti", Guid.NewGuid().ToString())
        };

        var expires = DateTime.UtcNow.AddMinutes(expiryMinutes);

        var tokenDescriptor = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds
        );

        var tokenString = new JwtSecurityTokenHandler().WriteToken(tokenDescriptor);
        return (tokenString, expiryMinutes * 60);
    }
}
```

- [ ] **Step 5: Chạy test để xác nhận PASS**
Run: `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~TokenServiceTests" -m:1`
Expected: PASS (1 passed).

- [ ] **Step 6: Commit**
```powershell
git add backend/src/PetSpa.Api/Services/Identity/ backend/tests/PetSpa.Tests/Identity/TokenServiceTests.cs
git commit -m "feat(identity): implement TokenService with cryptographic verification tests"
```

---

### Task 4: Cấu hình `Program.cs` (Envelope Validation & JWT Authentication)

**Files:**
- Modify: `backend/src/PetSpa.Api/Program.cs`

**Interfaces:**
- Consumes: `ApiBehaviorOptions`, `JwtBearerDefaults`, `ITokenService`, `TokenService`
- Produces: Toàn bộ controller tự động trả envelope chuẩn khi ModelState invalid; JWT Authentication middleware sẵn sàng.

- [ ] **Step 1: Cấu hình `Program.cs`**
Cập nhật `backend/src/PetSpa.Api/Program.cs`:
1. Xóa inbound claim type mapping:
   ```csharp
   System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();
   ```
2. Cấu hình `InvalidModelStateResponseFactory` trong `builder.Services.AddControllers()`:
   ```csharp
   builder.Services.AddControllers()
       .ConfigureApiBehaviorOptions(options =>
       {
           options.InvalidModelStateResponseFactory = context =>
           {
               var errors = context.ModelState
                   .Where(e => e.Value?.Errors.Count > 0)
                   .SelectMany(e => e.Value!.Errors.Select(err => new PetSpa.Core.Common.ApiErrorItem
                   {
                       Field = System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(e.Key),
                       Message = err.ErrorMessage
                   }))
                   .ToList();

               var response = PetSpa.Core.Common.ApiResponse<object>.Fail(
                   "Dữ liệu không hợp lệ.",
                   "VALIDATION_FAILED",
                   errors,
                   context.HttpContext.TraceIdentifier);

               return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(response);
           };
       });
   ```
3. Đăng ký `ITokenService`:
   ```csharp
   builder.Services.AddScoped<PetSpa.Api.Services.Identity.ITokenService, PetSpa.Api.Services.Identity.TokenService>();
   ```
4. Cấu hình JWT Bearer Authentication:
   ```csharp
   var jwtSecretKey = builder.Configuration["Jwt:SecretKey"];
   builder.Services.AddAuthentication(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
       .AddJwtBearer(options =>
       {
           if (!string.IsNullOrWhiteSpace(jwtSecretKey))
           {
               options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
               {
                   ValidateIssuer = true,
                   ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "PetSpaApi",
                   ValidateAudience = true,
                   ValidAudience = builder.Configuration["Jwt:Audience"] ?? "PetSpaClient",
                   ValidateLifetime = true,
                   ValidateIssuerSigningKey = true,
                   IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwtSecretKey)),
                   NameClaimType = "username",
                   RoleClaimType = "role",
                   ClockSkew = TimeSpan.Zero
               };
           }
       });
   ```
5. Đặt `app.UseAuthentication();` ngay trước `app.UseAuthorization();`.

- [ ] **Step 2: Build toàn bộ solution**
Run: `dotnet build backend/PetSpa.sln -m:1`
Expected: Build succeeded with 0 errors.

- [ ] **Step 3: Chạy test hiện có để đảm bảo không hồi quy**
Run: `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName!~SeedDataTests" -m:1`
Expected: Tất cả 28 tests pass.

- [ ] **Step 4: Commit**
```powershell
git add backend/src/PetSpa.Api/Program.cs
git commit -m "feat(identity): configure InvalidModelStateResponseFactory, JWT bearer authentication, and TokenService DI"
```

---

### Task 5: AuthController & Integration Tests (TDD cho Endpoint E01)

**Files:**
- Create: `backend/tests/PetSpa.Tests/Infrastructure/AuthApplicationFactory.cs`
- Create: `backend/tests/PetSpa.Tests/Identity/AuthLoginTests.cs`
- Create: `backend/src/PetSpa.Api/Controllers/Identity/AuthController.cs`

**Interfaces:**
- Consumes: `PetSpaDbContext`, `ITokenService`, `Login`, `Session`, `Employee`, `ApiResponse<T>`
- Produces: Endpoint `POST /api/auth/login` (E01)

- [ ] **Step 1: Tạo `AuthApplicationFactory.cs`**
Tạo file `backend/tests/PetSpa.Tests/Infrastructure/AuthApplicationFactory.cs` để inject cấu hình test an toàn:
```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PetSpa.Api.Data;

namespace PetSpa.Tests.Infrastructure;

public static class AuthApplicationFactory
{
    public const string TestJwtSecret = "petspa_integration_test_secret_key_entropy_256_bits_length_secure!";

    public static WebApplicationFactory<Program> Create(Action<PetSpaDbContext>? seedDatabase = null)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Jwt:SecretKey"] = TestJwtSecret,
                    ["Jwt:Issuer"] = "PetSpaApi",
                    ["Jwt:Audience"] = "PetSpaClient",
                    ["Jwt:ExpiryMinutes"] = "480",
                    ["SeedData:Enabled"] = "false"
                }));

            builder.ConfigureServices(services =>
            {
                // Remove Npgsql DbContext registration and replace with isolated in-memory for fast auth integration tests
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<PetSpaDbContext>));
                if (descriptor != null) services.Remove(descriptor);

                var dbName = "PetSpa_AuthTest_" + Guid.NewGuid().ToString("N");
                services.AddDbContext<PetSpaDbContext>(options =>
                    options.UseInMemoryDatabase(dbName));

                var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<PetSpaDbContext>();
                db.Database.EnsureCreated();
                seedDatabase?.Invoke(db);
            });
        });
    }
}
```
*(Lưu ý: Package `Microsoft.EntityFrameworkCore.InMemory` cần được kiểm tra hoặc thêm vào `PetSpa.Tests.csproj` nếu cần; nếu `SeedApplicationFactory` dùng Postgres, ta có thể dùng `PostgresTestDatabase` hoặc in-memory nếu đã cài đặt).*

- [ ] **Step 2: Viết full test suite `AuthLoginTests.cs` (Sad Paths + Happy Path)**
Tạo file `backend/tests/PetSpa.Tests/Identity/AuthLoginTests.cs`:
```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Identity;
using PetSpa.Core.Entities.Identity;
using PetSpa.Core.Enums;
using PetSpa.Tests.Infrastructure;
using Xunit;

namespace PetSpa.Tests.Identity;

public class AuthLoginTests
{
    [Fact]
    public async Task Login_EmptyPayload_Returns400ValidationFailed()
    {
        using var factory = AuthApplicationFactory.Create();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<Session>>();
        body.Should().NotBeNull();
        body!.Success.Should().BeFalse();
        body.ErrorCode.Should().Be("VALIDATION_FAILED");
        body.Errors.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Login_NonExistentUser_Returns400ValidationFailed()
    {
        using var factory = AuthApplicationFactory.Create();
        using var client = factory.CreateClient();

        var payload = new Login { TenDangNhap = "unknown_user", MatKhau = "Password123@" };
        var response = await client.PostAsJsonAsync("/api/auth/login", payload);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<Session>>();
        body!.Success.Should().BeFalse();
        body.ErrorCode.Should().Be("VALIDATION_FAILED");
        body.Message.Should().Be("Tên đăng nhập hoặc mật khẩu không chính xác.");
    }

    [Fact]
    public async Task Login_InactiveUser_Returns400ValidationFailed()
    {
        using var factory = AuthApplicationFactory.Create(db =>
        {
            var user = new NhanVien
            {
                TenDangNhap = "locked_user",
                TenNhanVien = "Bị Khóa",
                VaiTro = Role.STAFF,
                IsActive = false
            };
            user.MatKhauHash = new PasswordHasher<NhanVien>().HashPassword(user, "Password123@");
            db.NhanViens.Add(user);
            db.SaveChanges();
        });
        using var client = factory.CreateClient();

        var payload = new Login { TenDangNhap = "locked_user", MatKhau = "Password123@" };
        var response = await client.PostAsJsonAsync("/api/auth/login", payload);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<Session>>();
        body!.Success.Should().BeFalse();
        body.ErrorCode.Should().Be("VALIDATION_FAILED");
        body.Message.Should().Be("Tên đăng nhập hoặc mật khẩu không chính xác.");
    }

    [Fact]
    public async Task Login_WrongPassword_Returns400ValidationFailed()
    {
        using var factory = AuthApplicationFactory.Create(db =>
        {
            var user = new NhanVien
            {
                TenDangNhap = "staff1",
                TenNhanVien = "Nguyễn Văn Staff",
                VaiTro = Role.STAFF,
                IsActive = true
            };
            user.MatKhauHash = new PasswordHasher<NhanVien>().HashPassword(user, "CorrectPassword123@");
            db.NhanViens.Add(user);
            db.SaveChanges();
        });
        using var client = factory.CreateClient();

        var payload = new Login { TenDangNhap = "staff1", MatKhau = "WrongPassword123@" };
        var response = await client.PostAsJsonAsync("/api/auth/login", payload);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<Session>>();
        body!.Success.Should().BeFalse();
        body.ErrorCode.Should().Be("VALIDATION_FAILED");
        body.Message.Should().Be("Tên đăng nhập hoặc mật khẩu không chính xác.");
    }

    [Fact]
    public async Task Login_ValidCredentials_Returns200WithCryptographicallyValidJwtAndSession()
    {
        using var factory = AuthApplicationFactory.Create(db =>
        {
            var user = new NhanVien
            {
                TenDangNhap = "admin",
                TenNhanVien = "Quản trị viên",
                VaiTro = Role.ADMIN,
                IsActive = true
            };
            user.MatKhauHash = new PasswordHasher<NhanVien>().HashPassword(user, "Admin@123");
            db.NhanViens.Add(user);

            var q1 = new Quyen { MaQuyen = "BOOKING_APPROVE", TenQuyen = "Duyệt đơn", Module = "Booking" };
            var q2 = new Quyen { MaQuyen = "CARE_UPDATE", TenQuyen = "Cập nhật chăm sóc", Module = "Care" };
            db.Quyens.AddRange(q1, q2);

            db.NhanVienQuyens.AddRange(
                new NhanVienQuyen { NhanVien = user, MaQuyen = "BOOKING_APPROVE" },
                new NhanVienQuyen { NhanVien = user, MaQuyen = "CARE_UPDATE" }
            );
            db.SaveChanges();
        });
        using var client = factory.CreateClient();

        var payload = new Login { TenDangNhap = "admin", MatKhau = "Admin@123" };
        var response = await client.PostAsJsonAsync("/api/auth/login", payload);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<Session>>();
        body!.Success.Should().BeTrue();
        body.Message.Should().Be("Đăng nhập thành công");
        body.Data.Should().NotBeNull();

        var session = body.Data!;
        session.ExpiresIn.Should().Be(28800);
        session.NhanVien.TenDangNhap.Should().Be("admin");
        session.NhanVien.TenNhanVien.Should().Be("Quản trị viên");
        session.NhanVien.VaiTro.Should().Be("ADMIN");
        session.NhanVien.DangHoatDong.Should().BeTrue();
        session.NhanVien.Quyen.Should().Contain(new[] { "BOOKING_APPROVE", "CARE_UPDATE" });

        // Cryptographic verification of returned JWT
        var tokenHandler = new JwtSecurityTokenHandler();
        var validationParams = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "PetSpaApi",
            ValidateAudience = true,
            ValidAudience = "PetSpaClient",
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthApplicationFactory.TestJwtSecret)),
            ClockSkew = TimeSpan.Zero
        };

        var principal = tokenHandler.ValidateToken(session.AccessToken, validationParams, out _);
        principal.FindFirst("username")?.Value.Should().Be("admin");
        principal.FindFirst("role")?.Value.Should().Be("ADMIN");
    }
}
```

- [ ] **Step 3: Chạy test để xác nhận FAIL đúng lý do**
Run: `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~AuthLoginTests" -m:1`
Expected: FAIL vì endpoint `POST /api/auth/login` chưa được cài đặt (404 Not Found).

- [ ] **Step 4: Cài đặt `AuthController.cs`**
Tạo file `backend/src/PetSpa.Api/Controllers/Identity/AuthController.cs`:
```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PetSpa.Api.Data;
using PetSpa.Api.Services.Identity;
using PetSpa.Core.Common;
using PetSpa.Core.DTOs.Identity;
using PetSpa.Core.Entities.Identity;

namespace PetSpa.Api.Controllers.Identity;

[ApiController]
[Route("api/auth")]
public class AuthController(PetSpaDbContext dbContext, ITokenService tokenService) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<Session>>> Login([FromBody] Login request)
    {
        var nhanVien = await dbContext.NhanViens
            .Include(nv => nv.NhanVienQuyenNhanVienItems)
            .FirstOrDefaultAsync(nv => nv.TenDangNhap == request.TenDangNhap);

        if (nhanVien == null || !nhanVien.IsActive)
        {
            return BadRequest(ApiResponse<Session>.Fail(
                "Tên đăng nhập hoặc mật khẩu không chính xác.",
                "VALIDATION_FAILED",
                null,
                HttpContext.TraceIdentifier));
        }

        var hasher = new PasswordHasher<NhanVien>();
        var verifyResult = hasher.VerifyHashedPassword(nhanVien, nhanVien.MatKhauHash, request.MatKhau);
        if (verifyResult == PasswordVerificationResult.Failed)
        {
            return BadRequest(ApiResponse<Session>.Fail(
                "Tên đăng nhập hoặc mật khẩu không chính xác.",
                "VALIDATION_FAILED",
                null,
                HttpContext.TraceIdentifier));
        }

        var (token, expiresIn) = tokenService.GenerateToken(nhanVien);

        var session = new Session
        {
            AccessToken = token,
            ExpiresIn = expiresIn,
            NhanVien = new Employee
            {
                MaNhanVien = nhanVien.Id,
                TenNhanVien = nhanVien.TenNhanVien,
                TenDangNhap = nhanVien.TenDangNhap,
                VaiTro = nhanVien.VaiTro.ToString(),
                Quyen = nhanVien.NhanVienQuyenNhanVienItems.Select(q => q.MaQuyen).ToList(),
                DangHoatDong = nhanVien.IsActive
            }
        };

        return Ok(ApiResponse<Session>.Ok(session, "Đăng nhập thành công"));
    }
}
```

- [ ] **Step 5: Chạy lại test suite `AuthLoginTests`**
Run: `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~AuthLoginTests" -m:1`
Expected: PASS (5/5 tests passed).

- [ ] **Step 6: Commit**
```powershell
git add backend/src/PetSpa.Api/Controllers/Identity/ backend/tests/PetSpa.Tests/
git commit -m "feat(identity): implement POST /api/auth/login with full TDD coverage"
```

---

### Task 6: Toàn Diện Hóa Kiểm Thử & Kiểm Tra Hồi Quy (Verification)

**Files:**
- Toàn bộ files đã sửa đổi

**Interfaces:**
- Toàn bộ test suite trong `backend/PetSpa.sln`

- [ ] **Step 1: Chạy toàn bộ test suite non-postgres**
Run: `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName!~SeedDataTests" -m:1`
Expected: Toàn bộ tests pass (33+ tests), không có bất kỳ warning/lỗi nào.

- [ ] **Step 2: Kiểm tra Git status và code format**
Run: `git status`
Expected: Working tree clean, sẵn sàng mở PR.
