# Hợp đồng API — Spa & Hotel

**Phiên bản:** 0.1, 17/10/2026. **Trạng thái:** dự thảo để duyệt trước triển khai. D01–D14/Q01–Q08 và T01–T09 đã duyệt; tên route, DTO và cách gom endpoint dưới đây là đề xuất triển khai, chưa phải API đang hoạt động.

**Nguồn chuẩn:** [SRS 1.1](../requirements/SRS_He_Thong_Quan_Ly_Spa_Thu_Cung_AI_Readable.md), [thiết kế 1.0](system-design.md), [traceability](requirements-traceability.md). Không coi tên operation của mock frontend là HTTP contract bắt buộc.

## 1. Quy ước chung

- Base path `/api`. JSON dùng camelCase; bảng/cột trong ERD vẫn giữ tiếng Việt. ID số nguyên; `requestId` UUID do client tạo và giữ nguyên khi retry cùng thao tác.
- `P` = Public; `S` = STAFF hoặc ADMIN; `A` = ADMIN. JWT Bearer cho S/A; ngoài role vẫn kiểm tra assignment, ownership, ca và trạng thái ở Backend. Không có quyền workGroup.
- Ngày nghiệp vụ `YYYY-MM-DD` theo Việt Nam; thời điểm ISO 8601 có offset, server lưu timestamptz. Client không tự quyết định deadline hủy/no-show bằng đồng hồ máy khách.
- Tiền VND số nguyên, không float; Backend tính giá và snapshot. Request tạo đơn không nhận `total`, `status`, `trackingCode`, hệ số hoặc giá chốt do client quyết định.
- Thành công: `{ success: true, data, message }`; lỗi: `{ success: false, data: null, message, errorCode, errors, traceId }`. `errors` là danh sách `{ field, message }`, có thể rỗng. DTO không chứa ORM navigation hoặc password hash.
- Danh sách nội bộ: `page` mặc định 1, `pageSize` mặc định 20, tối đa 100; kết quả `{ items, page, pageSize, totalCount }`. Lọc ngày/trạng thái và thứ tự ổn định theo thời điểm + ID. Không tải toàn bộ lịch sử để lọc trên browser.
- Thao tác tạo mới trả 201; đọc/cập nhật/command thành công trả 200. Retry tạo booking đã tồn tại trả 200 và cùng booking. Không trả 200 cho lỗi nghiệp vụ.
- Public Tracking gửi `phone` + `trackingCode` trong body POST, không query string. Không ghi nguyên cặp vào log. Mỗi lần gọi lại phải xác minh; chưa thêm session/token Tracking riêng.
- Nội bộ nhận ID cần thiết để thao tác; Public nhận DTO riêng của đúng booking. Không đưa hồ sơ cũ, danh sách đơn cùng SĐT, ghi chú nội bộ, actor credentials hoặc object path Storage vào Public response.

## 2. Danh sách endpoint và quyền

Các mã E dùng cho traceability. Một hàng có thể mô tả hai phương thức của cùng tài nguyên; không đồng nghĩa một UC cần một controller riêng.

