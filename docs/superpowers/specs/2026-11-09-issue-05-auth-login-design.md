# Thiết Kế Chi Tiết: Issue #5 — API Đăng Nhập Nhân Viên & Cấp JWT Token (E01) (v1.1)

- **Mã tính năng:** E01 (`POST /api/auth/login`)
- **Tài liệu tham chiếu:** [`docs/design/api-contract.md`](../design/api-contract.md), [`docs/design/system-design.md`](../design/system-design.md), [`docs/plans/github-issues-sprint-1.md`](../plans/github-issues-sprint-1.md)
- **Tác giả:** Pair Programming (Tech Lead & Antigravity)
- **Ngày lập:** 2026-11-09 (Cập nhật sau phản biện: 2026-11-10)
- **Trạng thái:** Chờ duyệt lại (Awaiting Approval v1.1)

---

## 1. Mục đích & Bối cảnh (Intent & The Why)
Cung cấp cổng đăng nhập tập trung, an toàn cho nhân viên nội bộ của hệ thống PetSpa (Admin, Lễ tân, Kỹ thuật viên chăm sóc). 
Sau khi xác thực danh tính, hệ thống cấp phát JWT Access Token và trả về thông tin nhân viên kèm danh mục quyền hạn hạt mịn (**FR01**), làm cơ sở để:
1. Client (Frontend) hiển thị đúng thông tin, điều hướng router và ẩn/hiện các nút bấm/menu theo quyền.
2. Đính kèm `Bearer <token>` vào tiêu đề các request gọi tới các endpoint nội bộ cần bảo vệ trong Sprint 1 (E10a, E11, E12, E15, v.v.).

---

## 2. Mục tiêu, Giới hạn & Chống mục tiêu (Goals, Non-Goals, Anti-Goals)

### Goals
- Cung cấp endpoint `POST /api/auth/login` (Public, `[AllowAnonymous]`).
- Xác thực `TenDangNhap` và `MatKhau` qua `Microsoft.AspNetCore.Identity.PasswordHasher<NhanVien>` (đồng nhất với cơ chế seed tại `DbInitializer.cs`).
- Chỉ cho phép tài khoản có `is_active == true` đăng nhập thành công.
- Sinh JWT Access Token ký bằng HMAC-SHA256, thời hạn đọc từ cấu hình (mặc định 480 phút = 28800 giây), chứa bộ claims chuẩn: `id` (mã nhân viên), `username` (tên đăng nhập), `role` (`ADMIN` hoặc `STAFF`).
- Đảm bảo tính nhất quán của Envelope phản hồi:
  - Mọi lỗi validation (DTO Data Annotations) và lỗi sai thông tin đăng nhập đều trả về HTTP 400 `ApiResponse<Session>.Fail("...", "VALIDATION_FAILED")` chuẩn envelope.
  - Thành công: HTTP 200 `ApiResponse<Session>.Ok(...)` chứa `accessToken`, `expiresIn`, `nhanVien` (`Employee`).
- Kiểm thử đầy đủ theo TDD với `WebApplicationFactory<Program>`, trong đó ca Happy Path kiểm tra toàn diện cả tính hợp lệ chữ ký, Issuer, Audience và Lifetime của token.

### Non-Goals
- Không hỗ trợ Refresh Token hay Blacklist / Revoke Token ở Sprint 1.
- Không mở rộng vai trò (`Role`) ngoài `ADMIN` và `STAFF` (tuân thủ quyết định D18).
- Không làm xác thực khách hàng (khách hàng tra cứu qua SĐT + mã tracking E08/E09).
- Không nhồi 24 quyền vào JWT payload (tuân thủ kiến trúc phân quyền Server-side).

