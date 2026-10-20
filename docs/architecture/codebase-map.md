# Bản đồ codebase — PetSpa & Hotel

> Cập nhật 18/10/2026 sau đợt bổ sung booking/lịch/role. Frontend mock hoạt động; Backend chưa có. Đây là mô tả source hiện tại, không phải danh sách tính năng production đã hoàn thành.

## Cấu trúc và điểm vào

- `frontend/src/main.js`: Vue 3, Pinia, Vue Router, PrimeVue Aura với primary cam, CTA cam đậm/chữ trắng.
- `frontend/src/App.vue`: router outlet và skip link.
- `frontend/src/router/index.js`: lazy routes Customer/Staff/Admin; async auth hydration, role/capability guards và redirect route cũ.
- `frontend/src/layouts/CustomerLayout.vue`: PublicNavbar/Footer dùng chung. Menu Trang chủ / Dịch vụ / Tra cứu / Về chúng mình.
- `frontend/src/layouts/AppLayout.vue`: menu theo capability cho quầy/spa/Admin.
- `frontend/tailwind.config.js`, `src/assets/main.css`, `frontend/index.html`: MASTER chung Customer/Staff/Admin theo landing.html, nền trung tính/cam, Nunito/Plus Jakarta Sans; shared form/button/panel/table classes và reduced-motion. Teal phụ trợ cho tiến độ/selected states; không còn customer.css hay scope Customer-only.

## UI và routes

| Khu vực | Route / Source | Hành vi |
|---|---|---|
| Customer | `/`, `/services`, `/about` · views/customer | Bố cục landing mẫu, danh mục và chi tiết, thông tin liên hệ chưa xác nhận |
| Booking Customer | `/booking` · CustomerBookingView, components/booking/BookingForm.vue | Multi-pet, Spa/Hotel/combo, checkout/vaccine, giá/slot, mã và QR demo |
| Tracking | `/tracking` · CustomerTrackingView, components/tracking/BookingProgress.vue | SĐT + tracking, tiến độ từng pet, Blob photos, lịch sử của mã đã xác minh, invoice, add-on/review/complaint/refund |
| Booking nội bộ | `/staff/bookings`, `/admin/bookings` · components/booking/BookingWorkspace.vue, BookingDetailDialog.vue | List/ngày/calendar tuần, tạo đơn, pending queue, tiếp nhận/phân công, phụ phí/snapshot/thu tiền |
| Chăm sóc | `/staff/care-tasks` · StaffCareTasksView.vue | Pet được phân công, ảnh/ghi chú/đo cân nặng và transition guards |
| Tổng quan | `/admin/overview`, `/staff/overview` · OverviewWorkspace.vue, utils/dashboardSummary.js | Điểm vào sau login, KPI/lịch/việc theo quyền; Admin có ledger net và trend, Staff có lịch published của chính mình; dữ liệu qua service hiện có |
| Dashboard Staff | `/staff/dashboard` · components/schedule/ScheduleWorkspace.vue | Lịch published theo tuần, đăng ký ca và trạng thái đăng ký |
| Lịch Admin | `/admin/staff-schedule` · ScheduleWorkspace với admin prop | Đợt đăng ký, deadline, khóa/mở, duyệt/từ chối/chuyển người, chốt/công bố và browser print |
| Thống kê | `/admin/dashboard` · AdminDashboardView.vue | Số đơn theo ngày tạo; tiền thu trừ tiền hoàn trong kỳ ngày/tháng/năm |
| Finance | `/admin/finance` · AdminFinanceView.vue | Giá, invoices, complaint/refund requests, reviews chỉ Admin và phụ phí |
| Nhân viên | `/admin/staff` · AdminStaffView.vue | Tạo mock account, nhóm công việc, danh sách và trạng thái hoạt động |
| System | `/admin/settings` · AdminSettingsView.vue | Ca/giờ và slot theo ngày với điều kiện bảo toàn booking/lịch |
| Account | `/staff/settings`, `/admin/account` · views/shared/AccountSettingsView.vue | Username, hồ sơ, mock password derivation |

Các routes cũ `staff/check-in`, `staff/cashier` chuyển vào booking workspace; `staff/schedule` về dashboard; `admin/services` về finance; `admin/shifts-slots` về settings. Không còn các nút placeholder chỉ alert trên routes sử dụng.

## State, services và nguồn dữ liệu