| Mã | Method và route | Quyền | Mục đích / giới hạn |
|---|---|---|---|
| E01 | POST `/api/auth/login` | P | Username/password → token, expiresIn, user tối thiểu. Không CRUD user/reset password trong core. |
| E02 | GET `/api/catalog` | P | Dịch vụ/combo/thành phần, loại phòng, hệ số đang áp dụng; giá tham khảo. Không chứa dữ liệu khách. |
| E03 | GET `/api/availability` | P | Ngày nhận/trả, ca, nhu cầu Spa/phòng → sức chứa hiện tại; chỉ tham khảo, chưa giữ chỗ. |
| E04 | POST `/api/bookings` | P | Đặt mới; chỉ nhập hồ sơ mới, không cho chọn customerId/petId cũ. |
| E05 | POST `/api/internal/bookings` | S | Đặt tại quầy, có thể chọn hồ sơ cũ sau xác minh; cùng engine giá/giữ chỗ E04. |
| E06 | GET `/api/internal/customers/lookup?phone=...` | S | Tra chính xác SĐT, trả khách/pet phục vụ tại quầy. Redact query trong access log. Không Public route tương đương. |
| E07 | PATCH `/api/internal/pets/{petId}` | S | Sửa tên/giống/dị ứng/cân nặng đăng ký; không đổi chủ hoặc sửa lịch sử check-in/snapshot. |
| E08 | POST `/api/tracking/lookup` | P | Xác minh cặp, trả tiến độ, ảnh, giá và hóa đơn hiệu lực của đúng đơn. |
| E09 | POST `/api/tracking/cancel` | P | Cặp xác minh + reason; PENDING/CONFIRMED, trước ca ≥2h, chưa pet check-in. |
| E10 | GET `/api/internal/bookings`; GET `/api/internal/bookings/{id}` | S | Danh sách/chi tiết vận hành, lọc ngày/trạng thái. |
| E11 | POST `/api/internal/bookings/{id}/confirm` | S | PENDING→CONFIRMED; không giữ chỗ lần nữa. |
| E12 | POST `/api/internal/bookings/{id}/cancel` | S | `cancellationType`, reason; server kiểm tra loại hủy, deadline và chưa check-in. |
| E13 | POST `/api/internal/bookings/{id}/reschedule` | A | Ca/ngày nhận/trả mới; đổi tài nguyên nguyên tử, trước mọi check-in. |
| E14 | GET `/api/internal/assignment-candidates` | S | Ngày/ca thao tác → nhân viên có đăng ký APPROVED. Server vẫn kiểm tra lại khi gán. |
| E15 | POST `/api/internal/booking-pets/{id}/check-in` | S | Cân nặng/lông, người phụ trách, xác nhận giá tại quầy; NULL→WAITING. |
| E16 | PUT `/api/internal/booking-pets/{id}/assignment` | S | Đổi người phụ trách chưa DONE, người mới có ca APPROVED tại ngày/ca thao tác. |
| E17 | POST `/api/internal/booking-pets/{id}/care-actions` | S* | START/PAUSE/RESUME/READY; START/READY kèm ảnh và ghi chú bắt buộc. |
| E18 | POST `/api/internal/booking-pets/{id}/care-logs` | S* | Ghi chú/ảnh chăm sóc, cân nặng theo dõi tùy chọn; không sửa cân nặng chốt giá. |
| E19 | POST `/api/internal/booking-pets/{id}/handover` | S | READY→DONE riêng; chưa tự ghi PAID/COMPLETED. |
| E20 | POST `/api/internal/booking-pets/{id}/extra-services` | S | Spa/combo thêm, ca thực hiện, lý do; pet WAITING/IN_PROGRESS/PAUSED, chưa invoice hiệu lực. |
| E21 | POST `/api/internal/extra-services/{id}/actions` | S | COMPLETE hoặc CANCEL; COMPLETE cần quyền chăm sóc S*, CANCEL trước khi hoàn tất và chưa invoice. |
| E22 | POST `/api/internal/bookings/{id}/fees`; PATCH `/api/internal/fees/{id}`; POST `/api/internal/fees/{id}/cancel` | S | Tên/tiền/lý do, pet tùy chọn cùng đơn; trước invoice hiệu lực. Giữ dấu vết hủy. |
| E23 | POST `/api/internal/bookings/{id}/invoices` | S | Tạo snapshot UNPAID khi toàn bộ pet READY/DONE và extras hoàn tất; một invoice hiệu lực. |
| E24 | GET `/api/internal/invoices/{id}` | S | Invoice lines snapshot, trạng thái, thông tin QR nếu là invoice hiệu lực UNPAID. |
| E25 | POST `/api/internal/invoices/{id}/withdraw` | S | Chỉ UNPAID; bắt buộc lý do, giữ lịch sử. |
| E26 | POST `/api/internal/invoices/{id}/checkout` | S | Xác nhận đủ tiền + bàn giao còn lại; PAID/DONE/COMPLETED trong một transaction. |
| E27 | GET `/api/internal/work-schedule` | S | STAFF xem lịch mình; ADMIN xem toàn bộ/lọc nhân viên. APPROVED là lịch công bố. |
| E28 | POST `/api/internal/shift-registrations` | S | Tự đăng ký ngày/ca PENDING; không tự duyệt hoặc giả actor. |
| E29 | POST `/api/admin/shift-registrations/{id}/decision` | A | APPROVE/REJECT; bảo vệ lịch/assignment hiện hữu. Không endpoint publish riêng. |
| E30 | POST `/api/admin/services`; PATCH `/api/admin/services/{id}` | A | Tạo/cập nhật dịch vụ, ngừng áp dụng bằng isActive. |
| E31 | POST `/api/admin/combos`; PATCH `/api/admin/combos/{id}` | A | Tạo/cập nhật combo và danh sách serviceIds trong transaction. |
| E32 | PUT `/api/admin/weight-coefficients` | A | Toàn bộ dải cân hợp lệ, không chồng lấn; không sửa snapshot cũ. |
| E33 | PATCH `/api/admin/room-types/{id}` | A | Giá/sức chứa/hoạt động; không giảm dưới công suất đã cam kết. Seed loại phòng demo. |
| E34 | GET `/api/admin/capacity-settings`; PUT `/api/admin/shifts/{id}`; PUT `/api/admin/slots/{date}/{shiftId}` | A | Cấu hình giờ/sức chứa; không làm booking/ca đã duyệt vô hiệu hoặc giảm slot dưới đã giữ. |
| E35 | POST `/api/tracking/reviews` | P | Cặp xác minh + stars/comment, chỉ COMPLETED; một đánh giá/đơn. Route đã được người dùng chốt. |
| E36 | GET `/api/admin/reviews` | A | Danh sách đánh giá, thông tin liên hệ phục vụ xử lý ngoài hệ thống. Không ticket hoặc Staff list. Route đã chốt. |
| E37 | GET `/api/internal/overview` | S | Tổng hợp nhẹ từ booking/invoice; STAFF xem vận hành, dữ liệu doanh thu tổng hợp chỉ ADMIN. Không mở rộng analytics. |
| E38 | GET `/api/internal/care-tasks` | S | STAFF chỉ assignment của mình; ADMIN xem toàn bộ. Lọc ngày/trạng thái, phân trang. |

