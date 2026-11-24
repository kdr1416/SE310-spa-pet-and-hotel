# Issue #26 — Sprint 1 Allowlist Hybrid Routing

Ngày: 2026-11-23. Trạng thái: **APPROVED — Đã duyệt theo Báo cáo Kiểm toán Hợp nhất EGM (Consolidated Review Report); Sẵn sàng lập Implementation Plan Phase 1**.

## 1. Mục tiêu và nguồn quyết định

Gỡ G6 để Vue gọi 15 operations Sprint 1 qua Axios `httpClient` khi `VITE_USE_MOCK_API=false`, tiếp tục hỗ trợ 9 action UI trong Task 13 và giữ mock cho action Sprint 2/3 chưa nối. Sửa E01 Session mapping; không đổi backend contract hoặc bổ sung cờ môi trường.

Nguồn: `docs/plans/sprint-2/sprint-2-plan.md`, Task 13; `docs/design/api-contract.md`, mục 1–5; `docs/requirements/scope-and-decisions.md`, D03/D09/D12/D13/D15, các amendment M2 và quy tắc tách thu tiền/bàn giao; `AGENTS.md`, `.codex/instructions.md`, `.github/CONTRIBUTING.md`.

Thiết kế đã được kiểm toán hợp nhất qua 4 persona EGM (`egm-zoom-out`, `egm-implementation-readiness`, `egm-design`, `egm-codebase-details`) và được người dùng phê duyệt theo chiến lược phân tách 2 Phase: **Phase 1 (Core Transport & Adapter trong Issue #26)** và **Phase 2 (Follow-up UI Form Integration)**.

**Xác nhận từ người dùng:** Sprint 1 đã hoàn thành; các thiếu sót tích hợp phát hiện ở đây được tách bạch và giải quyết theo 2 Phase rõ ràng. Không mở lại hoặc phủ nhận nghiệm thu Sprint 1.

**Đồng bộ kế hoạch:** theo yêu cầu cập nhật plan chính và từng Dev, ownership/checkpoint đã được ghi tại `docs/plans/sprint-2/sprint-2-plan.md` và `dev-a-plan.md`, `dev-b-plan.md`, `dev-c-plan.md`. E14 được lập kế hoạch như endpoint hỗ trợ bổ sung tường minh trong follow-up #26 sau #18; không còn giới hạn 15 operations cho riêng checkpoint này. Các endpoint Sprint 2 khác vẫn ngoài allowlist. Nguồn slot và policy mock actor chưa được chốt giải pháp.

## 2. Bằng chứng hiện trạng và điều kiện tiên quyết

Khảo sát ban đầu tại local HEAD `63b7428`, nhánh `feat/issue-14-sprint2-foundation`. Đã kiểm tra lại sau `git checkout develop` và `git pull origin develop` theo yêu cầu người dùng: HEAD `067aea1`, đồng bộ `origin/develop` (ahead/behind `0/0`). Tree của commit mới không khác tree nhánh foundation đã khảo sát (`git diff feat/issue-14-sprint2-foundation HEAD` rỗng), nên các phát hiện về code bên dưới vẫn áp dụng.

- `operationClient.js` chặn toàn bộ live mode trước khi dispatch. Actor private chỉ lấy từ `petspa_mock_session`.
- `auth.js` live login đọc `res.data.user/token`; backend Session dùng `nhanVien/accessToken`.
- `httpClient.js` đã có duy nhất `IS_MOCK_ENABLED = import.meta.env.VITE_USE_MOCK_API !== "false"`, base URL mặc định `/api`, request interceptor Bearer và response interceptor unwrap envelope một lần.
- Axios mặc định JSON, chưa xử lý riêng FormData. Error interceptor làm mất `errorCode`, `traceId` và có thể làm mất field errors của envelope chuẩn.
- `getWorkspace` UI cần `{ bookings, users, requests }`; E10a trả PageBooking. Booking/BookingPet/Invoice cần projection sang các alias UI.
- Các route của 15 operations có trong controller local; sự tồn tại của route chưa chứng minh backend đang chạy hoặc tương thích end-to-end.
- Backend CORS cho localhost/127.0.0.1:5173 đã có. Vite chưa có proxy `/api`: live dev cần `VITE_API_BASE_URL` trỏ đúng API có hậu tố `/api`, hoặc reverse proxy ngoài task. Không thêm biến mới, không dùng URL `/api/...` trong adapter khi base URL đã chứa `/api`.
- Frontend baseline đã chạy `npm --prefix frontend test -- --run`: **12 files, 56 tests passed**. Đây chưa phải bằng chứng Task 13 đã hoàn tất.
- Không tìm thấy `CLAUDE.md` qua tìm kiếm tên file trong workspace hoặc danh sách tracked files. Không tự tạo nội dung thay thế.
- `codebase-memory` đã được gọi `list_projects`, `index_status`, `check_index_coverage`; workspace này chưa được index. Dùng CodeGraph có sẵn và đọc nguồn bổ sung; không tuyên bố đã xác minh bằng codebase-memory graph.
- **Dependency #14 đã có trên develop:** pull cập nhật `2c36971` → `067aea1`, commit `[Sprint 2] Foundation dùng chung: DTOs, capacity, expiry cọc và settlement (#36)`. Người dùng xác nhận Issue #14 đã merge; source foundation đã được kiểm chứng trên develop sau pull. `gh` không có trong PATH; chưa kiểm tra độc lập trạng thái review/CI của PR #36.
- Có hai file untracked của người dùng trong `docs/plans/2026-11-21-sprint-1-completion*.md`; không sửa, stage hoặc commit chúng.

Dependency source #14 đã đáp ứng. Trước implementation, sau khi duyệt thiết kế, tạo nhánh `feat/issue-26-frontend-sprint1-hybrid-wiring` từ develop mới nhất; giữ nguyên công việc hiện có. Các khoảng trống UI ở mục 8 vẫn cần chốt, vì foundation không thay đổi frontend hoặc contract liên quan.

## 3. Approaches

**Chọn theo yêu cầu người dùng:** allowlist tập trung trong `sprint1Adapter.js`; `operationClient.js` chọn live/mock bằng cờ có sẵn. Adapter chịu trách nhiệm đổi request/response; transport ở `httpClient`.

Các hướng không chọn: sửa từng component để gọi Axios trực tiếp sẽ phân tán routing; đổi toàn bộ app sang live bỏ mock không đáp ứng Sprint 2/3; catch lỗi live rồi mock làm sai nguồn dữ liệu và có thể báo thu tiền thành công giả. Không cần framework, dependency, global cache hoặc mode config mới.

## 4. Phạm vi và giới hạn (Chiến lược 2 Phase)

### Phase 1 (Nhiệm vụ cốt lõi của Issue #26 — Core Transport & Stateless Bridge):
- **5 Production Files:**
  1. `frontend/src/stores/auth.js`: Chuẩn hóa DTO Session backend E01 (`accessToken`, `nhanVien` $\rightarrow$ `user`), khởi tạo demo session song song trong `sessionStorage` cho fallback.
  2. `frontend/src/services/api/httpClient.js`: Bỏ JSON Content-Type cho FormData, bảo toàn đầy đủ `errorCode`, `traceId`, `errors` trong error interceptor (M-01).
  3. `frontend/src/services/api/operationClient.js`: Gỡ bỏ chốt chặn G6, triển khai Allowlist Hybrid Routing, chống thăng giáng session.
  4. `frontend/src/services/api/sprint1Adapter.js`: Ánh xạ 15 operations Sprint 1, cấp fallback `requestId = crypto.randomUUID()` khi thiếu (C-03), nhân bản slot cho service selection (M-03), chuẩn hóa SĐT 10 chữ số (M-04), áp dụng `kichThuocTrang=100` cho workspace (M-02).
  5. `frontend/src/utils/permissions.js`: Kiểm tra trực tiếp `user.quyen` khi chạy live mode, chống bị giáng về `BASE_STAFF` do thiếu `workGroup` (C-04).
- **1 Test File:** `frontend/tests/unit/sprint1HybridRouting.spec.js` (Viết trước theo TDD, kiểm tra toàn bộ 15 operations, mapping Session, fallback mock và bảo toàn lỗi).
- **Mục tiêu:** Hoàn thành trong 1–2 ngày, bảo vệ 100% bộ 56 tests Vitest hiện có trên `develop`.

### Phase 2 (Follow-up UI Form Integration Issue):
- Bổ sung trường input cho `BookingDetailDialog.vue`: chọn file ảnh `anhTiepNhan`, textarea `ghiChuSucKhoe`, radio `quyetDinhGia`, checkbox `xacNhanKhach` (C-05).
- Thêm nút/dialog "Bàn giao thú cưng" gọi `E19` khi hóa đơn đã `PAID` (I-01).
- Bổ sung nhập thực thu `soTienThucNhan` và trả thừa `soTienDaTraLai` khi thanh toán tiền mặt (I-02).
- Nối danh sách ứng viên kỹ thuật viên `E14` sau khi Dev B hoàn thành và merge Issue #18 (I-03).

---

## 5. Quy tắc dispatch

| Cờ / action | Đích | Khi lỗi |
|---|---|---|
| Mock bật (mặc định hoặc giá trị khác chuỗi `false`) | Luồng mock hiện tại (`mockCall`) | Giữ hành vi mock |
| Mock tắt, action thuộc allowlist (15 operations) | Adapter → httpClient (Live Backend) | Trả lỗi backend nguyên vẹn; **tuyệt đối không gọi mock** |
| Mock tắt, action ngoài allowlist (Sprint 2/3) | mockCall (Demo context) | Dùng seed demo actor từ `sessionStorage` (`Admin -> 1`, `Staff -> 2`); từ chối thao tác mock trên thực thể live |

**Quy tắc an toàn dữ liệu:**
1. Khi `VITE_USE_MOCK_API=false`, `auth.js` tự động khởi tạo song song một phiên demo trong `sessionStorage` (`{ id: user.vaiTro === 'ADMIN' ? 1 : 2 }`) để các action ngoài allowlist gọi `mockCall` không bị văng lỗi đăng nhập (C-01).
2. Khi một action thuộc allowlist gặp lỗi HTTP (4xx, 5xx), Adapter trả đúng mã lỗi và message từ backend ra UI; **nghiêm cấm âm thầm fallback sang mock (No Silent Fallback)**.
3. Event `petspa-workspace-updated` chỉ phát một lần sau mutation nội bộ thành công; các component lắng nghe cần bọc `debounce(load, 300)` để chống hiện tượng Thundering Herd (I-05).

## 6. Allowlist và HTTP contract

URL dưới đây tương đối với base `/api`. E01 nằm trong auth store; 14 operations còn lại nằm trong adapter. Số tên action lớn hơn số operations do alias và `reviewBooking` phân nhánh.

| Operation | Action canonical / alias UI | HTTP và body |
|---|---|---|
| E01 | auth.login | POST `/auth/login`, Login JSON |
| E02 | getCatalog / catalog | GET `/catalog` |
| E03 | checkAvailability | POST `/availability`, AvailabilityQuery JSON |
| E04 | createBooking | POST `/bookings`, multipart payload BookingCreate + files |
| E08 | lookupTracking / lookupBooking | POST `/tracking/lookup`, TrackingPair JSON |
| E10a | listBookings / getWorkspace | GET `/internal/bookings`, BookingQuery params |
| E11 | confirmBooking / reviewBooking CONFIRMED | POST `/internal/bookings/{bookingId}/confirm`, Command |
| E12 | cancelBooking / reviewBooking CANCELLED | POST `/internal/bookings/{bookingId}/cancel`, Cancel |
| E15 | checkInPet / checkIn | POST `/internal/booking-pets/{petDetailId}/check-in`, multipart Checkin + anhTiepNhan |
| E17 | performCareAction / careUpdate | POST `/internal/booking-pets/{petDetailId}/care-actions`, multipart CareAction + anhThanhPham nếu cần |
| E18 | addCareLog | POST `/internal/booking-pets/{petDetailId}/care-logs`, multipart CareLogCreate + anh tùy chọn |
| E19 | handoverPet | POST `/internal/booking-pets/{petDetailId}/handover`, Handover JSON |
| E23 | createInvoice | POST `/internal/bookings/{bookingId}/invoices`, Command JSON |
| E24 | getInvoice | GET `/internal/invoices/{invoiceId}` |
| E26 | checkoutInvoice / confirmPayment | POST `/internal/invoices/{invoiceId}/checkout`, Collect JSON |

### Request mapping

- Chỉ serialize trường contract; không gửi nguyên object UI, actor, giá ước tính hay state client. Field alias chỉ tồn tại ở frontend.
- **Xử lý `requestId` (C-03):** Nếu caller chưa truyền `requestId`, `sprint1Adapter` tự động cấp fallback: `payload.requestId = payload.requestId || crypto.randomUUID()` trước khi gửi HTTP, đảm bảo không bị lỗi 400 Bad Request từ backend model validation. Callers có thể truyền key ổn định để phục vụ retry idempotency.
- **Booking UI (E04 & M-03):** customer.name/phone/email → lienHe.hoTen/soDienThoai/email; checkout → ngayTra; pets → thuCung theo PetSelection; serviceId/comboId → ServiceSelection XOR. **Không coi shiftId (maCa) là maKhungGio**; phải có slot ID thật. Sao chép `maKhungGio` từ cấp đơn xuống từng `ServiceSelection.maKhungGio` của thú cưng theo đúng yêu cầu contract E04 (M-03). Giữ vaccinationFileKey khớp multipart file; không serialize File vào JSON.
- **Tracking (E08 & M-04):** `soDienThoai ?? phone`; chuẩn hóa loại bỏ khoảng trắng, dấu gạch thành 10 chữ số ASCII hợp lệ (M-04); gửi lại cả pair mỗi request, không dùng bookingId thay xác minh.
- **reviewBooking (E11, E12):** chỉ CONFIRMED/CANCELLED; quyết định khác phải lỗi trước HTTP. Cancel dùng `loaiHuy ?? STAFF_REJECTED`, `lyDo ?? reason`; không bịa lý do thiếu.
- **Check-in alias (E15):** detailId chọn path thay vì booking id; weight → canNangThucTe, employeeId → maNhanVien. coat cần mã hợp lệ theo danh mục `FUR_CONDITIONS`, không gửi nhãn hiển thị thành code; thiếu ảnh/sức khỏe/đồng thuận trả lỗi client-side ở Phase 1 (bổ sung form đầy đủ ở Phase 2).
- **Care alias `next` (E17):** IN_PROGRESS cần state trước đó để phân biệt START với RESUME; PAUSED → PAUSE, READY → READY. Không mặc định IN_PROGRESS luôn START. note → ghiChu; photo → anhThanhPham. E18 là command riêng, không tự ghi nhật ký phụ bằng requestId của E17.
- **E26 & E23 (Tách bạch hóa đơn và thanh toán):** Nghiêm cấm auto-chaining `E23 -> E26` trong adapter. E26 cần `invoiceId` thật, `phuongThuc`, `soTienThucNhan`, `soTienDaTraLai`; không xem payload.id của alias UI là invoiceId. Thiếu `invoiceId` phải từ chối rõ ràng yêu cầu lập hóa đơn trước.
- **Handover (E19):** Phải có xác nhận thực nhận rõ ràng; E26 chỉ chuyển hóa đơn sang `PAID`, không gọi E19 ngầm hoặc sửa các pet thành DONE.

### Response projection

Giữ nguyên trường chuẩn contract và thêm alias cần thiết, không thay đổi server schema. Canonical actions trả envelope với DTO chuẩn; UI aliases có projection tương thích nơi tiêu thụ.

Return shape luôn là API envelope `{ success, data, message, ...metadata }`, không phải AxiosResponse hoặc DTO trần. Interceptor chỉ bỏ lớp AxiosResponse, không bỏ lớp API envelope. Ví dụ `listBookings` trả `data: { items, trang, kichThuocTrang, tongSo }`; `getWorkspace` trả `data: { bookings, users, requests, trang, kichThuocTrang, tongSo }` sau projection. Adapter không bọc thêm một tầng `data` và bảo toàn envelope metadata.

- Catalog: dichVu/combo/loaiPhong/khoangSize → services/combos/rooms và ID/active aliases. Bảo toàn bangGia theo size; không quay lại hệ số nhân giá của mock cũ. Ở Phase 1, trả về mảng ca/availability tương thích UI; khi đặt lịch cần nguồn slot thật đã ánh xạ.
- Booking: maDon → id; lienHe → khachHang fields UI; thuCung → chiTietDonList; maChiTietDon → detail id; trangThai pet → trangThaiChamSoc; nhatKy/anhUrl → nhatKyChamSoc/photo; Invoice alias lấy từ Invoice thật. Ngày hiển thị chuyển Asia/Ho_Chi_Minh, không slice UTC nếu làm lệch ngày.
- **listBookings & getWorkspace (M-02):** listBookings giữ PageBooking; getWorkspace tạo `{ bookings, users, requests }` cùng metadata pagination. Adapter truyền `kichThuocTrang=100` để không làm mất đơn hoạt động khi chưa có phân trang UI. Mảng users rỗng ở Phase 1 cho tới khi E14 merge ở Sprint 2.
- Không suy ngược thông tin không có trong projection (assignment employee ID, maKhungGio, cân khai báo) bằng tên nhân viên hoặc số ID mock.

### Session và transport

Auth live dùng `accessToken ?? token`, `nhanVien ?? user`; bảo toàn contract fields và bổ sung id/name/role/permissions/isActive theo Task 13, dùng nullish coalescing để giữ `dangHoatDong=false`. Legacy fallback chỉ ở frontend; không nới DTO server.

- **Khởi tạo Demo Context song song (C-01):** Khi đăng nhập live thành công, `auth.js` lưu `petspa_auth_token` và `petspa_auth_user` vào `localStorage`, đồng thời khởi tạo một session demo trong `sessionStorage.setItem('petspa_mock_session', JSON.stringify({ id: user.vaiTro === 'ADMIN' ? 1 : 2 }))` để phục vụ riêng cho các action ngoài allowlist fallback về `mockCall`.
- **Phân quyền trực tiếp từ `user.quyen` (C-04):** `frontend/src/utils/permissions.js` kiểm tra trực tiếp mảng `user.quyen` trong live mode để cấp capability tương ứng (`CHECKIN_PERFORM`, `CARE_UPDATE`, `INVOICE_CREATE`, `BOOKING_APPROVE`), chống bị giáng về `BASE_STAFF` do thiếu trường `workGroup`.
- **httpClient (M-01):** Tiếp tục unwrap envelope đúng một lần. Với FormData bỏ JSON Content-Type để trình duyệt/Axios tạo multipart boundary; JSON requests giữ hành vi hiện có. Giữ Bearer interceptor. Error interceptor bắt buộc bảo toàn nguyên vẹn `errorCode`, `traceId`, và mảng `errors` từ ApiResponse backend, không nuốt mã lỗi (M-01). Network/HTTP lỗi không fallback mock; không retry tự động mutation.

## 7. Tiêu chí chấp nhận và chiến lược TDD

File test mới dùng jsdom, Pinia isolation, storage reset và `vi.stubEnv` trước dynamic import/resetModules vì cờ được đọc lúc import. Mock transport/repository để kiểm tra dispatch; có nhóm test chạy Axios interceptor thật với custom adapter để kiểm tra wire serialization mà không cần dependency mới.

1. Session chuẩn, legacy fallback, inactive=false, persist/init/logout và token gắn Bearer đúng.
2. Bảng test đủ 15 operations, toàn bộ 9 action UI và canonical aliases: đúng method/path/query, ID, JSON fields và multipart keys/file identity. Không mock adapter chính rồi chỉ kiểm tra wrapper.
3. Cả mock bật/tắt và default khi env absent; private live không cần mock session.
4. Outside-allowlist dispatch mock theo actor policy đã chốt; không để arbitrary backend employee ID vô tình nhận quyền mock seed.
5. Thiếu/nhầm ID, decision sai, payload thiếu, START/RESUME, false boolean, pagination và projection fixture dùng shape backend thật.
6. requestId giữ nguyên qua retry do caller; response lost/error không dẫn sang mock. Các lỗi 400/401/403/404/409/429/503 và network không phát mutation event.
7. Event/storage signal đúng một lần cho internal mutation success; read/public/error không phát.
8. Build và regression suite pass; nếu mở rộng UI, thêm component tests và live smoke trên môi trường test. Test routing giả lập không chứng minh đã nối backend thật.

Khi được duyệt implementation: **viết test trước → chạy FAIL vì G6/Session/mapping mong muốn (không phải import thiếu/fixture hỏng) → viết adapter nhỏ nhất → chạy PASS → refactor**. Có thể red bằng public callOperation hiện có trước khi import adapter mới.

Commands:

```powershell
npm --prefix frontend test -- --run tests/unit/sprint1HybridRouting.spec.js
npm --prefix frontend test -- --run
npm --prefix frontend run build
```

Trước PR chạy thêm backend build/test theo CONTRIBUTING và CI; báo rõ nếu thiếu PostgreSQL test/config nên không chạy được. Không ghi pass khi chỉ test frontend. Không dùng fixture credentials thật.

## 8. Bảng giải quyết khoảng trống (Đã chốt & phân tách theo 2 Phase)

Toàn bộ các khoảng trống kiến trúc đã được kiểm toán qua EGM Review và thống nhất hướng xử lý dứt khoát:

| Khoảng trống | Phân loại | Giải pháp chốt kỹ thuật | Giai đoạn thực hiện |
|---|---|---|---|
| **C-01: Mock Actor Fallback** | Blocker | Khi login live, `auth.js` ghi session demo (`Admin -> 1`, `Staff -> 2`) vào `sessionStorage` để phục vụ fallback; cấm thao tác mock trên thực thể live. | **Phase 1 (#26)** |
| **C-02: Nguồn slot đặt lịch** | Blocker | Adapter trả ca hiển thị tương thích cho UI, yêu cầu caller cung cấp slot ID thật hoặc ánh xạ xác minh; nếu thiếu trả `MISSING_SLOT_MAPPING`, không đoán ID. | **Phase 1 (#26)** |
| **C-03: Thiếu `requestId`** | Blocker | `sprint1Adapter` tự sinh fallback: `payload.requestId = payload.requestId \|\| crypto.randomUUID()` trước khi gửi HTTP. | **Phase 1 (#26)** |
| **C-04: Phân quyền Staff** | Blocker | `permissions.js` kiểm tra trực tiếp mảng `user.quyen` khi chạy live, loại bỏ phụ thuộc vào `workGroup` đã bị xóa bỏ ở backend. | **Phase 1 (#26)** |
| **C-05: Input Check-in E15** | Blocker | Phase 1: Adapter format đúng multipart và validate client-side.<br>Phase 2: Bổ sung input file ảnh, sức khỏe, giá vào `BookingDetailDialog.vue`. | Phase 1 (Adapter) / **Phase 2 (Form UI)** |
| **I-01: Bàn giao thú cưng E19** | Major | Không gộp E19 vào E26; bổ sung nút/hành động "Bàn giao thú cưng" riêng trên UI sau khi hóa đơn `PAID`. | **Phase 2 (Form UI)** |
| **I-02: Dữ liệu thanh toán E26** | Major | E26 bắt buộc truyền `invoiceId` thật; bổ sung thu nhận `soTienThucNhan` và `soTienDaTraLai` trên form thanh toán. | **Phase 2 (Form UI)** |
| **I-03: Nhân viên phân công E14** | Major | Giữ E14 ngoài #26; kết nối danh sách kỹ thuật viên vào form check-in sau khi Dev B hoàn thành và merge Issue #18. | **Sprint 2 (Sau #18)** |
| **I-05: Thundering Herd** | Major | Bọc `debounce(load, 300)` cho các component lắng nghe sự kiện `petspa-workspace-updated`. | **Phase 1 (#26)** |
| **M-01 đến M-04: Envelope & Hygiene** | Minor | Giữ nguyên `errorCode`/`traceId` trong `httpClient`, set `kichThuocTrang=100`, nhân bản slot cho con, chuẩn hóa SĐT 10 số. | **Phase 1 (#26)** |

---

## 9. GitHub và handoff

- Bản thiết kế đã được kiểm toán hợp nhất (EGM Consolidated Review) và chính thức được người dùng phê duyệt sang trạng thái **APPROVED**.
- **Bước tiếp theo:**
  1. Tạo nhánh `feat/issue-26-frontend-sprint1-hybrid-wiring` từ `develop` mới nhất.
  2. Lập Implementation Plan chi tiết theo quy trình TDD cho **Phase 1** (Core Transport & Adapter: `auth.js`, `httpClient.js`, `operationClient.js`, `sprint1Adapter.js`, `permissions.js`).
  3. Viết test `frontend/tests/unit/sprint1HybridRouting.spec.js` $\rightarrow$ xác nhận RED $\rightarrow$ hiện thực mã nguồn $\rightarrow$ xác nhận GREEN 100% (bao gồm toàn bộ 56 tests hiện có).
  4. Mở PR vào `develop`, kèm đầy đủ bằng chứng kiểm thử và tài liệu liên kết `Closes #26`.
