# Issue #26 follow-up — Phase 2 Spa UI Integration

Ngày: 2026-11-23. Trạng thái: **APPROVED — người dùng đã duyệt thiết kế và yêu cầu lập plan, triển khai trong chat ngày 2026-11-23**.

## 1. Mục tiêu, quyết định và baseline

Khép luồng Spa thật: Public chọn ngày/ca/slot → E03 kiểm tra nhu cầu → E04 đặt lịch → E11 duyệt → E14 chọn kỹ thuật viên và E15 tiếp nhận → E17/E18 chăm sóc → E23/E24 hóa đơn → E26 thu tiền → E19 bàn giao → Public E08 kiểm tra kết quả. Không dùng ID, quyền, sức chứa hoặc xác nhận từ mock thay dữ liệu backend.

**Người dùng đã chốt:** Sprint 1 hoàn tất; thiếu sót tích hợp xử lý trong Sprint 2; Phase 2 được bổ sung backend tối thiểu; chỉ nghiệm thu **Spa**, Hotel/Combo nối sau. #18 không bị mở lại như một Issue chưa hoàn tất. Người dùng đã duyệt thiết kế, gồm amendment E64 và đưa E38/E62 lên sớm, đồng thời yêu cầu lập plan và triển khai. Các đoạn mô tả “đề xuất/chờ duyệt” bên dưới ghi lịch sử thiết kế; approval này cho phép đồng bộ canonical docs trước code, không thay đổi schema.

**Baseline đã kiểm tra:**

- PR #38, Phase 1, head `ab03a9c`: allowlist 15 operations, Session/quyen, multipart/errors, demo isolation, giá theo size và debounce. PR đang mở, chưa merge; không thêm thiết kế này vào PR #38.
- `origin/develop` và checkout develop gốc đã pull tới `cb25111`: #18 qua PR #37 (`b0e7867`, merge `e60de0e`); #19 qua PR #39. #18 có E14/E16 và mở rộng E15; #19 có E61/E21 và READY guard. Không thấy thay đổi các file contract trong các commit này.
- E14 hiện nhận `ngay, maCa`, trả Employee[] active + CARE_UPDATE + ca APPROVED. E15/E17 vẫn tái kiểm tra quyền/ca/trạng thái tại server; kết quả E14 không phải bảo đảm write sau đó thành công.
- E38/E62 có trong contract nhưng chưa có controller trên develop được kiểm tra. BookingPet chỉ có tên người phụ trách; không có assignment ID. Catalog không có ca/slot. Không thể lấy maCa/maKhungGio từ hai nhãn ca hoặc từ tên nhân viên.
- UI cũ lọc assignment bằng `nhanVienId`, có nút invoice dựa trên booking READY, thiếu ảnh/input check-in và chỉ gửi bookingId/method khi thu tiền. READY là **PetState**, không phải BookingState.
- codebase-memory chưa index workspace này; đã dùng CodeGraph tại checkout develop và đọc nguồn có mục tiêu. Các test backend trong #18/#19 tồn tại nhưng chưa được chạy lại trong bước thiết kế; môi trường hiện thiếu PETSPA_TEST_POSTGRES.

Nguồn chuẩn: [API contract](../../design/api-contract.md) mục 1–5; [scope-and-decisions](../../requirements/scope-and-decisions.md) D03/D04/D09/D11/D12/D13/D15/D17 và amendment tách E26/E19; [system-design](../../design/system-design.md) phần ca/assignment, ảnh, giao dịch và phân quyền; [Phase 1 spec](2026-11-23-issue-26-sprint1-hybrid-routing-design.md), [Phase 1 plan](../plans/2026-11-23-issue-26-sprint1-hybrid-routing.md).

Mọi API/DTO mới dưới đây là **đề xuất amendment**, chưa thay thế contract APPROVED. Không sửa canonical contract để hợp thức hóa code chưa được duyệt.

## 2. Hướng thiết kế và phạm vi

Người dùng đã chọn hướng bổ sung backend tối thiểu + UI thật. Thiết kế cụ thể đề xuất:

