# PetSpa Backend — M3 schema foundation

Task 12 / Issue #25 bổ sung [Staging runbook](#staging-runbook--issue-25) bên dưới. Các mục M3 ghi lại nền tảng schema, không thay thế các checkpoint vận hành Staging.

- Core: 15 enums, 33 entities; no EF dependency.
- Api/Data: one configuration per entity, PetSpaDbContext and a connectionless design-time factory.
- Migrations: InitialCoreSchema and snapshot; no database has been updated by this task.

## Local verification

Run from repository root:

```powershell
dotnet tool restore
dotnet build backend/src/PetSpa.Core -m:1
dotnet build backend/PetSpa.sln -m:1
dotnet test backend/PetSpa.sln -m:1
```

The local dotnet-ef tool is pinned to 10.0.14. EF Relational is explicitly pinned to match EF Design so tests do not resolve an older runtime assembly through Npgsql.

## Configuration and offline migrations

Runtime uses ConnectionStrings:DefaultConnection (environment variable ConnectionStrings__DefaultConnection), provided outside source control, with the Neon pooled endpoint. Migrations use ConnectionStrings__MigrationConnection with the direct endpoint. Copy host and port from Neon Console: both normally use 5432; the pooled hostname contains -pooler. Do not force port 6543.

PetSpaDbContextFactory reads only ConnectionStrings__MigrationConnection from the process environment. When missing or whitespace, it builds connectionless UseNpgsql() options, so local migration generation still needs no credentials. Constructing the context does not open a connection; database update is a separate, deliberate operation. The command used to create the initial migration was:

```powershell
dotnet ef migrations add InitialCoreSchema --project backend/src/PetSpa.Api --startup-project backend/src/PetSpa.Api --no-build
```

Do not rerun that name after the migration exists. To apply existing migrations to Staging, follow the runbook below; the API and live smoke tests do not migrate automatically.

DatabaseModelTests inspect the real Npgsql model, DI wiring, restrictive FKs, keys/indexes/types and the generated migration script, including snapshot consistency. The SQL script has 33 application tables plus EF's own __EFMigrationsHistory; that history table is not a 34th domain entity.

## Boundaries

FKs use Restrict and simple row invariants use CHECK constraints. They do not implement authorization, cross-row ownership, price/state transitions, paid-invoice immutability, normalization, update timestamp maintenance or SELECT FOR UPDATE capacity protocols. Those require the later service layer and PostgreSQL integration tests. JSONB is persisted as a string; services must validate its schema before saving.

The latest M3 request makes ChiTietCombo a composite-key entity. ChiTietDon preserves declared weight and READY timestamp; see system-design section 4.6. No endpoint, authentication flow, seed credentials, remote database operation or frontend change is included.

## Quy ước triển khai Sprint 1

Các mục M3 ở trên mô tả nền tảng schema. Quy ước dưới đây áp dụng khi thêm tính năng; đường dẫn chưa tồn tại được tạo khi Issue cần đến, không tạo sẵn class rỗng.

Đọc [quy trình đóng góp](../.github/CONTRIBUTING.md) trước khi nhận việc. Phạm vi và phân công nằm ở [kế hoạch Sprint 1](../docs/plans/sprint-1-core-plan.md); route, trường DTO và HTTP status lấy từ [API contract](../docs/design/api-contract.md). Quy tắc nghiệp vụ lấy từ [scope-and-decisions](../docs/requirements/scope-and-decisions.md), ưu tiên amendment M2 khi khác phần cũ. Ownership nghiệp vụ/transaction theo [system-design mục 2 và 5](../docs/design/system-design.md).

### Đặt file và đặt tên

Các đường dẫn sau tính từ `backend/`:

