# Issue #24 — Shift Scheduling Implementation Plan

**GitHub tracking:** [Issue #48](https://github.com/kdr1416/SE310-spa-pet-and-hotel/issues/48). Mã #24 trong tên plan/spec/branch là mã task lịch sử; GitHub #24 là PR Billing đã đóng, không phải issue Scheduling.

> **For agentic workers:** REQUIRED SUB-SKILL: dùng `superpowers:executing-plans`, thực hiện tuần tự trong chat với TDD; không dùng subagent-driven-development. Người dùng đã duyệt plan và yêu cầu triển khai ngày 2026-11-24; đang thực hiện tuần tự theo TDD. Không tự commit/push/PR/merge/deploy.

**Goal:** Triển khai E27/E28/E29/E54/E55 đúng contract, bảo toàn assignment và capacity khi lịch nhân viên thay đổi đồng thời với booking/check-in.

**Architecture:** Controller gọi WorkScheduleService, service truy cập PetSpaDbContext. Các mutation dùng READ COMMITTED, audit idempotency và khóa PostgreSQL; EmployeeScheduleLock chia sẻ key theo employeeId với E15/E16, không chia theo ngày. Trade-off đã được người dùng xác nhận: thay đổi lịch tương lai và check-in hiện tại của cùng nhân viên có thể phải chờ nhau trong transaction SQL, nhưng đây là điều cần thiết để E55 rút ca ngày mai thấy assignment Hotel vừa được E15/E16 ghi hôm nay. Key theo ngày hoặc chỉ khóa registration hôm nay không bảo vệ được trường hợp khác ngày này. Không giữ key khi upload, chờ người dùng hoặc gọi integration bên ngoài. Không thêm interface/framework/dependency/schema.

**Tech Stack:** .NET 10, EF Core/Npgsql, PostgreSQL, xUnit, FluentAssertions, WebApplicationFactory; dùng phiên bản đang có.

**Spec:** [Thiết kế đã duyệt](../specs/2026-11-23-issue-24-shift-scheduling-design.md), [API contract](../../design/api-contract.md) §3/§5 và clarification 2026-11-24; [D11/Q07, D17](../../requirements/scope-and-decisions.md). Plan cụ thể hóa Task 11, không mở rộng frontend hoặc scope #25/#27.

## Global Constraints

- Worktree `C:/Users/LENOVO/.codex/worktrees/issue-24-shift-scheduling/FinalProject`, branch `feat/issue-24-shift-scheduling`, baseline audit `c554f3b`. Không dùng worktree #26, không ghi đè thay đổi người dùng.
- Chỉ E28 cần đợt đang mở, biên `MoAt <= now <= DongAt`; E54 độc lập; Admin E29/E55 được thao tác sau deadline. APPROVED là công bố, không publish riêng.
- Actor active, role và permission đọc DB cả request mới lẫn replay. STAFF không được truyền maNhanVien kể cả own ID; E27 tối đa 90 ngày inclusive.
- Duplicate đúng ngày/ca chặn ở mọi trạng thái; overlap chỉ xét PENDING/APPROVED, dùng khoảng nửa mở. Ca tiếp giáp được phép.
- Ngày nghỉ lấy từ `NgayNghiSet.Any(x => x.Ngay == ngay)`, không suy từ thứ trong tuần. Nhân viên được đăng ký hoặc điều chuyển đến phải có quyền CARE_UPDATE để trực Hotel; không giảm sức chứa phòng hoặc vô hiệu hóa việc chăm sóc Hotel.
- Slot mới lấy snapshot ca mẫu, counters bằng 0 và đóng tiếp nhận nếu là ngày nghỉ; slot cũ giữ nguyên. E55 giữ registration ID, APPROVED, DotId, DuyetAt và DuyetBoiId gốc; audit riêng lần chuyển.
- Ca qua nửa đêm không thuộc baseline: `CaLamViecConfiguration.cs:18` bắt buộc `GioKetThuc > GioBatDau`. Không mở schema để hỗ trợ ca đêm trong #24.
- Dùng TimeProvider, ngày Asia/Ho_Chi_Minh; đọc lại giờ sau lock waits. Không sửa Entity/DbContext/migration hoặc global validation/auth pipeline.
- PostgreSQL test trên cluster loopback riêng; fixture chỉ tạo/migrate/drop database `petspa_test_GUID` nó sở hữu. Không Neon/shared DB, không in credentials/connection string.
- Mỗi bước RED phải compile được và fail đúng behavior; lỗi restore/DB/Windows DLL lock không là RED. Build/test chạy tuần tự `-m:1`.
- Lệnh dưới đây là lệnh sẽ chạy khi triển khai, không phải kết quả đã kiểm chứng. Task 1–6 thực hiện tuần tự; xem bằng chứng execution ở cuối tài liệu.

## Review Focus

1. Hai E28 đầu tiên cho ca khác ID nhưng chồng giờ: chỉ một đăng ký được ghi (Task 3, Task 6).
2. Replay đợi request đầu commit: phải trả snapshot 200 trước terminal-state guard, quyền đã thu hồi vẫn 403 (Tasks 2–6).
3. E55 ca ngày mai chạy đồng thời với assignment Hotel hôm nay: dùng cùng employee key, không chỉ registration ngày hiện tại (Tasks 5–6).
4. Pet DONE/CANCELLED được loại khỏi guard; Hotel đã check-in chưa trả vẫn phải được kiểm tra dù qua giờ trả dự kiến (Task 5).
5. Slot đích chưa tồn tại hoặc nguồn vừa có booking giữ công suất: không slot trùng, không ghi đè slot đã đóng, không giảm capacity khả dụng dưới mức cam kết (Tasks 4–6).

## Files và interfaces chung

| File | Trách nhiệm |
|---|---|
| Create `backend/src/PetSpa.Api/Controllers/Scheduling/WorkScheduleController.cs` | Đúng năm routes; JWT actorId, validation cục bộ E27, status/envelope/traceId. |
| Create `backend/src/PetSpa.Api/Services/Scheduling/WorkScheduleService.cs` | Permission DB, query/projection, bốn commands, guards, transaction/audit/replay; không generic command framework. |
| Create `backend/src/PetSpa.Api/Services/Scheduling/EmployeeScheduleLock.cs` | Static helper lấy PostgreSQL employee advisory lock dùng chung cho Scheduling/E15/E16, không DI/interface. |
| Modify `backend/src/PetSpa.Api/Program.cs` | Chỉ thêm scoped WorkScheduleService, giữ CustomerLookupService và DI #42. |
| Modify `backend/src/PetSpa.Api/Services/Care/CheckinService.cs` | Thêm employee key và resource row locks tại pha commit E15/E16; giữ upload preflight và Admin override. |
| Create `backend/tests/PetSpa.Tests/Scheduling/WorkScheduleEndpointTests.cs` | WAF HTTP tests của năm endpoints; helper/seed riêng tính năng dùng fixture có sẵn. |
| Create `backend/tests/PetSpa.Tests/Scheduling/WorkScheduleConcurrencyTests.cs` | HTTP concurrency trên PostgreSQL; dùng blocker transaction và CareTestSupport.WaitForLockWaitersAsync, không tạo polling framework mới. |
| Modify `backend/tests/PetSpa.Tests/Care/CheckinEndpointTests.cs`, `AssignmentEndpointTests.cs` nếu cần | Regression policy và lock/time behavior E15/E16; không di chuyển test hiện hữu. |

DTO dùng lại nguyên bản ở `backend/src/PetSpa.Core/DTOs/Scheduling/ScheduleDto.cs`. Không sửa DTO hoặc global validation để đổi error mapping của riêng E27.

Các method service là instance methods, trả `Task<ScheduleResult<T>>`; parameter cuối luôn `CancellationToken ct`. Khai báo `public sealed record ScheduleResult<T>(int StatusCode, T? Data = default, string Message = "", string? ErrorCode = null, List<ApiErrorItem>? Errors = null)` ngay trong WorkScheduleService.cs. Đây là kết quả application service trong PetSpa.Api theo convention CheckinResult, không thêm abstraction vào domain Core. Controller tạo ApiResponse và HTTP response; private `IActionResult Respond<T>(ScheduleResult<T> result)` gắn traceId của request hiện tại vào lỗi. Bốn mutations vẫn nhận `string traceId` để ghi AuditLog.TraceId, không phải để service tạo HTTP envelope; snapshot KetQua chỉ lưu DTO.

### Controller và routes chính xác

WorkScheduleController kế thừa ControllerBase, có `[ApiController]` và `[Authorize]`; không đặt prefix route chung. Mỗi action có absolute route như bảng dưới, trả `Task<IActionResult>`. Actor lấy từ claim `id`; thiếu/sai actor ID trả 401. Service kiểm tra quyền DB: E54/E29/E55 cần ADMIN và SCHEDULE_APPROVE; E27/E28 cần SCHEDULE_REGISTER. Response lỗi theo contract có traceId, không có SQL/stack trace.

| Operation | Attribute | Action signature |
|---|---|---|
| E27 | `[HttpGet("/api/internal/work-schedule")]` | `List([FromQuery] string? tuNgay, [FromQuery] string? denNgay, [FromQuery] int? maNhanVien, CancellationToken ct)` |
| E54 | `[HttpPost("/api/admin/shift-windows")]` | `CreateWindow([FromBody] ShiftWindowWriteRequest request, CancellationToken ct)` |
| E28 | `[HttpPost("/api/internal/shift-registrations")]` | `Register([FromBody] ShiftRegisterRequest request, CancellationToken ct)` |
| E29 | `[HttpPost("/api/admin/shift-registrations/{id}/decision")]` | `Decide([FromRoute] int id, [FromBody] ShiftDecisionRequest request, CancellationToken ct)` |
| E55 | `[HttpPost("/api/admin/shift-registrations/{id}/transfer")]` | `Transfer([FromRoute] int id, [FromBody] ShiftTransferRequest request, CancellationToken ct)` |

Không dùng route constraint `{id:int}`: giá trị không parse được phải trả 400 VALIDATION_FAILED qua model binding, thay vì bị router trả 404. ID parse được nhưng <=0 được action từ chối với 400 trước service; ID hợp lệ không tồn tại trả 404 NOT_FOUND.

### Request key và fingerprint (dùng chung Tasks 2–6)

Request advisory key là `BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes($"shift-scheduling:{actorId}:{operation}:{requestId:N}")))`; dùng overload bigint. Employee key dùng không gian `(int,int)` khác, qua EmployeeScheduleLock. Audit unique key vẫn là `(actorId, operation, requestId)`.

Serialize đúng các anonymous objects dưới đây bằng `new JsonSerializerOptions(JsonSerializerDefaults.Web)` có giữ property null, rồi `Convert.ToHexString(SHA256.HashData(bytes))`. Giữ thứ tự property như bảng. Instant được chuẩn hóa UTC và microsecond theo phép mã hóa Npgsql (số microsecond nguyên tính từ epoch PostgreSQL 2000-01-01); lý do trim, optional không gửi thành null; lý do có gửi nhưng chỉ whitespace là validation error. RequestId thuộc key, không cần lặp trong fingerprint. Không đưa traceId, giờ server hoặc entity state vào fingerprint.

| Operation | Canonical object |
|---|---|
| SHIFT_WINDOW_CREATE | `new { tuNgay = request.TuNgay, denNgay = request.DenNgay, moAt = NormalizePgInstant(request.MoAt), dongAt = NormalizePgInstant(request.DongAt) }` |
| SHIFT_REGISTER | `new { ngay = request.Ngay, maCa = request.MaCa }` |
| SHIFT_DECISION | `new { registrationId, quyetDinh = request.QuyetDinh, lyDo = request.LyDo?.Trim() }` |
| SHIFT_TRANSFER | `new { registrationId, ngay = request.Ngay, maCa = request.MaCa, lyDo = request.LyDo.Trim() }` |

Tests phải kiểm tra lý do khác whitespace bao quanh và instant có offset khác nhưng cùng thời điểm được replay; đổi registrationId hoặc business value phải conflict. Operation codes E04/E14/E15/E16/E38/E64 lần lượt là tạo booking, ứng viên assignment, check-in, đổi người chăm sóc, danh sách pet phụ trách và booking options; xem registry contract. [Task 11](../../plans/sprint-2/sprint-2-plan.md) là task tổng của #24; PR #42 bổ sung BookingOptionsService/FurConditionsService/CareTasksService cần giữ trong DI.

## Task 1 — E27: lịch cá nhân và Admin filter (D11/Q07, UC53)

**Files:** tạo WorkScheduleController.cs, WorkScheduleService.cs, WorkScheduleEndpointTests.cs; sửa Program.cs.

**Interfaces:** `ListAsync(int actorId, ScheduleQuery query, CancellationToken ct)` trả `Task<ScheduleResult<IReadOnlyList<ShiftRegistrationDto>>>`.

- [x] **Step 1 — Chuẩn bị baseline và DB của tests.** Kiểm tra Git status/branch và file chỉ dẫn hiện hành; so base với origin/develop sau fetch nếu cần, không tự rebase qua thay đổi chưa commit. Đọc test support `Infrastructure/PostgresTestDatabase.cs`, `Infrastructure/PostgresWebApplicationFactory.cs`, `Foundation/FoundationTestSupport.cs`. Tạo hoặc reuse duy nhất cluster test riêng đã xác minh ownership, loopback và port riêng; thiết lập PETSPA_TEST_POSTGRES trong process ngoài Git. Biến này phải là kết nối Npgsql tới admin database của cluster test với quyền CREATE DATABASE; đọc backend/README.md, không trỏ runtime connection vào fixture. Chạy baseline build rồi tests; phân biệt lỗi môi trường, không tính chúng là RED. Test host dùng fake TimeProvider, seed employees/grants/ca của chính database test; không sửa fixture toàn suite nếu override host cục bộ đủ dùng.
- [x] **Step 2 — Viết WAF tests.** `E27_StaffSeesOnlyOwnRows`, `E27_StaffCannotSupplyEmployeeFilter` (own/other ID đều 403), `E27_AdminCanFilterOrReadAll`, `E27_ReadsRoleAndGrantsFromDatabase` (token cũ sau đổi role/quyền/IsActive), `E27_RejectsBadDates`. Các assertions bắt buộc:

  ```csharp
  // TuNgay = 2035-01-01: 2035-03-31 là 90 ngày inclusive.
  ninetyDays.StatusCode.Should().Be(HttpStatusCode.OK);
  ninetyOneDays.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  errorCode.Should().Be("INVALID_DATE_RANGE"); // đảo ngày hoặc 91 ngày
  staffOwnFilter.StatusCode.Should().Be(HttpStatusCode.Forbidden);
  ```

  Thiếu ngày, sai format hoặc maNhanVien<=0 trả 400 VALIDATION_FAILED; thiếu JWT trả 401; thiếu grant hoặc actor inactive trả 403. Kiểm tra mọi trạng thái trong own scope, array rỗng và sort ổn định theo ngày/giờ bắt đầu/ID. Thêm `E27_InvalidQueryValuesReturn400` cho filter không parse được; cả ID 0 và ID âm phải được kiểm tra rõ.
- [x] **Step 3 — RED.** Chạy `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~WorkScheduleEndpointTests" -m:1 --nologo`. Mong đợi HTTP route chưa có (404) trái expected 200/403, không compile/database failure.
- [x] **Step 4 — Implement tối thiểu.** Bind action theo bảng routes. Parse ngày bằng `DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)`; thiếu/sai format và maNhanVien có giá trị <=0 trả 400 VALIDATION_FAILED, với ApiErrorItem.Field đúng `tuNgay`/`denNgay`/`maNhanVien`. Giá trị filter không parse được do model binding xử lý. Tạo ScheduleQuery hiện hữu rồi gọi service, tránh automatic IValidatableObject xử lý đảo ngày trước custom mapping. Service trả INVALID_DATE_RANGE cho đảo ngày hoặc DayNumber difference >=90; kiểm tra permission DB và query scope. Sort `Ngay`, `CaLamViec.GioBatDau`, `Id`, chuyển sang DTO. Khai báo ScheduleResult và controller Respond theo interfaces chung; chỉ thêm `AddScoped<WorkScheduleService>()` vào Program.cs.
- [x] **Step 5 — GREEN/DoD.** Chạy lại đúng lệnh Step 3 và `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~SharedDtoContractTests" -m:1 --nologo`; toàn bộ tests liên quan pass. Self-review diff tập trung vào data leakage và validation. Chưa commit nếu chưa được yêu cầu.

## Task 2 — E54: mở đợt và audit idempotency (D11/Q07, UC41)

**Files:** WorkScheduleController.cs, WorkScheduleService.cs, WorkScheduleEndpointTests.cs.

**Interfaces:** `CreateWindowAsync(int actorId, ShiftWindowWriteRequest request, string traceId, CancellationToken ct)` trả `Task<ScheduleResult<ShiftWindowDto>>`. Operation `SHIFT_WINDOW_CREATE`, target type `DotDangKyCa`; traceId dùng cho audit.

- [x] **Step 1 — Viết tests.** `E54_CreatesFirstWindowWithoutExistingOpenWindow` →201 và DB row/audit; replay cùng requestId/payload →200 cùng maDot, đúng một row/audit; payload đổi →409 IDEMPOTENCY_CONFLICT. STAFF dù có grant →403, ADMIN thiếu grant →403, invalid/reversed dates hoặc MoAt>=DongAt hoặc instant thiếu offset →400. Cho phép đợt tương lai/đợt chồng nhau vì không có guard cấm. `E54_ReplayStillChecksActiveActor` và `E54_ReplayReturnsStoredResult` sau thay đổi state hỗ trợ.
- [x] **Step 2 — RED.** Chạy `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~WorkScheduleEndpointTests" -m:1 --nologo`; tests E54 mới phải fail ở missing behavior.
- [x] **Step 3 — Implement.** Kiểm tra ADMIN active và grant từ DB; dùng key/fingerprint chính xác ở mục interfaces chung. Mở transaction, lấy request advisory lock, kiểm tra lại actor và replay trước guard. Tạo entity và lưu ID; ghi audit KetQua chứa DTO rồi commit. Private helpers replay/audit dùng lại cho Tasks 3–5, không tách framework. Lưu operation, requestId, fingerprint, target, traceId, role, timestamp; từ chối requestId rỗng. Nếu audit unique bị xung đột, dispose transaction, clear tracker rồi đọc kết quả request đã commit; khác payload/target trả IDEMPOTENCY_CONFLICT.
- [x] **Step 4 — GREEN/DoD.** Chạy lại lệnh Step 2; xác nhận replay không ghi thêm và 201 chỉ lần đầu. Review không áp guard “đợt đang mở” lên E54.

## Task 3 — E28: đăng ký ca không trùng/chồng (D11/Q07, D17, UC41)

**Files:** WorkScheduleController.cs, WorkScheduleService.cs, WorkScheduleEndpointTests.cs; tạo EmployeeScheduleLock.cs và bắt đầu WorkScheduleConcurrencyTests.cs.

**Interfaces:** `RegisterAsync(int actorId, ShiftRegisterRequest request, string traceId, CancellationToken ct)` trả `Task<ScheduleResult<ShiftRegistrationDto>>`; operation `SHIFT_REGISTER`, target `DangKyCa`. Helper `internal static Task AcquireAsync(PetSpaDbContext db, int employeeId, CancellationToken ct)` trong EmployeeScheduleLock.

- [x] **Step 1 — Viết tests.** E54→E28→E27 với 201/PENDING, DotId đúng và replay 200. Ca không tồn tại trả HTTP 404, ca inactive trả HTTP 409; ngày ngoài đợt, trước mở/sau đóng →409; đúng mở/đúng đóng →201. Clock phải đọc lại sau lock wait, không dùng timestamp preflight. Nhiều đợt: chọn đợt đang mở theo MoAt DESC rồi Id DESC. Ngày nghỉ: nhân viên có quyền CARE_UPDATE được đăng ký, không có quyền này trả HTTP 409; không tạo mục đích/DTO mới.
- [x] **Step 2 — Kiểm tra duplicate/overlap và RED.** `E28_DuplicateAnyStateIsRejected` (PENDING/APPROVED/REJECTED →409 INVALID_TRANSITION); `E28_OverlappingPendingOrApprovedIsRejected`, `E28_AdjacentOrDifferentDayIsAllowed`. Viết concurrency test đầu tiên `E28_ConcurrentFirstOverlappingShiftsOnlyOneCommits` với hai shift IDs khác nhau, chưa registration, hai requestIds. Chạy riêng `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~WorkSchedule" -m:1 --nologo`; tests mới fail đúng guard/concurrency chưa có.
- [x] **Step 3 — Implement helper và E28.** Lấy request-key lock, kiểm tra replay, rồi lấy employee advisory key. Sau chờ khóa, kiểm tra lại actor, giờ, window, overlap và row state trước khi insert PENDING và audit. Employee key dùng overload `pg_advisory_xact_lock(int,int)` với namespace hằng `0x53434844` và employeeId; tất cả callers dùng helper này. Trong helper, `if (!db.Database.IsRelational()) return Task.CompletedTask;` để giữ InMemory regression; các raw SQL row locks mới trong CheckinService cũng nằm trong guard IsRelational hiện hành. Không dùng InMemory để khẳng định concurrency; toàn bộ tests mới Scheduling dùng PostgreSQL. Request-key theo mục interfaces chung. Không tạo slot cho PENDING. Duplicate unique index là backstop, map về 409 INVALID_TRANSITION thay vì SQL500.
- [x] **Step 4 — GREEN/DoD.** Chạy lại filter WorkSchedule; assert hai concurrent registrations có đúng một 201, một 409, một registration/audit; không dùng Task.Delay làm bằng chứng serialization. Recheck tất cả biên thời gian và ngày nghỉ.

## Task 4 — E29: duyệt sau deadline, slot thật và publication (D11/Q07, D17, UC50)

**Files:** WorkScheduleController.cs, WorkScheduleService.cs, WorkScheduleEndpointTests.cs, WorkScheduleConcurrencyTests.cs.

**Interfaces:** `DecideAsync(int actorId, int registrationId, ShiftDecisionRequest request, string traceId, CancellationToken ct)` trả `Task<ScheduleResult<ShiftRegistrationDto>>`; operation `SHIFT_DECISION`, target `DangKyCa`. Dùng replay helpers Task 2 và employee lock Task 3.

- [x] **Step 1 — Viết tests.** PENDING APPROVE/REJECT sau DongAt đều trả HTTP 200; terminal state trả 409 INVALID_TRANSITION trừ replay hợp lệ. Path ID sai format hoặc <=0 trả 400, ID không tồn tại trả 404. APPROVE kiểm tra lại employee/shift active, quyền CARE_UPDATE vào ngày nghỉ và overlap. Cả APPROVE và REJECT đều lưu DuyetBoiId=actorId, DuyetAt=now; lý do đã trim nếu có lưu AuditLog.LyDo, không ghi vào LyDoDieuChuyen. REJECT lưu REJECTED và không tạo slot. APPROVED xuất hiện ngay trong E27/E14, không publish action.
- [x] **Step 2 — Kiểm tra slot và RED.** `E29_ApprovalCreatesRealSlotVisibleToE64` kiểm tra maKhungGio là slot PK đúng ngày/ca, không giả bằng maCa; defaults TongSlot=SucChuaPet, TranPhut=ca.TranPhut, counters=0, holiday MoNhanKhach=false. `E29_ExistingClosedSlotIsPreserved` pin cả flags/capacity/counters; hai nhân viên approve cùng ngày/ca chỉ tạo một slot. Retry sau terminal-state change vẫn trả snapshot với HTTP 200. Chạy `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~WorkSchedule" -m:1 --nologo`, xác nhận failures mới đúng E29.
- [x] **Step 3 — Implement.** Lấy request lock và kiểm tra replay trước state guards. Đọc source không tracking, lấy employee lock rồi đọc lại source. Với APPROVE, upsert missing slot bằng `ON CONFLICT DO NOTHING` trước existing row locks; sau đó khóa slot IDs tăng rồi registration IDs tăng. Đọc fresh state để kiểm tra guards và ghi decision/audit trong cùng transaction. REJECT không upsert slot; cập nhật approval metadata và audit lý do như Step 1. Không ghi đè bằng Entity EF đã load trước lock wait; không yêu cầu đợt mở.
- [x] **Step 4 — GREEN/DoD.** Chạy filter WorkSchedule và regression `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~BookingOptionsEndpointTests|FullyQualifiedName~AssignmentEndpointTests" -m:1 --nologo`. Kiểm tra rollback command thất bại không để slot/audit dư.

## Task 5 — E55 phối hợp E15/E16: transfer bảo toàn assignment/capacity (D11/Q07, D17, UC50)

**Files:** WorkScheduleController.cs, WorkScheduleService.cs, EmployeeScheduleLock.cs (chỉ reuse), CheckinService.cs; WorkScheduleEndpointTests.cs, WorkScheduleConcurrencyTests.cs; Care/CheckinEndpointTests.cs và AssignmentEndpointTests.cs nếu cần.

**Interfaces:** `TransferAsync(int actorId, int registrationId, ShiftTransferRequest request, string traceId, CancellationToken ct)` trả `Task<ScheduleResult<ShiftRegistrationDto>>`; operation `SHIFT_TRANSFER`, target `DangKyCa`. Không đổi public ICheckinService interface; inject TimeProvider vào concrete CheckinService nếu cần đọc lại clock sau locks, cập nhật direct constructor calls nếu có.

- [x] **Step 1 — Viết tests hành vi.** Transfer APPROVED sau deadline sang ngày/ca mới trả HTTP 200; giữ ID/APPROVED/DotId/DuyetAt/DuyetBoiId, reason trim và audit before/after đúng. PENDING/REJECTED, same source/target, blank reason, inactive ca/employee, ngày nghỉ nhưng nhân viên thiếu quyền CARE_UPDATE, duplicate đích bất kỳ state hoặc overlap →400/409 đúng contract. Destination không cần thuộc một đợt mới. E55 không dời booking/counters, không tạo registration mới; destination slot theo Task 4.
- [x] **Step 2 — Kiểm tra assignment/capacity và RED.** `E55_ActiveAssignmentBlocksTransfer` kiểm tra `DonDatLich.KhungGioId` hoặc service ở nguồn có `TrangThai != OrderServiceState.HUY`; `E55_HotelWithoutReturnBlocksCurrentAndFutureShift` kiểm tra Hotel đã check-in/chưa trả, kể cả không có service line nguồn và overstay. Các guard assignment trả 409 INVALID_TRANSITION với message “Không thể điều chuyển ca khi nhân viên còn thú cưng đang phụ trách.” DONE/CANCELLED được loại theo thiết kế đã duyệt. `E55_TransferCannotBreakCommittedCapacity`: nếu SlotDaDat>0 mà không còn care staff active, hoặc min(TranPhut, remaining*shiftMinutes)<PhutDaGiu, trả 409 CAPACITY_EXCEEDED; bằng trần được phép. Không tính inactive/non-care vào capacity. Thêm replay cũ sau lần transfer tiếp theo. Chạy filter WorkSchedule, xác nhận RED đúng guards mới.
- [x] **Step 3 — Implement transfer.** Lấy request lock, kiểm tra replay và lấy employee key. Refresh source; sắp xếp toàn tập missing slots theo `(Ngay ASC, CaLamViecId ASC)` trước khi upsert bằng ON CONFLICT DO NOTHING. Sau đó khóa toàn tập slots theo ID tăng, rồi registrations theo ID tăng; đọc lại assignment/counters/counts trước update cùng record và commit audit. Không tự nhả phút, bù expiry, mở lại slot hay lấy booking lock sau slots. Hotel guard nguồn hiện tại/tương lai dùng `sourceDate > today || (sourceDate == today && sourceShift.GioKetThuc > nowTime)` theo giờ VN, kèm CheckInAt!=null, TraLaiAt==null và loại terminal states. Query assignment lọc employeeId/state trước với AnyAsync, không materialize lịch sử; index hiện hữu `(NhanVienId, TrangThaiChamSoc)` hỗ trợ lọc, nhưng không tự coi index là bằng chứng latency đã đạt.
- [x] **Step 4 — Viết tests race trước sửa E15/E16.** `E55_TransferVersusCheckinUsesSameEmployeeLock`, `E55_TomorrowTransferVersusTodayHotelAssignmentUsesSameEmployeeLock`, mỗi test điều khiển cả hai thứ tự thắng. Chạy filter từng tên để thấy RED ở thiếu serialization, không dựa vào timeout ngẫu nhiên. Ghi rõ trace/barrier chứng minh transaction nào đang chờ khóa.
  Thêm `E15_UploadDoesNotHoldEmployeeLock`: tạm dừng upload bằng Storage.BeforeUpload, xác nhận Scheduling không bị giữ employee key bởi pha upload; dùng helper hiện hữu để điều khiển các pha DB, không thêm khóa bao quanh I/O. Chấp nhận contention ngắn giữa các ngày trong pha SQL vì guard Hotel đã duyệt cần serialization theo nhân viên.
- [x] **Step 5 — Implement phối hợp tối thiểu.** Pha commit E15 sau upload đã giữ booking lock; lấy employee key cho assignee, tiếp theo khóa service/intake slots theo ID tăng và registrations ngày thao tác theo ID tăng. Đọc lại giờ/ca/permission trước khi ghi assignment. E16 lấy cùng employee key cho newEmployeeId sau booking lock, trước slot/registration hiện hữu. Áp dụng IsRelational guard Task 3 cho cả hai paths. Không giữ employee lock trong upload preflight; không đổi Admin override, ảnh, giá hoặc invoice guards. E55 không lấy booking lock. Nếu E15/E16 commit trước, E55 thấy assignment và chặn; nếu E55 commit trước, E15/E16 đọc ca hiện tại mới và áp policy hiện hành. Với transfer tương lai hoặc Admin override, hai request có thể đều thành công theo thứ tự hợp lệ; không giả định mọi race chỉ một request success.
- [x] **Step 6 — GREEN/DoD.** Chạy `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~WorkSchedule|FullyQualifiedName~CheckinEndpointTests|FullyQualifiedName~AssignmentEndpointTests" -m:1 --nologo`; kiểm tra trạng thái cuối registration/pet/audit/counters, giữ regressions check-in multi-pet/Admin override.

## Task 6 — Concurrency và regression toàn luồng E27–E29/E54–E55/E04/E15/E16

**Rules:** D11/Q07, D17 và system-design §5. **Files:** WorkScheduleConcurrencyTests.cs; chỉ sửa những implementation files Tasks 1–5 khi test mới chứng minh thiếu sót. Không production test hooks.

**Interfaces:** WAF HTTP endpoints và DB fixture có sẵn. Dùng một kết nối Npgsql với blocker transaction để giữ khóa kiểm soát và `CareTestSupport.WaitForLockWaitersAsync(connectionString, sqlPattern, expected, ct)` để xác nhận HTTP request đang chờ. Helper internal ở cùng test assembly nên gọi được, không cần extract. Dùng CancellationTokenSource với timeout hữu hạn; release blocker trong finally. Tất cả requests dùng scopes/connections độc lập; không chia sẻ DbContext giữa concurrent tasks. Không viết lại polling helper hoặc thêm lock-wait interceptor framework. Có thể dùng callback Storage.BeforeUpload hiện hữu để điều khiển preflight khi cần; polling có điều kiện của helper không phải Task.Delay tùy ý để đoán request đã vào lock.

- [x] **Step 1 — Viết tests chưa có, chạy RED từng case trước sửa.** Concurrent same-key cho cả bốn mutations: đúng một mutation/audit và snapshot HTTP 200 cho request replay; đổi payload/target trả 409 IDEMPOTENCY_CONFLICT, không trả INVALID_TRANSITION. Khác actor/operation độc lập. Quyền bị thu hồi trước replay trả 403. Hai transfer khác nhân viên cùng nguồn không được cả hai rút khiến thiếu capacity. E04 lấy suất/phút cuối đồng thời E55: request chờ phải kiểm tra lại capacity dưới lock, counters không âm/vượt trần. Dùng CareTestSupport.Storage và khai báo email stub test-local trong WorkScheduleConcurrencyTests.cs theo `PetSpa.Api.Services.Booking.IEmailSender`; SendTrackingCodeAsync trả Task.CompletedTask và đếm số lần gọi, ba methods còn lại throw NotSupportedException vì test không gọi. Không tham chiếu private BookingManagementEndpointTests.TestEmail, không extract shared email abstraction hoặc gửi SMTP thật.
- [x] **Step 2 — Kiểm tra lock ordering và atomicity.** Điều khiển cả hai thứ tự cho upsert slot chung, opposite transfer, E55/E04, E55/E15/E16 và Hotel khác ngày. Assert không deadlock/timeout và final state tương đương một thứ tự hợp lệ. Command thất bại rollback cả slot mới/audit/mutation. Nếu phát hiện deadlock, thu thập wait graph và sửa thứ tự lấy khóa; không thêm deadlock retry trong #24 để che lỗi ordering. Không khẳng định ordering là bằng chứng mọi deadlock đều bất khả thi.
- [x] **Step 2a — Kiểm tra lỗi transient trước commit.** Thêm một fault injector test-local theo pattern ReadOutage ở BookingOptionsEndpointTests.cs: chỉ bật sau seed/host startup; ném NpgsqlException có TimeoutException bên trong khi một DB read được thực hiện. Kiểm tra E27 và command E54 trả HTTP 503 TEMPORARY_FAILURE, error response không chứa SQL/exception, không tạo row/audit. Nếu cần case EF bọc exception, dùng cùng injector để kiểm tra wrapper thực tế; không dựng framework hoặc test toàn bộ lỗi provider. Giữ mapping service-scoped tương tự BookingManagementService/BookingOptionsService; không đổi GlobalExceptionMiddleware và không giả lập commit outcome chưa rõ là rollback chắc chắn.
- [x] **Step 3 — Implement đúng thiếu sót được RED chứng minh, chạy GREEN từng case.** Nếu case đã GREEN do slice trước, ghi đây là coverage bổ sung; không giả tạo production regression để có RED. Với `git diff` self-review, xác nhận mọi path lấy chung employee key trước resource rows, slot trước registration, recheck replay sau lock và không stale EF entities.
- [x] **Step 4 — Regression cuối chạy tuần tự.** Chạy `dotnet build backend/PetSpa.sln -m:1 --nologo`, sau khi xong mới chạy `dotnet test backend/PetSpa.sln -m:1 --nologo`. DoD: build có 0 errors/không warning mới; toàn bộ tests pass trên cluster test riêng, bao gồm Care InMemory regressions. Kiểm tra DI CustomerLookupService/BookingOptionsService/FurConditionsService/CareTasksService vẫn còn; E14/E64 đọc dữ liệu E29/E55 tạo, E38 không bị đổi schema/response. Không frontend checks vì không đổi frontend.
- [x] **Step 5 — Handoff.** `git diff --check`, đối chiếu routes/DTO/guards với contract; cập nhật checklist và bằng chứng RED/GREEN/regression trong plan, ghi rõ check chưa chạy nếu có. Khi người dùng yêu cầu commit mới tạo các commit theo slices đã GREEN. Trước push được yêu cầu: fetch/rebase develop, giữ DI của issue khác và chạy lại checks trên kết quả ghép. Không push/PR/merge/deploy tự động.

## Coverage và kiểm tra plan

| Yêu cầu thiết kế | Task |
|---|---|
| Permission DB, E27 scope/validation/90 ngày inclusive | 1, replay regressions 2–6 |
| E54 độc lập, command idempotency và replay snapshot | 2, 6 |
| TimeProvider/window boundaries, D17, duplicate/overlap mọi trạng thái | 3 |
| PENDING-only E29, APPROVED công bố, slot thật tương thích E14/E64 | 4 |
| E55 provenance, closed slot preservation, capacity và Hotel overstay | 5 |
| Chung employee key E15/E16 và cross-day Hotel concurrency | 5–6 |
| Concurrency cùng key/khác employee/booking, atomic rollback, regression | 6 |

Self-review plan: mỗi endpoint có WAF slice và DoD; method/DTO names thống nhất; năm Review Focus đã có test owner; không phụ thuộc schema/dependency mới. Kiểm tra plan là kiểm tra tài liệu, không phải xác nhận implementation/concurrency đã pass. Ở thời điểm viết plan, cluster test chưa được thiết lập; phần execution ghi lại cluster riêng và kết quả thực tế.

## Kết quả đối chiếu consolidated review — 2026-11-24

Đã đọc consolidated_review_report.md và cả năm stage reports tại checkout gốc `D:/Năm 3/dotnet/FinalProject`; không sửa các báo cáo đó. Bảng này ghi quyết định cho từng góp ý, dựa trên source ở worktree #24. Dòng bằng chứng lấy tại baseline c554f3b. Trong bảng: Services/ và Data/ tính từ backend/src/PetSpa.Api; Core/ tính từ backend/src/PetSpa.Core; Care/, Booking/ và Infrastructure/ tính từ backend/tests/PetSpa.Tests.

| Item | Quyết định và bằng chứng |
|---|---|
| BLK-01 | Bổ sung đầy đủ action signatures/bindings/absolute routes. Dùng `{id}` thay `{id:int}` để model binding trả đúng 400 cho ID sai format; contract §1 quy định path sai là 400. |
| BLK-02 | Đã đối chiếu: repo dùng nhiều convention, không phải mọi service trả tuple/envelope. CheckinService trả CheckinResult/AssignmentCandidatesResult (`Core/Interfaces/Care/ICheckinService.cs:47`), BookingCreateService trả BookingCreateResult (`Services/Booking/BookingCreateService.cs:17,34`), còn BookingManagementService.ListAsync trả ApiResponse (`Services/Booking/BookingManagementService.cs:26`). Chọn ScheduleResult trong cùng file để đồng bộ với Care và BookingCreate, controller tạo envelope; không refactor các service khác hoặc tuyên bố convention ApiResponse hiện hữu là sai kiến trúc. Giữ StatusCode theo CheckinResult và traceId argument cho audit (`Services/Care/CareOperation.cs:122`, `BookingManagementService.cs:173`); response dùng traceId request hiện tại. |
| MAJ-01 | Chấp nhận IsRelational guard cho helper và raw SQL mới. `Infrastructure/AuthApplicationFactory.cs:46` dùng InMemory; `CheckinService.cs:116` và `CareOperation.cs:87` đã guard relational. Bộ Care có cả InMemory lẫn PostgreSQL, không phải toàn bộ đều InMemory. |
| MAJ-02 | Không chia key theo ngày. User đã duyệt employee-wide key; registration lock ngày hiện tại không đồng bộ được E16 gán Hotel hôm nay với E55 rút ca ngày mai. Chấp nhận contention giữa các ngày trong transaction SQL, không giữ key khi upload/chờ người dùng. Bổ sung test không giữ key khi upload; không tự tối ưu bằng cách bỏ guard Hotel. |
| MAJ-03 | Chấp nhận test-local IEmailSender stub. `Booking/BookingManagementEndpointTests.cs:729` khai báo TestEmail private; Task 6 không tham chiếu nó và không extract fixture dùng chung. |
| MAJ-04 | Bổ sung chính xác key template/canonical objects, UTC instant normalization và reason trim; route registrationId có trong fingerprint E29/E55. |
| MAJ-05 | Đối chiếu contract §1 (409 chung), §3.2 ShiftDecision (lyDo optional) và §5 (không phá assignment/capacity). Bảng guard gộp ban đầu chưa chỉ rõ errorCode của từng nhánh E55; mapping chi tiết đã được làm rõ trong contract theo Task 11 và thiết kế đã duyệt: state/duplicate/overlap/assignment là INVALID_TRANSITION, thiếu capacity là CAPACITY_EXCEEDED, replay khác dữ liệu là IDEMPOTENCY_CONFLICT. DangKyCa có TrangThai/DuyetAt/DuyetBoiId/LyDoDieuChuyen, không có LyDoTuChoi (`Core/Entities/Scheduling/DangKyCa.cs:14`); Data/Configurations/AuditLogConfiguration.cs:25 có ly_do. REJECT ghi REJECTED, người/thời điểm quyết định và audit reason; không dùng trường lý do điều chuyển hoặc thêm cột. |
| MAJ-06 | Bổ sung thứ tự upsert `(Ngay ASC, CaLamViecId ASC)` trước row locks theo ID; có opposite-transfer test. |
| MAJ-07 | Bỏ retry dự phòng: gặp deadlock phải tìm lỗi ordering trước. Không bỏ kiểm thử 503: `Services/Booking/BookingManagementService.cs:184`, `BookingOptionsService.cs:38` đã map transient; `Booking/BookingOptionsEndpointTests.cs:23` đã có fault-injection test. Global middleware mặc định500 không chứng minh service phải trả500; contract §1 quy định503 cho hạ tầng chưa commit. Chỉ giữ injector test-local cần thiết, không framework mới. |
| MAJ-08 | Tái sử dụng `Care/CareTestSupport.cs:27` WaitForLockWaitersAsync với cancellation; không viết lại polling. Helper xác nhận waiters, còn blocker transaction điều khiển thứ tự; hai trách nhiệm khác nhau. |
| MAJ-09 | Làm rõ câu lệnh, HTTP statuses và thuật ngữ trong các bước thực hiện; giữ code identifiers/permission names nguyên bản. |
| NIT-01 | Respond<T> trong controller gắn response traceId; E27 parse ISO và kiểm tra filter ID rõ ràng, không sửa global validation. |
| NIT-02 | Sửa path thành Infrastructure/PostgresWebApplicationFactory.cs. |
| NIT-03 | Đối chiếu schema: Data/Configurations/NgayNghiConfiguration.cs:16 map bảng ngay_nghi, cột ngay kiểu date và unique index ở dòng25; Data/PetSpaDbContext.cs:43 cung cấp NgayNghiSet. Services/Booking/BookingOptionsService.cs:21 đang đọc ngày nghỉ bằng AnyAsync trên bảng này. Vì vậy dùng NgayNghiSet theo ngày chính xác, không suy day-of-week, ca mẫu hoặc input client; D17 quyết định tác động đóng tiếp nhận, không đóng chăm sóc Hotel. |

Các nhận xét ngoài bảng tổng hợp: không thêm cache permission vì trái yêu cầu DB-current authorization, JWT chỉ chứa id/username/role/jti (`Services/Identity/TokenService.cs:26`). Không thêm ca qua đêm vì schema cấm (`Data/Configurations/CaLamViecConfiguration.cs:18`). Hotel query dùng index employee/state hiện hữu (`ChiTietDonConfiguration.cs:73`), nhưng index không chứng minh query luôn nhanh; không tự thêm schema/index. Không áp feature-spec-standard để buộc plan lặp Problem/Alternatives/Goals: thiết kế đã liên kết chịu trách nhiệm phần đó. Giữ chi tiết E27 parsing vì đây là implementation plan và errorCode đã được người dùng duyệt.

Đối chiếu review chưa phát hiện lý do phải mở lại bốn quyết định nghiệp vụ/locking đã duyệt. Những sửa đổi trong mục review này đã được ghi nhận trước implementation. Trạng thái triển khai và bằng chứng kiểm thử được ghi ở mục execution cuối tài liệu.

## Execution — 2026-11-24

Đã triển khai cả sáu task theo superpowers:executing-plans, không dùng implementer/reviewer subagent theo từng task. Một lượt whole-branch review độc lập đã hoàn tất; hai finding Important đã được sửa trong một final fix pass có RED/GREEN và full regression 548/548. Chưa commit/push/PR/merge/deploy.

| Check | Kết quả đã chạy |
|---|---|
| Baseline build | PASS, 0 warnings/errors; baseline full tests ban đầu gặp sandbox TCP và tài nguyên cluster, không ghi nhận là GREEN hoặc RED nghiệp vụ. |
| Task 1 | E27 RED route404; GREEN13/13 và DTO contracts76/76. |
| Task 2 | E54 RED6; GREEN cùng E27:19/19. |
| Task 3 | E28 RED14 và employee-lock concurrency RED1; GREEN Scheduling34/34. |
| Task 4 | E29 RED8, một missing-target case đã GREEN trước implementation; GREEN E29/options/assignment37/37. |
| Task 5 | E55 RED16; GREEN16. E15/E16 cross-day RED4 vì bypass employee key; GREEN cùng Care regressions. |
| Task 6 | Transient RED500 thay503; stale eligibility sau slot wait RED200 thay409; empty employee filter RED200 thay400. Tất cả đã GREEN trong full suite. Additional concurrency coverage không bị ghi giả là RED khi implementation trước đó đã thỏa. |
| Scheduling suite | PASS99/99 trước case empty-filter bổ sung; full suite cuối có cả case bổ sung. |
| Final build | dotnet build backend/PetSpa.sln -m:1 --nologo: PASS, 0 warnings/errors. |
| Full regression trước final review | dotnet test backend/PetSpa.sln -m:1 --nologo: PASS542/542, skipped0, 4m52s. Kết quả sau final fix pass ghi bên dưới. |

Cluster test là PostgreSQL17 loopback riêng, data trong ignored scratch của plan; PETSPA_TEST_POSTGRES chỉ thiết lập trong process chạy tests. Fixture chỉ migrate/drop các database petspa_test_GUID tự tạo. Không Neon/shared DB. Initial sandbox tạo shared_buffers400kB và chặn TCP local; đã chạy với quyền loopback và dùng64MB buffers, tắt durability chỉ trên cluster test dùng một lần. Không dùng lỗi môi trường làm RED.

Hai test barriers được điều chỉnh theo protocol thực tế, không bỏ assertions: Care same-key race chờ một audit insert và một employee key; dual-withdraw race chờ một source row và một unique destination insert. Giữ nguyên assertions rollback pet/photo/capacity. Dùng PowerShell-equivalent cho ledger/briefs vì Bash skill scripts không có trong PATH; giữ ignored evidence vì người dùng chưa yêu cầu commit.

Những lựa chọn này có rủi ro nếu làm sai: adapter ledger có thể ghi thiếu evidence; tuning cluster có thể làm tests không đại diện durability production (không dùng để khẳng định durability); barrier sai có thể làm race test bỏ sót serialization. Assertions DB, wait-event quan sát thật và full regression đã được dùng để kiểm tra phạm vi đó.

Final review phát hiện hai lỗi thực tế và đã có test RED: (1) E54 trả500 khi hai instant 100ns collapse cùng microsecond, hoặc response/fingerprint khác giá trị lưu; (2) E29/E55 trả409/404 thay403 khi quyền Admin bị thu hồi trong lúc chờ employee key. Fix chuẩn hóa trước validation/fingerprint/insert/snapshot và tái kiểm tra actor ngay sau employee lock, đồng thời giữ lần kiểm tra sau slot locks. Tests precision gồm cả trước/sau epoch2000 để khớp Npgsql. Không thêm schema, dependency hoặc thay authentication pipeline. Không có minor findings bị hoãn; quyết định ADMIN_OVERRIDE_SHIFT và Hotel guard đã duyệt được giữ nguyên.

### Kết quả cuối sau final fix pass

- Build lại toàn solution: PASS, 0 warnings/errors (post-review-build.log).
- Sáu regression cases của hai finding final review: RED → GREEN 6/6, gồm cả timestamp trước/sau epoch PostgreSQL và actor bị thu hồi quyền khi chờ employee key.
- Full backend regression chạy lại: PASS 548/548, skipped0, 4m57s (post-review-full-tests.log).
- Không còn Critical/Important được phát hiện chưa xử lý; không có minor findings bị hoãn. Không dispatch review vòng hai; dùng tests tái hiện và full suite theo executing-plans.
- Hoàn tất thực hiện sáu task. Branch feat/issue-24-shift-scheduling và worktree được giữ nguyên; không commit/push/PR/merge/deploy. Không sửa Entity/DbContext/migration/dependency hoặc frontend.
- Bằng chứng chi tiết nằm trong ignored plan workspace .superpowers/sdd/2026-11-24-issue-24-shift-scheduling/. Server PostgreSQL test đã được dừng sau verification; data/log giữ lại, không xóa scratch hoặc database ngoài ownership.