`S*`: STAFF phải là người được gán hiện tại, có ca APPROVED phù hợp thời điểm thao tác; ADMIN được override actor nhưng không bỏ guard trạng thái/ảnh. Các thao tác quầy dùng S không bắt buộc người thu ngân là người chăm sóc.

## 3. Request/response của các luồng chính

### 3.1 Đặt lịch — E04/E05

HTTP multipart/form-data: phần `payload` là JSON, các phần file được tham chiếu bằng `vaccinationFileKey` trong từng pet. Spa không cần file. Chọn một cách truyền này cho cả hai endpoint để tránh thêm hệ thống upload Public rời có ownership khó kiểm soát.

| Field payload | Kiểu / quy tắc |
|---|---|
| requestId | UUID bắt buộc, retry cùng payload giữ nguyên. Fingerprint gồm dữ liệu chuẩn hóa và hash nội dung file, không gồm boundary/tên file tạm. |
| contact | `{ name, phone, email }`; email bắt buộc, lưu snapshot lần đặt. Public không trả thông tin master cũ. |
| slotId, endAt | Ca hẹn nhận; thời điểm trả dự kiến có offset. SPA nhận/trả cùng ngày; Hotel/Combo qua ít nhất một đêm. Server xác định startAt từ ca. |
| pets[] | Ít nhất một pet; `{ name, species, breed?, registeredWeight, allergyNotes?, serviceId?, comboId?, roomTypeId?, vaccinationFileKey? }`. serviceId/comboId không đồng thời; mỗi pet có Spa hoặc phòng. |
| notes | Tùy chọn, có giới hạn độ dài. |
| customerId, pets[].petId | Chỉ E05; chọn pet cũ phải thuộc customerId đã xác minh, không tin tên từ browser. E04 từ chối ID hồ sơ cũ. |

Hotel/Combo qua đêm bắt buộc mọi pet có roomTypeId. Theo D15 cập nhật ngày 18/10, upload giấy tiêm là tùy chọn khi đặt, không chặn E04/E05. Admin có policy xác minh theo loại dịch vụ, mặc định tắt; khi bật, E15 phải có nhân viên xác minh ảnh/giấy tại quầy trước tiếp nhận, không coi upload là đã xác minh. DTO và nơi lưu policy/actor/thời điểm cần bổ sung trước triển khai Backend. E05 có thể gửi vaccinationRecordId sẵn có thay file; Backend kiểm tra hồ sơ thuộc pet được chọn. Không chấp nhận arbitrary URL/object path. Cặp petId lặp trong một request nội bộ bị từ chối; Public không tự gộp pet chỉ theo tên.

