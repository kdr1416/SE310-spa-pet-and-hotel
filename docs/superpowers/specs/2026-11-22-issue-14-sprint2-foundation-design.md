# Spec triển khai Issue #14 — Foundation Sprint 2

- **Ngày:** 2026-11-22.
- **Trạng thái:** Được Tech Lead cho phép triển khai Native ngày 2026-11-22; đang kiểm chứng implementation, chưa chứng nhận CI/merge.
- **Owner:** Tech Lead; Dev A/B/C review; tối thiểu hai thành viên khác Approve trước merge.
- **Delivery:** một gói foundation hoàn chỉnh trong một PR, chia thành các bước kiểm chứng nhỏ.
- **Branch khi triển khai:** `feat/issue-14-sprint2-foundation`.

## 1. Mục đích và nguồn chuẩn

Issue #14 mở khóa nền tảng dùng chung để các làn Sprint 2 triển khai độc lập mà không tự định nghĩa DTO, công thức tiền, transaction protocol hoặc sửa chồng projection/DI. Hoàn tất issue nghĩa là các thành phần có hành vi thật và tests; không chỉ tạo interface hay stub.

Nguồn chuẩn, đọc theo phần liên quan:

- [Kế hoạch Sprint 2, Task 1 và Global Constraints](../../plans/sprint-2/sprint-2-plan.md).
- [API contract, mục 1, 3.1, 3.2, 4–6](../../design/api-contract.md).
- [Scope & decisions, D04/Q01, D05/Q02, M2-04–07, M2-11, M2-13–14](../../requirements/scope-and-decisions.md).
- [System design, mục 1, 4.7, 4.9, 5, 6 và 8](../../design/system-design.md).

Spec này chi tiết hóa Task 1, không thay đổi routes, schema hoặc các quyết định nghiệp vụ đã khóa. Nếu phát hiện dữ liệu/schema không đáp ứng được quy tắc, dừng phần bị ảnh hưởng và báo Tech Lead; không tạo migration để tự giải quyết.

### 1.1. Quyết định đã được Tech Lead xác nhận

1. Mốc 4 giờ trong kế hoạch là ước lượng có thể điều chỉnh theo spec.
2. Giữ #14 là một gói foundation hoàn chỉnh; kiểm chứng từng phần bên trong, một PR cuối cùng.

### 1.2. Đề xuất kỹ thuật trong spec, chờ duyệt cùng tài liệu

- Helper được thiết kế tinh gọn, thuần xử lý logic tính toán, không áp dụng ràng buộc cứng về số dòng mã nguồn.
- Deadline thống nhất: `now >= HetHanAt` là hết hạn; chỉ `now < HetHanAt` mới được xác nhận đủ cọc. Sửa cách diễn giải `now > HetHanAt` ở mô tả E42 trong kế hoạch khi nối #21.
- Quét hết hạn theo batch tối đa 20 booking mỗi transaction: các Writer đồng bộ (`E04`, `E05`, `E13`, `E20`) dọn đủ reservation quá hạn trực tiếp ảnh hưởng đến tài nguyên mục tiêu trước khi kiểm tra bộ đếm vật lý; có thể cần nhiều batch. Quét toàn hệ thống thuộc `DepositExpiryBackgroundService`. Mốc <500ms của M2-14 là mục tiêu cần đo, không được suy ra từ giới hạn batch.
- Phân biệt hủy toàn đơn bằng `DonDatLich.LoaiHuy` có giá trị do command hủy toàn đơn/expiry ghi. E47 và helper khép đơn do tất cả pet bị hủy không tự đặt `LoaiHuy`; vì vậy không phát sinh phạt hủy muộn từ terminal transition.
- Khi settle E47, helper luôn tính pet mục tiêu theo số đêm thực ở được truyền vào, không phụ thuộc caller đã đổi trạng thái pet trước hay sau lời gọi.
- Thời gian ước lượng tại Mục 12 là đề xuất kỹ thuật đang chờ Tech Lead xem xét và phê duyệt.

## 2. Hiện trạng và phạm vi

Đã đối chiếu mã nguồn trên nhánh `develop` (với commit baseline `226937e` chứa đủ 15 operations Sprint 1). Khi triển khai cần kiểm tra baseline mới nhất và đồng bộ an toàn; không checkout/rebase đè công việc đang dở.

- `BookingResponse.TienCoc` và `TrackingResponse.TienCoc` đang là `object?`; `BookingProjection.Project` luôn truyền `null` cho cọc.
- Projection đã có components và `MaLoaiPhong`; cần hoàn thiện cọc, không viết lại phần đang đúng hoặc thêm field Hotel ngoài contract.
- `SpaCapacityCalculator.CheckAsync` dùng bộ đếm lưu trữ trong cơ sở dữ liệu (persisted database counters), chưa bù reservation quá hạn.
- `SmtpEmailSender` dùng `System.Net.Mail`; `IEmailSender` hiện có hai phương thức.
- `DbInitializer` seed ba dịch vụ đơn, 12 dòng giá; chưa seed phòng/combo. Seed chỉ được chạy trong Development/Testing.
- `TimeProvider` đã đăng ký; tests có `PostgresTestDatabase` và `PostgresWebApplicationFactory`.

### 2.1. Trong phạm vi

Shared DTOs; projection Booking/Tracking tương thích; calculators; expiry service/worker; settlement helper thuần tính toán; MailKit và email interface; seed phòng/combo; DI/config công khai; tests nền tảng và sửa test doubles bị ảnh hưởng.

### 2.2. Ngoài phạm vi

- Không tạo mới endpoint nghiệp vụ trong #14; không triển khai 29 operations thay các issue sau.
- Không nối Hotel/Combo/Cọc vào E03/E04/E05, guard E11, hủy/dời lịch, E42–E46 hoặc các luồng E47/E49/E23/E26 trong issue này.
- Không dựng Staging, gửi email thật, cấu hình tài khoản SMTP thật hoặc spike Storage; thuộc #25.
- Không schema amendment, migration mới, OTP, webhook, queue/outbox, Repository/CQRS/MediatR, frontend hoặc thay đổi authentication.

### 2.3. Kết quả không được chấp nhận

Kiểm thử pass nhờ bỏ qua PostgreSQL; đọc công suất tạo write-lock; hoàn tiền hai lần; khôi phục trạng thái reservation đã hết hạn; dùng catalog hiện tại để thay giá snapshot; rút gọn helper làm lược bỏ các logic kiểm tra hợp lệ (guards); thêm trường Public để lộ dữ liệu nội bộ.

## 3. Phương án đã cân nhắc

### 3.1. Phương án đóng gói chuyển giao (Delivery & Packaging)

| Phương án | Lợi ích | Đánh đổi | Kết luận |
|---|---|---|---|
| Một PR foundation hoàn chỉnh, kiểm chứng từng phần | Một gate tích hợp rõ; interface và behavior được review cùng nhau | PR lớn hơn, cần chia phần để review | **Tech Lead chọn** |
| Hai PR nối tiếp: DTO/projection/seed rồi runtime services | Diff mỗi PR nhỏ hơn | Hai lượt tích hợp; nhóm vẫn phải chờ đủ runtime và CI | Không chọn |