| Hướng | Đánh đổi | Kết luận |
|---|---|---|
| Dùng E14, triển khai E38/E62 đã có contract, thêm một read API ca/slot Public | Thêm một operation mới và đưa hai operation Sprint 3 lên sớm; giữ Public DTO không có assignment/private data | **Đề xuất chọn**: ít thay đổi contract, guard dữ liệu ở server |
| Thêm assignment ID vào BookingPet chung rồi tải mọi booking và lọc ở Vue | DTO dùng cả Public/Internal bị mở rộng; Staff có CARE_UPDATE vẫn không có BOOKING_APPROVE để đọc E10a; client filtering không là data guard | Không chọn |
| Tiếp tục mock ca/nhân viên và default input | Ít code nhưng không đáp ứng nghiệm thu live, có thể gửi sai ID/đồng thuận | Không chọn |

Trong phạm vi: nguồn slot thật, E38/E62, tích hợp E14, form check-in/care log/thanh toán/bàn giao, pagination danh sách, UI phân quyền theo từng action, lỗi/empty/loading states và TDD. Không redesign trang hoặc thêm dependency/framework.

Ngoài phạm vi: Hotel/Combo, tiền cọc, E05 booking nội bộ, E16 UI phân công lại, E61/E21 UI, E10b, quản trị ca/slot/quyền, OTP, phụ phí/phát sinh, miễn phí đón trễ và ngoại lệ ảnh E52. Không đợi #15/#23 để nghiệm thu Spa. Backend #19 được giữ nguyên; Spa dịch vụ thường vẫn được HOAN_TAT khi E17 READY, không cần E61.

Luồng Public mới chỉ chọn dịch vụ Spa thường được backend E04 hiện hỗ trợ; không cho submit Hotel/Combo/SPA_ADVANCED cần Combo trong live mode. Có thông báo chức năng chưa nối khi gặp booking khác loại, không chuyển các mutation của booking thật sang mock. Mock mode giữ hành vi demo đã có.

## 3. Backend tối thiểu và amendment đề xuất

### 3.1 Một operation mới: GET /api/booking-options?ngay=YYYY-MM-DD

Đề xuất dành mã **E64**, tên DTO `BookingOptions`, `BookingShiftOption`. Mã/route chỉ trở thành canonical khi người dùng duyệt amendment này; chưa tự tạo số GitHub Issue.

| DTO | Fields |
|---|---|
| BookingOptionsQuery | ngay:date, bắt buộc |
| BookingOptions | ngay:date; ca:BookingShiftOption[] |
| BookingShiftOption | maCa:id; tenCa:text; gioBatDau:time; gioKetThuc:time; maKhungGio:id hoặc null; moNhanKhach:bool |

Response là success envelope chuẩn. Đọc CaLamViec active, left join KhungGioDatLich của đúng ngày và NgayNghi; sắp xếp giờ bắt đầu, sau đó maCa. Không tạo/upsert slot, đăng ký ca hoặc mutate counters khi GET.

- `maKhungGio` là ID slot có thật hoặc null. Không dùng maCa thay thế; cùng maCa ở hai ngày có thể có hai maKhungGio khác nhau.
- `moNhanKhach` chỉ true nếu ca active, slot có thật và mở nhận, ngày không nghỉ nhận. Không có slot vẫn có thể trả ca active với maKhungGio=null/moNhanKhach=false: danh sách ca cũng phục vụ chọn ca thao tác hiện tại cho E14.
- Không coi moNhanKhach=true là còn sức chứa. Không trả counters/phút đã dùng, employee/assignment/customer/policy nội bộ. UI gọi E03 với nhu cầu thật; E04 vẫn kiểm tra và giữ tài nguyên nguyên tử.
- Quyền Public; chỉ nhận ngày hợp lệ trong [hôm nay VN, hôm nay VN + 90 ngày]. Ngày sai/ngoài khoảng trả 400 VALIDATION_FAILED theo field ngay; lỗi hạ tầng trả 503 TEMPORARY_FAILURE. Không có ca active trả 200 ca=[]. Tính hôm nay bằng clock hiện có, Asia/Ho_Chi_Minh, không ngày UTC/browser tùy ý.
- UI Public disable slot đã qua giờ kết thúc; E03/E04 giữ quyền quyết định cuối. Không hardcode tên/giờ/ID vào Vue, không infer slot lịch sử để check-in.

