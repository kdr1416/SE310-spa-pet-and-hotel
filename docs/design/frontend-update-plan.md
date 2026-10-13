# Kế hoạch cập nhật Frontend PetSpa

> Ngày: 13/10/2026 · Trạng thái: Đã hoàn thành 10/10 bước (Giai đoạn Frontend Mock & Cross-portal Verification).
>
> Cập nhật: Toàn bộ 10 bước kế hoạch đã được triển khai và nghiệm thu thực tế vào ngày 13/10/2026 (chi tiết đối chiếu và kết quả test xem tại `frontend-update-progress.md`).
> Checklist bên dưới đã được đồng bộ để phản ánh hiện trạng hoàn tất của phần Frontend.
> Lưu ý: Role Backend và các API thật vẫn chưa chốt; Frontend hiện đang chạy trên nền Mock Repository (IndexedDB) với đầy đủ kịch bản kiểm thử xuyên suốt (33 tests pass). Không tự ý can thiệp Backend C# hay commit Git.

**Goal:** Cập nhật toàn bộ `frontend/` theo `updates.md`, áp dụng bố cục 3 HTML vào Customer và cho phép chạy các luồng liên kết bằng mock trước khi nối API.

**Architecture:** Giữ Vue SPA hiện có. Views dùng Pinia stores; stores gọi service theo nghiệp vụ; service chọn mock hoặc HTTP qua `IS_MOCK_ENABLED`. Mock có nguồn dữ liệu và quy tắc chuyển trạng thái dùng chung cho Customer, Staff và Admin.

**Tech Stack:** Vue 3 Composition API, Vue Router, Pinia, PrimeVue 4, Tailwind 3, Lucide, Axios. Đề xuất bắt đầu với Vitest cho logic; chỉ thêm `@vue/test-utils` và jsdom khi cần component tests. Chưa chốt hoặc cài thêm dependency.

**Spec:**
- `docs/requirements/updates/updates.md`: phạm vi chức năng mới.
- `docs/design/design-system/petspa/MASTER.md` và `pages/booking.md`: quy chuẩn UI.
- `docs/requirements/updates/{landing,booking,tracking}.html`: tham chiếu bố cục Customer.
- `docs/requirements/SRS_He_Thong_Quan_Ly_Spa_Thu_Cung_AI_Readable.md`: quy tắc nghiệp vụ nền, khi cập nhật không thay thế rõ ràng.
- `docs/architecture/codebase-map.md`: điểm vào và cấu trúc hiện có; có một số mô tả vượt quá mức triển khai thực tế.

## 1. Quyết định đã xác nhận

| Nội dung | Quyết định của người dùng |
|---|---|
| Giai đoạn dữ liệu | Mock đủ luồng, chuẩn bị nối API sau |
| Áp dụng HTML | Lấy bố cục; giữ màu/font của MASTER |
| Role nội bộ | Chưa chốt. `ADMIN`, `STAFF_RECEPTION`, `STAFF_SPA` chỉ là phương án tạm để thảo luận; chưa cho phép thay `STAFF` trong code |
| Lượt hiện tại | Chỉ lập kế hoạch, chưa chỉnh code |

### Ràng buộc chung

- Primary `#0D9488`, Secondary `#14B8A6`, Accent/CTA `#EA580C`, Background `#F0FDFA`, Foreground `#134E4A`.
- Heading **Varela Round**, body **Nunito Sans**; spacing 4/8/16/24/32/48/64px theo MASTER.
- Booking có max width **1200px** theo page override. Các trang khác dùng bố cục HTML nhưng không sao chép cứng màu/font hoặc số liệu.
- Responsive ở **375, 768, 1024, 1440px**; keyboard focus rõ, contrast chữ tối thiểu **4.5:1**, hỗ trợ `prefers-reduced-motion`, icons dùng Lucide thay emoji.
- MASTER tự mâu thuẫn về màu chữ CTA: bảng token ghi đen nhưng CSS button ghi trắng. Khi triển khai chọn màu đạt 4.5:1 trên nền tương ứng, kiểm tra cả normal/hover và cập nhật quy chuẩn cho nhất quán.
- Không nhúng Tailwind CDN v4, `window.PetDemo`, thao tác `innerHTML`, demo-nav hoặc đường dẫn `.html` vào Vue app. Giữ Tailwind 3 đã có.
- Giữ tách biệt trạng thái đơn, trạng thái từng pet và trạng thái thanh toán; không suy ra đơn READY khi chỉ một pet READY.
- Chưa làm Backend, migration, gửi email thật, upload cloud, thanh toán ngân hàng thật hoặc triển khai production.