### 3.2. Đánh giá các phương án kiến trúc kỹ thuật (Technical Alternatives)

| Vấn đề kỹ thuật | Phương án lựa chọn | Phương án thay thế bị bác bỏ | Lý do & Đánh đổi |
|---|---|---|---|
| **Cơ chế hết hạn giữ cọc 30p** | **Hybrid:** Đường đọc trừ bù query (`readAsOfNow`) + Writer dọn đủ hold quá hạn của tài nguyên mục tiêu theo batch ≤20 + Background Worker định kỳ 30s. | Pure Periodic Worker **hoặc** Event Scheduler (Quartz/Hangfire). | Pure worker có độ trễ quét khiến khách thấy chỗ bận giả; không thêm scheduler framework khi chưa có nhu cầu. Hybrid loại hold hết hiệu lực trên đường đọc; writer vẫn có thể chờ lock và cần đo độ trễ. |
| **Công suất phòng Hotel** | **Truy vấn động:** Tính theo khoảng giao ngày `[checkInDate, checkOutDate)` từ bảng `don_dat_lich`/`chi_tiet_don`. | Bảng đếm suất phòng vật lý theo đêm (như `KhungGioDatLich.SlotDaDat` của Spa). | Tuân thủ Schema Freeze (33 bảng); tránh nguy cơ trôi bộ đếm khi hủy dở/dời lịch. Đo query thực tế trên dữ liệu đại diện; chưa khẳng định thời gian truy vấn. |
| **Helper quyết toán chi phí** | **In-Memory Helper:** `BillingSettlementHelper : IBillingSettlementHelper` thuần tính toán trên graph đã nạp. | Stateful Domain Service tự inject DB **hoặc** Database Stored Procedures/Triggers. | Tránh truy vấn DB lặp lại; caller làm chủ transaction và graph nạp; cho phép viết unit test 100% in-memory không cần mock database. |
| **Giao thức chống tranh chấp** | **Pessimistic Locking:** Khóa `FOR UPDATE` theo thứ tự ID tăng dần (`System Design` mục 5). | Optimistic Concurrency token (`RowVersion` / `xmin`). | Loại bỏ deadlock AB-BA; tránh retry bùng nổ (retry storms) gây thất bại đột ngột cho khách hàng khi tranh chấp phòng/slot cao điểm. |

Giữ kiến trúc hiện có: controller → service/DbContext. Helper không sở hữu database hoặc transaction; expiry service sở hữu transaction dọn dẹp riêng, trừ entry point dùng dưới lock của E42.

## 4. Shared DTOs và projection

### 4.1. Danh mục sở hữu

Các tên C# dưới đây ánh xạ tới schema cùng ý nghĩa ở contract mục 3.1/3.2; JSON camelCase phải đúng từng field. Dùng DTO có sẵn cho `BookingQuery`, `Contact`, `RoomAvailability`, `BookingPet`, `Component` và kiểu chung; không tạo bản sao.

| File Core | Kiểu bổ sung |
|---|---|
| `DTOs/Billing/DepositDto.cs` | `DepositDto`, `RefundDto`, `PageDeposit`, `PageRefund`, `ReceiptCreate`, `RefundCreate`, `RefundConfirm` |
| `DTOs/Billing/FeeDto.cs` | `FeeDto`, `FeeCreate`, `FeeEdit`, `ExtraServiceRequest`, `ServiceActionRequest`, `ReasonCommand` |
| `DTOs/Booking/CustomerPetDto.cs` | `CustomerDto`, `PetDto`, `CustomerQuery`, `PetEdit`, `RescheduleRequest`, `TrackingCancel`, `TrackingResend`, `AcknowledgedResponse`, `AssignmentRequest`, `ShiftQuery`, `PetCancelRequest` |
| `DTOs/Scheduling/ScheduleDto.cs` | `ShiftRegistrationDto`, `ShiftWindowDto`, `ScheduleQuery`, `ShiftRegisterRequest`, `ShiftDecisionRequest`, `ShiftWindowWriteRequest`, `ShiftTransferRequest` |

Yêu cầu DTO:

- Immutable theo convention `record` hoặc `init`; ID dương, `Guid.Empty` không hợp lệ ở requestId; `decimal` nguyên VND, `DateOnly`, `DateTimeOffset` theo contract.
- Quy tắc xác thực phân biệt rõ giữa trường bắt buộc (required), trường tùy chọn (optional) và giá trị null: trường tùy chọn có thể vắng mặt trong JSON payload nhưng không mặc nhiên chấp nhận giá trị null. Tests deserialize phải kiểm tra sự khác biệt cho request liên quan.
- Dùng DataAnnotations và validation cục bộ theo convention hiện có. Những guard cần DB, quyền, ownership, state hoặc XOR nghiệp vụ phải được ghi rõ cho endpoint owner, không coi DTO đã tự kiểm tra đủ.
- PATCH từ chối field lạ, không cho patch rỗng. `FeeEdit` phải có ít nhất tên hoặc số tiền ngoài lý do/requestId; `PetEdit` phải có ít nhất một business field.
- `PageDeposit`/`PageRefund`: `items`, `trang`, `kichThuocTrang`, `tongSo`; `AcknowledgedResponse`: `daTiepNhan`, `thongBao`.
- Không thêm actor, state, giá server tính hoặc object storage path vào request/response ngoài contract. `RefundConfirm.bangChungFileKey` vẫn cần ownership check ở #21; DTO không cấp quyền truy cập file.

### 4.2. Load graph và contract response

`BookingProjection.IncludeDetails` tiếp tục dùng split queries, bổ sung:

- `TienCocDonDatLich` → `GiaoDichThuTienCocItems`, `HoanCocTienCocItems`.
- Giữ pets → services → components, nhân viên/nhật ký, invoice → creator/lines đang có.
- Với caller settlement, nạp thêm `PhuPhiDonDatLichItems`; không dựa vào lazy loading.

Đổi `BookingResponse.TienCoc` và `TrackingResponse.TienCoc` sang `DepositDto?`; cập nhật mapping/serialization liên quan. Booking không có cọc vẫn trả JSON `tienCoc: null`. Snapshot `creationResponse` Sprint 1 có cọc null phải deserialize/replay được.

“Full Hotel mapping” nghĩa là điền đúng field contract hiện có như `maLoaiPhong`; không tự thêm `giaPhongChot`, `soDemDaO` hoặc `soDemChot` vào `BookingPet`.

### 4.3. Công thức projection cọc

Chỉ một invoice hiệu lực (`TrangThai != REVOKED`). Nếu dữ liệu có nhiều invoice hiệu lực, báo vi phạm invariant; không tùy tiện chọn một bản để che sai lệch.