```text
Vue views / reusable workspaces
        ├─ stores/auth.js: current user, mock/live login, capabilities
        ├─ stores/workspace.js: bookings/users/addon queue/catalog dùng chung
        ├─ stores/booking.js: lookup result/loading/error, xóa kết quả cũ khi sai
        └─ composables/useAction.js: loading/error/success theo thao tác
                   ↓
services/api/{booking,catalog,schedule,finance,staff}Service.js
                   ↓
operationClient.js (envelope, mock session, integration boundary)
                   ↓
services/mock/mockRepository.js (IndexedDB transaction)
                   ↓
services/mock/mockOperations.js (permission/validation/state transitions)
        ├─ services/mock/seed.js
        └─ utils/{permissions,bookingRules,pricing,dates,mockCredentials}.js
```

`httpClient.js` giữ Axios transport, interceptors và cấu hình `VITE_USE_MOCK_API`. Legacy mockApiService/mockData còn làm nguồn seed/reference; các luồng UI mới không ghi trực tiếp vào mảng legacy. Khi mock=false, login dùng /auth/login; các service mới báo cần thống nhất hợp đồng Backend, không âm thầm chạy mock.

IndexedDB `petspa-frontend-mock`, version 1, store `data`, key `state` chứa domain records và ảnh Blob. Booking + slot / payment / refund ghi trong cùng transaction. Session ID mock ở sessionStorage, riêng theo tab. Kho legacy của HTML mẫu/localStorage được giữ nguyên và không tự migrate/reset.

## Quy tắc chính đang có trong mock

- Slot tính theo pet gốc, không theo số đơn hoặc các dòng add-on. Request ID chống tạo đơn hai lần; từ chối chỉ xử lý PENDING một lần.
- Spa theo cân nặng, Hotel theo đêm; cắt tỉa chỉ qua combo. Hotel cần ngày trả sau nhận; mọi pet qua đêm có phòng và cùng lịch trả. Vaccine đính kèm tùy chọn cho mọi loại; policy xác minh mặc định tắt, Admin bật theo loại dịch vụ thì check-in yêu cầu nhân viên kiểm tra giấy và lưu actor/thời điểm.
- Check-in chốt giá và phân công nhân viên spa hoạt động. Cân nặng cập nhật sau đó không sửa giá đã chốt hoặc cân nặng khai báo ban đầu.
- Waiting → In Progress cần ảnh + ghi chú; Ready cần ảnh cuối. Parent READY chỉ khi tất cả chi tiết đủ điều kiện.
- Một invoice snapshot khi all pets READY; không sửa dịch vụ/giá sau snapshot. Explicit payment mới chuyển PAID/COMPLETED. Refund là ledger riêng, không vượt tiền đã thu chưa hoàn, không duyệt cùng yêu cầu hai lần.
- Review chỉ COMPLETED, một lần/đơn, không trả reviews qua getWorkspace của Staff. Add-on phải được quầy/Admin duyệt; queue hiển thị pet/dịch vụ/phí trước duyệt.
- Lookup bắt buộc phone/code, lỗi chung; quá 5 lần sai trong cửa sổ 15 phút khóa thêm 15 phút. Đây không phải rate limiter server.
- Lịch đăng ký có deadline/duplicate/capacity/overlap checks, kiểm tra lại trước publish; staff chỉ thấy published. Chỉnh giờ không làm invalid các ca đã duyệt.
- Roles trong source vẫn ADMIN | STAFF, workGroup RECEPTION/SPA còn dùng cho mock. Ngày 17/10 đã chốt Backend chỉ ADMIN/STAFF và quyền chăm sóc theo assignment; source mock chưa được đổi theo quyết định mới. Xem scope-and-decisions.md bên dưới, không mang workGroup thành quyền Backend.

DTO live/reference ở `types/api.d.ts`, enums ở `types/api.js`; DTO frontend-only ở `types/mock.d.ts`. Không tuyên bố mock DTO là schema .NET đã xác nhận.

## Kiểm tra và cách chạy

```powershell
cd frontend
npm run test        # Vitest: rules, workflows, regressions, Vue component interactions
npm run build       # Vite bundler; không phải TypeScript type-check
npm run dev         # mở URL Vite hiển thị
```

Vitest 5.0.4 tương thích Vite 6.4, Vue Test Utils 2.4.6, jsdom 26; tests mặc định Node, component tests opt-in jsdom. Persistence/Blob/route/responsive kiểm tra bằng trình duyệt thật, không giả rằng jsdom cung cấp IndexedDB.

Kết quả kiểm tra và quyết định implementation: [frontend-update-progress.md](../design/frontend-update-progress.md). Kế hoạch đã duyệt: [frontend-update-plan.md](../design/frontend-update-plan.md).

## Phạm vi chưa có và điểm tích hợp

Chưa có Backend/.NET/database schema/migration, email/OTP/PIN, cloud upload, payment ngân hàng/VietQR hay đồng bộ nhiều thiết bị. QR hiện là PETSPA_DEMO tại máy, không thanh toán. Polling Tracking 10s khi tab visible, dừng khi unmount; không phải SignalR.