## 2. Source và kiểm tra hiện tại

Đã đối chiếu bằng CodeGraph: router, auth/booking stores, API mock/DTOs và các màn hình Customer/Staff/Admin. Dùng lại source đã khảo sát trong chat; truy vấn thêm đúng phần ảnh hưởng khi triển khai, không đọc lại toàn bộ codebase.

| Khu vực | Hiện trạng liên quan đến thay đổi |
|---|---|
| Customer | Có Home/Booking/Tracking. Submit chỉ sinh mã và redirect, chưa lưu đơn. Tracking chưa lấy query, chưa có lịch sử/review/complaint/add-on hoàn chỉnh. |
| Booking nội bộ | Có danh sách, chưa có calendar/ngày chọn/chi tiết và xử lý request đầy đủ. |
| Check-in/chăm sóc | Check-in search còn rỗng; nút tải ảnh chỉ `alert`. Store đổi trạng thái chưa bảo vệ BR-07 hoặc trạng thái multi-pet. |
| Thu ngân | Store chỉ sửa object; chưa tạo snapshot/QR và có thể báo thành công khi thiếu hóa đơn. |
| Lịch làm việc | Staff dùng dữ liệu tĩnh, gửi request chỉ `alert`; Admin chưa có xử lý duyệt/chốt. |
| Admin | KPI/slot là số tĩnh; dịch vụ chỉ đọc danh mục mẫu; chưa có finance/complaint/review/staff management hoàn chỉnh. |
| Auth | DTO và guard chỉ có `ADMIN | STAFF`; menu chưa phân biệt quầy/spa. |
| Tests | `package.json` chỉ có dev/build/preview; chưa tìm thấy tests của app hoặc Vitest. Tests trong node_modules không phải tests dự án. |

`npm run build` đã chạy thành công trong bước rà soát trước của cùng chat (1.873 modules). Đây là kiểm tra bundler, không phải type-check hoặc chứng minh nghiệp vụ. Chưa chạy lại vì lượt này chưa sửa frontend.

`git status` không chạy được: workspace hiện không phải Git repository. Khi triển khai phải kiểm tra lại; không tự `git init` hay ghi đè file có thay đổi của người dùng.

## 3. Thiết kế đề xuất và điểm cần duyệt

### UI tham chiếu

| HTML | Áp dụng vào Vue |
|---|---|
| `landing.html` | Header, hero hai cột + ảnh, trust items, dịch vụ có ảnh/giá, combo CTA, footer/contact. Menu chính đổi thành Trang chủ / Dịch vụ / Tra cứu / Về chúng mình theo updates. |
| `booking.html` | Form chia nhóm khách hàng/pets/thời gian + summary bên cạnh; mobile xếp dọc. Pet card thêm/xóa; Hotel mở checkout/vaccine. Giữ form một trang theo mẫu thay wizard hiện có. |
| `tracking.html` | Timeline đơn, danh sách tiến độ độc lập từng pet, thông tin đơn bên phải, nhật ký và nút refresh; thêm add-on, lịch sử, review/complaint theo trạng thái. |

Chỉ có 3 HTML trên; các link `lookup.html`, `journal.html`, `invoice.html`, `review.html` không có file mẫu tương ứng. Tạo các trạng thái/tab/dialog trong SPA, không coi đó là UI đã được cung cấp. Nội bộ dùng cùng tokens nhưng bố cục bảng/calendar/dashboard phù hợp thao tác, không sao chép landing.

### Đề xuất mặc định, chưa phải yêu cầu đã xác nhận