```text
thucNhan = deposit.GiaoDichThuTienCocItems.Sum(x => x.SoTien)
khauTru  = activeInvoice?.CocKhauTru ?? 0m (chỉ tính hóa đơn có TrangThai != REVOKED)
danhHoan = deposit.HoanCocTienCocItems.Sum(x => x.SoTien) (bao gồm cả CHO_HOAN và DA_HOAN)
daHoan   = deposit.HoanCocTienCocItems.Where(x => x.TrangThai == RefundState.DA_HOAN).Sum(x => x.SoTien)
giuLai   = max(0, thucNhan - khauTru - danhHoan) (chỉ áp dụng khi thỏa mãn điều kiện phạt)
```

Điều kiện phạt (giữ lại tiền cọc `giuLai`): Bắt buộc thỏa mãn đồng thời:
1. Đơn ở trạng thái `CANCELLED`.
2. Chưa có bất kỳ thú cưng nào check-in (`CheckInAt == null` trên toàn bộ pet).
3. Yêu cầu cọc không ở trạng thái `HET_HAN`.
4. `booking.LoaiHuy` có giá trị hợp lệ thuộc một trong ba loại: `CUSTOMER_CANCELLED`, `STAFF_CANCELLED` hoặc `NO_SHOW` (`STAFF_REJECTED` hoặc `LoaiHuy == null` không bị phạt).
5. Thời điểm hủy rơi vào kịch bản hủy muộn: Hoặc là `booking.LoaiHuy == NO_SHOW`, hoặc đơn bị hủy khi cách giờ bắt đầu dưới 2 giờ (`booking.HuyAt.HasValue && booking.BatDauDuKien - booking.HuyAt.Value < TimeSpan.FromHours(2)`). Hủy trước đúng 2 giờ trở lên không bị phạt.

Các trường hợp khác `giuLai = 0`; đặc biệt trường hợp hết hạn cọc (`HET_HAN`) luôn có `giuLai = 0` dù lưu `LoaiHuy = NO_SHOW` (`system-design.md` dòng 625).

`LoaiHuy` là dấu phân biệt persisted của thao tác hủy toàn đơn. Khi toàn bộ thú cưng trong đơn bị hủy qua E47, caller/helper giữ `LoaiHuy == null` kể cả khi khép booking sang `CANCELLED`; `HuyAt` một mình không đủ để suy ra phạt. Nghĩa vụ cọc của trường hợp hủy toàn bộ thú cưng qua E47 trước check-in phải vẫn bằng 0 trước và sau terminal transition khi không có phần việc/tiền phòng phải thu (bảo vệ M2-07).

Mapping `CoSoTinh` lấy `CoSoTinhSnapshot`. Danh sách hoàn cọc sắp xếp ổn định theo ID; chỉ xuất các trường theo api-contract. `BookingProjection.Project(DonDatLich booking, InvoiceResponseDto? invoice = null, bool includeInternalDetails = false)`:
- Khi `includeInternalDetails == false` (mặc định cho các API Public như `E04`, `E08`): tự động gán `thamChieu = null` trên các DTO hoàn cọc (`RefundDto`), tuyệt đối không để lộ `BangChungPath`, số tài khoản ngân hàng hoặc ghi chú đối soát nội bộ.
- Khi `includeInternalDetails == true` (dành riêng cho các API nội bộ như `E10b`): trả về **tham chiếu hiển thị đã được che phù hợp (masked reference)** theo đúng quy định tại `api-contract.md` dòng 227 (ví dụ: `***1234` hoặc mã giao dịch rút gọn an toàn), tuyệt đối không mặc định trả chuỗi raw nhạy cảm.

Mặc định kỹ thuật của #14: null/rỗng trả null; chuỗi tối đa bốn ký tự trả `***`; chuỗi dài hơn trả `***` và bốn ký tự cuối nếu cả bốn là số/chữ ASCII, ngược lại trả `***`. Không sửa giá trị gốc trong database. `ProjectCurrent` nhận thêm `bool includeInternalDetails = false` và truyền tiếp cờ; Tracking/creation/replay mặc định Public-safe, internal listing có thể bật cờ nhưng vẫn chỉ thấy tham chiếu đã che.

Logic tính tiền cọc dùng cùng công thức trong projection và settlement; triển khai dưới dạng internal helper function tinh gọn để tránh lặp mã.

## 5. Công suất Hotel và Spa

### 5.1. HotelCapacityCalculator

```csharp
Task<IReadOnlyList<RoomAvailability>> CheckRoomAvailabilityAsync(
    DateOnly checkInDate, DateOnly checkOutDate,
    IReadOnlyCollection<int> roomTypeIds, int? excludeBookingId,
    DateTimeOffset now, CancellationToken ct);
Task LockRoomTypesAsync(int[] roomTypeIds, CancellationToken ct);
```

Đây là chữ ký đề xuất cố định để các issue sau sử dụng. Calculator trả số suất còn mỗi `(loại phòng, đêm)`, sắp xếp theo loại phòng rồi đến ngày. Caller so sánh với demand của từng loại; số booking không thay thế cho số lượng pet. Khoảng ngày hợp lệ yêu cầu `checkOutDate > checkInDate`; nếu `checkOutDate <= checkInDate`, ném `ArgumentException` ngay lập tức (fail-fast); nếu `roomTypeIds` rỗng trả danh sách rỗng. Không trả loại phòng giả nếu ID không tồn tại; caller xác minh ID/IsActive trước khi công bố có thể đặt.

Tập giữ phòng gồm pets có `LoaiPhongId` thuộc đơn `PENDING`, `CONFIRMED`, `IN_PROGRESS`, giao với khoảng yêu cầu; loại trừ `excludeBookingId`. Loại reservation của đơn `PENDING` có cọc `CHO_THU` và `HetHanAt <= now`. Đơn `PENDING` không có bản ghi tiền cọc (khi extension cọc tắt) hoặc đã đáp ứng cọc (`DA_DAP_UNG`) vẫn giữ phòng bình thường.

- Ngày nhận/trả chuyển theo `Asia/Ho_Chi_Minh`; đếm `[ngày nhận, ngày trả)`.
- Mỗi pet một suất mỗi đêm; join nhiều dịch vụ/receipt không nhân đôi.
- Pet `DONE` sớm vẫn giữ khoảng cam kết khi booking còn `IN_PROGRESS` theo Q02.
- Với booking còn active, pet `CANCELLED` chưa từng check-in (`CheckInAt == null`) không giữ đêm nào. Pet đã check-in nhưng chưa trả (`TraLaiAt == null`) tiếp tục giữ toàn khoảng đã đặt. Khi pet hủy đã trả thực tế qua E49, chỉ giữ phần đêm đã ở trước `ngày nhận + (SoDemDaO ?? 0)` và nhả các đêm từ mốc đó trở đi. Khi booking terminal, toàn booking không còn thuộc tập giữ phòng theo quy tắc Q02.
- `suatConLai = max(0, SoLuongPhong - số pet giữ đêm đó)`. Hệ thống không tự động xử lý lưu trú quá hạn (overstay); trường hợp này yêu cầu Admin xử lý nghiệp vụ theo quy trình cảnh báo.