E64 bổ sung **một operation**. Kiểm tra registry mục 2 hiện có **70 mã operation riêng biệt** (đã gồm E62/E63), trong khi header/mục 7 và sprint plan vẫn ghi baseline 68. Đây là drift tài liệu đã tồn tại, không phải thêm hai operation trong Phase 2. Khi duyệt amendment cần đối chiếu lịch sử E62/E63 và đồng bộ tổng registry thành **71** nếu giữ toàn bộ các mã hiện có rồi thêm E64; không tự ghi 68→69. E38/E62 được đưa lên Sprint 2 chỉ thay thứ tự delivery. Phải đồng bộ registry/API dictionary, traceability và bảng phân bổ Sprint 2/3 trước implementation; schema 33 bảng không đổi.

### 3.2 E62 — GET /api/internal/fur-conditions

Giữ route/permission CHECKIN_PERFORM/response FurConditionItem[] đã có trong contract. Đọc FUR_CONDITIONS trong CauHinhHeThong, cùng nguồn cấu hình mà E15 dùng. Chỉ trả item đang hoạt động; mỗi item giữ ma/ten/dangHoatDong/choPhepPhuPhiGoRoi. Không tự seed hoặc ghi defaults khi đọc.

Cấu hình thiếu/hỏng trả 503 TEMPORARY_FAILURE có message công khai; không trả stack trace/raw JSON. UI không thay danh mục bằng chuỗi “Bình thường” hoặc default mock. E15 vẫn xác thực code theo cấu hình hiện tại; thay đổi danh mục giữa load/submit có thể trả 400 và UI reload để người dùng chọn lại.

### 3.3 E38 — GET /api/internal/care-tasks

Giữ permission CARE_UPDATE, BookingQuery và PageBookingPet đã có trong contract. Đây là nguồn dữ liệu cho StaffCareTasksView live, thay cho E10a + client filter assignment. Không mở rộng BookingPet Public bằng ID nhân viên và không tra ID theo tên.

- Quyền/active đọc lại từ server. STAFF chỉ nhận pet `NhanVienId == actorId`; ADMIN nhận các task trong scope truy vấn theo policy D03. Không nhận maNhanVien do client tự chọn.
- Mặc định chỉ pet WAITING/IN_PROGRESS/PAUSED/READY trong booking IN_PROGRESS; không trả pet NOT_ARRIVED/DONE/CANCELLED hoặc booking terminal. Query trangThai nếu có phải là một trong bốn PetState này, không BookingState; ghi rõ diễn giải E38 vào contract khi duyệt.
- tuNgay/denNgay lọc ngày nhận dự kiến của booking, không phải ngày log; mặc định không lọc ngày để không mất task còn phục vụ từ ngày trước. Cả hai có giá trị thì cần tuNgay<=denNgay và tối đa 90 ngày; page size 1..100, default 20. Sort ổn định batDauDuKien rồi maChiTietDon; count sau guard assignment và trước pagination.
- Trả BookingPet theo schema hiện tại; không chế parent bookingId, customer hoặc shift fields. Task UI live bỏ các vùng phụ thuộc dữ liệu không có; hiển thị pet, dịch vụ, trạng thái, người phụ trách bằng tên, logs được phép. E17/E18 chỉ cần maChiTietDon.
- Media theo đường ký URL private hiện có; chỉ ký sau khi đã giới hạn đúng actor/task/page. E38 không trả object path; URL hết hạn được tải lại, không cache credential/ảnh ngoài scope.
- E38 là danh sách được phân công, không phải giấy phép mutation: E17/E18 vẫn kiểm tra assignment và ca APPROVED hiện tại. 403/409 khi quyền/ca/assignment thay đổi phải hiển thị và reload.

### 3.4 Tái dùng #18 và các endpoint Sprint 1