- Giữ thương hiệu **PetSpa & Hotel** đang có; tên **Nhà Mềm**, địa chỉ, hotline, số liệu và giá trong HTML là nội dung mẫu, chưa thay thành thông tin chính thức.
- Customer tra cứu bằng **SĐT + Tracking Code** theo BR-01/UC15. Không thêm PIN qua email chỉ vì HTML có câu “đã xác thực PIN”; SRS cũng có đoạn mô tả PIN chưa thống nhất. Nếu cần OTP/PIN phải chốt riêng trước khi nối Backend.
- Giữ `TRK-...` của app; mẫu không quyết định prefix hay độ dài. Mock sinh mã bằng `crypto.getRandomValues`, kiểm tra trùng; không dùng `Math.random` làm hợp đồng bảo mật production.
- QR sau booking gắn với **giá tạm tính**, chưa có nghĩa đã thanh toán hoặc thu tiền cọc. Dùng QR demo không thể thanh toán, hiện số tiền/nội dung mẫu; VietQR thực chỉ bật khi có ngân hàng, tài khoản nhận và chính sách thu tiền được xác nhận.
- “Lịch sử” trước mắt là đơn và nhật ký của **mã đã xác minh**, không tự lộ các đơn khác cùng SĐT.
- “Chuyển nhân viên” hiểu là Admin đổi người cho một ca; “xuất lịch” mặc định bảng in/PDF qua trình duyệt, chưa thêm Excel export.
- “Cài đặt hệ thống” Admin tách cấu hình giờ/slot khỏi cài đặt tài khoản cá nhân. Sửa username/password là form mock; không giả lập thành hệ thống xác thực production và không lưu mật khẩu rõ vào browser storage.
- Refund/complaint ban đầu có trạng thái chờ xử lý/đã duyệt/từ chối; chưa tự tính mức hoàn tiền theo chính sách chưa có. Admin nhập mức hoàn, không vượt phần tiền đã thu còn lại.

Các đề xuất trên cần được duyệt cùng plan; quyết định chưa rõ không được âm thầm biến thành tính năng khác.

### Phân quyền UI và thao tác

**Ma trận tạm theo nhóm công việc, chưa phải hợp đồng role đã duyệt.** Hai lựa chọn còn mở: giữ `ADMIN | STAFF` rồi thêm loại công việc/capabilities, hoặc tách role như tên bên dưới. Chỉ sửa DTO/auth/seed/guard sau khi nhóm quyết định. Các bước sau nói đến quầy/spa theo nhóm công việc, không mặc định đã chốt enum mới.

| Role | Khu vực |
|---|---|
| Public Customer | Home, Services, About, Booking; tra cứu/thao tác theo cặp thông tin đã xác minh |
| STAFF_RECEPTION | Booking list/calendar/request, tạo đơn, chi tiết/check-in, thanh toán, lịch cá nhân, cài đặt cá nhân |
| STAFF_SPA | Booking list/calendar chỉ xem; care tasks được phân công, cập nhật pet/ảnh/tiến trình, lịch và cài đặt cá nhân |
| ADMIN | Thống kê, booking operations, xếp lịch nhân viên, finance/complaints/reviews, staff management, system/account settings |

Guard phải chặn truy cập URL trực tiếp; mock service cũng chặn thao tác trái quyền, không chỉ ẩn nút. Nếu quyết định tách role, đổi DTO/seed/menu/redirect đồng bộ và xử lý phiên `STAFF` cũ. Nếu giữ role hiện tại, phân biệt công việc bằng field/capabilities được thống nhất. Backend cần kiểm tra cùng chính sách; chưa sửa auth hoặc database trong bước lập plan.

### Data flow và module

```text
View → Pinia store → service theo nghiệp vụ
                        ├─ Mock: repository dùng chung + validation + transition
                        └─ Live: httpClient (endpoints chỉ nối khi Backend thống nhất)
```

