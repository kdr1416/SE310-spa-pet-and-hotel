# Issue #25 — Staging và Live Integration Smoke Tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cho Tech Lead bootstrap Neon Staging và xác minh Neon/Supabase/SMTP thật bằng ba smoke test opt-in, giữ toàn bộ backend regression PASS với 0 warnings.

**Architecture:** EF design-time factory đọc direct connection từ environment; API và live Neon test dùng pooled connection. Bootstrap Staging qua seed flag hiện có. Live tests gọi Npgsql và adapter hiện có, không khởi động API; runbook kiểm tra startup/DI bằng health và catalog HTTP thật.

**Tech Stack:** .NET 10, EF Core/Npgsql hiện có, Neon PostgreSQL, Supabase Storage REST qua SupabaseFileStorage, MailKit, xUnit 2.9.3, WebApplicationFactory cho regression local.

**Spec:** [Bản đã được Tech Lead chỉnh và duyệt](../specs/2026-11-24-issue-25-staging-and-live-smoke-design.md), ngày 2026-11-24. Bản này thay thiết kế cũ về prefix và migration CLI. Implementation và verification offline đã hoàn tất trong managed worktree; xem bằng chứng execution ở cuối tài liệu. Các checkpoint cloud vẫn chờ Tech Lead, không suy từ offline PASS.

**Bổ sung theo yêu cầu Tech Lead:** Ngày 2026-11-24, người dùng chọn lưu cấu hình local trong file `.env`. Đề xuất vị trí `backend/.env` (đã được `.gitignore` bao phủ), chỉ nạp tường minh vào process environment của cửa sổ Staging. Đây là file local chưa được agent tạo/đọc; không xem việc cập nhật plan là đã có credentials hoặc đã provision dịch vụ.

## Global Constraints

- Không thêm package, schema, migration, route, auth flow, seeder framework hoặc sửa CI workflow. Không sửa `SupabaseFileStorage.cs`, `SupabaseFileStorageTests.cs`, Core hoặc frontend.
- Host/port Neon lấy từ Console: direct cho migration; `-pooler` cho runtime. Không ép `6543`.
- Factory ưu tiên `ConnectionStrings__MigrationConnection`; biến thiếu/rỗng/whitespace thì giữ `.UseNpgsql()` connectionless. Không fallback sang runtime connection.
- `SeedData:Enabled=false`, `CatalogSeed:Enabled=false` mặc định. Staging chỉ bootstrap qua startup gate đang có; Production vẫn bị initializer chặn trước DB.
- Không in/commit secret, signed URL, raw connection string hoặc raw upstream exception. Không truyền credential qua CLI arguments. Seed passwords do người vận hành cấp ngoài Git.
- Live tests không gọi WAF, Migrate, EnsureCreated, EnsureDeleted, truncate hoặc CREATE/DROP database. PostgreSQL local qua `PETSPA_TEST_POSTGRES` chỉ dành cho regression hiện có.
- Ba live `[Fact]` cùng collection `LiveIntegrationTests`; cờ tắt ⇒ no-op PASS có output trước mọi đọc cấu hình/kết nối. Cờ bật ⇒ missing config/service/cleanup failure phải FAIL; không tự retry SMTP.
- Storage upload PNG hợp lệ tại root bucket riêng Staging, tên `{Guid:N}.png`; không prefix. Anonymous GET signed URL 200 và byte trùng; anonymous public URL chỉ chấp nhận 400/403/404. Cleanup token độc lập 15 giây.
- Chạy full suite `dotnet test backend/PetSpa.sln -m:1`: PASS 100%, 0 errors, 0 warnings trước commit implementation cuối.
- Agent chỉ sửa repository khi được yêu cầu triển khai; Tech Lead thực hiện provisioning, migration, bootstrap và live smoke trên công cụ ngoài. Offline PASS không thay thế live evidence.
- .NET/EF/xUnit không tự đọc `backend/.env`; runbook hướng dẫn import literal vào process environment trước lệnh Staging. Không thêm dotenv package, không auto-load secrets vào API/test startup, không đặt cloud keys trong `frontend/.env` hay `VITE_*`. Offline tests chạy ở shell riêng không import file Staging.

## Review Focus

1. Migration variable không có/whitespace nhưng runtime variable có: factory vẫn connectionless, không vô tình chọn runtime DB (Task 1).
2. Staging seed bị tắt hoặc thiếu password: không seed/không ghi dữ liệu; Production seed bật vẫn bị chặn trước DB (Task 2).
3. Live flag tắt trong CI thiếu mọi cloud config: không đọc config và không gọi mạng; chỉ thiếu SMTP config không ngăn riêng Neon/Storage chạy (Tasks 3–5).
4. Storage main request timeout và cleanup cùng lỗi: vẫn thử cleanup bằng token riêng, báo cả giai đoạn lỗi và objectPath, không log signed URL/inner exception (Task 4).
5. Public GET bị redirect hoặc 500: không coi là bằng chứng private; SMTP failure không bị chuyển thành PASS hoặc tự gửi lại (Tasks 4–5).

## File map và phụ thuộc