| Loại file | Vị trí | Ví dụ |
|---|---|---|
| Envelope và kiểu kỹ thuật dùng chung | `src/PetSpa.Core/Common/` | `ApiResponse.cs` |
| Request/response DTO | `src/PetSpa.Core/DTOs/<Module>/` | `Booking/BookingCreate.cs` |
| Interface service, khi cần ranh giới giữa module/integration | `src/PetSpa.Core/Interfaces/<Module>/` | `Booking/IBookingService.cs` |
| HTTP controller | `src/PetSpa.Api/Controllers/<Module>/` | `Booking/BookingsController.cs` |
| Logic nghiệp vụ | `src/PetSpa.Api/Services/<Module>/` | `Booking/BookingService.cs` |
| Seeder | `src/PetSpa.Api/Data/Seeding/` | `DbInitializer.cs` |
| Middleware | `src/PetSpa.Api/Middleware/` | `GlobalExceptionMiddleware.cs` |
| Test tính năng mới | `tests/PetSpa.Tests/<Module>/` | `Booking/BookingCreateTests.cs` |
| Fixture/factory test dùng chung | `tests/PetSpa.Tests/Infrastructure/` | `PetSpaWebApplicationFactory.cs` |

- Dùng tên module `Identity`, `Booking`, `Care`, `Billing`, `Scheduling` theo thiết kế; Catalog/giá thuộc `Billing`. Giữ nguyên vị trí các Entity/Configuration và test hiện có, không di chuyển chỉ để khớp bảng này.
- Namespace theo project và thư mục, ví dụ `PetSpa.Core.DTOs.Booking`. Class/property C# dùng PascalCase; JSON dùng đúng tên camelCase trong contract. Không trả Entity trực tiếp ra API. Tái sử dụng DTO đã có, không tạo nhiều bản `BookingPet` khác nhau cho từng thành viên.
- Controller xử lý HTTP, actor và ánh xạ request/response; service xử lý nghiệp vụ, guard và transaction bằng `PetSpaDbContext`. Core không phụ thuộc EF Core hoặc ASP.NET Core. Chỉ thêm interface khi có nhu cầu cụ thể; không bắt buộc mỗi class đều có interface, không thêm Repository/CQRS/MediatR.
- Một use case xuyên module dùng cùng DbContext/transaction; không gọi HTTP nội bộ hoặc mở transaction độc lập rồi coi toàn bộ thao tác là nguyên tử. Thống nhất trước service nào sở hữu việc đổi trạng thái; ví dụ checkout chỉ chuyển invoice `PAID`, bàn giao mới đổi pet `DONE`.
- Test mới đặt theo module và hành vi, ví dụ `BookingCreateTests`; endpoint phải có test bằng `WebApplicationFactory<Program>`. Endpoint giả lập lỗi chỉ nằm trong test assembly. Khi cần fixture chung, phối hợp theo quy tắc file dùng chung trong CONTRIBUTING, không sửa cấu hình test toàn suite từ một Issue riêng lẻ.

### Database phát triển và kiểm thử

- Mỗi thành viên dùng database phát triển riêng (hoặc Neon branch riêng được cấp); integration test có ghi dữ liệu dùng database test riêng, không dùng database phát triển chung của nhóm. Không tự tạo tài nguyên trả phí hay sửa quyền truy cập để thực hiện quy ước này.
- Cấu hình qua environment variables hoặc .NET User Secrets; không commit credential. Dùng đúng khóa kết nối và cổng runtime/migration ở phần Configuration bên trên.
- Test phải tạo dữ liệu riêng và dọn đúng dữ liệu của nó; không `EnsureDeleted`, truncate hoặc reset database dùng chung. Seed phải idempotent theo từng nhóm dữ liệu; không để khởi động API tự seed/reset môi trường ngoài phát triển/kiểm thử.
- Issue #2 cung cấp seed và test seed; không đồng nghĩa đã có JWT login (#5), storage/email integration hoặc mọi fixture cho các endpoint sau. Chỉ chạy migration lên database khi đã được cho phép theo quy trình schema của nhóm.

## Seed dữ liệu phát triển — Issue #2