E14 query `ngay, maCa` từ ca thao tác thực tế: lấy ngày VN hiện tại, ca trong BookingOptions có giờ chứa thời điểm thao tác. Nếu nhiều ca chứa thời điểm, yêu cầu chọn; nếu không có ca hiện hành, báo không có ca để tiếp nhận bình thường. Không mặc định ca hẹn là ca làm việc hiện tại. Trường hợp Admin override ngoài ca không được thêm vào Phase 2 UI, giữ backend behavior và guard hiện có.

E14 trả [] thì không auto-pick mock user hoặc self-assignment. E15 gửi maNhanVien người dùng chọn. Không đưa E16 UI vào scope này. E23/E24/E26/E19 giữ contract; không đổi tính tiền/transaction hay gom thu tiền–bàn giao.

## 4. Frontend, data flow và hành vi UI

### 4.1 Adapter/service boundaries

Giữ httpClient + duy nhất IS_MOCK_ENABLED. Bổ sung action tường minh `getBookingOptions`, `getFurConditions`, `listCareTasks`, `getAssignmentCandidates` vào routing live; không mở toàn bộ Sprint 2/3. E14/E38/E62/E64 đều là read, không phát workspace-updated.

`sprint1Adapter.js` tiếp tục sở hữu DTO/projection cũ. Đề xuất module nhỏ `phase2Adapter.js` sở hữu bốn read trên để tránh kéo dài file Phase 1; operationClient chọn từ hai allowlist có tên action không trùng, dùng cùng transport/error policy. Không có registry framework, cache global hoặc env flag mới. Services tiếp tục là entry point cho components; named actions theo mẫu service hiện tại. Mock mode cung cấp những read mới từ state demo bằng nhánh mock riêng; không gọi backend để làm demo. Seed shift ID chỉ dùng trong mock.

### 4.2 BookingForm và nguồn slot

Đổi tách state `date`, `selectedMaCa` và `maKhungGio`; load BookingOptions khi date đổi. Chỉ chọn option có slot thật và mở nhận. Thay đổi date/pet/dịch vụ làm invalid kết quả E03 trước đó; bỏ response cũ trả về chậm bằng request sequence hoặc AbortController mà Axios hiện hỗ trợ, không thêm thư viện.

Sau khi có slot và pet demand hợp lệ, gọi E03 đúng maKhungGio cấp đơn và từng dịch vụ. Submit E04 vẫn xử lý 409 CAPACITY_EXCEEDED bằng reload options/availability, giữ input người dùng. SPA-only ngayTra=date, files vaccine có vaccinationFileKey đúng pet. Giá chỉ là ước tính theo size, server chốt snapshot. Không gửi giá/state/actor từ client.

UI ghi rõ: kiểm tra chỗ trống không giữ chỗ; lưu booking thành công mới nhận tracking. EmailDelivery FAILED vẫn hiển thị booking/tracking thành công với nhắc lưu mã. Tắt lựa chọn Hotel/Combo live và bỏ copy “booking mock/chưa gửi email” trên luồng live; demo copy giữ ở mock mode.

### 4.3 Check-in

BookingDetailDialog hiển thị form khi pet NOT_ARRIVED, booking CONFIRMED/IN_PROGRESS, chưa invoice hiệu lực và actor có CHECKIN_PERFORM. Không dùng BOOKING_MANAGE làm quyền thay thế cho mutation.

Thu đủ input: cân thực tế >0 tối đa hai chữ số thập phân; chọn một hoặc nhiều mã tình trạng lông từ E62; ảnh anhTiepNhan; ghiChuSucKhoe 1..2000; kỹ thuật viên E14; radio SIZE_THUC_TE/THU_GON; noiDungThuGon bắt buộc khi THU_GON; checkbox xacNhanKhach bắt buộc. Đồng thuận, tình trạng sức khỏe và xác minh giấy không được default true hoặc default câu trả lời.

Luôn cho nhân viên nhập xác nhận giấy hợp lệ một cách explicit; chỉ gửi xacNhanGiayHopLe=true khi đã tick. Không suy policy snapshot từ Catalog hiện tại; nếu policy bắt buộc nhưng chưa tick, hiển thị field error E15 và giữ form để xác minh. Không cần thêm field policy vào Public DTO. Ảnh giấy upload trước đó không tự chứng minh đã xác minh.