| File | Trách nhiệm |
|---|---|
| Modify `backend/src/PetSpa.Api/Data/PetSpaDbContextFactory.cs` | Đọc duy nhất migration env key và giữ connectionless fallback; sửa XML comment cũ |
| Create `backend/tests/PetSpa.Tests/Infrastructure/DbContextFactoryTests.cs` | Factory env precedence/fallback, không mở DB |
| Modify `backend/src/PetSpa.Api/appsettings.json` | Khung config đúng adapter, connection/secret rỗng, seed mặc định tắt |
| Modify `backend/src/PetSpa.Api/Data/Seeding/DbInitializer.cs` | Cho phép Staging trong guard môi trường |
| Modify `backend/tests/PetSpa.Tests/Seeding/SeedDataTests.cs` | Bootstrap Staging enabled/disabled/password/Production regression |
| Create `backend/tests/PetSpa.Tests/Infrastructure/LiveIntegrationSpikeTests.cs` | Ba live tests và helper nhỏ cùng file, dùng collection definition đã tạo trong factory tests |
| Create `backend/tests/PetSpa.Tests/Infrastructure/LiveIntegrationSpikeSupportTests.cs` | Offline tests cho guard, validation, schema/config assertions và cleanup error paths |
| Modify `backend/README.md` | Runbook và sửa mô tả cũ về factory/port/seed |
| Modify `docs/plans/sprint-2/sprint-2-plan.md` | Chỉ Task 12 và các nhãn quy ước port liên quan |
| Modify `AGENTS.md`, `.codex/instructions.md` | Chỉ dòng quy ước port Neon đã được Tech Lead điều chỉnh |

Task 1 → Task 2 → Task 3 → Task 4 → Task 5 → Task 6. Task 3 tạo file smoke dùng chung, Tasks 4–5 bổ sung vào đó; triển khai tuần tự sẽ giảm xung đột và tránh thêm abstraction chỉ để chia việc.

### Task 0: Chuẩn bị execution và baseline

**Contract/rule:** Hạ tầng hỗ trợ E02 và handoff #27; không thay business rules D/Q. **DoD:** có kết quả backend regression baseline để phân biệt lỗi cũ với lỗi mới.

- [x] Kiểm tra Git status/diff ngay trước execution. Workspace lúc viết plan có thay đổi người dùng ở các Sprint 2 plans, tài liệu Sprint 1 và spec mới; `sprint-2-plan.md` có cả staged/unstaged changes. Không stash/restore/stage cả file này hoặc commit thay đổi người dùng. Stash dự phòng lần pull giữ nguyên.
- [x] Dùng skill worktree khi bắt đầu implementation để có workspace cô lập từ develop; đưa đúng spec/plan đã duyệt vào execution context. Nếu cần chỉnh Task 12 đang có sửa đổi local, dùng bản hiện tại làm context, chỉ mang/stage hunk thuộc #25; không coi untracked spec đã có trong checkout mới. Nhánh theo Sprint 2: `chore/issue-25-staging-and-integration-spike`.
- [x] Kiểm tra .NET 10, tool manifest và PostgreSQL local; chỉ xác nhận biến môi trường có/không, không in giá trị. Live flag phải tắt; seed/catalog flags và cloud config không được rò sang baseline test process.
- [x] Hướng dẫn Tech Lead lấy cấu hình theo thứ tự Neon → Supabase → Gmail trong phụ lục dưới. Kiểm tra `git check-ignore backend/.env` có kết quả và `git ls-files -- backend/.env` không có kết quả, không đọc/in nội dung `.env`. Không yêu cầu credential cloud để bắt đầu Tasks 1–5; chỉ đánh dấu cloud readiness khi Tech Lead xác nhận đã chuẩn bị.
- [x] Chạy `dotnet test backend/PetSpa.sln -m:1` với `PETSPA_TEST_POSTGRES` local. Ghi counts/errors/warnings. Thiếu local PostgreSQL là blocker môi trường, không phải RED của tính năng và không sửa test thành skip để vượt qua.

### Task 1: Migration factory và cấu hình Staging an toàn

**Contract/rule:** Không đổi endpoint; cung cấp schema cho E02/hạ tầng. Giữ giới hạn schema M2-15 và không mở rộng quản lý tài khoản D18.

**Files:** Factory, appsettings, `DbContextFactoryTests.cs` trong file map; `DatabaseModelTests.cs` chỉ chạy regression, không sửa nếu không có lỗi liên quan.

**Interfaces:** Giữ `PetSpaDbContext CreateDbContext(string[] args)`. Consumes `ConnectionStrings__MigrationConnection` từ process environment. Produces DbContext cấu hình Npgsql, chưa mở connection. Khai báo collection definition `LiveIntegrationTestCollection` với `[CollectionDefinition("LiveIntegrationTests", DisableParallelization = true)]` ngay trong `DbContextFactoryTests.cs`; factory tests tham gia collection này để các test đổi env không chạy song song với suite. Live class Task 3 dùng cùng collection, không khai báo definition lần hai.

- [x] **RED:** Viết test tên `Factory_UsesMigrationEnvironmentWithoutOpeningDatabase`, `Factory_AbsentOrWhitespaceMigrationDoesNotUseRuntimeConnection`. Lưu/khôi phục cả migration/runtime env trong `try/finally`; dùng host loopback không khả dụng và chuỗi không password, không dùng credential thật. Assertions trọng tâm:

  ```csharp
  Assert.Equal(expectedMigration, context.Database.GetConnectionString());
  Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
  // Missing migration, even with DefaultConnection present:
  Assert.Null(context.Database.GetConnectionString());
  Assert.Equal(33, context.Model.GetEntityTypes().Count());
  ```