Seeder nằm ở `src/PetSpa.Api/Data/Seeding/DbInitializer.cs`. Mặc định tắt; muốn chạy, đặt các khóa cấu hình sau qua environment variables hoặc .NET User Secrets (dùng prefix `SeedData__` thay cho `SeedData:` khi đặt environment variables):

| Khóa | Giá trị/cách sử dụng |
|---|---|
| `SeedData:Enabled` | `true` để seed khi API khởi động; sau khi chuẩn bị dữ liệu có thể tắt lại. |
| `SeedData:Date` | Ngày tạo slot và ca `APPROVED`, định dạng `yyyy-MM-dd`; bỏ trống thì dùng ngày hiện tại ở Việt Nam. |
| `SeedData:AdminPassword` | Mật khẩu riêng cho `admin`, 12–128 ký tự; không có giá trị mặc định. |
| `SeedData:Staff1Password` | Mật khẩu riêng cho `staff1` (Lễ tân), cùng giới hạn trên. |
| `SeedData:Staff2Password` | Mật khẩu riêng cho `staff2` (Kỹ thuật viên Spa), cùng giới hạn trên. |

Cho phép môi trường `Development`, `Testing` hoặc bootstrap `Staging` có chủ đích khi `SeedData:Enabled=true`; `Production` và môi trường khác bị chặn trước khi seeder truy cập database. Dùng database riêng đã có schema; seeder **không** gọi `Migrate`/`EnsureCreated`. Seed ghi dữ liệu vào database được cấu hình, vì vậy hãy kiểm tra đúng database trước khi bật. Sau bootstrap Staging phải tắt cờ, gỡ mật khẩu seed và restart theo runbook bên dưới.

Seed gồm 4 khoảng size, 2 ca, 3 dịch vụ, 1 combo đủ 4 size, 2 loại phòng và tổng 16 giá, 3 tài khoản hash bằng ASP.NET Core `PasswordHasher<NhanVien>`, 24 permissions cùng grants FR01, slot/đăng ký ca cho ngày chọn và cấu hình `EXTENSION_SETTINGS` với `otpEmail`, `cocHotel`, `giuCho` đều `false`. Login ở Issue #5 cần dùng cùng cơ chế verify hash. Các lựa chọn dữ liệu mẫu chưa được kế hoạch chỉ định chi tiết: dịch vụ thứ ba là **Vệ sinh tai**, 30 phút, giá S/M/L/XL lần lượt 50/70/90/110 nghìn VND; key nội bộ cho nhóm cờ là `EXTENSION_SETTINGS`. Đây là dữ liệu phát triển, không phải quyết định giá vận hành thật. Dịch vụ cắt tỉa vẫn không được mua riêng theo contract nghiệp vụ.

Mỗi lần chạy dùng một transaction và advisory lock PostgreSQL; chỉ thêm bản ghi thiếu theo username, mã size/permission, tên dịch vụ/ca và các khóa liên quan. Không đổi mật khẩu, giá, cờ cấu hình, reservation hoặc quyết định duyệt ca đã có. Xung đột role/tài khoản bị khóa thì dừng và rollback thay vì reset account. Chạy lại sẽ khôi phục cả grant seed bị xóa; không dùng seeder để quản trị quyền thực tế. Không sửa Entity, DbContext hoặc migration để seed.

### Chạy integration test PostgreSQL

`PETSPA_TEST_POSTGRES` là kết nối tới PostgreSQL **test local** có quyền tạo database (host chỉ chấp nhận `localhost`, `127.0.0.1`, `::1`). Cấu hình kết nối ngoài Git; không trỏ tới Neon hoặc database dùng chung. Sau khi đặt biến này, chạy từ root:

```powershell
dotnet test backend/PetSpa.sln
```