Button submit disabled khi metadata đang load/input thiếu; field errors liên kết label/input, summary lỗi có role=alert, focus lỗi đầu; trạng thái busy có role=status. Xử lý 403/409 bằng reload dữ liệu liên quan, không đổi UI thành check-in thành công trước backend.

### 4.4 StaffCareTasks và care log

Live view dùng listCareTasks/E38, page metadata và action-level permissions; không gọi E10a để lấy tất cả booking, không lọc bằng tên hoặc assignment ID suy đoán. Mock view giữ dữ liệu demo khi mock bật. Không hiển thị hàng giả với customer/bookingId thiếu trong PageBookingPet.

Thay `next` mơ hồ bằng action rõ: WAITING→START, IN_PROGRESS→PAUSE/READY, PAUSED→RESUME. PAUSE có lý do; READY cần ảnh thành phẩm thật trong Phase 2 (không UI ngoại lệ E52). E18 là form ghi nhật ký riêng khi actor có PHOTO_UPLOAD, có nội dung hoặc ảnh và requestId riêng. Không gộp E18 với E17 như một transaction hoặc dùng chung key.

Sau success, patch đúng BookingPet/CareLog từ response và reload page để phản ánh assignment/trạng thái mới. Không sửa parent booking READY. Khi task biến mất sau reload vì DONE/chuyển assignment, đóng dialog và báo trạng thái phù hợp.

### 4.5 Invoice, checkout và bàn giao

Front desk dùng E10a với page/filter metadata rõ; nút E23 dựa vào mọi pet Spa chưa hủy đều READY/DONE, không invoice hiệu lực, và quyền INVOICE_CREATE. Chỉ server quyết định đủ điều kiện cuối; không đợi booking.trangThai==READY. Sau E23 dùng Invoice trả về; E24 tải lại snapshot khi cần, giữ maHoaDon khác maDon.

Render dongTien/tongTien/conPhaiTra và QR từ Invoice backend. Live UI không tự dựng QR từ tracking code hoặc mã demo. Nếu qr null, báo chưa có mã QR và vẫn cho ghi nhận chuyển khoản thủ công theo contract; QR không tự xác nhận tiền.

E26 chỉ mở cho Invoice UNPAID và quyền INVOICE_COLLECT. Chọn CASH/BANK_TRANSFER; nhập soTienThucNhan, soTienDaTraLai là số nguyên VND không âm và tham chiếu/bằng chứng trả thừa khi cần. Tính preview `thực nhận - đã trả lại == conPhaiTra`; không mặc định “đã nhận đủ” từ tổng invoice. Xác nhận chỉ gửi đúng invoiceId và Collect; không gọi E23 ngầm. 409 AMOUNT_MISMATCH/INVALID_TRANSITION giữ input và tải lại E24; không báo đã thu.

Sau PAID: giữ pet READY tới khi nhân viên có PET_HANDOVER chọn từng pet và explicit xác nhận thực nhận, gửi E19 `{requestId,xacNhanDaNhan:true}`. E19 thành công trả DONE; chỉ reload booking để lấy COMPLETED sau pet cuối, không set parent terminal ở frontend. Không bàn giao cả đơn ngầm hoặc checkbox true sẵn. Hỗ trợ multi-pet Spa, bao gồm check-in pet thứ hai trong booking đã IN_PROGRESS.

### 4.6 Pagination, concurrency, errors và mode separation

Workspace store giữ query/page metadata riêng cho bookings và care tasks; trang 1/100 không được ghi là toàn bộ. List/calendar/notifications không tính “tổng đơn đang chờ” từ length trang; dùng tongSo của query PENDING khi cần. Chế độ lịch chỉ trình bày dữ liệu đã tải và nhãn phạm vi, không giả đầy đủ nếu có trang sau. Bộ lọc/đổi ngày reset trang 1; response cũ không ghi đè query mới. Selected dialog giữ snapshot đã tải, đóng/thông báo nếu target không còn truy cập được; không gọi E10b chưa triển khai.