IndexedDB chỉ dữ liệu theo origin/browser, có thể bị xóa theo cài đặt trình duyệt. Mock permission/password không thay bảo mật server. Giờ/lịch published được giữ nguyên; sửa lịch đã publish cần quy trình khác nếu bổ sung sau. Export hiện là browser print/PDF, không Excel.

## Dùng CodeGraph để tránh đọc lại codebase

### Bổ sung frontend/mock ngày 18/10

- `services/mock/bookingAvailability.js`: nâng dữ liệu cộng thêm (`featureVersion=2`, vẫn store/version IndexedDB cũ), cấu hình vaccine mặc định tắt, phòng mặc định 3 suất/loại nếu dữ liệu cũ chưa có số lượng, kiểm tra Hotel từng đêm và ngày nghỉ nhận/trả. Không clear booking/Blob. Kho mới có 2 booking demo; kho hiện hữu không bị chèn đơn mẫu.
- `BookingForm.vue`: loại lông riêng, upload vaccine tùy chọn, lịch tiếp nhận Spa/ngày nghỉ, suất ca chỉ tính pet Spa; Hotel-only không trừ Spa, lịch phòng 14 ngày và ngày trả chung. Mock giới hạn khoảng lưu trú tối đa 366 đêm để chặn vòng lặp không giới hạn.
- `BookingDecisionPanel.vue`: nhận/từ chối PENDING, dialog lý do/xác nhận dùng chung cho hàng chờ và cuối chi tiết. Lưu STAFF_REJECTED và nhả tài nguyên một lần; từ chối lặp cùng lý do trả kết quả cũ.
- `BookingNotifications.vue` trong AppLayout: PENDING count/list, mở đúng mã, polling 30s khi tab hiển thị; mutation có sự kiện đồng bộ trong tab và storage event giữa tab. Refresh Auth để role mới có hiệu lực; kiểm tra quyền service vẫn là chốt chính.
- `ScheduleWorkspace.vue`: cấu hình ngày nghỉ cho tuần chưa mở; chặn nhận/trả nhưng giữ ca HOTEL_CARE. Bấm ô ca mở xác nhận đăng ký; hiển thị thứ/ngày. Cơ chế period/published mock cũ vẫn còn, chưa đổi thành Backend APPROVED-only.
- `AdminStaffView.vue`/`staffService.changeRole`: ADMIN ↔ STAFF, chặn hạ quyền Admin hoạt động cuối; không đổi credential, assignment hoặc log cũ. WorkGroup chỉ còn là hạn chế legacy của mock, chưa đồng bộ toàn bộ quyền STAFF với thiết kế Backend.
- Tracking có `resendTracking`: response chung, chỉ gửi mã đơn đang mở theo email snapshot; cooldown 60s ở mock. Admin xem mail outbox tại settings; không có SMTP thật. Settings cũng cấu hình policy vaccine.
- Kiểm chứng và các UC còn thiếu xem [frontend-update-progress.md](../design/frontend-update-progress.md) và [mapping](../requirements/frontend-use-case-mapping.md). Không coi đợt UI này đã hoàn thiện đủ core hoặc đã đồng bộ schema/API/Word/Figma.

Baseline Q01–Q08/D01–D14 và T01–T09 ngày 17/10 đã chốt: [scope-and-decisions.md](../requirements/scope-and-decisions.md), [SRS 1.1](../requirements/SRS_He_Thong_Quan_Ly_Spa_Thu_Cung_AI_Readable.md), [mapping 28 mục + UC56](../requirements/frontend-use-case-mapping.md), [phân tích nghiệp vụ](../design/system-analysis.md), [thiết kế 1.0](../design/system-design.md), [API contract dự thảo](../design/api-contract.md), [traceability/lộ trình](../design/requirements-traceability.md), [kế hoạch tài liệu](../design/system-analysis-plan.md). Đây là yêu cầu/thiết kế mục tiêu, không chứng minh Backend/validation mới đã triển khai; source mock còn khác về workGroup, published, phòng, email và thanh toán/bàn giao.

Bắt đầu từ file/symbol liên quan ở bảng trên; query exact path và giới hạn maxFiles. Ví dụ booking bug → BookingForm.vue + mockOperations.js + bookingRules.js; care → StaffCareTasksView.vue + careUpdate; payment → BookingDetailDialog.vue + createInvoice/confirmPayment. Xem test tương ứng. Chỉ đọc file trực tiếp khi tool thiếu đoạn source hoặc báo stale. Không quét lại toàn bộ project/skills cho một lỗi cục bộ.