`CheckRoomAvailabilityAsync` chỉ đọc, không mở transaction ghi hoặc gọi expiry. `LockRoomTypesAsync` yêu cầu caller đã mở transaction, dùng truy vấn được tham số hóa (parameterized query), distinct IDs tăng dần: `SELECT id FROM loai_phong WHERE id = ANY(@roomTypeIds) ORDER BY id FOR UPDATE`. Calculator không tự commit hoặc SaveChanges.

### 5.2. SpaCapacityCalculator

Giữ tương thích lời gọi hiện có:

```csharp
Task<IReadOnlyDictionary<int, SpaSlotCapacity>> CheckAsync(
    IReadOnlyCollection<SpaSlotDemand> demands, CancellationToken cancellationToken,
    DateTimeOffset? readAsOfNow = null);
```

- `readAsOfNow == null`: giữ phép tính bộ đếm hiện tại cho writer dưới lock.
- Có cutoff: trừ bù phần giữ của các đơn `PENDING` có cọc `CHO_THU` hết hạn tại cutoff, trước khi tính seats/minutes còn lại; không sửa dữ liệu.
- Slot bù theo `DISTINCT(petId, KhungGioId)` có dịch vụ active; Hotel-only không bù slot. Phút theo `ThoiLuongSnapshot` dòng dịch vụ, combo tính đúng một lần (không cộng cả dòng cha và components).
- Giữ nguyên guard ca đóng, ngày nghỉ, quyền `CARE_UPDATE`, nhân viên active/APPROVED và trần phút. Không bù vượt phần giữ thực tế hợp lệ hoặc che bộ đếm bị sai; invariant sai phải được phát hiện trong tests/diagnostics.
- #15 chịu trách nhiệm truyền cutoff cho E03. #14 kiểm chứng trực tiếp calculator với fixture PostgreSQL.

## 6. Hết hạn cọc và transaction protocol

### 6.1. Interface và ownership

Tạo `PetSpa.Core/Interfaces/Billing/IDepositExpiryService.cs`:

```csharp
Task<int> ExpireOverdueDepositsAsync(
    DateTimeOffset now, int? targetBookingId, CancellationToken ct,
    IReadOnlyCollection<int>? targetKhungGioIds = null,
    IReadOnlyCollection<int>? targetRoomTypeIds = null);
Task ExpireLockedBookingDepositAsync(
    DonDatLich booking, DateTimeOffset now, CancellationToken ct);
```

`ExpireOverdueDepositsAsync` chạy khi caller chưa có transaction chính. Nếu có ambient/current transaction, fail fast thay vì âm thầm dùng chung. Writer gọi và đợi commit dọn dẹp trước khi khóa tài nguyên của request mới. Không gọi phương thức này từ luồng đọc `E03` hoặc duyệt đơn `E11`.

Phân tầng quét hết hạn (Bounded & Targeted execution protocol):
- **Writer đồng bộ (`E04`, `E05`, `E13`, `E20`):** truyền `targetKhungGioIds` và `targetRoomTypeIds` cần đặt. Dọn đủ các đơn `PENDING` quá hạn tại cutoff đang giữ ít nhất một tài nguyên mục tiêu, theo nhiều transaction nếu cần, tối đa 20 booking mỗi transaction. Không được dừng sau 20 booking khi vẫn còn hold liên quan quá hạn. Giới hạn phạm vi giúp giảm công việc; độ trễ vẫn phải đo, gồm cả thời gian chờ khóa.
- **Background Worker (`DepositExpiryBackgroundService`):** không truyền target IDs, chịu trách nhiệm quét vét cạn toàn bộ candidate pool trên toàn hệ thống bằng cách xử lý tuần tự từng batch 20 đơn cho đến khi hết sạch các đơn quá hạn tại thời điểm cutoff.
- **Đơn chỉ định (`targetBookingId != null`):** xử lý chính xác đơn mục tiêu được yêu cầu.

Filter: cả hai collection target là null và không có booking ID nghĩa là quét toàn cục; một collection được truyền nhưng cả hai không có ID nghĩa là no-op, không tự chuyển thành quét toàn cục. Hai tập ID có dữ liệu được hợp bằng OR, bỏ ID trùng; ID phải dương. Không kết hợp `targetBookingId` với resource filters. Cutoff cố định để kết thúc một lần quét hữu hạn; đơn chỉ mới hết hạn sau cutoff được xử lý ở lần gọi tiếp theo.

Handoff cho #15/#17/#22: sau khi chờ khóa tài nguyên, writer đọc lại giờ server. Nếu còn hold liên quan vừa hết hạn sau cutoff dọn dẹp, rollback/thả transaction chính rồi dọn với cutoff mới và thử lại; không gọi expiry quét booking khác khi đang giữ slot/room lock. Booking mới hợp lệ lấy suất cuối là tranh chấp công suất bình thường, không phải lỗi expiry.

Trong mỗi batch:

1. Khóa tất cả booking ứng viên theo ID tăng dần, kiểm tra lại state.
2. Khóa hợp nhất các slot bị ảnh hưởng theo ID tăng dần.
3. Khóa hợp nhất loại phòng theo ID tăng dần.
4. Khóa cọc theo thứ tự booking; nạp lại dữ liệu sau lock, gồm receipts/refunds và services/components cần nhả.
5. Đọc giờ server qua `TimeProvider` sau khi đã chờ khóa; dùng thời điểm không sớm hơn cutoff truyền vào. Đánh giá lại guard, gọi entry point dưới lock, SaveChanges và commit.

*Lưu ý về thứ tự khóa:* Quá trình expiry tuân thủ thứ tự khóa chuẩn nhưng không khóa `dang_ky_ca` vì thao tác hết hạn cọc chỉ hoàn trả bộ đếm `SlotDaDat`/`PhutDaGiu` trên khung giờ mà không làm thay đổi phân công ca làm việc của nhân viên.

Không giữ slot/room lock rồi quay lại khóa booking khác. PostgreSQL `READ COMMITTED`; truy vấn được tham số hóa. Không trả object entity đang track từ context dọn dẹp để caller tái sử dụng như dữ liệu mới.

### 6.2. Entry point dưới lock