- Thêm `services/api/{bookingService,catalogService,scheduleService,financeService,staffService}.js`; giữ `httpClient.js` cho transport/envelope và chuyển mock operations sang `services/mock/` khi có task cần dùng.
- `services/mock/mockRepository.js`: **đề xuất IndexedDB, chưa chốt** cho booking, lịch, hóa đơn và ảnh Blob cần dùng lại qua reload/đóng mở trình duyệt; seed một lần, dữ liệu versioned. Native browser API, không bắt buộc thêm thư viện; đổi lại cần xử lý mở DB, transactions và version. Không bảo đảm khóa nghiệp vụ giữa nhiều máy như Backend.
- `sessionStorage` chỉ đề xuất cho state nhỏ theo tab như bộ lọc đang chọn; reload vẫn giữ, đóng tab thì mất, các tab không có kho dữ liệu chung. Nếu chỉ cần demo một tab và không lưu ảnh/dữ liệu lâu dài, dùng sessionStorage sẽ đơn giản hơn; phải điều chỉnh tiêu chí persistence và demo nhiều portal tương ứng. Pinia giữ state đang hiển thị; browser storage không thay xác thực/authorization thật.
- Test logic/service bằng Vitest trong Node trước; tests có thể dùng repository in-memory để kiểm tra nghiệp vụ. Kiểm tra persistence/Blob/transaction IndexedDB trong trình duyệt thật; jsdom không tự cung cấp IndexedDB và không thay kiểm tra browser này.
- `services/mock/mockOperations.js`: kiểm tra quyền, validation và chuyển trạng thái; không nhân bản quy tắc trong từng view. DTO mở rộng trong `types/api.d.ts`, enums trong `types/api.js`; vẫn dùng envelope `{ success, data, message, errors }`.
- Giữ `booking.js`, `auth.js`; thêm stores `catalog.js`, `schedule.js`, `finance.js`, `staff.js` theo nhu cầu task. Loading/error theo thao tác, tránh một cờ loading khóa cả trang.
- Đề xuất helper nghiệp vụ `utils/{bookingRules,pricing,scheduleRules}.js`: hàm thuần dễ test; tiền dùng số nguyên VND, ngày lịch dùng `YYYY-MM-DD` theo giờ Việt Nam, không dùng UTC conversion làm lệch ngày.
- Lookup thất bại xóa kết quả cũ, trả lỗi chung. Mock mô phỏng khóa tra cứu sau hơn 5 lần sai trong 15 phút theo UC15; Backend sẽ chịu trách nhiệm rate limit thật.
- Ảnh trong mock chọn/preview/lưu Blob, giới hạn đề xuất 5MB/ảnh JPEG/PNG/WebP và báo lỗi rõ. SPA revoke object URLs khi không còn dùng; không coi giấy tiêm chủng đã được xác nhận chỉ vì tải ảnh lên.
- Nút reload Tracking tải dữ liệu mới; đề xuất refresh 10 giây khi trang đang hiển thị, dừng khi tab ẩn/unmount. Đây là mock polling, không thêm SignalR trong đợt frontend này.

## 4. Kế hoạch triển khai theo bước

Mỗi bước: bổ sung test cho hành vi cần bảo vệ → xác nhận test fail đúng lý do → triển khai nhỏ nhất → chạy test và build. UI thuần kiểm tra bằng trình duyệt; không viết test chỉ để so màu/class. Không chuyển sang bước có dependency nếu bước trước chưa ổn.

### Bước 1 — Nền UI, routes và phương án phân quyền

**Files:** sửa `frontend/{tailwind.config.js,index.html}`, `src/{assets/main.css,main.js,router/index.js,layouts/AppLayout.vue,stores/auth.js,types/api.js,types/api.d.ts,services/api/mockData.js}`; tạo `src/utils/permissions.js`, `src/layouts/CustomerLayout.vue`, `tests/unit/permissions.spec.js`, `tests/unit/auth.spec.js`.

