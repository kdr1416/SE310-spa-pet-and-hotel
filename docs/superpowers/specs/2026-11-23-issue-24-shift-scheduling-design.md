# Issue #24 — Scheduling: audit và thiết kế đề xuất

**GitHub tracking:** [Issue #48](https://github.com/kdr1416/SE310-spa-pet-and-hotel/issues/48). Mã #24 trong tên plan/spec/branch là mã task lịch sử; GitHub #24 là PR Billing đã đóng, không phải issue Scheduling.

Ngày: 2026-11-23 (Asia/Saigon). Trạng thái: APPROVED DESIGN — người dùng duyệt cả bốn quyết định ngày 2026-11-24. Người dùng đã duyệt implementation plan và yêu cầu triển khai ngày 2026-11-24.

## 1. Baseline và điều kiện sẵn sàng

- Đã đọc AGENTS.md, .github/CONTRIBUTING.md, .codex/instructions.md và backend/README.md. Không có CLAUDE.md ở root.
- Đã fetch origin. Worktree riêng: `C:/Users/LENOVO/.codex/worktrees/issue-24-shift-scheduling/FinalProject`; branch `feat/issue-24-shift-scheduling`; base `c554f3b80fc1bf9c02356473c2a6209d5783091d`, trùng origin/develop tại lần fetch.
- Giữ nguyên thay đổi chưa commit ở checkout gốc (các sprint plans và tài liệu mới); không mang chúng sang worktree này. Task 11 đọc thêm ở checkout gốc không khác nội dung Scheduling trên baseline mới; số dòng trong báo cáo dưới đây dùng worktree riêng.
- #14 foundation (#36, 067aea1), #18 (#37, e60de0e), #19 (#39, cb25111), #16 (#40, a686df6), #26 Phase 1 (#38, 141312a) đã nằm trong ancestry của base.
- GitHub REST xác nhận PR #42 đã MERGED lúc 2026-11-23 22:55:15 +07:00, head 03b5caa. Base c554f3b đã có E64/E62/E38; không cần cherry-pick nhánh #26.
- codebase-memory list_projects và check_index_coverage báo PetSpa/worktree mới chưa được index. CodeGraph chỉ có ở checkout gốc; đã dùng để nhận diện DTO/Care. Mọi bằng chứng quyết định bên dưới đối chiếu source hiện hành ở worktree mới; không tự index repository.
- `dotnet build backend/PetSpa.sln -m:1 --nologo`: PASS, 0 warnings/errors. Đã chạy `dotnet test backend/PetSpa.sln --no-build --filter "FullyQualifiedName~SharedDtoContractTests" -m:1 --nologo`: PASS 76/76, không cần DB. Chưa chạy PostgreSQL/full test suite: PETSPA_TEST_POSTGRES chưa được cấu hình trong process. Có dotnet, pg_ctl, initdb, psql; chưa xác nhận cluster test riêng đang hoạt động.
- Chưa có controller/service cung cấp năm routes Scheduling trong backend/src tại baseline. Có permission seed và DTO foundation. Có đủ schema để triển khai mà không sửa Entity/DbContext/migration.

## 2. Bằng chứng, drift và rủi ro tại thời điểm audit

Các đường dẫn dưới đây tương đối với worktree nêu trên; `:N` là dòng bắt đầu bằng chứng.

| Mức | Phát hiện và tác động | Bằng chứng |
|---|---|---|
| Cần chốt | E27 plan cho STAFF gửi maNhanVien bằng chính mình, contract ghi filter Admin-only. Đề xuất STAFF gửi bất kỳ maNhanVien nào đều 403, không gửi thì scope actorId. | docs/plans/sprint-2/sprint-2-plan.md:705; docs/design/api-contract.md:125 |
| Cần chốt | Contract gộp E28/29/54/55 với guard “Đợt đang mở”; plan chỉ kiểm tra thời gian ở E28. Nếu áp nguyên văn lên E54 thì không thể mở đợt đầu tiên; nếu áp E29/E55 sẽ hạn chế Admin sau deadline. Phải làm rõ baseline trước code. | docs/design/api-contract.md:247; docs/plans/sprint-2/sprint-2-plan.md:706 |
| Cao | E28 khóa ca mẫu và các registration hiện hữu chưa đủ chặn hai đăng ký đầu tiên cho hai ca khác ID nhưng chồng giờ. Unique index chỉ chặn đúng ngày/ca, không chặn overlap. | docs/plans/sprint-2/sprint-2-plan.md:710; backend/src/PetSpa.Api/Data/Configurations/DangKyCaConfiguration.cs:36 |
| Cao | E15 đọc HasCurrentShift rồi ghi assignment, nhưng transaction chung chỉ khóa booking; không khóa registration. E55 có thể kiểm tra chưa assignment, chuyển ca, trong khi E15 vẫn commit assignment dựa trên kết quả cũ. Đây là race suy ra từ source, chưa có reproduction test. | backend/src/PetSpa.Api/Services/Care/CareOperation.cs:87; backend/src/PetSpa.Api/Services/Care/CheckinService.cs:260; backend/src/PetSpa.Api/Services/Care/CheckinService.cs:324 |
| Cao | E16 đã khóa service slots rồi registration ngày hiện tại; E55 phải giữ thứ tự tương thích. Không lấy booking lock sau khi đã giữ slot/registration. | backend/src/PetSpa.Api/Services/Care/CheckinService.cs:116; docs/design/system-design.md:373 |
| Cao | Chỉ xét lịch intake/service ở nguồn sẽ bỏ sót Hotel đã lưu trú sang ngày sau, không có service line trong ca nguồn nhưng vẫn đang assignment. | docs/requirements/scope-and-decisions.md:84; backend/src/PetSpa.Core/Entities/Booking/ChiTietDon.cs:22; docs/plans/sprint-2/sprint-2-plan.md:727 |
| Trung bình | APPROVE phải tạo slot thật khi chưa có; schema không có default business cho MoNhanKhach/TongSlot/TranPhut. Không được ghi đè slot đã đóng hoặc counters hiện hữu. | backend/src/PetSpa.Core/Entities/Scheduling/KhungGioDatLich.cs:13; backend/src/PetSpa.Api/Data/Configurations/KhungGioDatLichConfiguration.cs:33 |
| Trung bình | ScheduleQuery có validate đảo ngày nhưng chưa giới hạn 90 ngày; automatic model validation trả VALIDATION_FAILED trước service cho đảo ngày, trong khi Task 11 muốn INVALID_DATE_RANGE. JsonRequired trên DateOnly không thay thế kiểm tra thiếu query qua model binding. | backend/src/PetSpa.Core/DTOs/Scheduling/ScheduleDto.cs:10; backend/src/PetSpa.Api/Program.cs:35; docs/plans/sprint-2/sprint-2-plan.md:704 |
| Trung bình | E55 phải đếm nhân viên active + CARE_UPDATE và kiểm tra cam kết dưới lock; không lấy tất cả APPROVED làm năng lực. Không dùng read-only expiry compensation để tự nhả cam kết chưa được writer giải phóng. | backend/src/PetSpa.Api/Services/SpaCapacityCalculator.cs:46; docs/design/system-design.md:375 |
| Tài liệu | Sprint plan vẫn ghi 29/68 operations; contract amendment hiện hành ghi 32/71 sau E62/E63/E64. Không đổi scope #24 vì số đếm cũ. | docs/plans/sprint-2/sprint-2-plan.md:38; docs/design/api-contract.md:272 |

DTO đã đối chiếu: ScheduleQuery, ShiftRegisterRequest, ShiftDecisionRequest, ShiftWindowWriteRequest, ShiftTransferRequest, ShiftRegistrationDto, ShiftWindowDto đều tồn tại và khớp tên/kiểu JSON contract. Giới hạn semantic bổ sung thuộc endpoint/service, không cần tạo DTO bản sao. Bằng chứng: ScheduleDto.cs:7–65; api-contract.md:125,147,168,214.

## 3. Cách tiếp cận và phạm vi

Đề xuất một service scoped WorkScheduleService và một controller WorkScheduleController với năm action dùng đúng routes/verbs contract. Controller xử lý model/path/JWT actorId và HTTP envelope; service kiểm tra actor/permission từ DB, guard, transaction và projection. DI thêm đúng một service, giữ CustomerLookupService cùng các đăng ký mới từ #42 (Program.cs:100–109).

Đây là hướng tối giản phù hợp kiến trúc đã chọn. Tách một orchestration/framework Scheduling dùng chung chưa có nhu cầu; chỉ khóa đăng ký trong Scheduling mà không phối hợp E15 không đáp ứng concurrency. Không thêm dependency/interface giả định, endpoint publish, frontend, migration hoặc schema mới. E38 đã cung cấp pet được phân công; E27 chỉ trả ShiftRegistration[], không ghép response mới.

Files dự kiến sau khi duyệt: bốn file Task 11; thêm thay đổi nhỏ ở hai code path E15/E16 trong CheckinService.cs và test cạnh tranh E15/E16 với E55. Có thể cần thay đổi cục bộ action E27 để kiểm soát model validation; không sửa global response factory. Những thay đổi này chưa được thực hiện.

## 4. Hành vi từng endpoint (đề xuất)

| Endpoint | Actor và dữ liệu | Guard/kết quả |
|---|---|---|
| E27 GET /api/internal/work-schedule | Active actor có SCHEDULE_REGISTER; role lấy DB. STAFF chỉ chính mình và không được truyền maNhanVien. ADMIN xem tất cả hoặc filter. | Hai ngày bắt buộc, tối đa 90 ngày tính cả hai đầu (`den.DayNumber - tu.DayNumber < 90`), đảo/vượt khoảng: 400 INVALID_DATE_RANGE; thiếu/sai format/ID: 400 VALIDATION_FAILED. Stable sort ngay, giờ bắt đầu, ID. Trả mọi trạng thái trong scope, array rỗng nếu không có. |
| E54 POST /api/admin/shift-windows | Active ADMIN có SCHEDULE_APPROVE. | Validate tuNgay<=denNgay, moAt<dongAt; tạo DotDangKyCa + audit, 201; replay 200. Không tự áp “đúng một tuần”, “chỉ tuần sau”, cấm chồng đợt hoặc cấm giờ mở trong quá khứ vì contract không quy định. |
| E28 POST /api/internal/shift-registrations | Active actor có SCHEDULE_REGISTER, đăng ký chính actorId. | Ca tồn tại/active; ngày trong đợt đang mở. Đề xuất giữ biên plan `moAt <= now <= dongAt`. Ngày nghỉ yêu cầu thêm CARE_UPDATE, không tạo loại ca HOTEL_CARE mới. Duplicate đúng ngày/ca ở mọi trạng thái hoặc overlap PENDING/APPROVED: 409 INVALID_TRANSITION; hai ca tiếp giáp được phép. Tạo PENDING + DotId + audit, 201/replay 200. |
| E29 POST /api/admin/shift-registrations/{id}/decision | Active ADMIN có SCHEDULE_APPROVE. | Chỉ PENDING. APPROVE kiểm tra lại ca active, nhân viên active, điều kiện ngày nghỉ/overlap rồi bảo đảm slot tồn tại trước khi APPROVED; REJECT không tạo slot. Lưu DuyetBoiId/DuyetAt + audit, 200. Không publish riêng. |
| E55 POST /api/admin/shift-registrations/{id}/transfer | Active ADMIN có SCHEDULE_APPROVE, lý do trim không rỗng <=1000. | Chỉ APPROVED; đích khác nguồn, ca active, nhân viên active; ngày nghỉ chỉ CARE_UPDATE. Chặn mọi duplicate đích và overlap khác, chặn assignment/capacity nguồn bên dưới. Đảm bảo slot đích, chuyển cùng registration ID, giữ APPROVED, DuyetBoiId/DuyetAt/DotId gốc; audit ghi trước/sau và người điều chuyển, 200. DotId giữ nguồn vì là provenance lần đăng ký, không tạo đăng ký mới. |

Các ID không tồn tại trả 404 NOT_FOUND; ca tồn tại nhưng inactive hoặc state/guard không hợp lệ trả 409 INVALID_TRANSITION. Quyền bị thu hồi/actor inactive phải chặn cả replay. JWT chỉ cung cấp actorId, không tin role/permission cũ trong token.

TimeProvider.GetUtcNow() dùng cho instant và audit; đọc lại sau khi chờ lock để không đăng ký quá hạn dựa trên giờ preflight. Ngày nghiệp vụ Asia/Ho_Chi_Minh. Không dùng DateTime.Now/UtcNow trực tiếp trong code mới.

Nếu có nhiều đợt cùng bao phủ ngày, chỉ chọn trong các đợt đang mở, thứ tự MoAt giảm rồi Id giảm để DotId xác định; không chọn đợt đóng mới nhất rồi bỏ qua đợt mở. Đây là default đề xuất, chưa phải requirement đã chốt.

## 5. Slot, capacity và assignment

Slot mới dùng unique (Ngay,CaLamViecId); TongSlot=CaLamViec.SucChuaPet; TranPhut=CaLamViec.TranPhut; counters=0; MoNhanKhach=true cho ngày thường, false cho ngày nghỉ. INSERT ... ON CONFLICT DO NOTHING, đọc slot thật sau đó. Slot tồn tại giữ toàn bộ flag/capacity/counters; APPROVE không mở lại slot Admin đã đóng. Mọi thao tác tạo slot cùng transaction với approval/transfer và audit; rollback không để slot mồ côi. E64 trả maKhungGio thật, E03/E04 tiếp tục xét quyền tiếp nhận/công suất; E14 đọc cùng APPROVED registration và active CARE_UPDATE employee. maCa không được giả làm maKhungGio.

Sau khi khóa nguồn và registrations liên quan, đếm distinct nhân viên active + CARE_UPDATE có APPROVED đúng nguồn, loại nhân viên đang chuyển. Từ chối CAPACITY_EXCEEDED nếu SlotDaDat>0 nhưng remainingCareStaff=0, hoặc `min(slot.TranPhut, remainingCareStaff*shiftMinutes) < slot.PhutDaGiu`. Không đổi counters, không tự chuyển reservation sang đích, không giảm Hotel room capacity vào ngày nghỉ. Có thể kiểm tra bằng công thức tương ứng SpaCapacityCalculator nhưng không tái sử dụng guard mở tiếp nhận của calculator cho ca Hotel-care ngày nghỉ.

Assignment nguồn: chặn nhân viên đang giữ pet chưa DONE/CANCELLED có intake hoặc service chưa hủy ở nguồn. Hotel phải xét cả pet đã check-in, chưa trả thực tế và nguồn giao với khoảng lưu trú; trường hợp overstay không được coi trống vì đã qua KetThucDuKien. Đề xuất bảo thủ: với ca nguồn hiện tại/tương lai, Hotel đã check-in và chưa trả vẫn chặn, kể cả không có service line ca nguồn. Điều này có thể yêu cầu E16 đổi người trước khi rút ca tương lai; cần người dùng duyệt. Không chặn chỉ vì một booking cũ của pet đã DONE. Không xóa/đổi tác giả ảnh cũ.

## 6. Transaction, khóa và idempotency

Dùng READ COMMITTED và PostgreSQL transaction như writer hiện hành. Không SERIALIZABLE toàn hệ thống, không đổi schema để thêm exclusion constraint.

1. Kiểm tra actor active/permission trước replay. Fingerprint canonical payload đã normalize và bao gồm target route ID; lookup key `(actorId, operation, requestId)` qua audit_log. Cùng key/payload trả snapshot KetQua 200, khác target/payload trả 409 IDEMPOTENCY_CONFLICT, không đọc lại state hiện tại để giả lập response cũ.
2. Dùng pg_advisory_xact_lock cho request key theo convention có sẵn ở BookingCreateService.cs:233. Với E28/E29/E55 và pha commit assignment E15/E16, dùng chung key namespace Scheduling theo employeeId để tuần tự hóa các thay đổi lịch của cùng một người, kể cả ngày chưa có registration. Key theo toàn employee đơn giản hơn nhiều key ngày, tránh đổi nguồn trong lúc xác định tập khóa. Mọi Scheduling writer và hai assignment writers dùng cùng employee key; E54 chỉ cần request key. Sau khi có request-key lock phải kiểm tra lại actor và lookup audit replay trước mọi state guard: request đầu có thể đã commit trong lúc request retry chờ lock; không được trả INVALID_TRANSITION thay replay 200. Đây là cách dùng primitive hiện có được đề xuất, chưa tự áp dụng.
3. E29/E55 đọc registration source không tracking để xác định employee; sau advisory lock đọc lại source. Source/owner thay đổi ngoài dự kiến phải bỏ snapshot và xác định lại trước khi giữ row locks. Không dùng EF entity cũ sau khi chờ lock.
4. Upsert các slot còn thiếu theo natural key xác định trước, trước khi giữ existing resource row locks. Sau đó khóa toàn tập slot nguồn/đích theo ID tăng, rồi tập registration liên quan theo ID tăng. Không upsert đích sau khi đã khóa registration. Đọc lại counters, state và tập nhân viên sau khóa. E28 không tạo slot chưa approved; employee advisory bảo vệ overlap kể cả khi không có row để khóa.
5. Kiểm tra assignment bằng read query sau registration lock; E55 không lấy booking lock. E04 đã khóa slot rồi registrations (BookingCreateService.cs:219), nên E55 và booking capacity được tuần tự hóa bởi slot nguồn. Hai nhân viên cùng rút khỏi nguồn cũng tuần tự hóa bởi chính slot nguồn.
6. Sửa tối thiểu E15 ở pha sau upload, trong transaction đang giữ booking: lấy employee advisory key CHUNG với E55 cho assignee trước, rồi tập service/intake slot liên quan theo ID, rồi registration của assignee trong ngày thao tác theo ID trước khi kiểm tra HasCurrentShift và ghi NhanVienId. Không giữ lock lúc upload. E16 cũng lấy cùng employee advisory key cho newEmployeeId sau booking lock và trước slot → registration. Cần key này ở cả E15/E16 để tuần tự hóa việc gán Hotel hôm nay với rút ca ngày mai: chỉ khóa registrations ngày hiện tại không bảo vệ được guard Hotel tương lai. E55 giữ registration đến commit nên E15/E16 đợi rồi đọc lại ca; nếu E15/E16 thắng thì E55 đọc thấy assignment và từ chối theo guard. Giữ nguyên policy ADMIN_OVERRIDE_SHIFT hiện có; không mở rộng hay thu hẹp quyền override trong #24. Các trường hợp override phải được test riêng, không dùng để bỏ guard capacity của transfer.
7. Ghi mutation + AuditLog trong cùng transaction: operation SHIFT_REGISTER/SHIFT_DECISION/SHIFT_WINDOW_CREATE/SHIFT_TRANSFER, LoaiDoiTuong, DoiTuongId, Fingerprint, KetQua, Truoc/Sau và LyDo khi phù hợp. Có thể save entity trước để lấy ID nhưng chỉ commit sau audit.
8. Unique audit index đã có tại AuditLogConfiguration.cs:33 là backstop. Nếu race unique, rollback/dispose transaction, clear tracker rồi đọc replay và kiểm tra lại actor; không tiếp tục dùng transaction PostgreSQL đã abort. Unique duplicate registration map INVALID_TRANSITION. Lỗi transient trước commit trả 503 TEMPORARY_FAILURE, không lộ SQL. Không tuyên bố request thất bại chắc chắn nếu commit outcome chưa rõ; retry cùng requestId giải quyết bằng audit.

Không thêm khóa booking sau resource locks, không gọi worker expiry bên trong transaction E55, không gọi HTTP nội bộ. Trường hợp lock timeout/deadlock phải rollback toàn bộ và điều tra thứ tự lấy khóa; không thêm deadlock retry dự phòng trong #24. Không suy đoán concurrency đã an toàn chỉ từ thiết kế.

## 7. Kiểm thử dự kiến sau duyệt (chưa phải implementation plan)

- WAF HTTP cho đủ năm routes: happy paths/status/JSON; unauthenticated, actor inactive, quyền bị thu hồi, STAFF giả role/Admin-only filter kể cả own ID, ADMIN thiếu permission, invalid dates/body/path.
- E28: trước mở/đúng mở/đúng đóng/sau đóng, clock đổi trong lúc chờ lock; ngày không trong đợt; nghỉ có/không CARE_UPDATE; inactive ca; exact duplicate PENDING/APPROVED/REJECTED; overlap, adjacency, khác ngày.
- E29: PENDING approve/reject, terminal decisions, inactive employee/ca, slot mới theo ca mẫu, giữ slot đóng/counters cũ, hai approvals cùng slot chỉ một slot; APPROVED hiện trong E14 và slot thật hiện trong E64.
- E55: state/reason/đích, giữ ID/DotId, không tự dời booking; duplicate mọi trạng thái; overlap; assignment Spa/Hotel/ngày sau/overstay; terminal pet; số phút bằng trần và vượt trần, rút nhân viên cuối có pets, nhân viên inactive/không care không được đếm.
- Idempotency cả bốn mutations: sequential + concurrent replay cùng key, đổi payload/target, actor/operation khác key độc lập, một audit/mutation, snapshot response vẫn giữ sau transition tiếp theo; permission bị thu hồi vẫn 403.
- Concurrency PostgreSQL thật: hai E28 ca chồng khác ID khi chưa registration; approve/transfer tạo cùng slot; hai transfer khác nhân viên khỏi nguồn cuối; E55 đối đầu E04/E15/E16, gồm transfer ca ngày mai đối đầu assignment Hotel hôm nay, có barrier quan sát SQL lock thay vì dựa vào Task.Delay. Kiểm tra cả hai thứ tự thắng, final state và audit/counters.
- Dùng PostgresTestDatabase (Infrastructure/PostgresTestDatabase.cs:24): loopback-only, database tên petspa_test_GUID do fixture tạo, migrate và drop duy nhất database đó. Kết nối admin phải trỏ cluster test riêng, không chỉ suy host loopback là đủ. FoundationTestSupport đã có TimeProvider giả, không sửa fixture shared nếu test-specific host override đủ dùng.
- Sau duyệt: xUnit RED đúng lỗi nghiệp vụ/routes chưa có → implementation nhỏ → GREEN → regression. Build và tests chạy tuần tự `-m:1`; lỗi môi trường/restore/database không tính RED. Cuối cùng chạy dotnet build backend/PetSpa.sln -m:1 và dotnet test backend/PetSpa.sln -m:1 trên cluster test riêng.

## 8. Quyết định đã được người dùng duyệt ngày 2026-11-24

1. E27 theo contract: STAFF không được truyền maNhanVien kể cả ID chính mình; giới hạn 90 ngày inclusive và validation cục bộ để giữ INVALID_DATE_RANGE.
2. Chỉ E28 yêu cầu đợt đang mở; E54 tạo đợt độc lập; E29/E55 được Admin thao tác sau deadline theo UC50. Clarification đã được ghi vào api-contract.md trước implementation.
3. Dùng employee advisory lock chung cho Scheduling và thay đổi tối thiểu cả E15/E16 trong CheckinService.cs, có concurrency tests PostgreSQL thật.
4. Slot mới theo defaults mục 5, slot hiện hữu giữ nguyên; transfer giữ DotId/DuyetAt gốc; áp dụng guard bảo thủ cho Hotel đã check-in chưa trả thực tế.

Các chữ “đề xuất/cần duyệt” trong phần audit và phân tích trên ghi lại trạng thái lúc soạn draft; bốn quyết định ở mục này là kết luận có hiệu lực. Default chọn đợt mở theo MoAt giảm rồi Id giảm là lựa chọn triển khai nhỏ đã nêu, không thêm guard cấm chồng đợt.

Implementation plan: [Issue #24](../plans/2026-11-24-issue-24-shift-scheduling.md). Plan đã được người dùng review và cho phép thực hiện bằng superpowers:executing-plans với TDD; không dùng subagent-driven-development. Implementation đã hoàn tất; full regression 548/548 và build 0 warnings/errors, xem bằng chứng trong plan. Chưa commit/push/PR/merge/deploy.

Review clarification 2026-11-24: plan đã đối chiếu consolidated review và ghi disposition từng item. Giữ employee-wide key để bảo vệ Hotel khác ngày, giữ traceId cho audit và 503 theo contract. Bổ sung ScheduleResult để controller tạo envelope, IsRelational guard cho Care InMemory regressions, canonical fingerprint và tái sử dụng helper test hiện có. Đây là làm rõ kỹ thuật trong phạm vi bốn quyết định đã duyệt, không thay đổi policy hoặc schema.

Công bố được người dùng yêu cầu 2026-11-24: Issue GitHub #48; rebase lên develop c82f612, build 0 warnings/errors và full regression 585/585, skipped 0. Cho phép commit/push/tạo PR, chưa merge/deploy; trạng thái chưa commit ở phần lịch sử trên đã được thay thế bởi mốc công bố này.