- [x] Chạy `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~DbContextFactoryTests" -m:1`; test có migration phải FAIL vì factory cũ bỏ qua env, không phải do kết nối hoặc compile.
- [x] **GREEN:** Sửa thân factory theo spec §4.2: đọc env, `UseNpgsql(connectionString)` nếu có, còn lại `UseNpgsql()`. Không mở connection hoặc migrate. Sửa comment “without reading credentials” cho đúng hành vi mới.
- [x] Chuẩn hóa appsettings: hai connection string `""`; `Supabase:Storage:{Url,Bucket,ServiceRoleKey}` rỗng, bỏ keys cũ; seed/catalog Enabled false; SMTP host/from/username/password rỗng, port 587, EnableSsl true. Giữ Logging/Jwt/AllowedHosts hiện có; không thêm LiveSpike recipient mặc định.
- [x] Chạy `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~DbContextFactoryTests|FullyQualifiedName~DatabaseModelTests" -m:1`; tất cả PASS, không mở DB ngoài ý muốn. Kiểm tra factory không lấy `DefaultConnection` và diff không có secrets.
- [x] Commit riêng file thuộc task: `chore(infra): configure staging migration connection and adapter settings`. Không dùng `git add .`.

### Task 2: Bootstrap Staging có flag và regression tests

**Contract/rule:** E01 account nền, E02 catalog, E57 chỉ là contract chưa triển khai; D18 không mở password CRUD. Giữ `EXTENSION_SETTINGS` false cả ba và bất biến `!cocHotel || giuCho`.

**Files:** `DbInitializer.cs`, `SeedDataTests.cs`. Dùng nguyên `SeedApplicationFactory.Create(string connection, string password, string environment = "Testing", bool enabled = true, bool missingStaffPassword = false, string? date = "2026-11-04")` và `PostgresTestDatabase.CreateAsync()`.

**Interfaces:** Giữ `Task DbInitializer.InitializeAsync(PetSpaDbContext context, SeedDataOptions options, IHostEnvironment environment, CancellationToken cancellationToken = default)`. Startup gate `SeedData:Enabled` thuộc Program hiện có, không thêm `Enabled` property vào options hay IConfiguration parameter chỉ để lặp guard.

- [x] **RED:** Bỏ riêng Staging khỏi theory từ chối môi trường cũ; giữ Production và thêm unknown environment bị chặn trước DB. Thêm `Staging_EnabledSeed_BootstrapsCatalogAccountsAndApprovedShifts` qua WAF/local PostgreSQL: 3 account đúng role/grants, 4 sizes, 3 services, 1 combo, 2 rooms, 16 prices, 2 ca/đăng ký APPROVED và slot đúng ngày cấu hình, 6 FUR codes, ba extension flags false. Tái dùng assertions của test seed hiện có, không tạo test framework mới.
- [x] Thêm `Staging_DisabledSeed_DoesNotAccessDatabase` với connection loopback port 1, enabled false, passwords rỗng; `GET /api/health` phải 200. Thêm `Staging_MissingPassword_RollsBackWithoutSeed` với DB local mới: startup fail, account/catalog vẫn 0. Giữ regression idempotence/no password reset/rollback hiện có.
- [x] Chạy `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~SeedDataTests" -m:1`; Staging enabled test phải FAIL vì guard cũ.
- [x] **GREEN:** Mở đúng guard `Development || Testing || Staging`; thông báo lỗi phù hợp spec. Không thay Program/auth/schema, transaction/advisory lock hoặc logic seed. Production bị chặn dù passwords đúng.
- [x] Chạy lại cùng lệnh và xác nhận PASS. Commit `chore(seed): allow explicit staging bootstrap with regression coverage` chỉ hai file của task.

### Task 3: Live guard và Neon smoke chỉ đọc

**Contract/rule:** Hỗ trợ E02 catalog/E15 FUR_CONDITIONS; không đổi D/Q hoặc dữ liệu nghiệp vụ. Giới hạn bảng theo model M2-15.

**Files:** Tạo hai file LiveIntegrationSpike trong file map. Class smoke tham gia collection không song song đã tạo ở Task 1, nhận `ITestOutputHelper`; chỉ ba live Facts cuối cùng, không fixture gọi WAF/DB ở constructor.

**Interfaces:** Trong `LiveIntegrationSpikeTests`, thêm helper test-local `internal static Task RunIfEnabledAsync(string? flag, Action<string> write, Func<Task> run)` và `internal static string RequireSetting(IConfiguration configuration, string key)`. Helper chỉ giữ guard/output, không tạo abstraction framework. Ba Facts bắt đầu gọi helper với flag process và lambda chỉ dựng configuration sau guard. `LiveIntegrationSpikeSupportTests` gọi helper với đầu vào kiểm soát, không đổi live flag process.