- [x] Thiết lập tokens/font theo MASTER; CTA orange, brand teal; theme PrimeVue dùng cùng tokens.
- [x] `canAccess(role, capability): boolean` dùng chung menu/guard/mock; capability BOOKING_READ, BOOKING_MANAGE, CARE_UPDATE, SCHEDULE_SELF, FINANCE_MANAGE, STAFF_MANAGE, SYSTEM_MANAGE.
- [x] Chốt cách thể hiện quầy/spa trước khi sửa auth/DTO/seed/guard/redirect; chưa mặc định đổi sang 3 roles. Customer layout dùng navbar/footer hiện có. Giữ route cũ qua redirect khi hợp lý (`staff/check-in`, `staff/cashier`, `staff/schedule`).
- [x] Đề xuất Vitest và scripts `test`, `test:watch` trước cho validation/pricing/transitions; giữ alias @. Khi đến component tests ở bước 4, mới bổ sung Vue Test Utils + jsdom nếu được duyệt. Các gói là devDependencies, không phải dependency chạy app production.
- [x] Test nhóm spa không có BOOKING_MANAGE, Admin truy cập khu vực quản trị; nếu đổi role, phiên cũ không tạo redirect loop. Browser kiểm tra URL cấm và điều hướng mobile.

**Đầu ra:** nền UI thống nhất và phân quyền theo phương án đã duyệt, chưa yêu cầu các chức năng chưa làm báo thành công.

### Bước 2 — Mock dùng chung và booking rules

**Files:** tạo repository/operations/services và `utils/{bookingRules,pricing}.js` nêu trên theo phạm vi booking/catalog; sửa `stores/booking.js`, `services/api/httpClient.js`; tạo `tests/unit/{bookingRules,pricing}.spec.js`, `tests/integration/mockBooking.spec.js`.

- [x] Định nghĩa `CreateBookingRequest` gồm khách hàng, ngày/ca, danh sách pet với dịch vụ/combo/phòng, checkout/vaccine cho Hotel; `BookingLookupRequest` gồm SĐT + tracking. Dùng DTO domain đang có, thêm field theo pet khi Hotel cần checkout khác nhau.
- [x] `validateBooking(payload, availability): string[]`, `estimateBooking(payload, catalog): { total, items }`, `deriveBookingStatus(details, currentStatus): TrangThaiDon`; service `createBooking(payload)`, `lookupBooking(credentials)`, `getBookings(filters)` trả Promise envelope.
- [x] Lưu booking PENDING và giảm slot theo số pet trong cùng transaction; hủy/từ chối trả slot đúng một lần. Seed dựa trên ngày hiện tại để demo không hết hạn.
- [x] Mock reload giữ được đơn/ảnh; đọc storage lỗi hoặc version không hỗ trợ báo rõ, không tự xóa dữ liệu đã có.
- [x] Test 2 pets / 1 slot bị từ chối, 2 pets / 2 slots thành công, booking trùng submit không sinh 2 đơn; Hotel tính số đêm thực; hệ số tại ranh giới 5/10/20kg; lookup sai SĐT không trả đơn.

**Đầu ra:** Customer và nội bộ có thể đọc cùng một booking; tiền/slot không dùng số cứng từ HTML.

### Bước 3 — Customer: Home, Services, About

**Files:** sửa `views/customer/CustomerHomeView.vue`, `components/public/{PublicNavbar,PublicFooter}.vue`; tạo `CustomerServicesView.vue`, `CustomerAboutView.vue`, `components/public/ServiceCard.vue`, `stores/catalog.js`; bổ sung router.

- [x] Chuyển bố cục landing sang Vue; giữ tokens/font MASTER, dùng lại ảnh nguồn mẫu khi phù hợp và có fallback nếu tải thất bại.
- [x] Routes `/`, `/services`, `/about`; danh mục đọc catalog service; xem chi tiết dịch vụ/combo bằng dialog hoặc route nhỏ theo nhu cầu, không thêm backend.
- [x] Menu đúng updates; nội dung liên hệ chưa xác nhận hiển thị là mẫu, không ghi đè bằng số liệu HTML như dữ liệu thật.
- [x] Browser kiểm tra CTA booking/tracking, menu mobile, focus và 4 viewport; không để link trỏ sang file HTML thiếu.

**Đầu ra:** các trang giới thiệu dùng được và dẫn đúng vào flow mới.

### Bước 4 — Customer: Booking, Tra cứu, lịch sử và phản hồi