Mỗi test seed tạo một database `petspa_test_<GUID>`, áp dụng migration hiện có vào đúng database mới đó, rồi xóa riêng database vừa tạo khi kết thúc. Không reset database mà biến môi trường trỏ tới. Thiếu biến kết nối thì test báo lỗi cấu hình, không âm thầm skip hoặc dùng database runtime.

CI backend dùng PostgreSQL 17 tạm của GitHub Actions và đặt biến này tự động. Chế độ trust chỉ nằm trong service test tạm bind `127.0.0.1`; không áp dụng cấu hình đó lên Neon hoặc server của nhóm. Các test model/envelope cũ vẫn không cần truy cập database.

## Cấu hình Task 01

- **Catalog Seed trên staging** — Đặt `CatalogSeed__Enabled=true` và `CatalogSeed__AdminId` bằng ID của Admin đang hoạt động. Giữ `SeedData__Enabled=false`. Catalog Seed tạo `FUR_CONDITIONS` nếu chưa có hoặc thêm các mã mặc định còn thiếu. Tên, trạng thái, cờ phụ phí và trường tùy chỉnh đã lưu được giữ nguyên. Chạy trên database đã có schema và tắt cờ sau khi kiểm tra.
- **Rate limit E01** — Mặc định mỗi IP có 20 request trong 60 giây. Có thể cấu hình qua `RateLimiting__Login__PermitLimit` và `RateLimiting__Login__WindowSeconds`. Nếu API đứng sau reverse proxy, khai báo IP của proxy tin cậy qua `ReverseProxy__KnownProxies__0` và các phần tử tiếp theo để xác định đúng IP khách.
- **Adapter ảnh private** — Cấu hình `Supabase__Storage__Url`, `Supabase__Storage__Bucket` và `Supabase__Storage__ServiceRoleKey` ngoài Git. Bucket phải private. Adapter kiểm tra giới hạn 5 MiB và chữ ký JPEG/PNG/WebP, dùng object path do server sinh và ký URL đọc trong 300 giây. E08 chỉ ký ảnh private sau khi xác minh đúng cặp số điện thoại và mã tra cứu. Nhật ký ảnh lưu cục bộ trước đây có đường dẫn bắt đầu bằng `check-in/` hoặc `care-logs/` vẫn xuất hiện với `anhUrl` bằng `null`. Trước khi dùng để nhận ảnh thật, luồng upload còn cần kiểm tra giải mã đầy đủ.

## E12 Đánh dấu khách vắng mặt

Khi chọn `loaiHuy` là `NO_SHOW`, nhân viên xác nhận khách chưa liên hệ bằng cách nhập `lyDo` là `Khách không liên hệ`. Có thể bắt đầu bằng `Khách không liên hệ:` rồi thêm chi tiết. API so khớp không phân biệt chữ hoa và chữ thường, lưu đầy đủ lý do vào audit. Thiếu xác nhận trả `409 INVALID_TRANSITION`, giữ nguyên lịch hẹn và công suất.

## Foundation Sprint 2 — Issue #14

Shared DTOs và typed Deposit/Refund projection tuân thủ API contract. Hotel capacity tính từng đêm; Spa read compensation dùng cutoff để bỏ phần giữ cọc hết hạn mà không ghi database. Các endpoint Hotel/Combo/Cọc vẫn do #15–#24 nối foundation; #14 không tự gỡ guards nghiệp vụ Sprint 1.

Expiry writer truyền IDs ca/loại phòng cần đặt qua optional target arguments, dọn đủ hold liên quan theo batch tối đa 20 booking/transaction và commit trước transaction chính. Hai filters null là sweep toàn cục; explicit empty filters là no-op. Service cleanup dùng DbContext riêng cùng options, không commit/detach thay đổi của caller. `ExpireLockedBookingDepositAsync` dùng context/transaction caller, yêu cầu đã khóa và nạp graph đầy đủ; caller SaveChanges/commit.