- [x] **RED:** Viết theory guard `null`, `""`, `"false"`, `"1"`, `" true "` ⇒ callback không chạy và output có `NO-OP`; `"true"`, `"TRUE"` ⇒ callback chạy một lần. Bám code `OrdinalIgnoreCase` của spec §5.1 khi phần mô tả “chính xác true” chưa đồng nhất; không trim hoặc nhận alias. Missing setting phải báo tên key, không giá trị. Dùng callback ném sentinel để chứng minh guard tắt không chạy nó.
- [x] Viết helper assertion nhỏ `internal static void VerifyNeonState(IReadOnlyCollection<string> expectedTables, IReadOnlyCollection<string> actualTables, string furConditionsJson, string extensionSettingsJson)` để kiểm thử dữ liệu đọc thật: thiếu một bảng nhưng thêm bảng khác vẫn fail; history/extra table không thay thế bảng ứng dụng; thiếu/duplicate FUR code không được bù bằng count; JSON sai kiểu hoặc cocHotel=true/giuCho=false fail. Dữ liệu hợp lệ có đủ sáu mã thì PASS, không yêu cầu tên/status mặc định.
- [x] Với helper mới, thêm signature/stub tối thiểu để test compile trước khi chạy; chưa thực hiện live I/O trong stub. Chạy `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~LiveIntegrationSpikeSupportTests" -m:1`; RED phải đến từ behavior, không giữ compile error làm bằng chứng. Áp dụng cùng nguyên tắc cho helper mới ở Tasks 4–5.
- [x] **GREEN:** Hoàn thiện helpers; mọi fail thông báo đã lọc. Tạo `Neon_PooledConnection_HasApplicationTablesAndSeedConfiguration` gọi Npgsql thật và `VerifyNeonState`. Build `IConfiguration` từ environment sau guard, chỉ yêu cầu `ConnectionStrings:DefaultConnection`; kiểm tra Neon pooled host/TLS, port theo config, timeout kết nối tối thiểu 30 giây và command timeout hữu hạn. Không dùng MigrationConnection hoặc PostgresTestDatabase.
- [x] Query catalog PostgreSQL đọc tên bảng kèm schema theo relational model; model expected có đúng 33 tên duy nhất. Loại bảng EF history khỏi tập expected; cho phép actual có bảng phụ. Đọc hai JSON config bằng query có parameter. Không lấy tên bảng từ user input hoặc ghi DB.
- [x] Chạy support tests và `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~LiveIntegrationSpikeTests" --logger "console;verbosity=detailed" -m:1` trong process live flag tắt. Neon Fact PASS với output no-op, không cần config Neon.
- [x] Commit `test(infra): add opt-in guard and read-only Neon live smoke` chỉ hai file mới. Đây chưa phải live Neon PASS.

### Task 4: Storage roundtrip, private behavior và cleanup failure

**Contract/rule:** Adapter ảnh hỗ trợ E15/E18; không đổi access rules endpoint hay D06/Q08. Test ảnh vô danh ở private bucket Staging, không dùng ảnh khách hàng.

**Files:** Chỉ bổ sung hai file LiveIntegrationSpike; adapter/interface và `SupabaseFileStorageTests.cs` giữ nguyên.

**Interfaces:** Consumes `IFileStorageService.UploadPhotoAsync(byte[], CancellationToken)`, `CreateReadUrlAsync(string, CancellationToken)`, `DeleteAsync(string, CancellationToken)`; concrete adapter là `SupabaseFileStorage(HttpClient, IConfiguration)`. Thêm helper cùng file `internal static Task VerifyStorageRoundTripAsync(IFileStorageService storage, HttpClient anonymousClient, Uri projectUrl, string bucket, Action<string> write, CancellationToken ct)`; production/live dùng adapter thật, chỉ support tests dùng fake interface/HTTP handler.

- [x] **RED:** Support tests bảo vệ thứ tự upload/sign/download/public-denial/delete, byte mismatch và signed GET !=200 phải fail; public GET 200/302/500 phải fail, 400/403/404 được chấp nhận. Anonymous client không có bearer/apikey, redirects tắt. Upload byte lấy PNG 1×1 đầy đủ đã có trong storage test hiện tại.
- [x] Thêm test main error + cleanup success vẫn fail main; main success + cleanup error fail `CLEANUP_FAILED` có objectPath; cả hai lỗi vẫn báo cả hai giai đoạn. Cho token chính canceled trước cleanup và assert token truyền Delete không canceled. Inject raw exception có sentinel secret/signed URL, assert output/failure text không chứa sentinel; không gắn raw exception làm InnerException. Nếu upload ném trước trả path, không xóa object không xác định.
- [x] Chạy `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~LiveIntegrationSpikeSupportTests" -m:1` và xác nhận RED đúng error/cleanup behavior.
- [x] **GREEN:** Tạo `Supabase_PrivateStorage_RoundTripsAndCleansUpPng` với config chỉ ba Storage keys. Validate URL HTTPS/bucket/key, dựng adapter HttpClient và anonymous HttpClient tách biệt với `AllowAutoRedirect=false`. Gọi helper, timeout chính 60 giây, cleanup CTS riêng 15 giây trong finally.
- [x] Upload root `{Guid:N}.png`, giữ path trả về trước khi sign; GET signed 200 và bytes bằng ảnh gốc; GET public URL đã escape bucket/path phải 400/403/404. Không admin bucket metadata call, không chờ 300 giây hết hạn; regression adapter hiện có đã kiểm tra TTL request 300.
- [x] Sanitized failure chứa service/stage và status/type phù hợp; riêng cleanup có đúng objectPath/bucket để dọn tay. Khi cả main và cleanup lỗi, tổng hợp thông báo đã lọc thay vì để finally che main error. Không log signed URI hoặc raw HTTP response/body.
- [x] Chạy lại support tests và `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~SupabaseFileStorageTests|FullyQualifiedName~LiveIntegrationSpikeTests" -m:1` với live flag tắt; PASS. Commit `test(infra): verify private storage roundtrip and cleanup failures` chỉ phần thay đổi task.