**Files:** sửa `CustomerBookingView.vue`, `CustomerTrackingView.vue`; tạo `components/booking/{BookingForm,PetBookingCard,BookingSummary,PaymentQrPanel}.vue`, `components/tracking/{BookingProgress,PetProgressCard,CustomerRequestDialog}.vue`; tạo `tests/components/{BookingForm,CustomerTracking}.spec.js`.

- [x] Form một trang theo booking HTML: add/remove tối thiểu 1 pet; SĐT/email/tên, dịch vụ/combo, ngày/ca, Hotel checkout/vaccine; summary lấy từ pricing helper.
- [x] Submit đi service; giữ input khi lỗi, khóa double submit; kết quả có mã và QR demo phí tạm tính. Query chỉ truyền code, không đưa SĐT/email lên URL; tra cứu yêu cầu thông tin xác minh.
- [x] Tracking có form trước kết quả; sau xác minh lấy bố cục tracking HTML: timeline, status từng pet, nhật ký, invoice info, refresh và lịch sử của đơn.
- [x] Add-on request gắn đúng pet/dịch vụ, chờ quầy/Admin duyệt trước khi đổi giá. Review chỉ khi COMPLETED và tối đa 1 lần; complaint/refund request sau dùng dịch vụ; phản hồi đánh giá chỉ Admin xem.
- [x] Test đơn vừa tạo tra được sau reload, sai mã không giữ kết quả cũ, pet thứ hai WAITING khiến đơn chưa READY, review trùng bị chặn, add-on chưa duyệt không thay invoice.

**Đầu ra:** flow Customer chạy xuyên từ booking đến tracking và phản hồi.

### Bước 5 — Booking nội bộ và tiếp nhận/thanh toán

**Files:** sửa `StaffBookingsView.vue`, `StaffCheckInView.vue`, `StaffCashierView.vue`; tạo `views/admin/AdminBookingsView.vue`, `components/booking/{BookingWorkspace,BookingCalendar,BookingDetailDialog,BookingRequestsPanel}.vue`; mở rộng booking/finance services; `tests/integration/bookingOperations.spec.js`.

- [x] `BookingWorkspace` dùng chung quầy/Admin: list theo ngày, calendar tuần, nút booking mới (reuse BookingForm), requests PENDING chấp nhận/từ chối, mở detail để cập nhật/check-in/thanh toán.
- [x] Staff spa dùng list/calendar cùng dữ liệu nhưng chỉ đọc; quầy/Admin mới sửa đơn và thu tiền. Calendar có click/keyboard, không thêm drag-drop/calendar dependency.
- [x] Check-in cân/kiểm tra lông theo từng pet, tính giá; chỉ chốt đơn khi từng pet đủ điều kiện. Đơn được hủy/từ chối không được check-in.
- [x] `createInvoice(bookingId)` tạo 1 snapshot; `confirmPayment(bookingId, method)` yêu cầu all pets READY, invoice tồn tại, explicit confirm và chống thu hai lần. BANK_TRANSFER hiển thị QR demo rồi cần xác nhận, không đánh dấu PAID khi mở QR.
- [x] Test lọc đúng ngày tại ranh giới UTC, reject trả slot đúng một lần, missing invoice không báo success, đổi catalog không đổi snapshot, sửa hóa đơn PAID bị chặn.

**Đầu ra:** quầy/Admin xử lý được đơn Customer; spa xem được đơn và phân công.

### Bước 6 — Staff spa: Thực hiện dịch vụ

**Files:** sửa `StaffCareTasksView.vue`, store booking; tạo `components/care/{CareTaskCard,CareUpdateDialog}.vue`, `tests/integration/careProgress.spec.js`.

