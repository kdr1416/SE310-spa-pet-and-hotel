# Issue #14 — Sprint 2 Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bàn giao một PR foundation hoàn chỉnh cho Sprint 2: shared DTOs, projection cọc an toàn, seed Room/Combo, capacity, expiry, settlement và MailKit, bảo toàn hồi quy Sprint 1.

**Architecture:** Giữ Controller → Service/DbContext và Schema Freeze. Expiry dọn đủ hold liên quan theo nhiều transaction độc lập, mỗi transaction tối đa 20 booking; helper settlement thuần tính toán trên graph caller đã nạp. Các issue consumer nối foundation vào endpoint nghiệp vụ sau khi #14 merge.

**Tech Stack:** .NET 10, EF Core 10/Npgsql, PostgreSQL, xUnit 2, WebApplicationFactory, MailKit/MimeKit, TimeProvider.

**Spec:** [Spec #14 đã cập nhật theo xác nhận Tech Lead](../specs/2026-11-22-issue-14-sprint2-foundation-design.md). Đọc spec cùng plan; triển khai sau khi Tech Lead review plan và chọn cách thực hiện.

## Global Constraints

- Một gói foundation hoàn chỉnh trong một PR; không mở khóa consumer bằng DTO/stub chưa hoạt động.
- Nhánh khi thực thi: `feat/issue-14-sprint2-foundation`; kiểm tra `226937e` là ancestor của baseline `origin/develop` mới nhất. Giữ nguyên công việc chưa commit của người dùng; không rebase/checkout tự động trên workspace bẩn.
- Không sửa Entity, EF Configuration, migration, authentication hoặc frontend. Không thêm Repository/CQRS/MediatR, scheduler, queue/outbox.
- Không thêm endpoint mới trong #14. Mỗi task dưới đây nêu endpoint consumer để nối traceability, không phải yêu cầu triển khai endpoint đó.
- `now >= HetHanAt` là hết hạn; xác nhận đủ cọc chỉ khi `now < HetHanAt`. Thứ tự lock: booking theo ID → slots tăng dần → rooms tăng dần → registrations nếu có → deposit/invoice.
- Quét targeted phải dọn đủ hold quá hạn tại cutoff, có thể nhiều batch; tối đa 20 booking mỗi transaction. Scope rỗng explicit là no-op, không phải global.
- Expiry không gọi từ E03/E11; cleanup commit trước transaction writer. Entry point locked không mở transaction hoặc SaveChanges.
- Snapshot giá dùng `Dictionary<SizeBand, decimal>` và `JsonSerializerDefaults.Web` theo convention hiện tại; không lấy giá catalog mới để thay snapshot cũ. Kiểm tra casing theo serializer hiện hữu, không tự đổi format dữ liệu.
- `EXTENSION_SETTINGS` mặc định vẫn `{"otpEmail":false,"cocHotel":false,"giuCho":false}`. Không ghi đè cấu hình/giá seed đã được người dùng chỉnh.
- Public refund reference là null; internal reference đã che; không trả `BangChungPath`, raw reference, credentials hoặc ghi chú đối soát nội bộ.
- Tests lock/SQL dùng PostgreSQL local qua `PostgresTestDatabase`, database riêng mỗi test, `PETSPA_TEST_POSTGRES` loopback; fake clock và synchronization gate thay sleep để đoán race.
- Backend build phải `0 errors, 0 warnings`; full backend tests phải pass. Không xem thiếu PostgreSQL/restore thất bại là test đỏ hợp lệ của TDD.
- Chỉ MailKit là dependency product mới trong phạm vi được duyệt; chọn và pin version tương thích lúc thực thi, không nâng package khác. Không gửi SMTP thật hay chạy migrations trên Neon.
- Ước lượng đề xuất 16–24 giờ tập trung, chưa gồm chờ review. Các lệnh/expected results dưới đây chưa được chạy ở giai đoạn lập plan.

## Review Focus

1. **RF01 — >20 hold đúng ca cần đặt:** targeted cleanup phải xử lý hết chúng, bỏ qua đơn không liên quan, không dừng sau batch đầu; Task 5.
2. **RF02 — Deadline trôi trong lúc chờ lock:** confirmer/expiry kiểm tra giờ sau lock; writer phát hiện hold mới hết hạn phải thả lock trước khi dọn lại; Task 5, HTTP wiring thuộc #15/#17/#22.
3. **RF03 — Hủy toàn bộ pet trước check-in:** `LoaiHuy` vẫn null, không phát sinh phạt khi terminal và không chờ refund như M2-05 đang phục vụ; Task 6.
4. **RF04 — Projection internal rồi Public trên cùng entity:** masked/null reference không mutate reference gốc và không rò dữ liệu qua reuse; Task 2.
5. **RF05 — Pet hủy đã ở 1/3 đêm:** sau khi trả chỉ nhả đêm chưa dùng, vẫn tính đêm đã ở khi booking active; pet DONE sớm không nhả khoảng cam kết; Task 4.

## File structure và phụ thuộc

Đường dẫn đầy đủ tính từ root repository. Trong bảng manifest, `Core/` viết gọn cho `backend/src/PetSpa.Core/`, `Api/` cho `backend/src/PetSpa.Api/`; các đường dẫn DTO không prefix ở cùng ô tiếp tục thuộc Core. Trong phần task, `Foundation/` là `backend/tests/PetSpa.Tests/Foundation/`. Thực thi tuần tự; cuối mỗi task có kiểm chứng và commit riêng trong cùng nhánh/PR. Chỉ stage đúng files của task đó; không dùng `git add .` hoặc commit hai tài liệu Sprint 1 chưa tracked của người dùng.

| Task | File sở hữu và trách nhiệm | Phụ thuộc / AC |
|---|---|---|
| 1 | `backend/src/PetSpa.Core/DTOs/Billing/{DepositDto,FeeDto}.cs`, `DTOs/Booking/CustomerPetDto.cs`, `DTOs/Scheduling/ScheduleDto.cs`: contract types và validation | AC01 |
| 2 | Sửa `Core/DTOs/Booking/{BookingResponse,BookingManagement}.cs`, `Api/Services/Booking/{BookingProjection,BookingManagementService}.cs`; mới `Api/Services/Billing/DepositAccounting.cs`: tính tiền/projection | Task 1; AC02 |
| 3 | Sửa `Api/Data/Seeding/DbInitializer.cs`, `tests/PetSpa.Tests/Seeding/SeedDataTests.cs`: seed idempotent | AC11 |
| 4 | Mới `Api/Services/HotelCapacityCalculator.cs`; sửa `SpaCapacityCalculator.cs`, `Program.cs`: capacity | Task 3; AC03–04 |
| 5 | Mới `Core/Interfaces/Billing/IDepositExpiryService.cs`, `Api/Services/Billing/DepositExpiryService.cs`; sửa `Program.cs`: expiry dưới lock và sweep | Tasks 2, 4; AC05–07 |
| 6 | Mới `Core/Interfaces/Billing/IBillingSettlementHelper.cs`, `Api/Services/Billing/BillingSettlementHelper.cs`; sửa `Program.cs`: tiền và terminal state | Task 2; AC08–09 |
| 7 | Sửa `Api/Services/Booking/{IEmailSender,SmtpEmailSender}.cs`, `Api/PetSpa.Api.csproj`, ba email doubles: MailKit | AC10 |
| 8 | Mới `Api/Services/Billing/DepositExpiryBackgroundService.cs`; sửa `Program.cs`, `backend/README.md`; integration/regression và handoff docs | Tasks 1–7; AC12, tổng hợp |

Test files tạo dưới `backend/tests/PetSpa.Tests/Foundation/`, namespace `PetSpa.Tests.Foundation`: `SharedDtoContractTests.cs`, `BookingDepositProjectionTests.cs`, `CapacityTests.cs`, `DepositExpiryTests.cs`, `DepositExpiryConcurrencyTests.cs`, `BillingSettlementTests.cs`, `SmtpEmailSenderTests.cs`, `Sprint2FoundationTests.cs`. `FoundationTestSupport.cs` chứa fixture PostgreSQL/host và clock/gates dùng lại, không thay logic product để phục vụ tests. Không cần `InternalsVisibleTo`: test internal accounting qua public projection/helper, load graph qua HTTP/services.

## Task 1: Shared DTOs đúng contract

**Traceability:** E06/E07/E09/E13/E14/E16/E20/E21/E22a–c/E27–29/E42–50/E54–55; D16/FR01, M2-02/07/13. Đây là DTO foundation, chưa tạo handlers hoặc route.

**Files:** tạo bốn file DTO và `Foundation/SharedDtoContractTests.cs` ở bảng trên.

**Interfaces:**
- Consumes: `BookingQuery`, `Contact`, `RoomAvailability` và kiểu enum/DTO có sẵn; [contract mục 3.1/3.2](../../design/api-contract.md).
- Produces: chính xác danh mục C# ở spec mục 4.1. `DepositDto` gồm `int MaTienCoc/MaDon`, state string, `decimal CoSoTinh/TyLe/SoTienYeuCau/ThucNhan/KhauTru/GiuLai/DanhHoan/DaHoan`, `DateTimeOffset HetHanAt`, `IReadOnlyList<RefundDto> HoanCoc`. `RefundDto` có `DateTimeOffset? HoanAt` và `string? ThamChieu`; page records có `Items/Trang/KichThuocTrang/TongSo`.
- Request types dùng `init`, explicit required presence cho scalar bắt buộc, DataAnnotations + `IValidatableObject` cho Guid.Empty/cross-field validation. JSON enum dạng string đúng whitelist; không nhận số thay enum.

- [x] **Step 1 — Viết tests contract/validation.** Parameterize toàn bộ request/response trong spec 4.1 bằng JSON hợp lệ theo dictionary; assert field names, date/instant, pages và nullable response. Tests riêng: `OptionalField_AbsentAccepted_ExplicitNullRejected`, `Patch_RejectsUnknownFieldsAndNoBusinessChanges`, `Money_RejectsFractionalVndAndNonPositiveReceipts`, `RequestId_RejectsEmptyGuid`, `ExtraService_RejectsBothOrNeitherServiceAndCombo`, `ScheduleQuery_ValidatesDateOrderButDefersAdminPermissionToHandler`. Optional khác null kiểm tra qua deserialize; các range/cross-field kiểm tra qua validator sau deserialize. Assertion snippets trên DTO đã arrange:

```csharp
Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PetEdit>("{\"requestId\":\"9de41cc1-c2b9-41a6-875c-99a29cd5cecb\",\"tenThuCung\":null}", options));
Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FeeEdit>("{\"actorId\":1}", options));
Assert.Equal(20, new BookingQuery().KichThuocTrang);
```

- [x] **Step 2 — Chạy đỏ:** `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~SharedDtoContractTests" -m:1`. Expected: thiếu DTO hoặc assertion cho behavior mới thất bại, không phải lỗi môi trường.
- [x] **Step 3 — Implement đúng types/schema.** Request scalar bắt buộc dùng `[JsonRequired]`; optional có backing field/init setter từ chối null khi field xuất hiện, mặc định bỏ qua vẫn hợp lệ; không đổi global JSON options. PATCH dùng `JsonUnmappedMemberHandling.Disallow`. Required strings có `[Required]` và giới hạn đúng contract. Không duplicate `BookingQuery` hoặc tự thêm permission check vào DTO.
- [x] **Step 4 — Chạy xanh:** lệnh Step 2, rồi `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~CoreEnvelopeTests" -m:1`. Expected: PASS và JSON contract cũ giữ nguyên.
- [x] **Step 5 — Commit:** stage bốn DTO files và test; `git commit -m "feat(foundation): add Sprint 2 shared contract DTOs"`.

## Task 2: Typed cọc, accounting và projection an toàn

**Traceability:** E04/E08/E10a hiện có; E10b/E42–46 consumer; M2-02/07/11, D08.

**Files:** sửa files Task 2 trong bảng; tạo `Foundation/{FoundationTestSupport,BookingDepositProjectionTests}.cs`. Chỉ sửa mapping trong `BookingManagementService`, không thay guard nghiệp vụ.

**Interfaces:**
- Produces: `BookingResponse.TienCoc` và `TrackingResponse.TienCoc` là `DepositDto?`.
- `BookingProjection.Project(DonDatLich booking, InvoiceResponseDto? invoice = null, bool includeInternalDetails = false)`; `ProjectCurrent(DonDatLich booking, IConfiguration configuration, bool includeInternalDetails = false)` giữ return type `BookingResponse`.
- `DepositAccounting.Calculate(DonDatLich booking)` internal static, trả internal record `DepositAmounts(decimal ThucNhan, decimal KhauTru, decimal GiuLai, decimal DanhHoan, decimal DaHoan)`. Không query DB/DI; Task 6 reuse. Không biến computed amounts thành cột entity.
- Test fixture sở hữu một `PostgresTestDatabase` và host `PostgresWebApplicationFactory`; tạo bằng `FoundationTestSupport.CreateAsync(DateTimeOffset now)`, trả `FoundationFixture : IAsyncDisposable` có `Database`, `Host`, `Clock`. JWT/SMTP/storage dùng test doubles theo conventions hiện có, không đọc credentials.

- [x] **Step 1 — Viết tests.** `Projection_SumsReceiptsAndBothRefundStates_ExcludesRevokedInvoice`: receipts 400k+200k, active khấu trừ 300k, revoked 500k, refunds 100k CHO_HOAN+50k DA_HOAN → thucNhan 600k, khauTru 300k, danhHoan 150k, daHoan 50k. `Penalty_RequiresWholeBookingCancellation_AndNeverAppliesToExpiry`: đúng 2h →0; 1h59m59s → số dư; STAFF_REJECTED/HET_HAN/LoaiHuy null/đã check-in →0. `MultipleActiveInvoices_FailsAsInvariantViolation`: không chọn ngẫu nhiên một invoice. `InternalThenPublicProjection_DoesNotMutateRawReference` (RF04), tham chiếu test tổng hợp `BANK-12345678`:

```csharp
var internalView = BookingProjection.Project(booking, includeInternalDetails: true);
var publicView = BookingProjection.Project(booking);
Assert.Equal("***5678", Assert.Single(internalView.TienCoc!.HoanCoc).ThamChieu);
Assert.Null(Assert.Single(publicView.TienCoc!.HoanCoc).ThamChieu);
Assert.Equal("BANK-12345678", booking.TienCocDonDatLich!.HoanCocTienCocItems.Single().ThamChieu);
```

Thêm cases reference null/rỗng/≤4 ký tự/ký tự không ASCII và `BookingWithoutDeposit_StillReturnsNull`. Fixture PostgreSQL seed trực tiếp cọc/receipt/refund, gọi HTTP E08 bằng cặp tracking hợp lệ và E10a với JWT để chứng minh IncludeDetails nạp đủ, response không có `bangChungPath`. `Sprint1CreationSnapshot_ReplaysWithNullDeposit_AndNoSecondEmail` dùng E04 replay hiện có, không tạo endpoint mới.

- [x] **Step 2 — Chạy đỏ:** `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~BookingDepositProjectionTests" -m:1`. Expected: projection còn null hoặc chưa có flag/new accounting.
- [x] **Step 3 — Implement projection/accounting.** Nạp cọc→receipts/refunds và phí, giữ split queries; reject nhiều invoice hiệu lực bằng `InvalidOperationException` trước tính tiền. Dùng entities đầy đủ, không dựa vào invoice DTO caller truyền để tính khấu trừ. Mask theo spec 4.3; default false truyền từ `ProjectCurrent`, E08/creation/replay vẫn false. Internal list có thể truyền true cho deposit projection nhưng giữ invoice Public-safe hiện hữu; không mở thêm trường invoice/ghi chú. Không sửa snapshot lịch sử khi replay.
- [x] **Step 4 — Chạy xanh:** lệnh Step 2 và `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~BookingCreateEndpointTests|FullyQualifiedName~BookingManagementEndpointTests" -m:1`. Expected: PASS, replay không gửi mail lại.
- [x] **Step 5 — Commit:** stage files Task 2; `git commit -m "feat(foundation): project typed deposits with safe references"`.

## Task 3: Seed Room/Combo idempotent

**Traceability:** E02 catalog, E03/E04/E05 và E23 consumer; D05/Q02, M2-L01; gỡ G1.

**Files:** sửa `backend/src/PetSpa.Api/Data/Seeding/DbInitializer.cs`, `backend/tests/PetSpa.Tests/Seeding/SeedDataTests.cs`.

**Interfaces:** giữ `DbInitializer.InitializeAsync(PetSpaDbContext context, SeedDataOptions options, IHostEnvironment environment, CancellationToken cancellationToken = default)`; bổ sung seed trong transaction/advisory lock có sẵn, không tạo service mới.

- [x] **Step 1 — Viết tests.** Đổi ba assert count giá 12→16 ở fixture sạch; `Seed_RoomsAndCombo_HaveExactCapacityPricesAndLinks`, `Seed_RepeatedOrConcurrentRun_DoesNotDuplicate`, `Seed_RepairsMissingComboPriceWithoutResettingUserChanges`. Phòng Tiêu Chuẩn=250000/5, VIP=450000/3; combo Tắm Cắt Toàn Diện liên kết đúng hai dịch vụ Tắm sấy vệ sinh/Cắt tỉa tạo kiểu, size S/M/L/XL=350000/480000/620000/800000. Assert phần sửa giá/capacity và extension bật sẵn không bị reset:

```csharp
Assert.Equal(16, await verify.BangGiaSizeSet.CountAsync());
Assert.Equal(2, await verify.LoaiPhongSet.CountAsync());
Assert.Equal(1, await verify.ComboSet.CountAsync());
```

- [x] **Step 2 — Chạy đỏ:** `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~SeedDataTests" -m:1`; expected count hiện tại 12 thay vì 16.
- [x] **Step 3 — Implement seed.** Lookup theo tên/convention, links theo FK, giá theo combo+size; thêm phần thiếu, không hardcode ID hay overwrite row hiện hữu. Giữ môi trường Development/Testing và EXTENSION_SETTINGS false khi chưa tồn tại.
- [x] **Step 4 — Chạy xanh:** lệnh Step 2 và `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~CatalogSeedTests|FullyQualifiedName~CatalogEndpointTests" -m:1`. Expected PASS; catalog fixture tự seed riêng không bị thay count chỉ để phù hợp global seed.
- [x] **Step 5 — Commit:** hai files; `git commit -m "feat(seed): add hotel rooms and complete spa combo"`.

## Task 4: Công suất Hotel và bù Spa khi đọc

**Traceability:** E03 đọc, E04/E05/E13/E20 ghi consumer; D04/Q01, D05/Q02, M2-11, M2-L01.

**Files:** tạo `backend/src/PetSpa.Api/Services/HotelCapacityCalculator.cs`, `Foundation/CapacityTests.cs`; sửa `SpaCapacityCalculator.cs`, `Program.cs` đăng ký scoped Hotel calculator.

**Interfaces:**
- `Task<IReadOnlyList<RoomAvailability>> CheckRoomAvailabilityAsync(DateOnly checkInDate, DateOnly checkOutDate, IReadOnlyCollection<int> roomTypeIds, int? excludeBookingId, DateTimeOffset now, CancellationToken ct)`.
- `Task LockRoomTypesAsync(int[] roomTypeIds, CancellationToken ct)` trên cùng calculator; yêu cầu current transaction.
- `SpaCapacityCalculator.CheckAsync(IReadOnlyCollection<SpaSlotDemand> demands, CancellationToken cancellationToken, DateTimeOffset? readAsOfNow = null)` giữ return `Task<IReadOnlyDictionary<int, SpaSlotCapacity>>`.

- [x] **Step 1 — Viết tests PostgreSQL.** `Hotel_CountsDistinctPetsPerNight_WithExcludedBooking`; `Hotel_EarlyDoneKeepsCommittedNights`; `Hotel_CancelledPetReturnedAfterOneOfThreeNights_ReleasesOnlyUnusedNights` (RF05); `Hotel_CancelledBeforeCheckin_ReleasesAllNights`; `Hotel_CancelledAfterCheckinNotReturned_KeepsOriginalInterval`; `Hotel_TerminalBookingDoesNotHoldRooms`. Fixture active IN_PROGRESS có pet ở 1/3 đêm đã trả, capacity phòng=5:

```csharp
var result = await hotel.CheckRoomAvailabilityAsync(day, day.AddDays(3), [roomId], null, now, ct);
Assert.Equal(new[] { 4, 5, 5 }, result.Select(x => x.SuatConLai));
```

`ExpiredHold_BeforeAtAfterDeadline_IsExcludedOnlyWhenDue`: tại deadline−1 tick vẫn giữ, đúng deadline/qua deadline bỏ khi CHO_THU; DA_DAP_UNG hoặc không có cọc vẫn giữ. `Spa_ReadCompensation_CountsPetOnceAndComboMinutesOnce`: một pet có hai dòng 60+90 phút cùng ca, counters=1/150 → read effective 0/0 khi hết hạn; default writer vẫn thấy 1/150. Pet Hotel-only không làm đổi counters. So database trước/sau calculator để chứng minh không ghi. Giữ tests ca đóng/nghỉ/không staffing/nhân viên inactive/thiếu quyền. Input invalid ngày ném `ArgumentException`, IDs rỗng trả empty; SQL lock IDs đảo/trùng phải đi tăng dần, gọi ngoài transaction thất bại.

- [x] **Step 2 — Chạy đỏ:** `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~Foundation.CapacityTests" -m:1`. Expected thiếu Hotel calculator hoặc chưa bù expiry.
- [x] **Step 3 — Implement calculators.** Query chỉ đọc; group pet/đêm, Spa distinct `(pet,slot)` và sum duration cha đúng một lần; business timezone Asia/Ho_Chi_Minh. Canceled room theo từng đêm như spec/AC03, không bỏ luôn lịch sử đã ở. LockRoomTypes query tham số hóa `ANY` + `ORDER BY id FOR UPDATE`. Không nối E03 cutoff hoặc gỡ Hotel/Combo guards trong task này.
- [x] **Step 4 — Chạy xanh:** lệnh Step 2 và `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~AvailabilityEndpointTests|FullyQualifiedName~BookingCreateEndpointTests" -m:1`. Expected PASS, lời gọi Spa cũ vẫn compile/chạy.
- [x] **Step 5 — Commit:** files Task 4; `git commit -m "feat(capacity): add hotel capacity and expired spa hold reads"`.

## Task 5: Expiry targeted, lock ordering và hoàn phần đã thu

**Traceability:** E04/E05/E13/E20 trước khi chiếm tài nguyên, E42 dưới lock consumer; D04/Q01, M2-11, M2-L05/06.

**Files:** tạo `backend/src/PetSpa.Core/Interfaces/Billing/IDepositExpiryService.cs`, `backend/src/PetSpa.Api/Services/Billing/DepositExpiryService.cs`, `Foundation/{DepositExpiryTests,DepositExpiryConcurrencyTests}.cs`; sửa `Program.cs` scoped DI.

**Interfaces:**
- `Task<int> ExpireOverdueDepositsAsync(DateTimeOffset now, int? targetBookingId, CancellationToken ct, IReadOnlyCollection<int>? targetKhungGioIds = null, IReadOnlyCollection<int>? targetRoomTypeIds = null)`.
- `Task ExpireLockedBookingDepositAsync(DonDatLich booking, DateTimeOffset now, CancellationToken ct)`.
- Consumes: DbContext, TimeProvider, Hotel lock helper, `BookingRowLock.AcquireAsync(PetSpaDbContext db, int bookingId, CancellationToken cancellationToken)` và accounting ở Task 2. Không inject singleton context.

- [x] **Step 1 — Viết tests state/transaction.** `Expiry_AtDeadline_CancelsBookingAndReleasesSpaOnce`; `Expiry_PartialReceipts_ReservesFullOutstandingRefundWithLatestActor`; `Expiry_NoReceipt_CreatesNoRefund`; `LockedExpiry_DoesNotCommitAndRollsBackWithCaller`; `Sweep_RejectsAmbientOrCurrentTransaction`; `Sweep_TargetBooking_DoesNotTouchAnotherBooking`. Receipt 80k+20k, refunds đã dành 30k → tạo thêm 70k, actor từ `XacNhanAt DESC, Id DESC`; replay không tạo lần hai. Assert state CANCELLED/NO_SHOW/HET_HAN, timestamp cutoff hợp lệ, giuLai=0 và Spa counters giảm đúng; Hotel-only không giảm Spa.

```csharp
Assert.Equal(BookingState.CANCELLED, booking.TrangThai);
Assert.Equal(DepositRequestState.HET_HAN, booking.TienCocDonDatLich!.TrangThaiYeuCau);
Assert.Equal(70000m, newlyCreatedRefund.SoTien);
Assert.Equal(RefundState.CHO_HOAN, newlyCreatedRefund.TrangThai);
```

- [x] **Step 2 — Viết tests batch/concurrency.** `TargetedSweep_DrainsTwentyOneRelatedBookings_LeavesUnrelatedBooking` (RF01): 21 liên quan + 1 ngoài scope; count=21, đơn ngoài scope vẫn PENDING. Đo transaction/SQL bằng test interceptor để assert không batch nào khóa >20 booking; không chỉ assert count tổng. `TargetedSweep_EmptyFiltersAreNoOp_CombinedFiltersUseOr_RejectsMixedBookingAndResourceScope`; global all-null quét hết. `ConcurrentSweeps_ReleaseAndRefundExactlyOnce`: hai context cùng booking, tổng count chuyển=1, counters/refund không double. `ConfirmedWhileExpiryWaits_IsRecheckedAfterLock`: bên thắng đã đáp ứng trước hạn không bị expiry hủy. `ReceiptConfirmationWaitingPastDeadline_CannotKeepReservation` (RF02): mô phỏng confirmer dưới booking lock, fake clock tiến đúng deadline sau khi bị chặn; dùng locked expiry và hoàn delta sau receipt, không đổi về DA_DAP_UNG. `WriterProtocol_NewHoldExpiresWhileWaiting_ReleasesLocksThenCleansAgain`: fixture mô phỏng rollback main transaction, cleanup cutoff mới rồi mới reserve; HTTP wiring để consumer làm. `CleanupCommit_SurvivesNextWriterRollback`: cleanup đã commit không hồi sinh do transaction sau rollback.
- [x] **Step 3 — Chạy đỏ:** `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~DepositExpiryTests|FullyQualifiedName~DepositExpiryConcurrencyTests" -m:1`. Expected thiếu service hoặc assertion state/batch/concurrency thất bại.
- [x] **Step 4 — Implement service.** Candidates AsNoTracking tại cutoff, filter OR theo active Spa slot/room type, keyset theo booking ID; mỗi batch lock toàn bộ booking trước union slots/rooms/deposits. Recheck/load fresh sau lock, giờ server không sớm hơn cutoff. Không mutate tracked graph lấy trước lock; không clear tracker làm mất thay đổi unrelated của caller. Candidate không còn đủ guard bỏ qua, không lặp vô hạn. Current/ambient transaction và mixed scope sai ném `InvalidOperationException`/`ArgumentException` phù hợp; IDs không dương ném `ArgumentException`. Entry point chỉ mutate graph dưới current transaction, không SaveChanges. Invalid counters/receipt actor làm rollback batch, không clamp che sai dữ liệu. Save/commit từng batch, trả count thực chuyển; đã commit batch trước không rollback vì batch sau lỗi.
- [x] **Step 5 — Chạy xanh:** lệnh Step 3; rồi filter `Foundation.CapacityTests` để xác minh read compensation không double-trừ sau cleanup. Expected PASS; timeout test được xem là fail, không bỏ qua.
- [x] **Step 6 — Commit:** files Task 5; `git commit -m "feat(deposits): expire targeted holds safely in bounded transactions"`.

## Task 6: Settlement helper và terminal matrix

**Traceability:** E47/E49/E19/E23/E26/E45 consumer; M2-04/05/07, D06/Q08, D08.

**Files:** tạo `backend/src/PetSpa.Core/Interfaces/Billing/IBillingSettlementHelper.cs`, `backend/src/PetSpa.Api/Services/Billing/BillingSettlementHelper.cs`, `Foundation/BillingSettlementTests.cs`; sửa `Program.cs` scoped registration.

**Interfaces:** ba method void/decimal đúng spec mục 7: `SettleCancelledPetInMemory(DonDatLich booking, ChiTietDon pet, IReadOnlySet<int> completedComponentIds, int nightsStayed, DateTimeOffset now)`, `decimal CalculateObligatedDepositAmount(DonDatLich booking)`, `void TryTransitionBookingTerminalState(DonDatLich booking, DateTimeOffset now)`. Consumes internal DepositAccounting; không DbContext/email/SaveChanges.

- [x] **Step 1 — Viết tests tính tiền thuần graph.** `PartialCombo_ChargesOnlyCompletedRetailSnapshot`: size S, combo350k, component đã làm150k, component250k chưa làm → cha HOAN_TAT giá150k, phần chưa làm HUY. Full combo giữ350k; chưa làm giá0/HUY; dịch vụ đơn HOAN_TAT giữ giá. `CancelledTarget_UsesActualNightsBeforeOrAfterStateMutation`: room250k×3, nightsStayed1, một component150k → tổng400k ở cả hai thứ tự state. `OtherPendingPet_UsesFrozenSizeAndSnapshotNotCurrentCatalog` thêm pet khác size cam kết M=200k và phí hiệu lực50k → tổng650k. Invalid IDs hoặc thiếu snapshot ở pet khác phải fail trước khi mutate bất kỳ service, nights hoặc total nào. Assert components không có HuyAt/schema mới, không có refund được tạo.

```csharp
helper.SettleCancelledPetInMemory(booking, pet, completedIds, 1, now);
Assert.Equal(650000m, booking.TongTamTinh);
Assert.Equal(1, pet.SoDemDaO);
Assert.Equal(150000m, comboLine.GiaChot);
```

- [x] **Step 2 — Viết tests nghĩa vụ/terminal.** `RefundBudget_DoesNotGrowWhenReservedRefundIsPaid`: received600k/payable400k/reserved100k → budget100k cả trước/sau hoàn; active invoice dùng CocKhauTru, revoked không chiếm cọc. Matrix M2-04: invoice PAID + mọi noncancel DONE + canceled đã trả →COMPLETED; thiếu từng điều kiện giữ state. Matrix M2-05 IN_PROGRESS/all canceled: còn pet chưa trả, UNPAID, CHO_HOAN, surplus bất kỳ → giữ; trả đủ+quyết toán+refund xong →CANCELLED. Payable0 không invoice được khép nếu tiền/refund đã xử lý; payable>0 chưa invoice không được khép. `PrecheckinAllCancelled_WithPendingRefund_ClosesWithoutForfeiture` (RF03): CONFIRMED, mọi CheckInAt null/canceled, refund CHO_HOAN và dư cọc vẫn được CANCELLED nếu không UNPAID; LoaiHuy null, nghĩa vụ/giuLai0 trước/sau. Có pet thực tế đã check-in không được vào nhánh này chỉ vì booking state sai. Empty pets không terminal, gọi lặp giữ timestamp.
- [x] **Step 3 — Chạy đỏ:** `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~BillingSettlementTests" -m:1`. Expected thiếu helper hoặc sai công thức/matrix.
- [x] **Step 4 — Implement helper.** Validate toàn graph cần giá trước khi mutate; target coi như hủy khi tính phòng dù state chưa đổi. Recompute từ giá chốt/size cam kết snapshot, không cộng lại components. Chỉ set state dịch vụ/nights/total như spec; caller sở hữu pet cancellation/audit/resources. Terminal precheckin yêu cầu thực tế chưa pet nào CheckInAt, không chỉ state PENDING/CONFIRMED; M2-05 đang phục vụ chờ hoàn, M2-04 không thêm điều kiện đó. LoaiHuy all-E47 giữ null. Dùng accounting chung, lỗi graph không fallback0; không tự tạo refund.
- [x] **Step 5 — Chạy xanh:** lệnh Step 3, rồi filter `BookingDepositProjectionTests`. Expected PASS, công thức penalty đồng nhất.
- [x] **Step 6 — Commit:** files Task 6; `git commit -m "feat(billing): settle cancelled pets and terminal booking states"`.

## Task 7: MailKit và email interface không đổi semantics

**Traceability:** E04/E11 hiện có, E50/E45 consumer; M2-09/13/14.

**Files:** sửa `backend/src/PetSpa.Api/PetSpa.Api.csproj`, `Services/Booking/{IEmailSender,SmtpEmailSender}.cs`, `backend/tests/PetSpa.Tests/Booking/{BookingCreateEndpointTests,BookingManagementEndpointTests}.cs`, `Flows/SpaFlowTests.cs`; tạo `Foundation/SmtpEmailSenderTests.cs`. Chỉ sửa các doubles khi mở interface.

**Interfaces:** giữ hai method hiện tại; thêm `Task SendTrackingResendAsync(string recipient, string trackingCode, CancellationToken ct)` và `Task SendRefundConfirmedAsync(string recipient, string trackingCode, int refundId, decimal amount, DateTimeOffset refundedAt, CancellationToken ct)`.

- [x] **Step 1 — Viết tests qua SMTP loopback trong test file.** Dùng `TcpListener` .NET với script SMTP tối thiểu, không package test mới; chỉ lắng nghe loopback, port động. `FourEmails_UseCorrectRecipientAndVietnameseText`, `TrackingResend_ReusesExistingCode`, `RefundEmail_DoesNotIncludeRawEvidence`, `SmtpFailure_PropagatesAndCannotReportSent`, `Cancellation_StopsPendingSend`. Capture DATA/MIME của bốn method, assert đúng recipient/trackingCode; refund gồm ID/amount/time. Cases thiếu Host/From fail rõ. Giữ regression existing `BookingCreateEndpointTests` về gửi lỗi sau commit và replay không gửi lần hai.
- [x] **Step 2 — Chạy đỏ:** `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~SmtpEmailSenderTests" -m:1`. Expected thiếu method hoặc khác behavior sender, không dựa gửi SMTP ngoài máy.
- [x] **Step 3 — Pin package và implement.** Kiểm tra version MailKit ổn định/tương thích lúc thực thi bằng nguồn chính thức/NuGet, ghi exact version vào csproj, restore solution. Dùng `MimeMessage`, `TextPart("plain")`, MailKit SMTP client per send; 465 `SslOnConnect`, 587 `StartTls` khi EnableSsl, explicit false cho local SMTP tests. Timeout hữu hạn; đề xuất client Timeout=8000ms, caller token giữ quyền hủy sớm hơn. Không bỏ certificate validation hoặc tự retry. Port SSL khác hai port trên fail-fast cấu hình thay vì tự downgrade; nếu cần hỗ trợ thì phải có cấu hình explicit được review. Cập nhật ba doubles với hai method mới và giữ recording semantics cũ.
- [x] **Step 4 — Chạy xanh:** lệnh Step 2 và `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~BookingCreateEndpointTests|FullyQualifiedName~BookingManagementEndpointTests|FullyQualifiedName~SpaFlowTests" -m:1`. Expected PASS, booking commit còn tồn tại khi SMTP lỗi.
- [x] **Step 5 — Commit:** chỉ files Task 7; `git commit -m "feat(email): use MailKit for booking and refund notifications"`.

## Task 8: Worker, host integration và handoff một PR

**Traceability:** nền tảng E03/E04/E05/E13/E20/E42–46; M2-11/14, gate #14.

**Files:** tạo `backend/src/PetSpa.Api/Services/Billing/DepositExpiryBackgroundService.cs`, `Foundation/Sprint2FoundationTests.cs`; sửa `Program.cs`, `backend/README.md`. Handoff docs: cập nhật đúng phần #14/expiry/reference trong `docs/plans/sprint-2/sprint-2-plan.md`, chữ ký/cách gọi expiry của `dev-a-plan.md`, `dev-c-plan.md`, quy tắc all-E47/terminal trong `dev-b-plan.md` khi có nội dung cũ trái spec; không rewrite các phần khác.

**Interfaces:** `DepositExpiryBackgroundService : BackgroundService` consumes `IServiceScopeFactory`, `IConfiguration`, `TimeProvider`, `ILogger<DepositExpiryBackgroundService>` và optional `IHostEnvironment` để đánh giá cờ ở lúc chạy sau config override; protected `Task ExecuteAsync(CancellationToken stoppingToken)`. Resolve IDepositExpiryService trong scope, gọi all-null target theo cutoff từ clock; không nhận DbContext trong constructor worker.

- [x] **Step 1 — Viết host tests.** `Host_ResolvesAllFoundationServices`; `Testing_DefaultWorkerDisabled_ExplicitTrueEnablesIt`; `Worker_DrainsTwentyOneBookings_WithoutOverlappingRuns`; `Worker_DisposesScope_ContinuesAfterFailedSweep_AndStopsOnCancellation`; `Worker_GlobalSweep_RunsEvenWhenExtensionCurrentlyOff`. Dùng fake service có gates/throw ở lần đầu; bổ sung timer điều khiển được cho test clock trong FoundationTestSupport, không thêm package và không sleep chờ interval. Interval=0/âm fail config rõ; mặc định30s. Khi disabled không có lượt sweep nào:

```csharp
using var scope = host.Services.CreateScope();
Assert.NotNull(scope.ServiceProvider.GetRequiredService<HotelCapacityCalculator>());
Assert.NotNull(scope.ServiceProvider.GetRequiredService<IDepositExpiryService>());
Assert.NotNull(scope.ServiceProvider.GetRequiredService<IBillingSettlementHelper>());
```

- [x] **Step 2 — Chạy đỏ:** `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~Sprint2FoundationTests" -m:1`. Expected worker/config chưa có; không giả implementer đã test full endpoint Sprint 2.
- [x] **Step 3 — Implement worker/config/docs.** Worker tuần tự, tạo/dispose scope từng vòng, cancellation không bị log như lỗi bất ngờ, lỗi sweep không giết các vòng sau; chờ bằng `Task.Delay(interval, clock, stoppingToken)` để test điều khiển timer. `DepositExpiry:BackgroundEnabled` fallback `!IsEnvironment("Testing")`, explicit config override; `IntervalSeconds` mặc định30 và >0. README chỉ key công khai, topology test loopback, lệnh kiểm chứng; không đọc/copy credentials từ appsettings. Handoff docs phải dùng targeted args, `now >= HetHanAt`, LoaiHuy whole-cancel, masked references và giữ đêm đã ở; bỏ giả định số dòng helper/4h là hard limit. Nhắc E42 bổ sung refund sau late receipt trong transaction; E19/E47/E49/E23/E26/E45 nối terminal helper ở issue owner. Giữ scope và thứ tự lock của spec.
- [x] **Step 4 — Chạy xanh task:** lệnh Step 2; expected PASS. Chạy smoke HTTP E08/E10a của Task 2 để chứng minh host/worker mặc định không phá tests.
- [x] **Step 5 — Full verification và kiểm scope:**

```powershell
dotnet build backend/PetSpa.sln -m:1
dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~Foundation|FullyQualifiedName~SeedDataTests" -m:1
dotnet test backend/PetSpa.sln -m:1
git diff --check
git status --short
```

Expected: build0 errors/0 warnings, full tests PASS, không skip PostgreSQL. Kiểm diff không có Entity/Configuration/migration/frontend, không có secret. Nếu có lỗi, giữ bằng chứng và sửa trong phạm vi trước khi tuyên bố hoàn tất. Đo thời gian capacity/cleanup và lock wait trên fixture đại diện để ghi số đo; không dùng stopwatch dưới500ms trên máy dev làm chứng nhận NFR hoặc hard assertion dễ flaky.

- [x] **Step 6 — Commit và chuẩn bị PR:** `git commit -m "feat(foundation): wire deposit expiry worker and verify Sprint 2 baseline"` sau khi stage đúng files Task 8. PR mô tả foundation/consumer boundary, AC matrix và output kiểm chứng thực chạy. Tối thiểu hai thành viên khác Approve, CI xanh trước Squash & Merge; việc gửi PR/merge theo ủy quyền lúc thực thi, không coi checkbox này là đã merge.

## Coverage và self-review của plan

| Spec / AC | Task chứng minh |
|---|---|
| DTO / AC01 | 1 |
| Typed cọc, projection, privacy/replay / AC02 | 2 |
| Hotel + Spa read / AC03, AC04 | 4 |
| Expiry, filter, batch, transaction, race / AC05, AC06, AC07 | 5; worker global ở 8 |
| Settlement và terminal / AC08, AC09 | 6 |
| MailKit/doubles / AC10 | 7 |
| Seed / AC11 | 3 |
| DI, worker, regression / AC12 | 4–8, full gate 8 |

Self-review: signatures expiry có optional targets cuối để giữ source compatibility; DTO response không thêm field ngoài contract; RF01–05 có named tests; Không có task endpoint implementation hoặc schema change. Helper vẫn gọn theo trách nhiệm, không ép số dòng. Không gắn nhãn tests đã pass khi chỉ mới viết kế hoạch.

## Execution handoff

Đề xuất **Native**: một implementer làm tuần tự tám task và giữ nhất quán graph/lock/accounting, review toàn nhánh ở cuối theo workflow được chọn. Có thể chọn **Subagent-driven** nếu muốn review độc lập từng task; chi phí context/review cao hơn. Một PR và tối thiểu hai reviewer của nhóm vẫn áp dụng với cả hai cách. Plan cần Tech Lead review và chọn cách thực thi; việc tạo tài liệu này không bắt đầu implementation.

Execution: Tech Lead chọn Native và yêu cầu triển khai ngày 2026-11-22. Tasks1–7 đã commit sau các bước RED/GREEN; Task8 đã qua local verification; chờ whole-branch fresh review. CI và hai approvals của nhóm là gate merge riêng, chưa được chứng nhận bởi local tests.

## Local verification — 2026-11-22

- Build: PASS, 0 warnings/errors.
- Foundation + Seed: PASS151/151, skipped0, PostgreSQL17 local.
- Full backend: PASS358/358, skipped0 (2m25s); SMTP loopback, không gửi mail thật.
- `git diff --check`: PASS; không Entity/Configuration/migration/auth/frontend change.
- Native: task commits riêng trên feat/issue-14-sprint2-foundation. PR description chuẩn bị ở scratch; chưa push/merge hoặc chứng nhận CI/hai approvals.