Mỗi logical command tại caller có requestId ổn định. Giữ cùng key+payload+File sau timeout/503 không rõ kết quả; chỉnh input hoặc tạo command mới thì cấp key mới. Không retry tự động. Nếu đổi input sau timeout mà kết quả chưa rõ, reload trạng thái trước khi tạo command khác. Double-click bị chặn bằng busy; server idempotency vẫn là ranh giới cuối. Chỉ reset form/key sau success hoặc bỏ command rõ ràng.

Giữ event một lần sau internal mutation và listener debounce 300 ms. Không phát khi GET, E03/E08 read POST hoặc error. Các màn demo ngoài scope không nhận entity live; giữ HYBRID_ENTITY_UNSUPPORTED thay vì dùng seed trùng ID. Trong form live không hiển thị các nút ghi chú/phụ phí/customerRequest còn đi mock như thao tác được hỗ trợ.

Nếu live session mất/hết hạn hoặc role đổi ở tab khác, xử lý 401/permission failure và clear context theo Phase 1, không đổi identity live sang seed. Không thêm JWT refresh flow ở Phase 2.

## 5. Thành phần thay đổi dự kiến và phối hợp

| Owner | Phần thiết kế / file area |
|---|---|
| Lead + Dev A | Amendment E64, read ca/slot từ Scheduling entities; E03/E04 handoff và BookingForm/bookingRules/pricing; không sửa schema hoặc chức năng #15 chưa merge |
| Lead + Dev B | E38/E62 read services/controllers/DTO PageBookingPet, source FUR_CONDITIONS; handoff E14/E15/E17/E18/E19 đã có; không thay nghiệp vụ #18/#19 hoặc tự nới assignment guard |
| Lead + Dev C | Xác nhận Invoice/Collect/QR/current E23/E24/E26; frontend snapshot/thu tiền; không kéo Hotel/cọc/#23 vào scope Spa |
| Lead frontend | operationClient, phase2Adapter mới, service exports, workspace store, BookingForm/BookingWorkspace/BookingDetailDialog/StaffCareTasks, notifications và conditional live/demo copy; components dùng lại style hiện có |

Backend giữ pragmatic Controller→service/DbContext, AsNoTracking cho read, page/count sau authorization, không N+1 hoặc load tất cả rồi filter ở memory. Tái dùng media signing/privacy conventions. Chỉ thêm helper/interface khi phục vụ concrete read use case; không CQRS/Repository hoặc schema migration.

Theo implementation plan tương lai sẽ chỉ rõ files/test classes và thứ tự merge. Không giao Lead sửa service Dev B đang làm mà không phối hợp. Thời điểm viết thiết kế chưa có số Issue follow-up mới; không tự đổi/đóng #18/#26/#38.

## 6. Acceptance criteria và kiểm thử

| Mã | Tiêu chí có thể kiểm chứng |
|---|---|
| P2-01 | E64 trả ca/slot đúng ngày; ID khác nhau giữa ngày; null/đóng/nghỉ/ca qua giờ không dùng để booking; GET không ghi DB, không lộ nhân viên hoặc counters |
| P2-02 | Public chọn Spa thường→E03/E04 với slot thật và File đúng; 409 cạnh tranh không mất input; email FAILED vẫn giữ tracking thành công |
| P2-03 | E14 gọi ca thao tác thật; []/403/assignment thay đổi có UI phù hợp, không mock candidates |
| P2-04 | E62 dùng cấu hình active hiện tại; chọn code đúng, metadata lỗi không fallback; E15 đủ ảnh/sức khỏe/giá/đồng thuận và giấy khi yêu cầu |
| P2-05 | E38 trả đúng assignment/permission, không lộ task người khác; UI Staff không cần BOOKING_APPROVE; date/page/filter đúng và ảnh private đã ký |
| P2-06 | START/PAUSE/RESUME/READY/E18 đúng action, lý do, ảnh và key riêng; READY dịch vụ Spa thường tương thích #19 |
| P2-07 | E23 mở từ trạng thái pet, không booking READY; Invoice/QR đúng snapshot; E26 sai tiền 409, retry không thu hai lần; không auto E23/E19 |
| P2-08 | E19 từng pet sau PAID với explicit confirmation; chưa PAID bị chặn; booking multi-pet chỉ COMPLETED sau pet cuối |
| P2-09 | Trang >100 không mất dữ liệu/giả tổng; response cũ không ghi đè filter mới; busy/double-click/debounce/unmount ổn định |
| P2-10 | Mock regression còn chạy; action live lỗi không fallback; quyền CARE_UPDATE/PHOTO_UPLOAD/CHECKIN_PERFORM/INVOICE_CREATE/INVOICE_COLLECT/PET_HANDOVER kiểm tra riêng; bảng giá trống hiển thị “Chưa có giá”, không “Từ 0 ₫” |