- [x] Chỉ hiển thị pet được phân công; update cân nặng/thông tin phù hợp và nhật ký ảnh, không cho spa sửa giá/thu tiền.
- [x] WAITING → IN_PROGRESS cần ảnh check-in + ghi chú; IN_PROGRESS → READY cần ảnh hoàn tất; PAUSED/resume có ghi chú; reject transition ngược không hợp lệ. Giá đã thu không đổi theo update thông tin pet.
- [x] Service lưu ảnh/nhật ký và status; Tracking refresh thấy đúng pet thay đổi; đơn READY khi toàn bộ pet đủ điều kiện.
- [x] Test thiếu ảnh/ghi chú bị chặn, pet không thuộc người dùng bị từ chối, 2 pets khác trạng thái, upload lỗi không báo thành công hoặc đổi status.

**Đầu ra:** luồng quầy check-in → spa chăm sóc → Customer thấy tiến độ → quầy thanh toán chạy đủ.

### Bước 7 — Dashboard lịch cá nhân và Admin xếp lịch

**Files:** sửa `StaffDashboardView.vue`, `StaffScheduleView.vue`, `AdminStaffScheduleView.vue`; tạo `stores/schedule.js`, schedule service, `utils/scheduleRules.js`, `components/schedule/{WeeklySchedule,ShiftRegistrationForm}.vue`; `tests/unit/scheduleRules.spec.js`, `tests/integration/scheduleFlow.spec.js`.

- [x] Dashboard cả hai staff là lịch tuần và form đăng ký tuần kế tiếp; dùng timezone Việt Nam và tuần bắt đầu thứ Hai. Redirect trang lịch cũ vào dashboard mới.
- [x] Admin tạo đợt đăng ký gồm tuần, ca, số nhân viên, thời điểm mở/deadline, trạng thái mở/khóa; review approve/reject/reassign; publish và print lịch.
- [x] `validateShiftRegistration(request, period, assignments): string[]`; service `registerShift(request)`, `reviewRegistration(id, decision)`, `publishSchedule(periodId)`.
- [x] Hết deadline chặn gửi ở service; không vượt số người/ca hoặc xếp một người vào giờ trùng; staff chỉ xem lịch published và trạng thái request của mình.
- [x] Test trước/sau deadline, đăng ký trùng, reassign gây trùng giờ, vượt số người, lịch draft không xuất hiện như lịch đã chốt; print hiển thị đúng tuần/nhân viên.

**Đầu ra:** Staff đăng ký → Admin duyệt/chuyển/chốt → Dashboard nhận lịch.

### Bước 8 — Admin thống kê, tài chính và khiếu nại

**Files:** sửa `AdminDashboardView.vue`, `AdminServicesView.vue`; tạo `AdminFinanceView.vue`, `stores/finance.js`, finance service, `components/finance/{InvoiceList,CustomerRequestsPanel,PricingSettings}.vue`; `tests/integration/financeFlow.spec.js`.

- [x] Thống kê số đơn/doanh thu theo ngày/tháng/năm từ dữ liệu mock; đề xuất doanh thu = khoản đã thu trừ khoản hoàn đã duyệt trong kỳ; thống kê số đơn theo createdAt, ghi rõ cách tính trên UI.
- [x] Tài chính có tabs giá dịch vụ/combo, invoices, complaint/refund requests, reviews và phụ phí; AdminServices cũ redirect tab giá.
- [x] Refund được duyệt tạo ledger riêng, giữ snapshot hóa đơn; complaint chỉ đổi trạng thái xử lý, không tự hoàn tiền. Review không gửi về màn staff.
- [x] Test đổi giá không làm đổi invoice PAID, hoàn 2 lần bị chặn, hoàn vượt đã thu bị từ chối, filter kỳ chính xác; quầy/spa không truy cập reviews/finance admin.

**Đầu ra:** phản hồi Customer đến đúng Admin và số liệu tài chính nhất quán.

### Bước 9 — Nhân viên, tài khoản và cài đặt hệ thống

**Files:** tạo `views/admin/{AdminStaffView,AdminSettingsView}.vue`, `views/shared/AccountSettingsView.vue`, `stores/staff.js`, staff service; sửa `AdminShiftsSlotsView.vue`, auth/menu/DTOs; `tests/integration/staffSettings.spec.js`.