| Key | Ý nghĩa |
|---|---|
| `DepositExpiry:BackgroundEnabled` | Mặc định false trong Testing, true môi trường khác; explicit config ưu tiên. |
| `DepositExpiry:IntervalSeconds` | Mặc định 30 giây; số dương trong giới hạn timer. Không quyết định hiệu lực reservation. |
| `Smtp:Host`, `Smtp:Port`, `Smtp:From` | Host/cổng/địa chỉ gửi của MailKit5; cấu hình ngoài source khi có dữ liệu vận hành. |
| `Smtp:EnableSsl` | Mặc định true: 465 TLS ngay, 587 STARTTLS bắt buộc; cổng TLS khác fail config. False chỉ dùng cho SMTP local/test được cấu hình chủ động. |
| `Smtp:Username`, `Smtp:Password` | Credential qua environment/user secrets; không ghi vào Git/log. Không có username thì không authenticate. |

Email adapter có timeout8 giây và cancellation; gửi sau commit theo semantics SENT/FAILED hiện tại. SMTP sau commit thất bại không rollback booking. SMTP tests dùng loopback port động, không email thật. Seed mới thêm Phòng Tiêu Chuẩn250k/5 suất, VIP450k/3 suất và Combo Tắm Cắt Toàn Diện350k/480k/620k/800k; chạy lại chỉ bổ sung phần thiếu. EXTENSION_SETTINGS mặc định vẫn ba cờ false.

Chạy với PETSPA_TEST_POSTGRES đã cấu hình trỏ PostgreSQL loopback riêng, không Neon:

```powershell
dotnet build backend/PetSpa.sln -m:1
dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~Foundation|FullyQualifiedName~SeedDataTests" -m:1
dotnet test backend/PetSpa.sln -m:1
```