Backend endpoint mới E38/E62/E64 bắt buộc xUnit WebApplicationFactory + PostgreSQL test riêng; có happy/sad paths, revoked permission, hai Staff assignment khác nhau, filter/page/sort, read không mutate DB và missing config. Không dùng Neon/shared database hoặc tự migrate/seed để làm test pass.

Frontend TDD dùng Vitest: adapter/request tests giữ httpClient thật ở seam HTTP; component tests nhập đầy đủ rồi assert payload, lỗi/empty/permission; integration test luồng Spa với fixtures backend thật. Bổ sung test files theo feature thay vì tiếp tục nhồi toàn bộ vào Phase 1 test file; implementation plan sẽ chốt paths. Không viết production code trước RED và không khóa tổng số tests ở 227.

Nghiệm thu cuối cần backend full tests + frontend suite/build + browser/API smoke trên môi trường test được cấp với ca APPROVED/slot/file storage thật. Ghi rõ môi trường/commit; không coi fake HTTP pass là đã gọi .NET thật. Các lệnh chuẩn: `dotnet build backend/PetSpa.sln -m:1`, `dotnet test backend/PetSpa.sln -m:1`, `npm --prefix frontend test -- --run`, `npm --prefix frontend run build`.

## 7. Git, amendment và điều kiện chuyển sang implementation plan

Thiết kế ở nhánh tài liệu riêng `codex/issue-26-phase2-design`, không push thay đổi vào PR #38 hoặc merge PR thay người dùng. Implementation Phase 2 cần nền kết hợp **Phase 1 PR #38 + develop có #18/#19**; khi #38 chưa merge, có thể chuẩn bị tài liệu hoặc stacked branch, nhưng base và PR dependency phải tường minh. Không bắt người dùng merge #38 chỉ để duyệt thiết kế.

Trước code: người dùng duyệt spec này và amendment E64 cùng việc đưa E38/E62 lên sớm; cập nhật API registry/dictionary/traceability/sprint ownership bằng PR docs được duyệt, rồi mới lập implementation plan TDD. Không thay contract LOCKED âm thầm, không sửa authentication/schema/deployment. Tổng operations phải được đối chiếu theo registry hiện tại, gồm drift 68/70 nói ở mục 3.1; tổng mới chỉ có hiệu lực sau amendment được duyệt.

**Các lựa chọn kỹ thuật do người thiết kế đề xuất để người dùng duyệt:** E64 Public read tối thiểu; E38 server-side assignment thay ID mới trong Public DTO; E62 cùng nguồn cấu hình E15; chọn ca thao tác hiện tại; phạm vi live chỉ Spa thường; module phase2Adapter riêng; server-side pagination. Khung giờ/check-in ngoài ca Admin và toàn bộ Hotel/Combo giữ ngoài phạm vi.

Không còn câu hỏi sản phẩm bắt buộc sau khi người dùng đã chọn Spa. Các API/DTO/policy đề xuất trên là nội dung cần duyệt trong thiết kế; builder check có thể phát hiện một vòng câu hỏi còn thiếu trước handoff.

## 8. Kiểm tra thiết kế

Builder check độc lập đã đọc spec và không còn câu hỏi ảnh hưởng implementation plan; amendment E64, việc đưa E38/E62 lên sớm và phê duyệt tổng thể vẫn là các gate đã nêu. Đã kiểm tra các liên kết tài liệu. Kiểm tra số lượng dựa trên mã operation riêng biệt trong registry mục 2, không đếm các hàng guard lặp E26/E47. Chưa chạy product tests vì chỉ viết tài liệu thiết kế; không tuyên bố backend mới hoặc UI mới đã được triển khai.