### Task 5: SMTP smoke với recipient riêng

**Contract/rule:** Adapter được dùng bởi E04/E50; D19 không cho tiết lộ tracking thật. Test dùng mã giả, không tạo booking/gửi tới khách hàng.

**Files:** Bổ sung hai file LiveIntegrationSpike. Không sửa SmtpEmailSender hoặc IEmailSender.

**Interfaces:** Consumes `SmtpEmailSender(IConfiguration)` và `Task SendTrackingCodeAsync(string recipient, string trackingCode, CancellationToken ct)`. Helper validation nhỏ `internal static void ValidateSmtpConfiguration(IConfiguration configuration)`; không thêm interface/service gửi mail mới.

- [x] **RED:** Support tests missing Host/From/recipient hoặc port/TLS không hợp lệ fail với tên key; username có thì password bắt buộc, không yêu cầu password cho relay không auth. Live TLS bật, port chỉ 587/465 theo adapter. Callback ném SMTP sentinel phải fail đã lọc, không bị guard/helper biến thành no-op, không gọi lần thứ hai.
- [x] Chạy support tests, xác nhận RED đúng validation/propagation behavior.
- [x] **GREEN:** Tạo `Smtp_AcceptsSmokeMessageForConfiguredRecipient`; guard trước config, đọc riêng `Smtp:*` và `LiveSpike:EmailRecipient`; không validate Neon/Storage. Mã `"SMOKE-TEST-" + Guid.NewGuid().ToString("N")[..8]`; gọi adapter một lần với token hữu hạn, giữ timeout 8 giây hiện có. Không custom email template hoặc ghi DB.
- [x] Chạy `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~LiveIntegrationSpikeSupportTests|FullyQualifiedName~SmtpEmailSenderTests|FullyQualifiedName~LiveIntegrationSpikeTests" -m:1` với flag tắt; ba live Facts PASS no-op. Commit `test(infra): add opt-in SMTP smoke for test mailbox` chỉ diff task.

### Task 6: Runbook, đồng bộ Task 12 và nghiệm thu

**Contract/rule:** E02 `GET /api/catalog`, technical health `GET /api/health`; E57 chưa triển khai; D18 không thêm reset password, `cocHotel ⇒ giuCho` theo contract. Handoff browser #26 → #27.

**Files:** README, Task 12 Sprint 2 plan, dòng port AGENTS và `.codex/instructions.md`. Không tự sửa spec APPROVED. Cập nhật README theo convention hiện có, thay đoạn cũ trái thiết kế thay vì append nội dung mâu thuẫn.