`ExpireLockedBookingDepositAsync` không mở transaction, không SaveChanges/commit; caller phải giữ booking → slots → rooms → deposit lock và load graph đầy đủ. Nếu thiếu transaction, fail fast. E42 (#21) phải giữ cùng protocol trước khi gọi; không chỉ khóa booking/cọc rồi quay lại slot.

Chỉ mutate khi booking còn `PENDING`, cọc `CHO_THU`, và `now >= HetHanAt`:

- Booking → `CANCELLED`, `LoaiHuy = NO_SHOW`, `LyDoHuy = "Quá hạn thanh toán tiền cọc 30 phút"`, `HuyAt = now`, cập nhật timestamp. Tác vụ hủy do hệ thống tự động thực hiện, không gán actor ID giả lập (mocked admin).
- Cọc → `HET_HAN`, `KetThucAt = now`, cập nhật timestamp.
- Giảm slot/phút theo phần Spa còn giữ, đúng distinct pet/ca và duration snapshot, không trừ Hotel-only; không đặt counters âm. Phòng được nhả qua trạng thái booking trong truy vấn, không có bộ đếm phòng để giảm.
- Nếu `thucNhan > danhHoan`, tạo `HoanCoc` `CHO_HOAN` cho `thucNhan - danhHoan`, dùng `TaoBoiId` của receipt gần nhất theo `XacNhanAt DESC, Id DESC`, thời gian tạo/cập nhật `now`, lý do hết hạn. Tạo `RequestId` nội bộ khác rỗng; không dùng actorId giả.
- Không tạo bản ghi hoàn tiền (`HoanCoc`) khi `thucNhan <= danhHoan` (không tạo refund 0 đồng). Thiếu receipt actor hợp lệ trong dữ liệu bất nhất phải rollback thay vì tự gán người thực hiện.
- Lần gọi tiếp theo không giảm counter hay tạo lại refund. Concurrency được serialize bằng booking lock và guard kiểm tra lại.

**Tích hợp E42 về sau:** khi receipt đến muộn được ghi thêm sau khi đơn đã `HET_HAN`, #21 phải phân bổ refund bổ sung cho phần mới nhận còn chưa dành hoàn trong cùng transaction. Không kỳ vọng gọi lại expiry của đơn đã hết hạn sẽ tự thực hiện lần chuyển trạng thái nữa. Thứ tự an toàn: xác định expiry dưới lock → ghi receipt → bổ sung phần hoàn còn thiếu → commit; không khôi phục lại trạng thái đơn/reservation đã hết hạn.

### 6.3. Background worker

Tạo `DepositExpiryBackgroundService` mỏng: mỗi vòng tạo DI scope, resolve expiry service/clock, quét, giải phóng scope. Không inject scoped DbContext vào singleton worker.

- `DepositExpiry:BackgroundEnabled`: mặc định false trong `Testing`, mặc định true ở môi trường khác; cấu hình explicit được ưu tiên để có thể bật trong test riêng.
- Interval mặc định đề xuất 30 giây, cấu hình công khai `DepositExpiry:IntervalSeconds`, giá trị phải >0.
- Không chạy hai lượt chồng nhau trong một worker. Lỗi một vòng được log loại lỗi/ID kỹ thuật an toàn và vòng sau tiếp tục; shutdown hủy đúng token. Không log dữ liệu khách hoặc connection string.
- Worker không quyết định hiệu lực reservation: calculator dùng deadline, writer kiểm tra dưới lock kể cả khi worker dừng.

## 7. BillingSettlementHelper

Tạo `Core/Interfaces/Billing/IBillingSettlementHelper.cs`, implementation tại `Api/Services/Billing/BillingSettlementHelper.cs` với ba phương thức đã nêu trong Task 1. Helper chỉ làm việc trên graph caller đã nạp; không query DB, SaveChanges, transaction, SMTP hoặc tự tạo refund.

### 7.1. SettleCancelledPetInMemory

```csharp
void SettleCancelledPetInMemory(DonDatLich booking, ChiTietDon pet,
    IReadOnlySet<int> completedComponentIds, int nightsStayed, DateTimeOffset now);
```

Preconditions: pet thuộc booking; caller đã kiểm tra quyền/state, invoice guard, số đêm hợp lệ; tập component IDs khớp chính xác các component `HOAN_TAT` trên server của pet. Helper bảo vệ preconditions thuần dữ liệu trước khi mutate; xác thực dữ liệu từ server, không dùng dữ liệu client tự khai báo.

- Dịch vụ đơn đã hoàn tất giữ giá; chưa làm chuyển `HUY`, `GiaChot = 0`, `HuyAt = now`.
- Combo chưa làm component nào: cha `HUY`, giá 0; các component chưa làm chuyển `HUY`.
- Combo hoàn tất toàn bộ: giữ giá combo snapshot đã chốt, không đổi sang tổng giá lẻ.
- Combo hoàn tất một phần: giữ component hoàn tất, hủy phần còn lại; cha `HOAN_TAT`, `GiaChot = tổng giá lẻ snapshot tại pet.SizeChot`, `HoanTatAt = now`. Entity component không có `HuyAt`; không thêm cột, lưu mốc hủy ở cha và audit của E47.
- Set `pet.SoDemDaO = nightsStayed`. Caller E47 sở hữu `TrangThaiChamSoc`, actor/lý do hủy, `TraLaiAt`, audit và nhả công suất theo phần đã tiêu thụ. Trong lần tính tổng này, helper coi riêng pet mục tiêu là bị hủy dù caller chưa set `TrangThaiChamSoc = CANCELLED`: tiền phòng pet đó luôn dùng `nightsStayed`. Các pet khác dùng trạng thái đã nạp. Sau helper, caller phải hoàn tất đổi trạng thái/hồ sơ hủy trước khi gọi terminal helper và SaveChanges trong cùng transaction. Tests phải cho cùng kết quả tiền với cả hai thứ tự đổi state của pet mục tiêu; không thay state pet bên trong helper.
- Tính lại tổng toàn đơn: dịch vụ không hủy + tiền phòng + phụ phí chưa hủy. Dịch vụ dùng `GiaChot` khi có, nếu chưa check-in dùng size cam kết trong `ChinhSachSnapshot` cùng `BangGiaSizeSnapshot`; không lấy giá catalog hiện tại. Phòng pet active dùng giá chốt hoặc cam kết × số đêm cam kết; pet hủy dùng giá chốt hoặc cam kết × số đêm thực ở. Không cộng riêng components vào tổng nếu đã tính giá dòng cha.
- Không âm thầm gán giá trị 0 khi thiếu snapshot bắt buộc; validate trước mutate và báo lỗi dữ liệu cho caller rollback. Không tự tính lại số tiền yêu cầu cọc.

### 7.2. CalculateObligatedDepositAmount

```csharp
decimal CalculateObligatedDepositAmount(DonDatLich booking);
```

- Không cọc: 0.
- Hủy trước check-in: bằng `giuLai` theo mục 4.3, expiry/quầy từ chối luôn 0.
- Đơn phục vụ hoặc hủy riêng toàn bộ đang chờ quyết toán: invoice hiệu lực có thì lấy `CocKhauTru`; chưa có thì `min(thucNhan - daHoan, currentPayableTotal)` với tổng nghĩa vụ không âm. Trong đó, `currentPayableTotal` chính là `booking.TongTamTinh` được tính lại sau khi đã khấu trừ/cập nhật qua `BillingSettlementHelper`.
- `REVOKED` không chiếm khấu trừ. Phương thức trả phần nghĩa vụ, **không** trả hạn mức hoàn mới.

Caller E44 tính `max(0, thucNhan - obligatedAmount - danhHoan)` và kiểm tra bảo toàn tiền dưới lock. `danhHoan` đã gồm `daHoan`, không trừ `daHoan` lần thứ hai trong công thức hạn mức. Không tạo refund trong helper.

Ví dụ: nhận 600.000 VND, nghĩa vụ 400.000 VND, đã dành hoàn 100.000 VND → chỉ còn tạo thêm 100.000 VND; sau xác nhận hoàn 100.000 VND, hạn mức vẫn 100.000 VND, không tăng thành 200.000 VND.

### 7.3. TryTransitionBookingTerminalState

```csharp
void TryTransitionBookingTerminalState(DonDatLich booking, DateTimeOffset now);
```

| Nhánh | Điều kiện đủ | Kết quả |
|---|---|---|
| **M2-04: còn ≥1 pet không hủy** | Invoice hiệu lực `PAID`; mọi pet không hủy `DONE`; mọi pet hủy đã có `TraLaiAt` | `COMPLETED`, set `HoanTatAt` một lần |
| **M2-05: tất cả pet hủy sau khi bắt đầu phục vụ (`IN_PROGRESS`)** | Mọi pet có `TraLaiAt`; nếu nghĩa vụ > 0 phải có invoice `PAID`; nếu = 0 không còn invoice hiệu lực `UNPAID`; không còn refund `CHO_HOAN` hoặc cọc dư chưa phân bổ | `CANCELLED`, ghi thời điểm kết thúc phù hợp, giữ pet `CANCELLED` |
| **Tất cả pet hủy trước check-in (từ `PENDING` hoặc `CONFIRMED`)** | Mọi dòng pet đã chuyển `CANCELLED`; không còn invoice hiệu lực `UNPAID` | `CANCELLED`, ghi thời điểm kết thúc phù hợp; các khoản cọc/hoàn cọc (`CHO_HOAN` hoặc cọc dư) tiếp tục xử lý bất đồng bộ theo vòng đời cọc, không chặn kết thúc đơn (`system-design.md` dòng 401). |

Đơn không có thú cưng (`booking.ChiTietDonDonDatLichItems.Count == 0`) không được tự động chuyển sang trạng thái kết thúc khi đánh giá `All()` trên tập rỗng. Nhánh trước check-in kiểm tra thực tế mọi pet `CheckInAt == null`, không chỉ dựa state booking. Không thay đổi state terminal hoặc timestamp lịch sử khi gọi lặp lại. Khi khép đơn do hủy toàn bộ thú cưng qua E47, giữ `LoaiHuy == null` để không biến lý do hủy riêng thành phạt toàn đơn. Không áp điều kiện chờ hoàn tiền của M2-05 sang M2-04 hoặc đơn hủy trước check-in: việc chờ mọi `HoanCoc` hoàn tất và không còn cọc dư chưa phân bổ chỉ áp dụng cho nhánh M2-05 đang phục vụ.

## 8. Email, DI và seed

### 8.1. MailKit

Thay implementation SMTP bằng `MailKit.Net.Smtp.SmtpClient`/`MimeKit.MimeMessage`; thêm package MailKit vào Api, chỉ định phiên bản tương thích (version pinning) .NET 10 tại bước triển khai. Việc chọn version/restore phải kiểm tra lúc triển khai; spec không khẳng định một version chưa kiểm chứng.

Giữ hai method hiện có; bổ sung chữ ký đề xuất:

```csharp
Task SendTrackingResendAsync(string recipient, string trackingCode, CancellationToken ct);
Task SendRefundConfirmedAsync(string recipient, string trackingCode,
    int refundId, decimal amount, DateTimeOffset refundedAt, CancellationToken ct);
```

- Nội dung text tiếng Việt; resend gửi mã hiện có, không tạo mã mới; xác nhận hoàn chỉ gồm thông tin cần thiết, không kèm chứng từ/raw reference nhạy cảm.
- Giữ các key `Smtp:Host/Port/From/Username/Password/EnableSsl`. Với SSL bật: 465 dùng TLS ngay khi kết nối, 587 dùng STARTTLS bắt buộc; không chấp nhận certificate tùy tiện. SSL tắt chỉ dành SMTP test/local được cấu hình chủ động. Port khác cần chế độ TLS explicit khi triển khai, không tự downgrade.
- Thao tác connect/auth/send/disconnect có cancellation/timeout hữu hạn. Timeout có thể do caller cung cấp như luồng hiện tại; không bắt và bỏ qua ngoại lệ (swallow exceptions) khiến caller báo cáo sai trạng thái gửi email thành công.
- Giữ semantics gửi sau commit và `emailDelivery: SENT/FAILED`; lỗi SMTP sau commit không rollback booking và không đổi thành HTTP 503. Không thêm retry tự động gây gửi trùng.
- Cập nhật mọi fake/recording `IEmailSender` trong booking/flow tests để build được; không gửi SMTP thật từ test suite. Test message/transport dùng test double hoặc SMTP local kiểm soát được, không cần dependency hạ tầng mới.

### 8.2. DI và cấu hình

- Scoped: `HotelCapacityCalculator`, `IDepositExpiryService → DepositExpiryService`, `IBillingSettlementHelper → BillingSettlementHelper`; giữ `SpaCapacityCalculator`, `IEmailSender` scoped.
- Singleton: `TimeProvider` hiện có; hosted service lấy scoped dependency qua `IServiceScopeFactory`.
- Config example chỉ chứa key công khai, không credentials. Không thay mặc định extension của booking; background flag độc lập với extension flag và vẫn xử lý đơn cũ đã có cọc.

### 8.3. Seed G1

| Dữ liệu | Giá | Số lượng/thành phần |
|---|---|---|
| Phòng Tiêu Chuẩn | 250.000 VND/đêm | 5 phòng |
| Phòng VIP | 450.000 VND/đêm | 3 phòng |
| Combo Tắm Cắt Toàn Diện | S/M/L/XL: 350.000 / 480.000 / 620.000 / 800.000 VND | Tắm sấy vệ sinh + Cắt tỉa tạo kiểu |

- Bổ sung theo convention seed idempotent hiện tại, trong transaction/advisory lock có sẵn; không hardcode IDs. Chỉ tạo phần thiếu, không reset giá/phòng đã chỉnh hoặc nhân đôi links/giá khi chạy lại.
- Database mới: ba dịch vụ, một combo, hai liên kết combo, hai loại phòng, 16 dòng giá (12 dịch vụ + 4 combo). Count 16 áp dụng fixture sạch, không giả định DB tùy ý chỉ có 16 dòng.
- Giữ `EXTENSION_SETTINGS = {"otpEmail":false,"cocHotel":false,"giuCho":false}` khi chưa tồn tại; không ghi đè cấu hình đã chỉnh. Seed không được tự mở cho Staging/Production.
- Tests Hotel/Cọc bật extension qua fixture riêng; không bật mặc định để làm pass các tests mới một cách nhân tạo.

## 9. File và ranh giới bàn giao

Đường dẫn trong bảng tính từ root repository.

| Khu vực | Files dự kiến |
|---|---|
| DTOs | Bốn file mới ở mục 4.1; sửa `Core/DTOs/Booking/BookingResponse.cs`, `BookingManagement.cs` cho đối tượng cọc có kiểu dữ liệu mạnh (`DepositDto?`) |
| Interfaces | `backend/src/PetSpa.Core/Interfaces/Billing/{IBillingSettlementHelper,IDepositExpiryService}.cs` |
| Foundation services | `backend/src/PetSpa.Api/Services/HotelCapacityCalculator.cs`; sửa `SpaCapacityCalculator.cs`; mới `Services/Billing/{BillingSettlementHelper,DepositExpiryService,DepositExpiryBackgroundService}.cs` |
| Projection/email | Sửa `Services/Booking/{BookingProjection,IEmailSender,SmtpEmailSender}.cs`; chỉnh mapping Tracking liên quan nếu cần, không đổi guard/endpoint |
| Wiring/seed | Sửa `Program.cs`, `PetSpa.Api.csproj`, `Data/Seeding/DbInitializer.cs`; cấu hình/example liên quan, chỉ key công khai |
| Tests | Sửa `Seeding/SeedDataTests.cs`; mới `Foundation/Sprint2FoundationTests.cs`; có thể tách tests DTO/settlement/email/concurrency thành file cùng thư mục; cập nhật email doubles bị ảnh hưởng |

| Issue consumer | Phần #14 cung cấp | Phần consumer phải làm |
|---|---|---|
| #15 | Hotel calculator, Spa read cutoff, expiry, DTO/projection | Nối E03/E04/E05, tạo cọc và snapshot Combo/Hotel |
| #16/#17 | Customer/Pet/Reschedule/Tracking DTO, email resend, calculators/expiry | Endpoints, quyền/ownership, E11/E12/E09, dời lịch, E50 |
| #18–#20 | Assignment/PetCancel DTO, helper, components mapping | Check-in/assignment/combo, E47/E48/E49; E19 gọi terminal helper; giữ đúng phần công suất đã dùng |
| #21–#23 | Cọc/hoàn/phí DTO, expiry dưới lock, helper, email refund | Receipt/refund/fee/invoice endpoints, idempotency/audit, E42 tiền muộn; E45/E23/E26 gọi terminal helper |
| #24 | Scheduling DTOs | E27–E29/E54–E55 và service registration của nhánh |
| #25 | MailKit implementation/config keys | Staging, credentials ngoài repo, SMTP/Storage spike thật |

## 10. Acceptance criteria và kiểm chứng

| ID | Tiêu chí bắt buộc | Kiểm chứng |
|---|---|---|
| AC01 | DTO đúng tên/kiểu/nullability/validation; không duplicate kiểu có sẵn | Serialization/deserialization và validation tests theo contract |
| AC02 | Booking/Tracking có typed cọc, công thức tiền đúng, không lộ dữ liệu nhạy cảm (Public trả thamChieu = null; nội bộ trả tham chiếu đã che theo api-contract dòng 227); replay Sprint 1 đọc được | Projection tests, ít nhất một HTTP regression trên dữ liệu cọc seed trực tiếp, snapshot replay test |
| AC03 | Hotel đếm distinct pet từng đêm, exclude booking, bỏ hold hết hạn; không nhả DONE sớm; khi booking còn active, pet hủy trước check-in nhả toàn bộ, pet hủy sau check-in chưa trả giữ khoảng đặt, đã trả chỉ nhả đêm chưa dùng và vẫn tính đêm đã ở | PostgreSQL tests: pet ở 1/3 đêm đã trả chỉ nhả đêm 2–3; CANCELLED chưa check-in/chưa trả/đã trả; deadline trước/đúng/sau; booking terminal không còn giữ phòng |
| AC04 | Spa bù đúng slot/phút trên đường đọc và không ghi DB | Nhiều service cùng pet, nhiều ca, combo, Hotel-only; so DB trước/sau; writer default giữ hành vi Sprint 1 |
| AC05 | Expiry hủy/nhả/hoàn đúng một lần | Chạy lặp và hai context đồng thời; partial receipts; refund CHO_HOAN/DA_HOAN; đúng deadline; rollback khi lỗi |
| AC06 | Writer dọn đủ hold liên quan qua nhiều batch nếu cần, mỗi transaction ≤20 booking; background quét toàn cục; transaction cleanup độc lập | ≥21 đơn liên quan đều được dọn, đơn không liên quan giữ nguyên; scope rỗng không thành global; filter OR; background vét cạn; rollback transaction kế tiếp không phục hồi hold cũ |
| AC07 | Entry point locked không tạo transaction lồng; no-op trên đơn không còn đủ điều kiện | Test trong transaction của caller, rollback toàn bộ khi caller thất bại; competing confirmer mô phỏng dưới cùng booking lock |
| AC08 | Settlement tính đúng combo chưa làm/hoàn tất một phần/hoàn tất, dịch vụ đơn, phòng, phí và cọc | Unit tests graph snapshot; input sai không mutate; pending pet khác giữ giá cam kết; pet mục tiêu cho cùng tiền trước/sau đổi state; ví dụ hạn mức mục 7.2 |
| AC09 | Terminal transitions đúng M2-04/M2-05; hủy toàn bộ pet trước check-in không phát sinh phạt và không bị chặn bởi khoản hoàn CHO_HOAN | Matrix còn pet chưa trả, invoice UNPAID/PAID/REVOKED, phí 0/>0, refund chờ/đã hoàn, dư cọc, all-cancel trước check-in chuyển CANCELLED ngay, tập rỗng và gọi lặp |
| AC10 | MailKit giữ semantics, interface tương thích mọi doubles | Nội dung bốn loại email, cancellation/lỗi SMTP, regression booking commit vẫn tồn tại khi gửi lỗi; không gửi mail thật |
| AC11 | Seed đúng dữ liệu, chạy lặp/khôi phục phần thiếu không ghi đè cấu hình/giá người dùng | SeedDataTests: 12→16 tại các fixture sạch, kiểm tra links/giá/phòng; chạy lại/concurrent seed |
| AC12 | DI resolve được, worker đúng scope/config/shutdown, toàn bộ suite Sprint 1 pass 100% | Host test worker mặc định tắt Testing, bật explicit; backend build 0 warnings/errors và full tests |

Tests transaction/locking dùng PostgreSQL local, mỗi test database riêng qua `PostgresTestDatabase`; không dùng EF InMemory để chứng minh lock/SQL. Thời gian test được điều khiển bằng `TimeProvider`; dùng primitive synchronization (barrier/gate) cho concurrency, không dựa vào Thread.Sleep để phỏng đoán race condition. `PETSPA_TEST_POSTGRES` trỏ loopback theo guard fixture hiện có; không chạy tests trên Neon thật.

Kiểm thử tích hợp HTTP toàn luồng giữa E42 và background worker thuộc #21/#27 vì endpoint chưa có ở #14. #14 vẫn phải chứng minh serialization bằng hai context với receipt confirmation mô phỏng dưới booking lock; không báo đã kiểm thử HTTP endpoint chưa triển khai. FR01 endpoint mới thuộc các issue consumer; không tự tạo endpoint kiểm thử vào product.

Lệnh kiểm chứng **dự kiến khi triển khai**, chưa chạy trong giai đoạn viết spec:

```powershell
dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~Foundation|FullyQualifiedName~SeedDataTests" -m:1
dotnet build backend/PetSpa.sln -m:1
dotnet test backend/PetSpa.sln -m:1
git diff --check
```

Backend build cần 0 errors, 0 warnings. Không chạy frontend tests cho thay đổi chỉ ở foundation backend trừ khi implementation thực tế làm thay đổi frontend; #26/#27 có gate frontend riêng.

## 11. Các bước kiểm chứng bên trong một PR

Đây là thứ tự bàn giao ở mức thiết kế; implementation plan chi tiết được lập sau khi Tech Lead duyệt spec.

1. **DTOs và projection:** chốt signatures/JSON, bảo vệ compatibility/null và snapshot replay. Kiểm tra AC01–02 trước khi consumer dựa vào contract.
2. **Seed:** mở khóa fixture Room/Combo, kiểm tra idempotency/không ghi đè. Kiểm tra AC11 và seed regression.
3. **Capacity:** Hotel + Spa read compensation, kiểm thử không ghi và boundaries. Kiểm tra AC03–04.
4. **Expiry:** hoàn chỉnh lock/cleanup/refund, batching, độc lập transaction rồi worker. Kiểm tra AC05–07 và phần worker của AC12.
5. **Settlement:** tổng tiền, hạn mức cọc, terminal matrix. Kiểm tra AC08–09; không kéo endpoint E47/E23 vào foundation.
6. **MailKit và tổng hợp:** triển khai sender, cập nhật doubles, DI/config, full regression. Kiểm tra AC10–12.

Các thay đổi hành vi áp dụng TDD khi triển khai: kiểm thử thất bại vì thiếu hành vi → thay đổi mã nguồn nhỏ nhất → kiểm thử thành công (pass). Không giữ implementation stub làm kết quả hoàn tất một bước. Review từng phần trong cùng PR; chỉ merge sau khi toàn bộ tiêu chí nghiệm thu (gates) được đáp ứng.

## 12. Rủi ro, Giới hạn, Ước lượng và Cửa duyệt

### 12.1. Rủi ro kiến trúc, Giới hạn và Biện pháp giảm thiểu (Risks & Mitigations)

| Nhóm | Rủi ro / Giới hạn tiềm ẩn | Biện pháp giảm thiểu kỹ thuật | Tiêu chí kiểm chứng |
|---|---|---|---|
| **Failure Modes** | **Tính trùng tài nguyên do join:** Đếm thừa slot/đêm khi pet có nhiều dịch vụ hoặc receipt. | Dùng `DISTINCT(petId, KhungGioId)` cho Spa và đếm theo pet có phòng cho Hotel; join receipt/refund qua aggregation riêng. | AC03, AC04 |
| **Failure Modes** | **Deadlock AB-BA:** Tranh chấp khóa ngược chiều giữa các luồng ghi. | Khóa tài nguyên theo thứ tự tăng dần cố định: `don_dat_lich` $\rightarrow$ `khung_gio_dat_lich` $\rightarrow$ `loai_phong` $\rightarrow$ `tien_coc`. | AC05, AC07 |
| **Failure Modes** | **Helper tính thiếu tiền do thiếu graph nạp:** Caller quên `.Include` các collection con khiến tổng tính sai. | `BookingProjection.IncludeDetails` nạp đầy đủ split query; `BillingSettlementHelper` bảo vệ preconditions dữ liệu; AC08 test chặt chẽ. | AC08 |
| **Failure Modes** | **Bỏ sót refund cho receipt nộp muộn sau hạn:** Biên lai thu đến sau khi cọc đã `HET_HAN`. | Luồng E42 (#21) phối hợp dưới booking lock: nhận diện expiry $\rightarrow$ ghi receipt $\rightarrow$ tạo bổ sung `HoanCoc` `CHO_HOAN` trong cùng transaction. | AC05, AC07 |
| **Scalability** | **Tail-latency & Lỗi hết chỗ giả do quét hết hạn:** Writer quét toàn DB làm chậm request hoặc bỏ sót đơn quá hạn tại ca cần đặt. | Writer ưu tiên dọn sạch các đơn quá hạn tại khung giờ/phòng mục tiêu (tối đa 20 đơn/transaction); chuyển việc quét vét cạn toàn hệ thống cho `DepositExpiryBackgroundService`. | AC06 |
| **Scalability** | **Tranh chấp khóa phòng Hotel:** Nhiều luồng cùng khóa loại phòng. | `SELECT ... FOR UPDATE` chỉ khóa các ID cần thiết theo thứ tự tăng dần; đo thời gian giữ/chờ khóa, chưa khẳng định mốc <50ms. | AC03, AC07 |
| **Security** | **Rò rỉ dữ liệu nhạy cảm qua Public projection:** Lộ thông tin tài khoản hoặc ghi chú đối soát cọc ra ngoài. | Tham số `includeInternalDetails = false` tự động che giấu `thamChieu = null` trên các API Public; API nội bộ (`E10b`) chỉ trả chuỗi tham chiếu đã che phù hợp (`masked reference` theo api-contract dòng 227), không trả raw. | AC02 |
| **Security** | **Gán quyền actor không hợp lệ:** Tác vụ hệ thống dùng actor ID giả lập Admin. | Tác vụ tự động hủy cọc quá hạn ghi nhận actor là hệ thống (`System`), không gán actor ID giả lập. | AC05 |

### 12.2. Ước lượng nỗ lực và Cửa duyệt (Estimation & Approval Gate)

Ước lượng đề xuất: **16–24 giờ tập trung**, chưa gồm thời gian chờ review và sửa lỗi ngoài phạm vi. Phân bổ định hướng: DTO/projection 3–4h; seed 1–2h; capacity 3–4h; expiry/concurrency/worker 4–6h; settlement 3–5h; MailKit/DI/regression 2–3h. Cập nhật sau bước đầu nếu lượng validation hoặc môi trường test làm thay đổi đáng kể.

Những lựa chọn nhỏ để implementer quyết định: cách chia file test, tên private helper, cấu trúc SQL parameter hóa/EF query tương đương và text email cụ thể. Không được tự đổi signature dùng chung, điều kiện deadline, công thức tiền hoặc phạm vi endpoint; nếu cần đổi phải cập nhật spec và báo Tech Lead.

**Gate #14:** spec được Tech Lead duyệt → lập implementation plan → implement/verify theo TDD → CI pass 100%, 0 warnings, ít nhất hai thành viên khác Approve → Squash & Merge vào `develop` → cả nhóm đồng bộ baseline mới trước khi mở các nhánh phụ thuộc. Spec hiện tại chưa là bằng chứng implementation hay tests đã hoàn tất.