### Anti-Goals & Architectural Risk Notes
- **Không vi phạm Schema Freeze:** Tuyệt đối không chỉnh sửa 33 Entity, DbContext hay tạo file Migration mới.
- **Không over-engineering:** Tuyệt đối không tạo Repository Pattern, UnitOfWork, MediatR hay CQRS.
- **Bảo mật chống User Enumeration:** Khi sai tài khoản, sai mật khẩu hoặc tài khoản bị khóa, luôn trả về cùng một thông điệp: `"Tên đăng nhập hoặc mật khẩu không chính xác."` nhằm ngăn chặn kẻ tấn công dò tìm tài khoản tồn tại (User Enumeration).
- **Ghi nhận rủi ro Token Revocation (Non-Goal):** Vì không có token revocation, tài khoản bị Admin vô hiệu hóa trong lúc token còn hạn vẫn có thể gửi request có chữ ký hợp lệ. Rủi ro này được triệt tiêu tại các endpoint nội bộ tiếp theo nhờ nguyên tắc thiết kế [`system-design.md` mục 3 dòng 92](../design/system-design.md#L92): *"Mọi request kiểm tra server; quyền và trạng thái tài khoản phải được xác thực dưới transaction/database request tiếp theo, không chỉ tin vào JWT còn hạn."*

---

## 3. Ràng buộc kỹ thuật & Quản lý bí mật (Security & Configuration)

### 3.1. Quản lý Secret Key (Bảo mật thông tin đăng nhập)
- **Tuyệt đối không commit Secret Key thật lên source control.**
- `appsettings.json` chỉ lưu cấu hình công khai:
  ```json
  "Jwt": {
    "Issuer": "PetSpaApi",
    "Audience": "PetSpaClient",
    "ExpiryMinutes": 480
  }
  ```
- `SecretKey` được nạp qua:
  - Môi trường Local Development: .NET User Secrets (`dotnet user-secrets set "Jwt:SecretKey" "<chuỗi ngẫu nhiên >= 256 bits>"`) hoặc biến môi trường `Jwt__SecretKey`.
  - Môi trường Testing: Inject key test ngẫu nhiên an toàn qua `AddInMemoryCollection` trong `WebApplicationFactory`.
  - Môi trường Production: Secret Manager / Environment Variables.

### 3.2. Chuẩn hóa Claims & Tránh trùng lặp Mapping
- Trong `Program.cs`, vô hiệu hóa cơ chế tự động mapping claim types mặc định của .NET:
  ```csharp
  JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();
  ```
- Bộ claim trong JWT được chuẩn hóa:
  - `id`: mã nhân viên (dạng chuỗi).
  - `username`: tên đăng nhập.
  - `role`: vai trò (`ADMIN` hoặc `STAFF`).
  - `jti`: UUID duy nhất của token.
- Cấu hình `TokenValidationParameters`:
  - `NameClaimType = "username"`
  - `RoleClaimType = "role"`
  - `ValidateIssuer = true`, `ValidateAudience = true`, `ValidateLifetime = true`, `ValidateIssuerSigningKey = true`.

### 3.3. Ranh giới giữa JWT Claims và Danh sách Quyền (FR01)
- **Token JWT:** Chỉ lưu định danh (`id`, `username`, `role`) để giữ token nhẹ và an toàn.
- **Response DTO `Session.nhanVien.quyen`:** Chứa danh sách mã quyền FR01 (`string[]`) truy vấn từ database để Frontend phục vụ mục đích render giao diện (ẩn/hiện menu, nút thao tác, client-side route guards).
- **Backend Authorization:** Ở các endpoint nghiệp vụ nội bộ (E10a, E11, E15, v.v.), backend sẽ kiểm tra quyền thực tế của nhân viên trên server (Database / Authorization policy) theo đúng `system-design.md`, đảm bảo tính an toàn độc lập với việc ẩn/hiện nút trên UI.

### 3.4. Thời hạn Token (Single Source of Truth)
- `ExpiryMinutes` được cấu hình tại một nơi duy nhất (`Jwt:ExpiryMinutes`).
- `TokenService` sử dụng `ExpiryMinutes` để đặt thời hạn cho JWT.
- `ExpiresIn` trong response DTO được tính toán tự động bằng công thức:
  $$\text{ExpiresIn} = \text{ExpiryMinutes} \times 60$$
  Tuyệt đối không hardcode giá trị giây độc lập trong Controller.

---

## 4. Thiết kế chi tiết thành phần (Detailed Specifications)

### 4.1. Cấu hình Envelope Validation trong `Program.cs`
Để đảm bảo framework không tự ý trả về `ValidationProblemDetails` mặc định khi vi phạm Data Annotations, ta cấu hình `InvalidModelStateResponseFactory`:
```csharp
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
```

### 4.2. DTOs (`PetSpa.Core/DTOs/Identity/`)
- `Login.cs`:
  ```csharp
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
- `Employee.cs`:
  ```csharp
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
- `Session.cs`:
  ```csharp
  public class Session
  {
      public string AccessToken { get; set; } = string.Empty;
      public int ExpiresIn { get; set; }
      public Employee NhanVien { get; set; } = new();
  }
  ```

### 4.3. Service cấp phát Token (`PetSpa.Api/Services/Identity/`)
- `ITokenService`:
  ```csharp
  public interface ITokenService
  {
      (string Token, int ExpiresInSeconds) GenerateToken(NhanVien nhanVien);
  }
  ```
- `TokenService`:
  - Đọc `SecretKey`, `Issuer`, `Audience`, `ExpiryMinutes` từ `IConfiguration`. Nếu thiếu `SecretKey` thì ném exception rõ ràng ngay lúc khởi động.
  - Tạo claims: `"id"`, `"username"`, `"role"`, `"jti"`.
  - Ký bằng `SymmetricSecurityKey` và `SecurityAlgorithms.HmacSha256`.
  - Trả về tuple `(Token: tokenString, ExpiresInSeconds: expiryMinutes * 60)`.

### 4.4. Controller (`PetSpa.Api/Controllers/Identity/AuthController.cs`)
- Route: `[HttpPost("api/auth/login")]`, gắn `[AllowAnonymous]`.
- Luồng thực thi:
  1. DTO được ASP.NET Core validate tự động thông qua `InvalidModelStateResponseFactory`.
  2. Truy vấn database qua `PetSpaDbContext`:
     ```csharp
     var nhanVien = await dbContext.NhanViens
         .Include(nv => nv.NhanVienQuyenNhanVienItems)
         .FirstOrDefaultAsync(nv => nv.TenDangNhap == request.TenDangNhap);
     ```
  3. Kiểm tra điều kiện xác thực:
     - Nếu `nhanVien == null` hoặc `!nhanVien.IsActive` $\rightarrow$ trả HTTP 400 `ApiResponse<Session>.Fail("Tên đăng nhập hoặc mật khẩu không chính xác.", "VALIDATION_FAILED")`.
     - Verify mật khẩu:
       ```csharp
       var hasher = new PasswordHasher<NhanVien>();
       var result = hasher.VerifyHashedPassword(nhanVien, nhanVien.MatKhauHash, request.MatKhau);
       if (result == PasswordVerificationResult.Failed)
       {
           return BadRequest(ApiResponse<Session>.Fail("Tên đăng nhập hoặc mật khẩu không chính xác.", "VALIDATION_FAILED"));
       }
       ```
  4. Tạo token qua `_tokenService.GenerateToken(nhanVien)`.
  5. Ánh xạ dữ liệu sang `Session`:
     - `AccessToken = tokenResult.Token`
     - `ExpiresIn = tokenResult.ExpiresInSeconds`
     - `NhanVien = new Employee { ... }` (danh sách `quyen = nhanVien.NhanVienQuyenNhanVienItems.Select(q => q.MaQuyen).ToList()`).
  6. Trả về HTTP 200 `ApiResponse<Session>.Ok(session, "Đăng nhập thành công")`.

---

## 5. Chiến lược kiểm thử theo TDD (Test Strategy)
Đặt trong `backend/tests/PetSpa.Tests/Identity/AuthLoginTests.cs`, kế thừa `WebApplicationFactory<Program>`:

1. **Sad Path 1 (Validation Error):** Gửi body rỗng hoặc thiếu trường $\rightarrow$ HTTP 400 `VALIDATION_FAILED`, có mảng `errors` chuẩn format envelope.
2. **Sad Path 2 (Tài khoản không tồn tại):** Gửi username không có trong DB $\rightarrow$ HTTP 400 `VALIDATION_FAILED` với message chuẩn.
3. **Sad Path 3 (Tài khoản bị khóa):** Tài khoản có `IsActive = false` $\rightarrow$ HTTP 400 `VALIDATION_FAILED`.
4. **Sad Path 4 (Sai mật khẩu):** Username đúng nhưng sai mật khẩu $\rightarrow$ HTTP 400 `VALIDATION_FAILED`.
5. **Happy Path:** Đăng nhập đúng tài khoản seed:
   - Trả về HTTP 200 OK, `success == true`.
   - `data.expiresIn == 28800`.
   - `data.nhanVien`: thông tin đầy đủ, `dangHoatDong == true`, danh sách `quyen` chính xác.
   - **Xác minh chữ ký điện tử Token (Cryptographic Verification):**
     Dùng `JwtSecurityTokenHandler.ValidateToken()` với `TokenValidationParameters` chứa đúng `SecretKey`, `Issuer`, `Audience` để chứng minh token hợp lệ về mặt mật mã học, không chỉ giải mã đọc payload thông thường.