- [x] Viết bảng environment keys và bước chuẩn bị Neon branch/bucket private/hộp thư test riêng. Hướng dẫn nhập secrets ngoài Git/history, không echo chúng hoặc lưu vào artifact. Hai connection strings cùng branch/database; role migration phải đủ quyền DDL. Environment Staging không bảo vệ được khi người vận hành trỏ nhầm Production: bắt buộc kiểm tra target trước seed/update.
- [x] Đưa mẫu `.env` và đoạn import PowerShell từ phụ lục vào README; giải thích `.env` không tự được .NET/EF/test đọc. Import trong shell Staging riêng; offline suite dùng shell sạch và PETSPA_TEST_POSTGRES local. Không dùng Invoke-Expression/dot-source file `.env`, shell interpolation hoặc in các giá trị đã import. Không thêm script/package hoặc unit tests chỉ để sao chép đoạn hướng dẫn import.
- [x] Viết migration command từ spec không `--connection`, không auto-migrate; xác nhận connectionless fallback cho tạo migration/script local. Sửa thông tin factory cũ. Không lặp khẳng định CLI `--connection` bị bỏ qua do có factory: [Microsoft CLI reference](https://learn.microsoft.com/en-us/ef/core/cli/dotnet) vẫn hỗ trợ tùy chọn này; lựa chọn env ở đây nhằm tránh credential trong process arguments.
- [x] Viết bootstrap giữ Staging, date Việt Nam, ba password cấp ngoài Git; kiểm tra Admin/grants/catalog/ca/slot/config; tắt SeedData, gỡ seed passwords, restart. Nhắc đổi mật khẩu bootstrap qua quy trình quản trị được duyệt; không có default password hay API reset trong scope, đổi env không reset account đã tồn tại. CatalogSeed chỉ dùng AdminId thật khi chạy bổ sung, tắt sau kiểm tra; extension flags ban đầu false, không âm thầm bật OTP/deposit.
- [x] Viết startup check API thật: health 200/status Healthy rồi E02 catalog 200/envelope success + catalog có dữ liệu mẫu. Health một mình không chứng minh DB hoạt động. `dotnet run --no-launch-profile --project backend/src/PetSpa.Api` nếu chạy local với Staging env; launch profile không được ghi đè môi trường. Frontend live localhost:5173 phù hợp CORS sẵn có; hosting domain khác nằm ngoài scope.
- [x] Viết lệnh live filter và no-op filter có console detailed output; lưu/khôi phục opt-in bằng `try/finally` để cả test FAIL vẫn tắt flag. Hướng dẫn SMTP inbox/spam là checkpoint delivery riêng; server acceptance không bảo đảm inbox. Storage cleanup lỗi tra objectPath tại root bucket riêng; upload mất response có thể còn object không biết path, không hứa luôn cleanup tuyệt đối. Không dọn bằng xóa toàn bucket.
- [x] Đồng bộ Task 12: scope factory/env/Staging guard/3 real-adapter tests, port, file list mới, RED là regression hành vi (không ép live defaults fail). Giữ handoff #27 và sửa đúng nhãn port liên quan; không chỉnh nội dung #26/#27 hoặc changes của người dùng ngoài scope. Không đánh dấu live nghiệm thu hoàn thành khi chưa có evidence.
- [x] **Verification tự động cuối:** với local PG đã cấu hình và live flag tắt, chạy `dotnet test backend/PetSpa.sln -m:1`; yêu cầu toàn suite PASS, 0 errors/warnings. Chạy `git diff --check`, kiểm tra diff secrets/links/mệnh lệnh runbook và chỉ những files/hunks dự kiến. Không chạy frontend tests vì không đổi frontend. Warning/failure phải ghi đúng blocker, không tuyên bố PASS.
- [x] Commit docs/hunks đúng scope sau checks. Kiểm tra commits trước đó không lấy thay đổi người dùng; commit cuối theo Task 12: `chore(infra): add Neon Staging runbook and Supabase/MailKit live integration spike tests`. Không push/PR/deploy trong task nếu chưa được yêu cầu.
- [ ] **Checkpoint do Tech Lead thực hiện sau khi code sẵn sàng:** migration direct → bootstrap → tắt seed/restart → health/catalog → chạy live ba services → inbox/cleanup. Ghi evidence đã lọc và thông tin cấp account qua kênh riêng. Chưa chạy checkpoint này thì handoff ghi “code/offline verified; live acceptance pending”, không đóng nghiệm thu Staging/#27.

## Self-review và handoff

- Spec §§1–4 và factory acceptance: Tasks 0–2, Task 6. Spec §5 guard/Neon/Storage/SMTP: Tasks 3–5. Spec §§6–9 runbook/risk/verification: Task 6 và Review Focus tests.
- Khác biệt nhỏ được ghi rõ: guard dùng `OrdinalIgnoreCase` theo code mẫu spec §5.1; lỗi cleanup không giữ raw inner exception để tuân thủ acceptance “không rò secrets”. Không thêm prefix/overload đã bị bản spec mới bỏ.
- `NhanVienGrant` trong spec là diễn giải grants; implementation dùng entity sẵn có `NhanVienQuyen`, không tạo entity/schema mới.
- Các file test bổ sung chỉ bảo vệ hành vi factory/guard/error paths; live Facts vẫn dùng adapter thật. Không mutate process env ngoài factory tests trong collection không parallel và phải restore trong finally.
- Tài liệu EGM được link từ spec chưa hiện diện tại đường dẫn repo trong phiên này; plan dựa trên chính spec APPROVED do Tech Lead cung cấp, không tuyên bố đã đọc báo cáo EGM đó.
- Checkbox chỉ được đánh dấu khi có bằng chứng execution; checkpoint cloud giữ pending cho đến khi Tech Lead thực hiện. Plan được tự đối chiếu spec; người dùng review và chọn execution method trước implementation. Khuyến nghị **Native/inline** vì các task dùng chung file test, các thay đổi production nhỏ và tuần tự; một review độc lập toàn diff sau verification có thể thực hiện theo workflow được chọn.


## Phụ lục: Chuẩn bị Cloud và file .env local

### A. Neon trước — hai kết nối tới cùng database Staging

1. Mở Neon Console, chọn đúng project/branch/database Staging đang trống; ghi lại target không nhạy cảm để đối chiếu trước migration/seed. Không cần tạo project/branch mới nếu tài nguyên Staging riêng đã có.
2. Mở Connect, chọn database và role. Tắt Connection pooling để lấy direct host; bật để lấy pooled host (`-pooler`). Lấy port thực tế hiển thị, không tự đổi sang 6543. [Neon connection guide](https://neon.com/docs/connect/connect-from-any-app).
3. Dùng connection string kiểu Npgsql/.NET `Host=...;Port=...;Database=...;Username=...;Password=...;SSL Mode=Require;Timeout=30`. Nếu Console trả URI `postgresql://...`, chuyển sang các trường Npgsql bằng thông tin Console, không paste URI thẳng vào biến Npgsql của dự án. Password có ký tự đặc biệt cần escaping đúng connection-string format; không URL-encode password trong trường Npgsql. Không paste chuỗi thật vào chat hoặc converter online.
4. Lưu direct vào `ConnectionStrings__MigrationConnection`, pooled vào `ConnectionStrings__DefaultConnection`. Không dùng Neon account API token thay DB password. Chưa chạy migration cho đến khi factory Task 1 sẵn sàng.

### B. Supabase — bucket riêng và legacy key tương thích adapter

1. Chọn project Staging, mở Storage → New Bucket, đặt tên ví dụ `petspa-staging-media`; Public bucket **tắt**. Có thể giới hạn file 5 MiB và MIME PNG/JPEG/WebP cho phù hợp adapter. [Creating buckets](https://supabase.com/docs/guides/storage/buckets/creating-buckets).
2. Lấy Project URL gốc dạng `https://<project-ref>.supabase.co` từ Connect, không dùng URL dashboard, bucket hoặc signed URL. Mở Settings → API Keys, tìm legacy `service_role` JWT tương thích adapter hiện tại; không chọn anon/publishable hoặc thay trực tiếp bằng `sb_secret_...`. [Supabase API keys](https://supabase.com/docs/guides/getting-started/api-keys).
3. Lưu URL, bucket name, key vào ba biến Storage trong mẫu dưới. Legacy keys là đường tương thích hiện hữu; nếu project không có hoặc đã tắt loại key này, dừng phần Supabase để báo Tech Lead và xem lại scope adapter, không âm thầm sửa production adapter trái spec.
4. Chỉ dùng Supabase cho Storage trong Task 12; PostgreSQL nghiệp vụ vẫn trên Neon. Không cần Supabase database password, JWT signing secret hoặc publishable key cho bộ smoke này.

### C. Gmail — App Password và hộp thư nhận thử

1. Chọn Gmail dùng gửi thử, bật Google Account → Security → 2-Step Verification. Mở [App passwords](https://myaccount.google.com/apppasswords), tạo mục `PetSpa Staging`, lưu App Password vào `Smtp__Password`; không dùng mật khẩu Google thông thường. Nếu Google hiển thị nhóm ký tự có khoảng trắng, nhập chuỗi App Password không có khoảng trắng định dạng. [Google App Password help](https://support.google.com/mail/answer/185833?hl=en).
2. `Smtp__From` và `Smtp__Username` dùng cùng địa chỉ Gmail đầy đủ; host `smtp.gmail.com`, port 587, EnableSsl true (STARTTLS). [Gmail SMTP reference](https://developers.google.com/workspace/gmail/imap/imap-smtp).
3. `LiveSpike__EmailRecipient` là địa chỉ Tech Lead mở được; có thể cùng Gmail gửi. Nếu không thấy App Password dù đã bật 2-Step Verification, kiểm tra giới hạn tài khoản Workspace/Advanced Protection theo Google Help; không tắt các bảo vệ tài khoản để ép dùng SMTP.

### D. Mẫu backend/.env — Tech Lead tự điền trên máy

Mẫu chỉ là placeholder, không phải credentials. Mọi dòng giá trị nằm trên một dòng; không có `export`, inline comments hoặc nội suy biến `${...}`. Dấu nháy ngoài chỉ bao toàn bộ giá trị. Dấu `#`, `=`, `$`, `;` bên trong giá trị được giữ nguyên bởi đoạn import; password vẫn phải đúng escaping Npgsql khi nằm trong connection string.

```dotenv
ASPNETCORE_ENVIRONMENT=Staging
DOTNET_ENVIRONMENT=Staging
PETSPA_LIVE_SPIKE_ENABLED=false
ConnectionStrings__MigrationConnection='Host=<DIRECT_HOST>;Port=<PORT>;Database=<DB>;Username=<DB_USER>;Password=<DB_PASSWORD>;SSL Mode=Require;Timeout=30'
ConnectionStrings__DefaultConnection='Host=<POOLED_HOST>;Port=<PORT>;Database=<DB>;Username=<DB_USER>;Password=<DB_PASSWORD>;SSL Mode=Require;Timeout=30'
Supabase__Storage__Url=https://<PROJECT_REF>.supabase.co
Supabase__Storage__Bucket=petspa-staging-media
Supabase__Storage__ServiceRoleKey=<LEGACY_SERVICE_ROLE_JWT>
Smtp__Host=smtp.gmail.com
Smtp__Port=587
Smtp__EnableSsl=true
Smtp__From=<SENDER_GMAIL>
Smtp__Username=<SENDER_GMAIL>
Smtp__Password=<GOOGLE_APP_PASSWORD>
LiveSpike__EmailRecipient=<TEST_RECIPIENT_EMAIL>
SeedData__Enabled=false
SeedData__Date=<YYYY-MM-DD>
SeedData__AdminPassword=<UNIQUE_BOOTSTRAP_PASSWORD_12_TO_128_CHARS>
SeedData__Staff1Password=<DIFFERENT_BOOTSTRAP_PASSWORD_12_TO_128_CHARS>
SeedData__Staff2Password=<DIFFERENT_BOOTSTRAP_PASSWORD_12_TO_128_CHARS>
CatalogSeed__Enabled=false
Jwt__SecretKey=<RANDOM_SECRET_AT_LEAST_32_BYTES>
```

Đặt Enabled=false trước, không bootstrap/live chỉ vì import file. Khi Task 2 sẵn sàng, người vận hành bật SeedData riêng cho lần bootstrap có chủ đích; sau đó tắt và xóa seed passwords khỏi cả file local lẫn process environment. Cờ bật trong shell có thể bị ghi đè nếu import lại file còn giá trị false; kiểm tra presence/flag không nhạy cảm trước lệnh, không in toàn environment. Không để PETSPA_TEST_POSTGRES trỏ Neon.

### E. Import literal vào process environment bằng PowerShell

Runbook dùng đoạn sau từ repository root trong cửa sổ Staging riêng, sau khi Tech Lead hoàn tất điền file. Agent không chạy đoạn này trên file secret của người dùng trong giai đoạn lập kế hoạch. Không dùng `Get-Content ...` trực tiếp để in nội dung file hoặc `Invoke-Expression` để chạy nó.

```powershell
$petspaEnvPath = Join-Path (Get-Location) 'backend/.env'
foreach ($petspaEnvLine in [IO.File]::ReadAllLines($petspaEnvPath)) {
    if ([string]::IsNullOrWhiteSpace($petspaEnvLine) -or $petspaEnvLine.TrimStart().StartsWith('#')) { continue }
    $petspaEnvParts = $petspaEnvLine.Split('=', 2)
    if ($petspaEnvParts.Count -ne 2 -or $petspaEnvParts[0].Trim() -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
        throw 'Invalid .env entry; use one NAME=value per line. Values are not displayed.'
    }
    $petspaEnvName = $petspaEnvParts[0].Trim()
    $petspaEnvValue = $petspaEnvParts[1].Trim()
    if ($petspaEnvValue.Length -ge 2 -and (
        ($petspaEnvValue.StartsWith('"') -and $petspaEnvValue.EndsWith('"')) -or
        ($petspaEnvValue.StartsWith("'") -and $petspaEnvValue.EndsWith("'")))) {
        $petspaEnvValue = $petspaEnvValue.Substring(1, $petspaEnvValue.Length - 2)
    }
    [Environment]::SetEnvironmentVariable($petspaEnvName, $petspaEnvValue, 'Process')
}
Remove-Variable petspaEnvLine, petspaEnvParts, petspaEnvName, petspaEnvValue -ErrorAction SilentlyContinue
```

Không dùng `setx` để persist credentials ra user/machine environment. Chỉ các tiến trình khởi chạy sau import từ cùng shell nhận biến này. Đóng shell khi hết thao tác Staging; chạy regression ở shell sạch không kế thừa Staging env. Không commit file `.env`; mẫu hướng dẫn nằm trong README/plan, không tạo file secrets hộ người dùng hoặc tự xác nhận cloud readiness.


## Execution evidence — 2026-11-24

- Worktree: `C:/Users/LENOVO/.codex/worktrees/issue-25-staging/FinalProject`, branch `chore/issue-25-staging-and-integration-spike`, base `a0ef916`. Workspace gốc và `backend/.env` thật giữ nguyên; agent không đọc/import file secret.
- Tech Lead xác nhận đã chuẩn bị thông tin trong `backend/.env`; đây là xác nhận của người dùng, không phải live validation của agent.
- Local PostgreSQL tạm chỉ nghe `127.0.0.1:55425`; baseline `dotnet test backend/PetSpa.sln -m:1`: **585/585 PASS**, 0 skipped, 0 warnings.
- RED: **39 failed / 16 passed**, đúng factory bỏ qua migration env, guard từ chối Staging và helper behaviors chưa triển khai.
- GREEN liên quan: **86/86 PASS**, 0 skipped, 0 warnings. Seed assertions dùng lại dưới Testing/Staging theories, không nhân đôi toàn bộ test body.
- `dotnet tool restore`: EF tool **10.0.14** restored. `dotnet ef migrations script ... --no-build --output <ignored scratch SQL>` thành công khi migration env không có; không kết nối hoặc áp dụng database.
- Fresh whole-branch review phát hiện hai P2: PNG fixture cũ sai IDAT CRC và hướng dẫn chạy test từ shell đang bị API foreground chiếm. Sửa bằng fixture/check CRC+pixel và cửa sổ smoke thứ hai; đã kiểm chứng RED/GREEN và full suite cuối 632/632 PASS.
- `.codex/instructions.md` là file local bị Git ignore: đã chỉnh dòng port trong worktree để dùng tại đây, không force-add vào commit. `AGENTS.md` tracked mang cùng quy ước đã cập nhật.
- Cloud migration/bootstrap, live Neon/Storage/SMTP, API health/catalog thật, inbox, cleanup thực tế và password rotation: **PENDING — Tech Lead thực hiện theo README**. Không push/deploy/merge trong execution này.

- Review fix RED: PNG integrity test failed on mismatched IDAT CRC. GREEN: **40/40 smoke/support tests PASS**, including PNG checksum and decompressed 1×1 pixel. First full regression before this fix: **631/631 PASS**; final full run: **632/632 PASS, 0 skipped, 0 errors, 0 warnings**.

- Final verification: `dotnet test backend/PetSpa.sln -m:1`, PostgreSQL local loopback, live flag false: **632/632 PASS**, 0 skipped, 0 errors, 0 warnings (5m39s). No-op filter riêng: **3/3 PASS**, có thông báo NO-OP, từng Fact dưới 7ms.
- README PowerShell đã qua syntax parsing và import behavior với dữ liệu giả chứa `=;#$`; không đọc `.env` thật. README/plan local links và `git diff --check` PASS. Approved spec giữ nguyên nội dung do người dùng duyệt.
- Implementation commits: `f708b22` factory/config; `fe45d2e` Staging bootstrap; `805278e` live smoke/support; `ed53e42` review fixes. Docs commit đi cùng phiên bàn giao này. Hai P2 review đã được xử lý; không merge/push/PR.

### Bổ sung trước PR theo yêu cầu Tech Lead

- Thêm `backend/.env_example` chứa placeholders và các cờ live/seed mặc định false; không đọc/copy từ `.env` thật.
- README trỏ tới template và có lệnh tạo file local chỉ khi chưa tồn tại; giữ hướng dẫn import tường minh.
- Tech Lead yêu cầu push nhánh #25 và tạo PR vào develop; không tự merge PR hoặc chạy thao tác cloud.
