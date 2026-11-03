# PetSpa Backend — M3 schema foundation

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

Runtime uses the existing ConnectionStrings:DefaultConnection key (environment variable ConnectionStrings__DefaultConnection), provided outside source control; runtime Neon uses pooler port 6543. The existing MigrationConnection key is reserved for an explicitly authorized future database operation via direct port 5432.

PetSpaDbContextFactory builds UseNpgsql() options without reading configuration or supplying a connection string. Creating migrations and generating migration SQL do not open a database. The command used to create the initial migration was:

```powershell
dotnet ef migrations add InitialCoreSchema --project backend/src/PetSpa.Api --startup-project backend/src/PetSpa.Api --no-build
```

Do not rerun that name after the migration exists. This task did not run database update, EnsureCreated or Migrate.

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