Response tối thiểu: `{ bookingId, trackingCode, status: "PENDING", startAt, endAt, estimatedTotal, pets, emailDelivery: "SENT" | "FAILED" }`. Phân biệt estimatedTotal với finalTotal sau check-in. Email lỗi vẫn trả thành công với trackingCode; không rollback hoặc yêu cầu đặt đơn khác.

Retry requestId và fingerprint giống trả cùng booking, không giữ chỗ/gửi email lại mặc định. Khác fingerprint → 409 `IDEMPOTENCY_CONFLICT`. Đường E04/E05 dùng chung không gian requestId; retry Public chỉ trả projection Public, không nhận dữ liệu nội bộ từ request cũ. Query availability trước đặt không thay kiểm tra và lock trong transaction.

### 3.2 Đọc và hủy Tracking — E08/E09/E35

Request cơ bản `{ phone, trackingCode }`. E09 thêm reason; E35 thêm stars (1–5), comment tùy chọn. Public không được truyền bookingId để bỏ xác minh. Sai SĐT, mã hoặc không tồn tại đều 404 `TRACKING_NOT_FOUND` cùng thông báo; rate limit không cho dò hồ sơ bằng SĐT.

E08 trả `{ trackingCode, status, startAt, endAt, estimatedTotal, finalTotal, pets, invoice, allowedActions }`. Pet gồm tên trong đơn, tiến độ, care logs và ảnh ký hạn ngắn; không bao gồm toàn bộ hồ sơ cũ hoặc nhãn xét duyệt nội bộ. Invoice chỉ hiệu lực và đã được phép xem, không lịch sử nháp bị thu hồi. `allowedActions` hỗ trợ UI; server kiểm tra lại khi mutation. Không dựa vào nó để cấp quyền.

### 3.3 Tiếp nhận, chăm sóc, phát sinh — E15–E22

- E15 JSON `{ actualWeight, coatCondition, assignedStaffId, priceAccepted: true }`. Server tính từ giá catalog và hệ số, lưu từng thành phần snapshot; cập nhật cân nặng hiện tại của pet. Nếu giá ước tính chưa được khách đồng ý trực tiếp thì không check-in.
- E17 multipart: payload `{ action, note }` và file `photo`. START cần ghi chú sức khỏe + ảnh đầu; READY cần ảnh cuối và mọi phần việc Spa thêm đã hoàn tất. PAUSE cần lý do; RESUME không xóa ảnh/lịch sử. Không nhận arbitrary `careState` PATCH.
- E18 multipart: `{ note, followupWeight? }` và file ảnh tùy chọn; phải có nội dung nhật ký. Không cho giả createdBy/createdAt.
- E20 `{ requestId, serviceId? , comboId?, slotId, reason }`. Server lấy cân nặng phù hợp của pet, snapshot giá và giữ công suất theo T08: cùng pet/cùng ca chỉ một suất Spa; khác ca giữ thêm. Ca trong khoảng lưu trú/thực hiện, không ca quá khứ. Không tạo root chi_tiet_don mới hoặc thêm suất phòng.
- E21 `{ action: "COMPLETE" | "CANCEL", reason? }`; COMPLETE cần root đang IN_PROGRESS. Hủy phải có lý do và chưa hoàn tất; tính delta công suất dựa trên tập reservation còn hiệu lực, không trừ bộ đếm mù.
- E22 `{ name, amount, reason, bookingPetId? }`, amount >0. Chặn thay đổi khi invoice hiệu lực tồn tại; không đổi nguồn sang booking khác.

Upload thành công ngoài DB transaction; sau đó mới kiểm tra lại quyền/trạng thái và commit metadata + transition nguyên tử. Lỗi không được để trạng thái mới thiếu ảnh. File không tham chiếu vẫn private, dọn best effort như thiết kế. Đề xuất giới hạn ban đầu: JPEG/PNG/WebP, tối đa 5 MiB/file, tối đa 10 file/request; xác minh signature/content, không chỉ extension. Các ngưỡng này là cấu hình triển khai cần rà soát, không phải quyết định nghiệp vụ đã chốt.

### 3.4 Invoice và checkout — E23–E26

