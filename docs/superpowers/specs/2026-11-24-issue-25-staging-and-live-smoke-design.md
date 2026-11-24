# Issue #25 / Task 12 — Staging và Live Integration Smoke Tests

Ngày: 2026-11-24. Trạng thái: **ĐÃ DUYỆT (APPROVED)** bởi Tech Lead sau EGM Review.

Nguồn: Task 12 trong [Sprint 2 plan](../../plans/sprint-2/sprint-2-plan.md), kết quả kiểm toán [EGM Review](../../../brain/c199dc75-b33c-403b-bf0a-905655b796c2/intermediate_zoom_out_report.md) ngày 2026-11-24, và [API contract](../../design/api-contract.md). Thiết kế này chuẩn hóa cơ chế kết nối Neon, đơn giản hóa kiểm thử theo YAGNI, phòng thủ rò rỉ credential và hoàn thiện runbook vận hành cho Staging.

---

## 1. Mục đích và phạm vi

### 1.1 Problem Statement
Dự án cần một môi trường Staging hoàn chỉnh trên nền tảng đám mây (Neon PostgreSQL, Supabase Storage, SMTP MailKit) với schema 33 bảng và dữ liệu bootstrap chuẩn để phục vụ nghiệm thu tích hợp. Trước Task 12:
- Cơ chế EF Core migration ngoại tuyến bị chặn bởi `PetSpaDbContextFactory` do không truyền được chuỗi kết nối vào DbContext.
- `DbInitializer` chỉ cho phép seed trên `Development` hoặc `Testing`, chặn hoàn toàn `Staging`.
- Chưa có cơ chế xác minh tích hợp (live smoke test) để chứng minh kết nối thực tế tới ba dịch vụ đám mây mà không gây ảnh hưởng đến pipeline CI/CD ngoại tuyến.

### 1.2 Goals
1. Cấu hình cơ chế migration qua direct connection và runtime qua pooled connection theo thông số Neon Console.
2. Cho phép bootstrap dữ liệu trên `Staging` có kiểm soát thông qua `SeedData:Enabled=true` và mật khẩu cấp ngoài source control.
3. Cung cấp bộ ba test tích hợp trực tiếp (`LiveIntegrationSpikeTests.cs`) opt-in qua biến môi trường `PETSPA_LIVE_SPIKE_ENABLED=true` để kiểm tra độc lập Neon, Supabase Storage và SMTP MailKit.
4. Xây dựng runbook chi tiết trong `backend/README.md` để Tech Lead tự thao tác hạ tầng ngoài repository, kiểm tra tính sẵn sàng của API runtime (`GET /api/health`, `GET /api/catalog`), chuẩn bị bàn giao môi trường cho đợt kiểm thử giao diện `#27 Browser E2E smoke` theo biên bản bàn giao `#26 Frontend Spa UI` → `#27`.