Tham chiếu [spec #14](../docs/superpowers/specs/2026-11-22-issue-14-sprint2-foundation-design.md) và [implementation plan](../docs/superpowers/plans/2026-11-22-issue-14-sprint2-foundation.md). Build yêu cầu0 warnings/errors. Số đo local chỉ phục vụ đánh giá; không thay nghiệm thu NFR hoặc CI/review của nhóm.


## Staging runbook — Issue #25

### 1. Chuẩn bị và giới hạn

Tech Lead thực hiện các thao tác cloud. Chọn Neon branch/database riêng cho Staging, Supabase private bucket riêng và Gmail/hộp thư test. Kiểm tra lại đúng project, branch và database trước migration hoặc seed; biến môi trường Staging không ngăn được việc trỏ nhầm database Production. Không dùng tài nguyên Production.

- Neon Console → Connect: lấy direct connection cho migration và pooled connection cho API/live smoke, cùng database. Host/port theo Console; pooled host có `-pooler`, thông thường cả hai là 5432. Dùng connection string Npgsql `Host=...;Port=...;Database=...;Username=...;Password=...;SSL Mode=Require;Timeout=30`, không đặt URI `postgresql://...` trực tiếp vào khóa Npgsql. Giữ escaping password đúng định dạng connection string. Không dùng Neon account API token thay database password. [Neon guide](https://neon.com/docs/connect/connect-from-any-app).
- Supabase → Storage → New Bucket: ví dụ `petspa-staging-media`, Public **tắt**; cho phép PNG/JPEG/WebP tối đa 5 MiB. Project URL gốc lấy từ Connect; Settings → API Keys lấy legacy `service_role` JWT tương thích adapter hiện tại, không dùng anon/publishable hoặc tự thay bằng `sb_secret_...`. Nếu project không có key tương thích, báo Lead để thống nhất thay đổi adapter riêng. Supabase chỉ phục vụ Storage; dữ liệu nghiệp vụ vẫn trên Neon. [Buckets](https://supabase.com/docs/guides/storage/buckets/creating-buckets), [API keys](https://supabase.com/docs/guides/getting-started/api-keys).
- Gmail: bật 2-Step Verification, tạo [App Password](https://myaccount.google.com/apppasswords) tên PetSpa Staging. Dùng App Password thay mật khẩu Google thông thường; bỏ khoảng trắng phân nhóm hiển thị. Host `smtp.gmail.com`, port 587 STARTTLS, From/Username cùng Gmail đầy đủ. Không có mục App Password thì kiểm tra chính sách Workspace/Advanced Protection, không hạ bảo vệ tài khoản. [Google Help](https://support.google.com/mail/answer/185833?hl=en), [SMTP ports](https://developers.google.com/workspace/gmail/imap/imap-smtp).

### 2. File cấu hình local và import vào shell Staging

Tech Lead tự điền `backend/.env` trên máy. File đã được Git ignore; không gửi nội dung vào chat, commit hoặc đặt cloud secrets trong frontend/VITE variables. Kiểm tra từ repository root (không in nội dung):

```powershell
git check-ignore backend/.env
git ls-files -- backend/.env
```

Lệnh đầu phải hiện đường dẫn, lệnh sau không có kết quả. File được ignore vẫn là plaintext trên máy; giữ trong nơi chỉ người vận hành được dùng. Mẫu dưới chỉ chứa placeholders, phải thay toàn bộ trước vận hành. Dùng mật khẩu bootstrap riêng cho ba tài khoản và JWT secret ngẫu nhiên ít nhất 32 bytes; không có mật khẩu seed mặc định.

Mẫu được theo dõi trong Git: [.env_example](.env_example). Nếu chưa có file local, tạo bản sao rồi tự điền secrets; không ghi đè file .env đã có:

```powershell
if (-not (Test-Path -LiteralPath 'backend/.env')) {
    Copy-Item -LiteralPath 'backend/.env_example' -Destination 'backend/.env'
}
```


.NET, EF CLI và xUnit **không tự đọc .env**. Mở cửa sổ PowerShell riêng tại repository root, import literal bằng đoạn sau; không chạy file bằng Invoke-Expression/dot-source, không echo biến secrets. Chỉ hỗ trợ một `NAME=value` trên mỗi dòng, optional cặp nháy ngoài, comment ở đầu dòng; không có export, multiline, inline comments hoặc nội suy `${...}`. Dấu `=`, `#`, `$`, `;` trong giá trị được giữ nguyên.

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

Chỉ process con được mở từ shell này nhận các biến. Không dùng setx để persist secret. Khi dùng worktree riêng, file .env từ checkout cũ không tự được copy; import file người vận hành đang giữ bằng đường dẫn tuyệt đối hoặc tạo cấu hình local riêng. Không copy file secret vào artifact/review package. Test offline dùng shell sạch, không import file Staging. Đóng shell Staging sau khi thao tác.

### 3. Migration qua direct connection

Từ root, sau import, xác nhận đúng target trong Console và giữ hai seed flags false. Chỉ lệnh dưới áp dụng migrations hiện có; không tạo migration mới hoặc EnsureCreated/EnsureDeleted:

```powershell
dotnet tool restore
dotnet ef database update --project backend/src/PetSpa.Api --startup-project backend/src/PetSpa.Api
```

Factory đọc `ConnectionStrings__MigrationConnection` trong process, không truyền password bằng CLI arguments. Role migration phải có quyền DDL để áp dụng schema. Không bật verbose/trace làm lộ cấu hình. Khi thiếu biến, factory vẫn dùng connectionless fallback cho migration generation nhưng database update sẽ không có connection để chạy. Runtime không đọc MigrationConnection và không tự migrate. Schema gồm 33 bảng ứng dụng và bảng EF `__EFMigrationsHistory` riêng.

### 4. Bootstrap một lần trong môi trường Staging

Chọn `SeedData__Date` theo ngày nghiệm thu tại Việt Nam. Trong cùng shell đã import:

```powershell
$env:SeedData__Enabled = 'true'
try {
    dotnet run --no-launch-profile --project backend/src/PetSpa.Api
}
finally {
    $env:SeedData__Enabled = 'false'
    Remove-Item Env:SeedData__AdminPassword,Env:SeedData__Staff1Password,Env:SeedData__Staff2Password -ErrorAction SilentlyContinue
}
```

`--no-launch-profile` giữ Staging env thay vì bị launch profile đổi về Development. Chờ startup hoàn tất và kiểm tra dữ liệu; dừng API bằng Ctrl+C, rồi khối finally tắt seed/gỡ seed passwords. Xóa ba seed password khỏi file .env, giữ `SeedData__Enabled=false` trong file, sau đó khởi động lại API không seed:

```powershell
dotnet run --no-launch-profile --project backend/src/PetSpa.Api
```

Không import lại .env còn cờ seed true. Không đổi môi trường sang Development. Seeder tạo 3 account `admin/staff1/staff2`, 24 permissions và grants, 4 sizes, 3 services, 1 combo, 2 room types, 16 prices, 2 ca và slot/đăng ký APPROVED của staff chăm sóc cho ngày đã chọn. `FUR_CONDITIONS` có 6 mã; `EXTENSION_SETTINGS` ban đầu là `{ "otpEmail": false, "cocHotel": false, "giuCho": false }`. Giá/catalog seed là mẫu phục vụ nghiệm thu, chưa là giá vận hành được duyệt.

Nhắc đổi mật khẩu bootstrap, đặc biệt Admin, theo quy trình quản trị được Lead duyệt trước khi bàn giao rộng. Đổi biến seed password hoặc chạy lại seed không đổi hash account đã có. Issue này không tạo API đổi/reset password; không dùng route giả hoặc SQL ghi hash tùy tiện. Ghi trạng thái rotation còn chờ nếu quy trình chưa sẵn sàng.

CatalogSeed chỉ chạy bổ sung FUR_CONDITIONS: đặt `CatalogSeed__AdminId` là ID Admin active thật và `CatalogSeed__Enabled=true`, giữ SeedData false; sau kiểm tra tắt lại và restart. Bootstrap đầy đủ đã gọi cùng logic catalog nên không cần bật cả hai đồng thời. Không tự bật OTP/deposit flags trong smoke test. Muốn bật Hotel deposit phải có `cocHotel=true` cùng `giuCho=true`; E57 chưa được triển khai trong Issue này, thao tác quản trị cờ cần thống nhất riêng.

### 5. Kiểm tra API thật sau restart

Trong cửa sổ khác, điền URL API thực tế đã được cấp hoặc URL API local đang chạy với Staging env/Neon:

```powershell
$petspaApiBase = '<API_BASE_URL>'
Invoke-RestMethod "$petspaApiBase/api/health"
Invoke-RestMethod "$petspaApiBase/api/catalog"
```

Health phải HTTP 200 và `status=Healthy`; endpoint này không query DB. Catalog E02 phải HTTP 200, `success=true`, data có danh mục/size/room/combo đã seed. Đây là kiểm tra startup/DI/runtime DB mà ba adapter tests độc lập không thực hiện. Kiểm tra HTTP status khi request lỗi; không coi JSON bất kỳ là đạt.

Frontend chạy `VITE_USE_MOCK_API=false`, URL backend đúng; CORS hiện hỗ trợ `http://localhost:5173` và `http://127.0.0.1:5173`. Domain hosting khác cần thay đổi được thống nhất riêng. Không đưa key server vào frontend.

### 6. Chạy live smoke có chủ đích

Giữ API chạy ở cửa sổ riêng sau restart. Sau migration/bootstrap và API read checks, mở **cửa sổ PowerShell thứ hai** tại repository root, import lại file `.env` đã gỡ seed passwords và giữ SeedData=false theo bước 2. Cửa sổ chạy API đang bị chiếm bởi process foreground nên không dùng nó để nhập lệnh test. Chạy live smoke từ cửa sổ thứ hai vừa import:

```powershell
$petspaPreviousLiveFlag = $env:PETSPA_LIVE_SPIKE_ENABLED
try {
    $env:PETSPA_LIVE_SPIKE_ENABLED = 'true'
    dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~LiveIntegrationSpikeTests" --logger "console;verbosity=detailed" -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Live smoke failed; inspect sanitized test output.' }
}
finally {
    $env:PETSPA_LIVE_SPIKE_ENABLED = $petspaPreviousLiveFlag
}
```

File .env mặc định giữ flag false; nếu trước đó đã bật true trong shell, cuối phiên phải tự tắt hoặc đóng shell. Flag theo code mẫu spec chấp nhận true không phân biệt hoa/thường, không trim hay nhận 1/yes. Thiếu/sai config khi bật phải fail, không fallback sang mock. Mỗi test chỉ yêu cầu config dịch vụ của nó; có thể filter thêm tên test để chạy lại đúng dịch vụ cần kiểm tra.

- Neon: chỉ đọc qua pooled endpoint, timeout kết nối tối thiểu 30 giây; kiểm tra đủ 33 tên bảng đúng schema, FUR_CONDITIONS và EXTENSION_SETTINGS hợp lệ. Cold start có thể chậm; có thể đánh thức branch qua Console trước chạy, không tắt TLS hoặc đổi sang local để lấy PASS.
- Storage: PNG 1×1 hợp lệ được upload phẳng tại root bucket Staging thành `{GUID}.png`, signed URL TTL 300 giây được tải anonymous và đối chiếu byte, public URL anonymous phải từ chối 400/403/404. Delete đúng object trong finally bằng token riêng 15 giây. `CLEANUP_FAILED` làm test fail và hiện objectPath để Lead dọn tay; không xóa theo toàn bucket. Nếu upload thành công nhưng mất response trước nhận path, có thể còn object chưa xác định: kiểm tra bucket theo thời điểm chạy, không cam kết cleanup tuyệt đối khi mất mạng.
- SMTP: gửi một email thật chứa mã giả `SMOKE-TEST-...` tới `LiveSpike__EmailRecipient`; không tự retry. PASS chỉ chứng minh server chấp nhận mail. Lead kiểm tra inbox/spam để xác nhận delivery và ghi hai kết quả riêng.

Không chia sẻ raw logs từ nhà cung cấp, connection strings, signed URLs hay key. Bộ smoke chỉ xuất giai đoạn/status/type lỗi đã lọc và objectPath cleanup. Không coi offline PASS/no-op là đã chạy live.

### 7. Regression offline và bàn giao #27

Mở shell sạch, cấu hình `PETSPA_TEST_POSTGRES` tới PostgreSQL local riêng bằng cách cấp secret phù hợp môi trường của bạn; giữ live flag false. Không import backend/.env Staging:

```powershell
$env:PETSPA_LIVE_SPIKE_ENABLED = 'false'
dotnet test backend/PetSpa.sln -m:1
```

Yêu cầu toàn suite PASS, 0 errors, 0 warnings. No-op output của ba live tests xem bằng `--filter "FullyQualifiedName~LiveIntegrationSpikeTests" --logger "console;verbosity=detailed"`. PostgreSQL regression fixture chỉ tạo/migrate/drop database `petspa_test_<GUID>` nó sở hữu; không được trỏ PETSPA_TEST_POSTGRES sang Neon.

Handoff #27 ghi commit/version, topology/URL backend/frontend không nhạy cảm, ngày có slot/ca APPROVED, cách cấp tài khoản và quyền qua kênh riêng. Ghi riêng: offline regression; migration/bootstrap; API health/catalog; live Neon/Storage/SMTP; inbox delivery; cleanup; rotation. Checkpoint chưa chạy ghi **PENDING**, lỗi ghi **FAIL**, không đánh dấu live hoàn tất bằng kết quả offline. Browser nghiệm thu có đăng nhập thuộc #27, không thuộc ba tests này.