- [x] Admin tạo tài khoản mock, chọn quyền/nhóm công việc theo phương án được chốt ở bước 1, danh sách/trạng thái; validation username trùng. Phân công pet dùng nhân viên spa hoạt động.
- [x] Tài khoản cá nhân: username/password/thông tin cá nhân, lỗi và loading; chỉ sửa chính mình; password mock không lưu rõ, không in log hoặc đưa vào docs/fixtures.
- [x] System settings: giờ hoạt động/ca/sức chứa theo ngày, dùng màn slot hiện có; không cho giảm sức chứa dưới số pet đã giữ chỗ hoặc đổi lịch published âm thầm.
- [x] Test username trùng, sửa tài khoản khác bị chặn, không gán chăm sóc cho reception, inactive không nhận phân công mới, giảm slot gây quá tải bị từ chối.

**Đầu ra:** menu Admin đúng updates và account settings dùng được cho Admin/quầy/spa theo phương án quyền đã duyệt.

### Bước 10 — Kiểm tra xuyên portal và cập nhật tài liệu

**Files:** tests của các bước; cập nhật `docs/architecture/codebase-map.md`, design specs phần token mâu thuẫn và overrides cần thiết, SRS/updates phần phân quyền nếu quyết định được duyệt làm thay đổi hợp đồng hiện tại.

- [x] `npm run test` và `npm run build` tại `frontend/`; không coi build là type-check.
- [x] Browser: Customer tạo 2 pets → quầy accept/check-in → spa ảnh/progress → Customer refresh → all pets READY → quầy snapshot/confirm → Customer review/complaint → Admin xử lý và thấy thống kê.
- [x] Browser: staff đăng ký → Admin duyệt/publish → staff reload thấy lịch; kiểm tra URL trái quyền của Admin/quầy/spa và tài khoản cá nhân theo phương án đã chốt.
- [x] Kiểm tra 4 viewport, keyboard/dialog focus, labels/error announcements, reduced motion, ảnh lỗi, loading/empty/error, reload phục hồi dữ liệu. Không còn nút hành động giả báo thành công bằng alert.
- [x] Tài liệu phân biệt mock hoạt động với bảo đảm Backend chưa có; ghi rõ contracts API cần triển khai, phương án phân quyền, PIN/QR/payment policy còn cần thống nhất.

**Đầu ra:** frontend mock có thể demo đầy đủ; báo lệnh đã chạy và giới hạn thực tế khi bàn giao.

## 5. Review Focus

| Rủi ro cần bảo vệ | Test/check chịu trách nhiệm |
|---|---|
| 2 pet không cùng trạng thái, giá và số đêm khác nhau | Bước 2/4/6: pricing, aggregate status, Tracking từng pet |
| Reload/đổi portal mất đơn hoặc lộ kết quả tra cứu cũ | Bước 2/4: repository persistence, lookup failure xóa kết quả |
| Gửi lại thao tác gây giữ slot/thu tiền/refund hai lần | Bước 2/5/8: idempotency và state validation tại service |
| Calendar lệch ngày, deadline/ca trùng nhau | Bước 5/7: local date boundary, fake clock, overlap/capacity |
| Ẩn nút nhưng truy cập URL/service vẫn làm thao tác cấm | Bước 1/6/8/9: guard, capabilities, phân công và ownership |

## 6. Thứ tự và điều kiện triển khai

`1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9 → 10`. Bước 7/8/9 là các phần nghiệp vụ riêng nhưng dùng dữ liệu và quyền của bước 1/2; thực hiện tuần tự để tiết kiệm context và kiểm tra dễ.

Đề xuất duyệt theo từng mốc: (1–4) Customer, (5–6) vận hành booking, (7) lịch, (8–9) Admin; mỗi mốc có demo chạy được. Plan bao phủ toàn bộ updates, không chỉ 3 trang HTML.

Trước phần implementation liên quan cần duyệt: phạm vi/luồng, phương án phân quyền, lựa chọn storage và dependency test theo từng giai đoạn. Chưa bắt buộc cài cả 3 gói test. QR thanh toán thực và hợp đồng Backend chưa đủ dữ kiện vẫn tách thành việc cần chốt sau; mock không bị phụ thuộc vào chúng.