E23 không nhận danh sách giá từ frontend; server snapshot nguồn hiện có. Bấm lại trả invoice hiệu lực hiện tại nếu điều kiện không đổi; không sinh invoice thứ hai. Invoice response `{ id, bookingId, status, lines, serviceTotal, feeTotal, total, paymentMethod, paidAt, withdrawnAt, qr }`; lines `{ kind, name, quantity, unitPrice, total }`, qty Hotel = số đêm, Spa unitPrice đã nhân hệ số.

E25 `{ reason }`; giữ nháp bị thu hồi để truy vết. E26 `{ paymentMethod: "CASH" | "BANK_TRANSFER", receivedAmount, handoverConfirmed: true }`. receivedAmount là tiền đã nhận, phải ≥total; invoice ghi khoản thanh toán đúng total, tiền thừa trả lại chỉ là thông tin UI. Chuyển khoản không có webhook. Server trả invoice PAID và booking COMPLETED sau commit; retry cùng invoice/phương thức/số tiền trả kết quả đã ghi, thông tin khác báo 409. Không cập nhật tiền/giờ thu lần nữa.

QR chỉ cho invoice UNPAID hiệu lực: `{ imageUrl, amount, transferContent }`; cấu hình ngân hàng ở server, nội dung dùng mã đơn, không dùng SĐT/email. Không có API khách tự xác nhận PAID. Lỗi tải QR không cản lựa chọn tiền mặt hoặc xác nhận chuyển khoản đã thực nhận.

## 4. Lỗi và concurrency

| HTTP | errorCode tiêu biểu | Cách client xử lý |
|---|---|---|
| 400 | VALIDATION_FAILED, INVALID_DATE_RANGE | Hiển thị lỗi field; không tự đổi payload rồi retry âm thầm. |
| 401 / 403 | UNAUTHENTICATED / FORBIDDEN | Đăng nhập lại / thông báo không có quyền, kể cả assignment không đúng. |
| 404 | NOT_FOUND / TRACKING_NOT_FOUND | Public dùng lỗi chung; không xác nhận SĐT có hồ sơ. |
| 409 | CAPACITY_EXCEEDED, INVALID_TRANSITION, INVOICE_LOCKED, IDEMPOTENCY_CONFLICT, REVIEW_EXISTS, ASSIGNMENT_UNAVAILABLE | Tải trạng thái mới, cho người dùng quyết định lại. |
| 413 / 415 | FILE_TOO_LARGE / UNSUPPORTED_FILE | Chọn file phù hợp; không bỏ ảnh bắt buộc để tiếp tục. |
| 429 | RATE_LIMITED | Tôn trọng Retry-After; không lặp request liên tục. |
| 503 | STORAGE_UNAVAILABLE, TEMPORARY_FAILURE | Retry có giới hạn, giữ nguyên requestId cho create. SMTP lỗi sau commit không trả 503 cho booking. |

Lock/idempotency tuân theo system-design mục transaction: booking trước, các slot ID tăng dần, các roomType ID tăng dần; thao tác cần registration cũng phải khóa và kiểm tra lại. Ràng buộc unique bảo vệ requestId, invoice hiệu lực, review/booking, root/booking-pet. Chuẩn hóa lỗi constraint/concurrency thành lỗi nghiệp vụ, không trả SQL/stack trace.

Các command đã đạt cùng kết quả (confirm, cancel cùng loại/lý do, handover, hoàn tất extra, withdraw cùng lý do, checkout cùng dữ liệu) trả trạng thái hiện tại, không lặp side effect. Dữ liệu khác hoặc trạng thái không tương thích trả 409. Update không phải retry được chỉ vì HTTP method là POST; mỗi use case phải kiểm tra trong transaction. Trong draft này chưa thêm entity version column hay distributed idempotency store.

## 5. Điểm rà soát trước code

Route/DTO/envelope và giới hạn file là đề xuất ở bản 0.1; không mở lại T01–T09. Chốt phiên bản .NET/EF/PostgreSQL, JWT expiry, nơi triển khai/SMTP và biến cấu hình khi lập kế hoạch code cụ thể; không đưa secrets vào báo cáo. Đánh giá lại dữ liệu cá nhân trong response/log khi nối frontend. Tài liệu chưa phải OpenAPI được sinh từ server và chưa có endpoint nào được chạy xác nhận.