### 1.3 Non-Goals
1. Không triển khai tự động hóa CI/CD deploy hoặc cấu hình máy chủ hosting mới.
2. Không thay đổi cơ chế xác thực JWT, không thêm chức năng đổi/reset mật khẩu trên API.
3. Không thực hiện browser test tự động (thuộc phạm vi Issue #27).
4. Không triển khai endpoint hợp đồng `E57` (`PUT /api/admin/extension-settings`).
5. Không thay đổi chính sách CORS cho domain hosting bên ngoài (giữ nguyên cho phép `http://localhost:5173` và `http://127.0.0.1:5173`).
6. Không dùng dữ liệu hoặc tài nguyên Production cho kiểm thử live smoke.

---

## 2. Các quyết định kỹ thuật đã thống nhất

1. **Kết nối Neon PostgreSQL:** `ConnectionStrings:MigrationConnection` trỏ vào direct endpoint (phục vụ `dotnet ef database update`); `ConnectionStrings:DefaultConnection` trỏ vào pooled endpoint (phục vụ API runtime và live smoke test). Lấy host và port trực tiếp từ Neon Console (mặc định cùng cổng 5432, phân biệt bằng hostname `-pooler`), không ép cứng cổng 6543.
2. **Cơ chế EF Core Migration:** `PetSpaDbContextFactory.CreateDbContext(string[] args)` ưu tiên đọc `ConnectionStrings__MigrationConnection` từ biến môi trường. Khi biến này chưa được thiết lập, factory giữ nguyên fallback về `.UseNpgsql()` không tham số hiện tại để bảo đảm công cụ design-time local (`dotnet ef migrations add`) hoạt động bình thường mà không cần credentials.
3. **Bootstrap Staging có kiểm soát:** Cho phép chạy `DbInitializer.InitializeAsync` khi môi trường là `Staging` VÀ cấu hình `SeedData:Enabled=true`. Mật khẩu seed cho ba tài khoản (`SeedData__AdminPassword`, `SeedData__Staff1Password`, `SeedData__Staff2Password`) được cấp qua biến môi trường ngoài Git. Sau khi hoàn tất bootstrap, cờ `SeedData:Enabled` được tắt ngay và dịch vụ được khởi động lại. Môi trường `Production` tiếp tục bị chặn tuyệt đối.
4. **Bộ smoke test độc lập (YAGNI):** Chạy ba smoke test độc lập dùng trực tiếp client/adapter hiện có, không khởi động API qua `WebApplicationFactory` trong live test để tránh kích hoạt background worker hoặc seeder ngoài ý muốn.
5. **Kích hoạt Opt-In an toàn:** Bộ test live chỉ thực thi khi biến môi trường `PETSPA_LIVE_SPIKE_ENABLED` có giá trị chính xác là `true`. Khi cờ này tắt hoặc không có, toàn bộ test live lập tức pass dạng no-op để bảo vệ CI/CD offline.
6. **Kiểm thử Storage theo đường dẫn Production:** Live smoke test gọi trực tiếp `IFileStorageService.UploadPhotoAsync(byte[])` hiện có, lưu file phẳng `{Guid:N}.png` vào private bucket Staging, tạo Signed URL đọc lại, xác nhận tính private bằng request không chứng thực, và xóa file trong khối `finally`. Nếu bước dọn dẹp thất bại, test in rõ `objectPath` / `objectKey` đã upload để phục vụ dọn dẹp thủ công. Không thêm overload hay logic prefix vào production code `SupabaseFileStorage.cs`.
7. **Bảo toàn kiểm thử hồi quy cục bộ:** Toàn bộ test suite ngoại tuyến tiếp tục sử dụng PostgreSQL local độc lập thông qua biến `PETSPA_TEST_POSTGRES`.

---

## 3. Các phương án đã xem xét (Alternatives Analysis)

| Hạng mục kiến trúc | Phương án chọn | Phương án thay thế / từ chối | Lý do lựa chọn & Đánh đổi |
|---|---|---|---|
| **Cơ chế áp dụng Schema Migration** | **Factory đọc biến môi trường:** `PetSpaDbContextFactory` đọc `ConnectionStrings__MigrationConnection`, fallback `.UseNpgsql()` | **1. Mở rộng tham số CLI:** `dotnet ef --connection "$val"`<br/>**2. Xuất script SQL:** `dotnet ef migrations script -i` | Phương án CLI `--connection` bị EF Core bỏ qua khi có factory và làm lộ mật khẩu trong bảng tiến trình OS (Process Table). Xuất script SQL an toàn nhưng tăng thao tác thủ công, được giữ làm phương án dự phòng. Phương án chọn vừa bảo đảm tự động hóa, vừa an toàn tuyệt đối cho local dev. |
| **Kiến trúc bộ Live Smoke Test** | **Ba test độc lập gọi adapter trực tiếp:** `NpgsqlConnection`, `SupabaseFileStorage`, `SmtpEmailSender` | **Khởi động API qua `WebApplicationFactory`** | `WebApplicationFactory` kích hoạt hosted background services (`DepositExpiryBackgroundService`) và startup seeder trên Staging database, gây tác dụng phụ khó kiểm soát. Ba test độc lập cho kết quả cô lập từng dịch vụ đám mây. |
| **Đường dẫn lưu ảnh Smoke Test** | **Sử dụng đường dẫn production:** Gọi `UploadPhotoAsync(byte[])`, lưu `{Guid:N}.png` tại root bucket Staging | **Bổ sung overload prefix `smoke-tests/` vào `SupabaseFileStorage`** | Production lưu ảnh phẳng tại root bucket. Staging đã có private bucket riêng biệt. Việc sửa production code chỉ để phục vụ smoke test là vi phạm YAGNI. Test đường dẫn production bảo đảm độ tin cậy thực tế cao nhất. |
| **Xác thực tính Private của Bucket** | **Kiểm thử hành vi (Behavioral Assertion):** Gửi GET không chứng thực tới URL public, xác nhận mã 400/403/404 | **Gọi Supabase Admin REST API:** `GET /storage/v1/bucket/{id}` | API quản trị đòi hỏi token admin và thêm DTO/helper phức tạp. Kiểm thử hành vi xác nhận trực tiếp tính riêng tư từ góc nhìn người dùng bên ngoài một cách đơn giản, đáng tin cậy. |
| **Kiểm thử gửi Email SMTP** | **Gửi email thật qua MailKit:** Gửi mã tracking mẫu tới hộp thư test được chỉ định | **Mock / Giả lập relay cục bộ (Mailpit/MailHog)** | Mục tiêu của Task 12 là xác minh kết nối mạng và xác thực thực tế tới máy chủ SMTP đám mây. Dùng mock cục bộ không phát hiện được sự cố xác thực, chặn cổng hoặc cấu hình TLS từ hạ tầng thật. |

---

## 4. Cấu hình và bootstrap hạ tầng

### 4.1 Cấu hình không chứa secrets
File `backend/src/PetSpa.Api/appsettings.json` chỉ chứa cấu trúc khung và giá trị mặc định không nhạy cảm:
- `ConnectionStrings:DefaultConnection` và `ConnectionStrings:MigrationConnection` để chuỗi rỗng `""`.
- Cấu hình Supabase chuẩn hóa theo adapter hiện hữu: `Supabase:Storage:Url`, `Supabase:Storage:Bucket`, `Supabase:Storage:ServiceRoleKey` để trống.
- `SeedData:Enabled=false`, `CatalogSeed:Enabled=false`. Không chứa mật khẩu trong JSON.
- Cấu hình SMTP: `Smtp:Host`, `Smtp:Port` (587), `Smtp:EnableSsl` (true), `Smtp:From`, `Smtp:Username`, `Smtp:Password` để giá trị mẫu/trống.

Biến môi trường sử dụng định dạng phân cấp chuẩn của .NET (`__` thay cho `:`). Không ghi nhận secret vào mã nguồn Git hoặc shell history.

### 4.2 Thực thi Migration trên Neon
1. Tech Lead lấy direct connection string từ Neon Console và thiết lập biến môi trường `ConnectionStrings__MigrationConnection`.
2. `PetSpaDbContextFactory` nạp connection string từ biến môi trường này:
   ```csharp
   public sealed class PetSpaDbContextFactory : IDesignTimeDbContextFactory<PetSpaDbContext>
   {
       public PetSpaDbContext CreateDbContext(string[] args)
       {
           var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__MigrationConnection");
           var builder = new DbContextOptionsBuilder<PetSpaDbContext>();
           if (!string.IsNullOrWhiteSpace(connectionString))
           {
               builder.UseNpgsql(connectionString);
           }
           else
           {
               builder.UseNpgsql();
           }
           return new PetSpaDbContext(builder.Options);
       }
   }
   ```
3. Lệnh thực thi migration trong runbook:
   ```powershell
   dotnet ef database update --project backend/src/PetSpa.Api --startup-project backend/src/PetSpa.Api
   ```
   Lệnh này tự động sử dụng chuỗi kết nối từ biến môi trường của tiến trình mà không truyền mật khẩu qua tham số dòng lệnh, tránh lộ lọt trong bảng tiến trình hệ điều hành.

### 4.3 Bootstrap dữ liệu trên Staging
1. Mở guard trong `DbInitializer.InitializeAsync` để chấp nhận `Development`, `Testing`, `Staging`. Môi trường `Production` tiếp tục bị chặn:
   ```csharp
   if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing") && !environment.IsEnvironment("Staging"))
       throw new InvalidOperationException("Seeding data is strictly prohibited in this environment.");
   ```
2. Cấp cấu hình bootstrap qua biến môi trường đầy đủ:
   - `SeedData__Enabled=true`
   - `SeedData__Date=YYYY-MM-DD` (ngày nghiệm thu theo múi giờ Việt Nam)
   - `SeedData__AdminPassword=<SecretAdmin12-128Chars>`
   - `SeedData__Staff1Password=<SecretStaff1Password>`
   - `SeedData__Staff2Password=<SecretStaff2Password>`
3. Dữ liệu seed khởi tạo:
   - Tài khoản Admin và Staff (lễ tân, chăm sóc) kèm phân quyền `NhanVienGrant`.
   - Bảng giá Size, danh mục dịch vụ, loại phòng và combo mẫu.
   - Ca làm việc và đăng ký ca trạng thái `APPROVED` cho ngày nghiệm thu.
   - `FUR_CONDITIONS`: Đầy đủ 6 mã mặc định: `BINH_THUONG`, `ROI_NHE`, `ROI_NANG`, `VE_RAN`, `LONG_NGAN`, `LONG_DAI`.
   - `EXTENSION_SETTINGS`: Mặc định `{"otpEmail":false,"cocHotel":false,"giuCho":false}`, tuân thủ bất biến logic `(!cocHotel || giuCho)`. Tuyệt đối không tự động kích hoạt tính năng khi chưa có yêu cầu nghiệp vụ.
4. Sau khi xác minh seed hoàn tất: Đặt `SeedData__Enabled=false`, xóa các biến mật khẩu khỏi môi trường runtime và restart API.

---

## 5. Hợp đồng bộ smoke test tích hợp trực tiếp

File `backend/tests/PetSpa.Tests/Infrastructure/LiveIntegrationSpikeTests.cs` triển khai ba test `[Fact]` độc lập, được đóng gói trong test collection tuần tự để tránh xung đột tài nguyên:

```csharp
[Collection("LiveIntegrationTests")]
public sealed class LiveIntegrationSpikeTests
```

### 5.1 Guard Opt-In chung
Ở đầu mỗi test method, kiểm tra biến môi trường:
```csharp
if (!string.Equals(Environment.GetEnvironmentVariable("PETSPA_LIVE_SPIKE_ENABLED"), "true", StringComparison.OrdinalIgnoreCase))
{
    output.WriteLine("NO-OP: Live integration tests are disabled. Set PETSPA_LIVE_SPIKE_ENABLED=true to run.");
    return;
}
```
Nếu cờ tắt, hàm ghi log và kết thúc thành công (pass nhanh, không ném exception, không đọc cấu hình nhạy cảm). Khi cờ bật, thiếu cấu hình hoặc lỗi dịch vụ bắt buộc phải fail test rõ ràng.

### 5.2 Test 1: Neon PostgreSQL (Chỉ đọc)
- Đọc cấu hình `ConnectionStrings__DefaultConnection` (pooled endpoint).
- Mở kết nối `NpgsqlConnection` thật tới Neon Staging.
- Lấy danh sách 33 tên bảng ứng dụng từ relational model của `PetSpaDbContext` và đối chiếu với bảng hiện hữu trong schema PostgreSQL; xác nhận đủ 33 bảng.
- Truy vấn bảng `cau_hinh_he_thong`:
  - Khóa `FUR_CONDITIONS`: Chứa JSON array có đủ 6 mã mặc định (`BINH_THUONG`, `ROI_NHE`, `ROI_NANG`, `VE_RAN`, `LONG_NGAN`, `LONG_DAI`).
  - Khóa `EXTENSION_SETTINGS`: Tồn tại 3 cờ boolean, thỏa mãn điều kiện logic `(!cocHotel || giuCho)`.
- Tuyệt đối không thực hiện ghi, xóa hay thay đổi dữ liệu trong test.

### 5.3 Test 2: Supabase Storage (Kiểm thử thực tế & Dọn dẹp an toàn)
- Đọc cấu hình đầy đủ: `Supabase__Storage__Url`, `Supabase__Storage__Bucket`, `Supabase__Storage__ServiceRoleKey`.
- Sử dụng trực tiếp instance `SupabaseFileStorage` (không thêm overload).
- **Upload:** Upload 1 file ảnh PNG 1×1 hợp lệ (byte tĩnh), nhận về `objectPath` dạng `{Guid:N}.png`.
- **Signed URL:** Gọi `CreateReadUrlAsync(objectPath)` (TTL 300 giây), dùng `HttpClient` không kèm auth header tải lại URL, xác nhận HTTP `200` và toàn vẹn dữ liệu byte.
- **Xác thực Private Bucket:** Dùng `HttpClient` gửi unauthenticated `GET` tới URL public `/storage/v1/object/public/{bucket}/{objectPath}`, xác nhận upstream từ chối truy cập (HTTP `400`, `403` hoặc `404`).
- **Cleanup trong `finally`:** Sử dụng `CancellationTokenSource` độc lập (timeout 15 giây) gọi `DeleteAsync(objectPath)`.
  - Nếu bước cleanup thất bại, test bắt buộc phải fail và **in rõ ràng `objectPath` / `objectKey` đã upload** trong exception/log để Tech Lead có thể định danh và dọn dẹp thủ công trên Supabase Console:
    ```csharp
    throw new InvalidOperationException($"CLEANUP_FAILED: Failed to delete smoke test object '{objectPath}' from bucket '{bucket}'. Please clean up manually in Supabase Console.", cleanupEx);
    ```

### 5.4 Test 3: SMTP MailKit (Kiểm thử gửi email)
- Đọc cấu hình: `Smtp__Host`, `Smtp__Port`, `Smtp__EnableSsl`, `Smtp__From`, `Smtp__Username`, `Smtp__Password`, và `LiveSpike__EmailRecipient`.
- Khởi tạo `SmtpEmailSender` với cấu hình môi trường.
- Gọi `SendTrackingCodeAsync` gửi một email chứa mã tracking mẫu (`SMOKE-TEST-{Guid:N}[..8]`) tới hộp thư test.
- Test pass xác nhận máy chủ SMTP đã chấp nhận message (acceptance). Runbook hướng dẫn Tech Lead kiểm tra hộp thư đến thực tế để xác nhận phân phối thành công (delivery).

---

## 6. Rủi ro và biện pháp giảm thiểu (Risks & Mitigations)

| # | Rủi ro tiềm ẩn | Mức độ | Biện pháp giảm thiểu kiến trúc & vận hành |
|---|---|---|---|
| **R1** | **Neon Cold-Start Timeout:** Neon bản miễn phí có thể tự động ngắt compute sau thời gian không hoạt động, gây timeout kết nối lần đầu. | Trung bình | Cấu hình timeout kết nối Npgsql tối thiểu 30 giây trong live test và runbook; hướng dẫn đánh thức endpoint trước khi chạy suite nghiệm thu. |
| **R2** | **Nhiễm dữ liệu seed vào Production:** Vô tình cấu hình `ASPNETCORE_ENVIRONMENT=Staging` nhưng chuỗi kết nối trỏ nhầm database Production. | Nghiêm trọng | Áp dụng phòng vệ 2 lớp: (1) `DbInitializer` chặn cứng `Production` trong code; (2) Mặc định `SeedData:Enabled=false` trong `appsettings.json`, chỉ bật qua biến môi trường khi bootstrap và tắt ngay sau khi hoàn tất. |
| **R3** | **Lộ lọt chuỗi kết nối qua dòng lệnh CLI:** Mở rộng biến môi trường chứa credential trên CLI làm lộ mật khẩu vào tiến trình OS. | Nghiêm trọng | Cập nhật `PetSpaDbContextFactory` tự nạp biến môi trường nội bộ. Lệnh CLI không truyền tham số chứa mật khẩu dạng bản rõ. |
| **R4** | **Tích tụ rác Storage khi mất kết nối mạng:** Sự cố mạng khiến bước cleanup trong test không thể xóa object vừa tạo. | Thấp | Khối `finally` sử dụng timeout token độc lập. Khi xóa lỗi, thông báo test ghi rõ chính xác tên file `objectPath` để Tech Lead dọn dẹp thủ công. |
| **R5** | **SMTP Quota & Blacklisting:** Gửi email test tự động nhiều lần làm cạn kiệt quota hoặc bị đánh dấu spam. | Thấp | Cờ opt-in mặc định tắt; chỉ chạy test khi có chủ đích; mã test không thực hiện retry tự động khi gửi email. |

---

## 7. Staging Runbook trong `backend/README.md`

Tài liệu `backend/README.md` được bổ sung mục hướng dẫn vận hành Staging:
1. **Chuẩn bị hạ tầng:** Tech Lead khởi tạo Neon branch trống, Supabase private bucket, và hộp thư test.
2. **Cấu hình môi trường:** Thiết lập các biến môi trường ngoài source control.
3. **Thực thi Migration:** Chạy `dotnet ef database update --project backend/src/PetSpa.Api --startup-project backend/src/PetSpa.Api` qua direct connection.
4. **Bootstrap dữ liệu:** Khởi động API với `ASPNETCORE_ENVIRONMENT=Staging` và `SeedData__Enabled=true` kèm mật khẩu; xác minh dữ liệu, tắt cờ seed và restart API.
5. **Kiểm tra API Runtime:**
   - `GET /api/health` trả HTTP 200 `Healthy`.
   - `GET /api/catalog` (E02) trả HTTP 200 chứa danh mục dữ liệu mẫu.
6. **Thực thi Live Smoke Test:** Chạy lệnh:
   ```powershell
   $env:PETSPA_LIVE_SPIKE_ENABLED="true"
   dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~LiveIntegrationSpikeTests" -m:1
   $env:PETSPA_LIVE_SPIKE_ENABLED="false"
   ```
7. **Bàn giao Issue #27:** Ghi nhận thông tin endpoint, tài khoản test và bằng chứng live phục vụ kiểm thử browser E2E (frontend chạy `VITE_USE_MOCK_API=false` với giao thức `http://localhost:5173`).

---

## 8. Phạm vi file dự kiến

| File | Thay đổi |
|---|---|
| `backend/src/PetSpa.Api/appsettings.json` | Chuẩn hóa cấu trúc cấu hình adapter, mặc định seed tắt, không chứa thông tin nhạy cảm. |
| `backend/src/PetSpa.Api/Data/PetSpaDbContextFactory.cs` | Ưu tiên nạp `ConnectionStrings__MigrationConnection` từ môi trường; giữ fallback `.UseNpgsql()` local. |
| `backend/src/PetSpa.Api/Data/Seeding/DbInitializer.cs` | Mở guard môi trường cho phép `Staging`, tiếp tục chặn cứng `Production`. |
| `backend/tests/PetSpa.Tests/Seeding/SeedDataTests.cs` | Thêm unit test kiểm tra guard Staging (chỉ seed khi enabled) và chặn Production. |
| `backend/tests/PetSpa.Tests/Infrastructure/LiveIntegrationSpikeTests.cs` | Cài đặt ba live smoke test opt-in (Neon, Supabase, SMTP) kèm in rõ `objectPath` nếu cleanup lỗi. |
| `backend/README.md` | Hướng dẫn runbook chi tiết cho Staging: migration, bootstrap, runtime verification, live spike và handoff. |
| `docs/plans/sprint-2/sprint-2-plan.md` | Đồng bộ mô tả Task 12, phạm vi file và các chỉ thị kỹ thuật đã duyệt. |
| `AGENTS.md`, `.codex/instructions.md` | Cập nhật quy ước cổng Neon theo hostname Console (`-pooler`). |

*(Lưu ý: Không thay đổi `SupabaseFileStorage.cs` và `SupabaseFileStorageTests.cs`, tuân thủ nguyên tắc YAGNI).*

---

## 9. Verification và Acceptance Criteria

### 9.1 Quy trình kiểm thử TDD
1. **RED:** Thêm test trong `SeedDataTests.cs` xác nhận `DbInitializer` cho phép Staging khi bật cờ và chặn khi tắt cờ/Production. Chạy test xác nhận thất bại do guard cũ.
2. **GREEN:** Cập nhật `DbInitializer.cs` và `PetSpaDbContextFactory.cs`, chạy lại test xác nhận pass 100%.
3. **Smoke Opt-In Verification:** Chạy `LiveIntegrationSpikeTests` với `PETSPA_LIVE_SPIKE_ENABLED` chưa bật, xác nhận test no-op pass nhanh.

### 9.2 Lệnh nghiệm thu chuẩn
Chạy kiểm thử toàn bộ backend suite với PostgreSQL local đã cấu hình:
```powershell
dotnet test backend/PetSpa.sln -m:1
```
Yêu cầu: **PASS 100%, 0 errors, 0 warnings**.

### 9.3 Acceptance Checklist
- [ ] `PetSpaDbContextFactory` đọc chuỗi kết nối từ biến môi trường khi có; giữ fallback local an toàn khi không có.
- [ ] Staging bootstrap thành công khi có cờ `SeedData:Enabled=true`; Production bị chặn tuyệt đối.
- [ ] Không có secret, credential hoặc URL có chữ ký rò rỉ trong log, git diff hay console output.
- [ ] Ba live smoke test no-op pass khi cờ tắt; khi cờ bật kiểm tra độc lập từng dịch vụ.
- [ ] Test Supabase upload và verify ảnh qua đường dẫn production, in rõ `objectPath` nếu cleanup thất bại.
- [ ] Runbook hướng dẫn đầy đủ quy trình từ migration, bootstrap, runtime health check đến bàn giao Issue #27.
