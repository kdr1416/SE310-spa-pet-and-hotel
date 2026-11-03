# BỘ GIAO VIỆC CHI TIẾT 13 GITHUB ISSUES — SPRINT 1

> Tài liệu này chứa nội dung mẫu sẵn sàng để Tech Lead copy-paste trực tiếp lên GitHub Issues khi khởi tạo dự án.  
> **Milestone trên GitHub:** `Sprint 1 - Core Spa Flow`

---

## Issue #1: [Core] Base Response Envelope & Global Exception Middleware

- **Tiêu đề:** `[BE-Core] Base Response Envelope (ApiResponse<T>) & Global Exception Middleware`
- **Labels:** `backend`, `feature`
- **Assignee:** `Tech Lead`
- **Nhánh Git:** `feat/issue-01-core-envelope`
- **Phê duyệt yêu cầu:** Tối thiểu 2 thành viên khác (Dev A, B hoặc C) Approve.

### Mô tả công việc:
Xây dựng lớp vỏ bọc dữ liệu trả về chuẩn `ApiResponse<T>` và Middleware bắt lỗi toàn cục theo quy ước tại [docs/design/api-contract.md mục 1](file:///d:/Năm%203/dotnet/FinalProject/docs/design/api-contract.md#1-quy-ước-schema-http-và-lỗi).

### Yêu cầu kỹ thuật:
1. Tạo class generic `ApiResponse<T>` trong `backend/src/PetSpa.Core/Common/`:
   ```csharp
   public class ApiResponse<T>
   {
       public bool Success { get; set; }
       public T? Data { get; set; }
       public string Message { get; set; } = string.Empty;
       public string? ErrorCode { get; set; }
       public List<ApiErrorItem>? Errors { get; set; }
       public string TraceId { get; set; } = string.Empty;
   }
   ```
2. Tạo `GlobalExceptionMiddleware` trong `backend/src/PetSpa.Api/Middleware/`:
   - Bắt mọi unhandled exception.
   - Ghi log lỗi có kèm `TraceId`.
   - Trả về JSON theo `ApiResponse<object>` với HTTP status 500 (hoặc HTTP status tương ứng nếu là DomainException).
3. Đăng ký middleware trong `Program.cs`.

### Tiêu chí nghiệm thu (DoD):
- [ ] Controller trả dữ liệu thành công có cấu trúc `{ "success": true, "data": ..., "message": "..." }`.
- [ ] Khi controller throw exception, response trả về HTTP 500 với `{ "success": false, "data": null, "message": "...", "errorCode": "INTERNAL_SERVER_ERROR", "traceId": "..." }`.
- [ ] Có ít nhất 1 unit test cho `ApiResponse<T>` và 1 integration test giả lập throw exception.

---

## Issue #2: [Core] Nạp Dữ Liệu Nền Tảng Mẫu (Seed Data)

- **Tiêu đề:** `[BE-Core] Nạp dữ liệu nền tảng mẫu cho môi trường phát triển & kiểm thử`
- **Labels:** `backend`, `feature`
- **Assignee:** `Tech Lead`
- **Nhánh Git:** `feat/issue-02-seed-data`
- **Phụ thuộc:** Issue #1 đã merge.

### Mô tả công việc:
Tạo cơ chế Seeder để nạp dữ liệu ban đầu cho database phục vụ các bạn Dev A, Dev B, Dev C kiểm thử cục bộ và chạy integration test.

### Dữ liệu cần Seed tối thiểu:
1. **4 Khoảng cân nặng chuẩn (`he_so_can_nang`):**
   - Size S: `[0, 5)` kg
   - Size M: `[5, 15)` kg
   - Size L: `[15, 25)` kg
   - Size XL: `[25, +∞)` kg (`can_nang_den = NULL`).
2. **2 Ca làm việc (`ca_lam_viec`):**
   - Ca Sáng: `08:00 - 12:00`, sức chứa 10 pet, trần 240 phút.
   - Ca Chiều: `13:30 - 17:30`, sức chứa 10 pet, trần 240 phút.
3. **3 dịch vụ Spa cơ bản (`dich_vu` & `bang_gia_size`):**
   - Dịch vụ Tắm sấy vệ sinh (loại SPA, 60 phút, giá: S: 150k, M: 200k, L: 250k, XL: 300k).
   - Dịch vụ Cắt tỉa tạo kiểu (loại SPA, 90 phút, giá: S: 250k, M: 350k, L: 450k, XL: 600k); không được mua riêng theo system-design mục 4.10.
   - Seed thêm một dịch vụ Spa cơ bản hợp lệ để đủ 3 dịch vụ theo kế hoạch tổng; dữ liệu chỉ phục vụ phát triển/kiểm thử.
4. **Tài khoản nhân viên ban đầu (`nhan_vien`):**
   - 1 Admin: username `admin`; mật khẩu lấy từ cấu hình môi trường phát triển/kiểm thử, chỉ lưu hash, không ghi credential mặc định trong repo.
   - 2 Staff: `staff1` (Lễ tân), `staff2` (Kỹ thuật viên Spa).
5. **Dữ liệu vận hành phụ thuộc:**
   - Seed `quyen`, `nhan_vien_quyen` theo template FR01; kiểm tra permission hiện tại từ Sprint 1, chỉ dời API quản trị quyền E53/E60 sang Sprint 3.
   - Seed `khung_gio_dat_lich` mở nhận khách cho ngày kiểm thử và `dot_dang_ky_ca` + `dang_ky_ca` trạng thái `APPROVED` cho kỹ thuật viên đủ quyền care. Chỉ có `ca_lam_viec` thì E03/E15 chưa đủ điều kiện hoạt động.
   - Tắt các cờ `otpEmail`, `cocHotel`, `giuCho` cho luồng Spa Sprint 1 và snapshot chính sách khi đặt; không bỏ guard ảnh hoặc tiêm chủng khi policy yêu cầu.

### Tiêu chí nghiệm thu (DoD):
- [ ] Có class `DbInitializer` nạp dữ liệu idempotent theo khóa của từng nhóm dữ liệu; không dùng riêng `NhanVienSet.Any()` để bỏ qua mọi nhóm seed khác.
- [ ] Integration test truy vấn database thấy đủ 4 size (XL không có cận trên), 2 ca, dịch vụ, tài khoản, quyền, slot và đăng ký ca `APPROVED`; chạy seed lại không nhân đôi dữ liệu.

---

## Issue #3: [Catalog] API Xem Danh Mục Dịch Vụ & Bảng Giá (E02)

- **Tiêu đề:** `[BE-Catalog] API Lấy danh mục dịch vụ, combo và khoảng size (E02 GET /api/catalog)`
- **Labels:** `backend`, `feature`
- **Assignee:** `Dev C`
- **Nhánh Git:** `feat/issue-03-catalog-view`
- **Phụ thuộc:** Issue #1, #2 đã merge.

### Mô tả công việc:
Triển khai endpoint Public `GET /api/catalog` trả về danh mục dịch vụ đang hoạt động kèm giá theo từng size.

### Hợp đồng API:
- **Tham chiếu:** `api-contract.md` mục 2 (E02) & mục 3.2 (`Catalog`, `Service`, `SizePrice`).
- **Route:** `GET /api/catalog` (Không cần JWT).
- **Quy tắc:** Chỉ lấy dịch vụ có `is_active = true`. Giá phải trả đủ 4 size S/M/L/XL; response là `Catalog` gồm `dichVu`, `combo`, `loaiPhong`, `khoangSize`.

### Tiêu chí nghiệm thu (DoD):
- [ ] Response trả về 200 kèm danh sách dịch vụ và bảng giá 4 size.
- [ ] Test Happy Path: Trả về danh sách dịch vụ seed.
- [ ] Test Edge Case: Khi không có dịch vụ nào active, trả về `data.dichVu: []` trong `Catalog` (không đổi toàn bộ `data` thành mảng, không ném lỗi 500).

---

## Issue #4: [Booking] API Kiểm Tra Sức Chứa Ca Đặt Lịch Spa (E03)

- **Tiêu đề:** `[BE-Booking] API Kiểm tra công suất ca đặt lịch Spa (E03 POST /api/availability)`
- **Labels:** `backend`, `feature`
- **Assignee:** `Dev A`
- **Nhánh Git:** `feat/issue-04-availability`
- **Phụ thuộc:** Issue #1, #2 đã merge.

### Mô tả công việc:
Triển khai endpoint kiểm tra ca làm việc còn nhận thêm thú cưng cho dịch vụ Spa hay không.

### Hợp đồng API:
- **Tham chiếu:** `api-contract.md` mục 2 (E03), DTO `AvailabilityQuery`, `Availability`.
- **Route:** `POST /api/availability`
- **Logic kiểm tra:**
  - Kiểm tra ngày ca: Không rơi vào ngày đóng tiếp nhận khách (`ngay_nghi`).
  - Kiểm tra 2 trần đồng thời:
    1. Số lượng thú cưng trong ca chưa vượt `suc_chua_pet`.
    2. Tổng phút snapshot không vượt `min(tran_phut, tổng phút nhân viên APPROVED đủ quyền care)`; đếm pet riêng biệt theo ca, không đếm từng dòng dịch vụ thành một pet.

### Tiêu chí nghiệm thu (DoD):
- [ ] Test Happy Path: Ca còn đủ chỗ và đủ phút $\rightarrow$ trả `coTheDat: true`.
- [ ] Test Sad Path: Ca hết slot, vượt trần phút hoặc không có nhân sự `APPROVED` đủ quyền care $\rightarrow$ trả HTTP 409 `CAPACITY_EXCEEDED` theo api-contract mục 5 (E03–05).

---

## Issue #5: [Auth] API Đăng Nhập Nhân Viên & Cấp JWT Token (E01)

- **Tiêu đề:** `[BE-Identity] API Đăng nhập nhân viên nội bộ (E01 POST /api/auth/login)`
- **Labels:** `backend`, `feature`
- **Assignee:** `Tech Lead`
- **Nhánh Git:** `feat/issue-05-auth-login`
- **Phụ thuộc:** Issue #1, #2 đã merge.

### Mô tả công việc:
Triển khai endpoint đăng nhập cho nhân viên nội bộ (Lễ tân, Kỹ thuật viên, Admin) và cấp JWT Access Token.

### Hợp đồng API:
- **Tham chiếu:** `api-contract.md` mục 2 (E01), DTO `Login`, `Session`, `Employee`.
- **Route:** `POST /api/auth/login`
- **Payload:** `{ "tenDangNhap": "...", "matKhau": "..." }`
- **Xử lý:** Kiểm tra tài khoản `is_active = true`, verify password hash, sinh JWT token thời hạn 8 giờ.
- **Response/authorization:** Trả đúng `Session { accessToken, expiresIn, nhanVien: Employee }`; JWT không thay thế kiểm tra permission hiện tại theo FR01 ở các endpoint nội bộ. Không mở rộng role ngoài `ADMIN`/`STAFF`.

### Tiêu chí nghiệm thu (DoD):
- [ ] Test Happy Path: Nhập đúng tài khoản seed trả về 200 kèm JWT token và thông tin `nhanVien`.
- [ ] Test Sad Path: Sai mật khẩu hoặc tài khoản bị khóa $\rightarrow$ trả về 400 `VALIDATION_FAILED`.

---

## Issue #6: [Booking] API Đặt Lịch Spa Mới Kèm Sinh Mã Tracking (E04)

- **Tiêu đề:** `[BE-Booking] API Đặt lịch Spa cho khách vãng lai (E04 POST /api/bookings)`
- **Labels:** `backend`, `feature`
- **Assignee:** `Dev A`
- **Nhánh Git:** `feat/issue-06-booking-create`
- **Phụ thuộc:** Issue #3, #4 đã merge.

### Mô tả công việc:
Triển khai endpoint đặt lịch dịch vụ Spa trực tuyến cho khách hàng không cần tài khoản.

### Hợp đồng API:
- **Tham chiếu:** `api-contract.md` mục 2 (E04), DTO `BookingCreate`, `Booking`.
- **Route:** `POST /api/bookings`
- **Body:** `multipart/form-data` gồm `payload` JSON `BookingCreate` và file tiêm chủng tùy chọn theo `vaccinationFileKey` (api-contract mục 4).
- **Phạm vi đã chốt 03/11/2026:** Chỉ nhận dịch vụ đơn; booking combo chuyển Sprint 2 cùng E61. Cắt tỉa không được mua riêng theo system-design mục 4.10.
- **Quy tắc nghiệp vụ:**
  - Lưu liên hệ snapshot của lần đặt; khi chưa xác định master an toàn theo SĐT dùng chung, không ghi đè hồ sơ cũ hoặc gộp pet theo tên. Public không chọn pet cũ khi OTP đang tắt (M2-10).
  - Tạo `don_dat_lich` ở trạng thái `PENDING`.
  - Sinh mã `trackingCode` 8 ký tự ngẫu nhiên duy nhất (ví dụ: `TRK-8X2A`).
  - Snapshot bảng giá dịch vụ tại thời điểm đặt vào `bang_gia_size_snapshot`.
  - Xử lý `requestId` (UUID): Nếu gửi trùng `requestId` và cùng nội dung thì trả lại kết quả cũ (Idempotent 200).
  - Tạo booking và mọi bản ghi con, kiểm tra/giữ sức chứa trong cùng transaction. Gửi email sau commit; SMTP lỗi vẫn trả success với `emailDelivery: FAILED`.

### Tiêu chí nghiệm thu (DoD):
- [ ] Test Happy Path: Tạo đơn Spa hợp lệ trả về HTTP 201 kèm mã tracking và thông tin tạm tính.
- [ ] Test Idempotency: Gửi lại cùng `requestId` trả về 200 với mã tracking cũ (không sinh đơn mới).
- [ ] Test Idempotency conflict: Cùng `requestId` khác nội dung trả 409 `IDEMPOTENCY_CONFLICT`.
- [ ] Test Sad Path: Ca đã hết chỗ trả về 409 `CAPACITY_EXCEEDED`.

---

## Issue #7: [Booking] API Quản Lý & Duyệt/Từ Chối Đơn Đặt Lịch (E10a, E11, E12)

- **Tiêu đề:** `[BE-Booking] API Lễ tân xem danh sách đơn chờ duyệt và Duyệt/Từ chối đơn (E10a, E11, E12)`
- **Labels:** `backend`, `feature`
- **Assignee:** `Dev A`
- **Nhánh Git:** `feat/issue-07-booking-manage`
- **Phụ thuộc:** Issue #5, #6 đã merge.

### Mô tả công việc:
Triển khai cụm API cho Lễ tân tại quầy xem các đơn đang chờ xử lý (`PENDING`), sau đó bấm Duyệt hoặc Từ chối đơn.

### Hợp đồng API:
- **Tham chiếu:** `api-contract.md` mục 2 (`E10a`, `E11`, `E12`).
- **Routes:**
  - `GET /api/internal/bookings?trangThai=PENDING` (E10a): Lấy danh sách đơn chờ duyệt.
  - `POST /api/internal/bookings/{id}/confirm` (E11): Chuyển trạng thái đơn từ `PENDING` $\rightarrow$ `CONFIRMED`.
  - `POST /api/internal/bookings/{id}/cancel` (E12): Chuyển trạng thái đơn sang `CANCELLED` kèm lý do.
- **Request/guard:** E11 dùng `Command`; E12 dùng `Cancel` với `loaiHuy` và `lyDo`. Từ chối `STAFF_REJECTED` chỉ khi `PENDING`, nhả đúng tài nguyên đã giữ một lần; cả ba endpoint yêu cầu `BOOKING_APPROVE`.

### Tiêu chí nghiệm thu (DoD):
- [ ] Test E10a: Trả về đúng danh sách các đơn có trạng thái `PENDING`.
- [ ] Test E11: Duyệt đơn `PENDING` thành công chuyển sang `CONFIRMED`.
- [ ] Test E11 lỗi: Cố tình duyệt đơn đã hủy hoặc đơn đã xác nhận $\rightarrow$ trả về 409 `INVALID_TRANSITION`.
- [ ] Test E12: Từ chối đơn `PENDING` chuyển thành công sang `CANCELLED`.

---

## Issue #8: [Tracking] API Tra Cứu Tiến Độ Đơn Qua Mã Tracking (E08)

- **Tiêu đề:** `[BE-Tracking] API Tra cứu chi tiết đơn bằng SĐT và mã Tracking (E08 POST /api/tracking/lookup)`
- **Labels:** `backend`, `feature`
- **Assignee:** `Dev A`
- **Nhánh Git:** `feat/issue-08-tracking-lookup`
- **Phụ thuộc:** Issue #6 đã merge.

### Mô tả công việc:
Triển khai endpoint Public cho khách hàng tra cứu trạng thái đơn, tiến độ chăm sóc và hóa đơn bằng số điện thoại và mã tracking.

### Hợp đồng API:
- **Tham chiếu:** `api-contract.md` mục 2 (E08), DTO `TrackingPair`, `Tracking`.
- **Route:** `POST /api/tracking/lookup`
- **Payload:** `{ "soDienThoai": "0912345678", "trackingCode": "TRK-8X2A" }`
- **Quy tắc an toàn:** Không lộ thông tin nhạy cảm nội bộ nhân viên.
- **Guard:** Xác minh lại cặp SĐT + tracking từng request; khóa/rate limit theo contract trả 429, chỉ chiếu dữ liệu Public-safe của đúng đơn.

### Tiêu chí nghiệm thu (DoD):
- [ ] Test Happy Path: Nhập đúng SĐT và mã tracking trả về 200 kèm đầy đủ thông tin thú cưng và trạng thái.
- [ ] Test Sad Path: Nhập sai mã tracking hoặc sai số điện thoại $\rightarrow$ trả về 404 `TRACKING_NOT_FOUND`.

---

## Issue #9: [Care] API Tiếp Nhận Thú Cưng Check-in Tại Quầy (E15)

- **Tiêu đề:** `[BE-Care] API Check-in tiếp nhận thú cưng tại quầy (E15 POST /api/internal/booking-pets/{id}/check-in)`
- **Labels:** `backend`, `feature`
- **Assignee:** `Dev B`
- **Nhánh Git:** `feat/issue-09-care-checkin`
- **Phụ thuộc:** Issue #3, #7 đã merge.

### Mô tả công việc:
Khi khách mang thú cưng tới cửa hàng, nhân viên cân thực tế, chốt size theo bảng cam kết lúc đặt, phân công kỹ thuật viên chăm sóc và chuyển trạng thái tiếp nhận.

### Hợp đồng API:
- **Tham chiếu:** `api-contract.md` mục 2 (E15), DTO `Checkin`.
- **Route:** `POST /api/internal/booking-pets/{id}/check-in`
- **Body/guard:** `multipart/form-data` gồm `payload` JSON `Checkin` và file `anhTiepNhan` bắt buộc; kiểm tra `CHECKIN_PERFORM`, assignment có ca `APPROVED`, chưa invoice hiệu lực, xác nhận khách và giấy hợp lệ khi policy snapshot yêu cầu. Assignment không hợp lệ trả 409 `ASSIGNMENT_UNAVAILABLE`.
- **Quy tắc chuyển trạng thái:**
  - Đơn đặt lịch phải đang ở trạng thái `CONFIRMED` hoặc `IN_PROGRESS`.
  - Thú cưng (`chi_tiet_don`) chuyển từ `NOT_ARRIVED` $\rightarrow$ `WAITING`.
  - Đơn đặt lịch (`don_dat_lich`) chuyển sang `IN_PROGRESS` (khi có thú cưng đầu tiên check-in).
  - Map request `canNangThucTe` vào `chi_tiet_don.can_nang_chot`, lưu `size_thuc_te`, `size_chot`, thỏa thuận giá và `nhan_vien_id`; cập nhật cân hiện tại ở hồ sơ pet.

### Tiêu chí nghiệm thu (DoD):
- [ ] Test Happy Path: Check-in hợp lệ chuyển trạng thái pet sang `WAITING`, đơn sang `IN_PROGRESS`.
- [ ] Test Sad Path: Cố tình check-in khi đơn còn ở `PENDING` (chưa duyệt) $\rightarrow$ trả về 409 `INVALID_TRANSITION`.

---

## Issue #10: [Care] API Cập Nhật Tiến Độ Chăm Sóc & Ghi Nhật Ký Ảnh (E17, E18)

- **Tiêu đề:** `[BE-Care] API Cập nhật tiến độ chăm sóc thú cưng và tải ảnh nhật ký (E17, E18)`
- **Labels:** `backend`, `feature`
- **Assignee:** `Dev B`
- **Nhánh Git:** `feat/issue-10-care-actions`
- **Phụ thuộc:** Issue #9 đã merge.

### Mô tả công việc:
Kỹ thuật viên cập nhật vòng đời chăm sóc của thú cưng và đăng ảnh kết quả sau khi hoàn thành dịch vụ.

### Hợp đồng API:
- **Tham chiếu:** `api-contract.md` mục 2 (E17, E18), DTO `CareAction`, `CareLogCreate`.
- **Routes:**
  - `POST /api/internal/booking-pets/{id}/care-actions` (E17): Nhận hành động `START`, `PAUSE`, `RESUME`, `READY`.
  - `POST /api/internal/booking-pets/{id}/care-logs` (E18): `multipart/form-data` gồm `payload` JSON `CareLogCreate` và file `anh` tùy chọn, phải có nội dung hoặc ảnh; không nhận URL/object path tùy ý.
- **Guard:** E17 cần `CARE_UPDATE`, E18 cần `PHOTO_UPLOAD`, đúng assignment và ca `APPROVED` (Admin chỉ override actor). E17 READY nhận file `anhThanhPham` bắt buộc, trừ ngoại lệ ảnh được Admin duyệt hợp lệ; START kế thừa ảnh tiếp nhận, PAUSE cần lý do.
- **Quy tắc trạng thái:**
  - `WAITING` $\rightarrow$ `START` $\rightarrow$ `IN_PROGRESS`.
  - `IN_PROGRESS` $\rightarrow$ `PAUSE` $\rightarrow$ `PAUSED`.
  - `PAUSED` $\rightarrow$ `RESUME` $\rightarrow$ `IN_PROGRESS` (Cấm `PAUSED` nhảy thẳng sang `READY`).
  - `IN_PROGRESS` $\rightarrow$ `READY`: Chỉ khi phần việc hoàn tất và có ảnh/ngoại lệ hợp lệ; lưu `ready_at`. Booking cha vẫn `IN_PROGRESS`, không thêm trạng thái booking `READY`.
  - **Phạm vi đã chốt 03/11/2026:** Sprint 1 chỉ nhận dịch vụ đơn; booking combo cùng E61 triển khai Sprint 2, giữ 15 operations. Catalog vẫn trả đúng DTO, nhưng không nhận booking combo trong Sprint 1; không giả hoàn tất thành phần bằng READY.

### Tiêu chí nghiệm thu (DoD):
- [ ] Test E17: Chuyển tuần tự `WAITING` $\rightarrow$ `IN_PROGRESS` $\rightarrow$ `READY` thành công với ảnh và phần việc hoàn tất; thiếu ảnh/ngoại lệ thì bị chặn.
- [ ] Test E17 lỗi: Cố tình chuyển từ `PAUSED` sang `READY` mà không qua `RESUME` $\rightarrow$ trả 409 `INVALID_TRANSITION`.
- [ ] Test E18: Tạo nhật ký chăm sóc thành công lưu vào bảng `nhat_ky_cham_soc`.

---

## Issue #11: [Billing] API Lập Hóa Đơn, Xem Chi Tiết & Thu Tiền (E23, E24, E26)

- **Tiêu đề:** `[BE-Billing] API Lập hóa đơn snapshot bất biến, xem và xác nhận thu đủ tiền (E23, E24, E26)`
- **Labels:** `backend`, `feature`
- **Assignee:** `Dev C`
- **Nhánh Git:** `feat/issue-11-invoice-checkout`
- **Phụ thuộc:** Issue #10 đã merge.
- **Phê duyệt yêu cầu:** Tối thiểu 2 thành viên khác Approve (Issue tài chính quan trọng).

### Mô tả công việc:
Lập hóa đơn chốt tiền cho các thú cưng đã chăm sóc xong (`READY`), xem hóa đơn tại quầy và xác nhận thu đủ tiền.

### Hợp đồng API:
- **Tham chiếu:** `api-contract.md` mục 2 (`E23`, `E24`, `E26`), DTO `Invoice`, `Collect`.
- **Routes:**
  - `POST /api/internal/bookings/{id}/invoices` (E23): Lập hóa đơn trạng thái `UNPAID`.
  - `GET /api/internal/invoices/{id}` (E24): Xem thông tin hóa đơn và các dòng chi tiết.
  - `POST /api/internal/invoices/{id}/checkout` (E26): Thu tiền mặt hoặc chuyển khoản chuyển `PAID`.
- **Quy tắc tài chính bất biến:**
  - Mỗi đơn chỉ có tối đa 1 hóa đơn hiệu lực (`trang_thai <> 'REVOKED'`).
  - Hóa đơn đã lập chụp lại toàn bộ đơn giá snapshot vào `ct_hoa_don`.
  - Thu tiền phải đủ 100% số tiền (`soTienThucNhan - soTienDaTraLai == conPhaiTra`). Không chấp nhận trả thiếu.

### Tiêu chí nghiệm thu (DoD):
- [ ] Test E23: Lập hóa đơn thành công khi thú cưng đã `READY`, tính đúng tổng tiền theo size chốt.
- [ ] Test E24: Xem lại hóa đơn trả đúng thông tin các dòng dịch vụ.
- [ ] Test E26 Happy Path: Xác nhận thu đủ tiền $\rightarrow$ hóa đơn chuyển sang `PAID`, lưu `paid_at`.
- [ ] Test E26 Sad Path: Khách đưa thiếu tiền $\rightarrow$ bị chặn trả về 409 `AMOUNT_MISMATCH`.
- [ ] Test E26 Idempotent: Retry cùng `requestId` và nội dung đã commit trả 200 kết quả cũ, không tạo thêm giao dịch thu; cùng key khác nội dung trả 409 `IDEMPOTENCY_CONFLICT`. Lệnh thu mới trên invoice `PAID` bị chặn 409 `INVALID_TRANSITION`.

---

## Issue #12: [Care] API Bàn Giao Thú Cưng Cho Khách (E19)

- **Tiêu đề:** `[BE-Care] API Bàn giao thú cưng cho khách sau khi đã thanh toán (E19 POST /api/internal/booking-pets/{id}/handover)`
- **Labels:** `backend`, `feature`
- **Assignee:** `Dev B`
- **Nhánh Git:** `feat/issue-12-pet-handover`
- **Phụ thuộc:** Issue #11 đã merge.

### Mô tả công việc:
Thực hiện bàn giao thú cưng ra về sau khi khách đã hoàn tất nghĩa vụ thanh toán hóa đơn.

### Hợp đồng API:
- **Tham chiếu:** `api-contract.md` mục 2 (E19), DTO `Handover`.
- **Route:** `POST /api/internal/booking-pets/{id}/handover`
- **Quy tắc nghiệp vụ cốt lõi:**
  - **Hóa đơn của đơn đặt lịch bắt buộc phải mang trạng thái `PAID`** mới được bàn giao.
  - Sau khi bàn giao:
    - Thú cưng (`chi_tiet_don`) chuyển trạng thái sang `DONE`, lưu `ban_giao_at`.
    - Khi toàn bộ thú cưng trong đơn đã `DONE`: Đơn đặt lịch (`don_dat_lich`) chuyển trạng thái sang `COMPLETED`.

### Tiêu chí nghiệm thu (DoD):
- [ ] Test Happy Path: Bàn giao khi hóa đơn đã `PAID` $\rightarrow$ pet chuyển `DONE`, đơn chuyển `COMPLETED`.
- [ ] Test Multi-pet: Bàn giao pet đầu sau PAID vẫn giữ đơn `IN_PROGRESS`; chỉ pet cuối được bàn giao mới chuyển đơn `COMPLETED` (M2-04).
- [ ] Test Sad Path: Cố tình bàn giao khi hóa đơn chưa thanh toán (`UNPAID`) $\rightarrow$ bị chặn trả về 409 `INVALID_TRANSITION`.

---

## Issue #13: [Integration] Kiểm Thử Tích Hợp Tự Động Toàn Bộ Luồng Spa E2E

- **Tiêu đề:** `[BE-Test] Kiểm thử tích hợp tự động (E2E Integration Test) luồng Spa khép kín`
- **Labels:** `backend`, `feature`
- **Assignee:** `Tech Lead` *(Dev A, B, C hỗ trợ kiểm tra)*
- **Nhánh Git:** `test/issue-13-e2e-spa-flow`
- **Phụ thuộc:** Toàn bộ Issue #1 đến #12 đã merge.

### Mô tả công việc:
Viết kịch bản test tích hợp tự động hoàn chỉnh chạy từ đầu đến cuối trên `WebApplicationFactory<Program>`:
1. Đăng nhập nhân viên lấy token JWT (`E01`).
2. Xem danh mục dịch vụ (`E02`).
3. Kiểm tra công suất ca Spa (`E03`).
4. Khách đặt lịch mới Spa tạo đơn `PENDING` và nhận mã tracking (`E04`).
5. Tra cứu đơn bằng mã tracking (`E08`).
6. Lễ tân xem danh sách đơn (`E10a`) và bấm Duyệt đơn sang `CONFIRMED` (`E11`).
7. Khách mang thú cưng tới quầy, lễ tân check-in chốt size cam kết, pet sang `WAITING`, đơn sang `IN_PROGRESS` (`E15`).
8. Kỹ thuật viên bắt đầu chăm sóc `START` $\rightarrow$ `IN_PROGRESS` (`E17`).
9. Kỹ thuật viên hoàn tất chăm sóc tải ảnh thành phẩm $\rightarrow$ `READY` (`E17`, `E18`).
10. Lập hóa đơn snapshot bất biến (`E23`).
11. Thu ngân xem hóa đơn (`E24`) và xác nhận thu đủ tiền chuyển `PAID` (`E26`).
12. Nhân viên bàn giao thú cưng chuyển `DONE`, đơn hoàn tất `COMPLETED` (`E19`).

### Tiêu chí nghiệm thu (DoD):
- [ ] File test `SpaE2EFlowTests.cs` trong `backend/tests/PetSpa.Tests/` chạy pass 100%.
- [ ] Kiểm tra toàn bộ các mốc trạng thái dữ liệu trong database sau khi luồng kết thúc đều ở trạng thái cuối cùng hợp lệ (`COMPLETED`, `DONE`, `PAID`).
