# Mapping Frontend ↔ bảng 28 mục cốt lõi

**Ngày đối chiếu:** 17/10/2026. **Nguồn:** SRS mục 3.1, source được truy vấn bằng CodeGraph; đọc trực tiếp các đoạn template tool đã lược bớt. Không phải kết quả kiểm thử trình duyệt hay bằng chứng API thật đã hoạt động.

Đọc cùng [scope-and-decisions.md](scope-and-decisions.md). Các quy tắc mới ưu tiên hơn hành vi mock. Bảng SRS có **27 UC + FR01 = 28 mục**; UC56 là ngoại lệ API đã duyệt, không tự gộp vào 28 mục. Source frontend chưa được thay đổi theo baseline Backend mới.

## 1. Quy ước

- **Có UI mock:** thấy đường thao tác tương ứng; chưa khẳng định đủ mọi luồng/validation của UC.
- **Một phần:** UC ghép trong màn hình khác hoặc thiếu thao tác quan trọng.
- **Thiếu:** chưa thấy entry/thao tác trong source đã đối chiếu; chưa nên báo cáo là hoàn thành UC.
- **Core API:** cần nối Backend cho chức năng này. Một UC không tương đương một endpoint; các UC tạo khách/pet có thể là bước nội bộ của transaction đặt lịch.

Tên source ở bảng là đường dẫn tương đối dưới `frontend/src/`. Booking workspace = `components/booking/BookingWorkspace.vue`; booking detail = `components/booking/BookingDetailDialog.vue`; booking form = `components/booking/BookingForm.vue`.

## 2. Mapping đủ 28 mục

| # | Mã | UC theo SRS | Màn hình / source hiện có | Mức hiện diện | Hướng core và phần thiếu |
|---|---|---|---|---|---|
| 1 | UC01 | Tạo thông tin khách hàng | `/booking`, booking mới nội bộ → booking form | Có UI mock, ghép UC14 | Core API: tạo/map khách trong đặt lịch; public không được dò hoặc ghi đè hồ sơ cũ. |
| 2 | UC02 | Tra cứu thông tin khách hàng qua SĐT | Nội bộ hiện có danh sách/chi tiết booking; form đặt lịch chưa có thao tác lấy hồ sơ cũ qua SĐT | Thiếu tra hồ sơ cũ | Core API nội bộ + UI tối thiểu chọn khách/pet cũ. Actor Public của SRS gốc đã được sửa ở SRS 1.1; tracking public không thay cho UC tra mọi hồ sơ. |
| 3 | UC06 | Tạo hồ sơ thú cưng | Booking form có thêm pet, tên/loài/giống/cân nặng | Có UI mock, ghép UC14 | Core API lưu hồ sơ và ownership; không gộp pet chỉ theo tên; Public không tự gộp pet, nội bộ chọn ID pet cũ sau xác minh theo D10/Q05 đã đóng. |
| 4 | UC07 | Xem danh sách hồ sơ thú cưng | Pet của booking tại booking detail và `/tracking` | Một phần | Core: nội bộ xem/chọn pet thuộc khách; Public chỉ pet trong booking đã xác minh. Chưa có danh sách hồ sơ cũ nội bộ đầy đủ. |
| 5 | UC08 | Cập nhật hồ sơ & cân nặng | Check-in ở booking detail; `views/staff/StaffCareTasksView.vue` cập nhật cân nặng/ghi chú | Một phần | Core check-in lưu can_nang_thuc_te và cập nhật cân nặng hiện tại theo D10; chưa có màn sửa mọi thuộc tính hồ sơ pet. Cần chốt phạm vi field trong đặc tả UC. |
| 6 | UC10 | Xem danh mục dịch vụ & giá | `/services` → `views/customer/CustomerServicesView.vue`, ServiceCard/dialog, booking form | Có UI mock | Core API catalog: Spa, Combo, Hotel, hệ số; giữ giá ước tính khác giá chốt. |
| 7 | UC14 | Đặt lịch và cấp Tracking | `/booking`, booking mới `/staff/bookings`, `/admin/bookings` | Có UI mock | 18/10: mock kiểm tra Spa theo pet có Spa, phòng theo từng đêm, cùng lịch trả và ngày nghỉ; vaccine upload tùy chọn, policy kiểm tra ở check-in. Chưa có email/API thật hoặc transaction PostgreSQL. |
| 8 | UC15 | Tra cứu qua SĐT + Tracking | `/tracking` → `views/customer/CustomerTrackingView.vue` | Có UI mock | Core API xác minh đúng cặp, lỗi chung, giới hạn thử sai; chỉ trả một booking. Bỏ PIN/hash ở customer. |
| 9 | UC16 | Xem danh sách/chi tiết lịch hẹn | `/staff/bookings`, `/admin/bookings` → booking workspace (list/calendar) và detail | Có UI mock | Core API nội bộ; dữ liệu/phạm vi được phép phải kiểm tra server, không dựa vào workGroup mock. |
| 10 | UC17 | Duyệt/từ chối lịch | Pending queue trong booking workspace | Có UI mock | Core transaction: duyệt không trừ chỗ lần hai; từ chối trả chỗ đúng một lần. D13 đã chốt CANCELLED + loai_huy=STAFF_REJECTED, không booking state REJECTED. |
| 11 | UC18 | Dời lịch | Booking detail hiện sửa ghi chú, chưa thấy form đổi ngày/ca | Thiếu UI | Core Admin: PENDING/CONFIRMED, chưa pet nào check-in theo D14; bổ sung thao tác tối thiểu, đổi slot/kiểm tra phòng trong cùng transaction, thiếu chỗ thì giữ nguyên lịch cũ. Update ghi chú không thay UC18. |
| 12 | UC19 | Hủy lịch | Chưa thấy nút hủy booking đã xác nhận tại tracking/detail | Thiếu UI | Core theo actor/thời điểm được duyệt; từ chối pending của UC17 không thay toàn bộ UC19. Hủy/no-show phải giải phóng cả tài nguyên liên quan. |
| 13 | UC23 | Check-in tại quầy | Booking detail: cân nặng, lông, chọn nhân viên, tiếp nhận | Có UI mock | Core: xác nhận giá, ca hợp lệ và assignment; bỏ lọc cứng workGroup SPA hiện tại. Backend phải validate nhân viên được chọn. |
| 14 | UC24 | Cập nhật trạng thái chăm sóc | `/staff/care-tasks` → StaffCareTasksView | Có UI mock | Core STAFF được gán pet; kiểm tra transition, ảnh mốc đầu/cuối; READY/DONE theo state model; Admin có override, STAFF theo assignment. |
| 15 | UC25 | Nhật ký chăm sóc & ảnh | StaffCareTasksView form upload/ghi chú | Có UI mock | Core upload Supabase Storage, lưu tham chiếu và tác giả; không dùng Blob IndexedDB làm storage Backend. |
| 16 | UC26 | Tracking tiến độ | `/tracking` → BookingProgress, nhật ký và ảnh từng pet | Có UI mock | Core đọc đúng booking đã xác minh, mọi ảnh nhật ký của booking; không cần tự thêm realtime SignalR, có thể polling nếu đủ yêu cầu đã chốt. |
| 17 | UC30 | Bàn giao & hoàn tất | Booking detail có xác nhận thanh toán rồi mock hoàn tất đơn | Một phần, đang ghép UC35 | Core: xác định hành động bàn giao và điều kiện tất cả READY/DONE để chốt tiền, pet READY được DONE riêng; xác nhận thu đủ chuyển PAID + READY còn lại sang DONE + COMPLETED nguyên tử theo Q08 đã chốt. Hiện UI chưa có bước bàn giao độc lập. |
| 18 | UC31 | Tạo/chốt hóa đơn | Booking detail nút tạo snapshot khi READY | Có UI mock | Core một invoice/booking, snapshot tên/giá/phụ phí, không tạo trùng khi bấm lại; loại đặt cọc khỏi core. |
| 19 | UC32 | Thêm phụ phí | Booking detail chọn phụ phí/lý do; AdminFinance cấu hình rule | Có UI mock | Core STAFF ghi nhận phát sinh trước invoice, giao tiếp khách ngoài hệ thống. Thêm dịch vụ nội bộ theo D07 còn thiếu thao tác trực tiếp; không phát triển approval public bắt buộc. |
| 20 | UC34 | Xem hóa đơn | Booking detail, `/tracking`, `/admin/finance` tab hóa đơn | Có UI mock | Core hóa đơn theo booking; Public phải xác minh cặp SĐT/tracking. Không trả các invoice khác cùng SĐT. |
| 21 | UC35 | Xác nhận thanh toán | Booking detail: tiền mặt/chuyển khoản + xác nhận; PaymentQrPanel hiện QR demo | Có UI mock | Core STAFF/Admin có quyền xác nhận tiền thật đã nhận; QR chỉ hiển thị. Sửa tác nhân SRS: khách chuyển tiền, không được gọi API xác nhận PAID. |
| 22 | UC41 | Đăng ký ca | `/staff/dashboard` → `components/schedule/ScheduleWorkspace.vue` | Có UI mock | Core đăng ký ca, điều kiện mở/đóng/deadline và không trùng; không biến workGroup thành role mới. |
| 23 | UC50 | Xếp ca và xuất bản lịch | `/admin/staff-schedule` → ScheduleWorkspace | Có UI mock | Core duyệt/xếp ca; APPROVED đồng thời công bố, không published riêng. UI/mock còn đợt đăng ký/publish cần thu gọn lúc tích hợp; browser print đủ, không bắt buộc Excel/export server. |
| 24 | UC53 | Xem lịch làm việc & pet phụ trách | `/staff/dashboard`, `/staff/care-tasks` | Có UI mock | Core lịch của chính STAFF và assignment đúng người. Nhân viên có thể làm quầy lẫn chăm sóc theo D03, không phụ thuộc workGroup mock. |
| 25 | UC11 | Thêm dịch vụ/Combo | `/admin/finance` mới có sửa giá các entry sẵn có | Thiếu UI tạo catalog | Core Admin tạo dịch vụ/combo và thành phần combo; cần form tối thiểu. Danh mục seed không thay thế UC11 nếu vẫn nằm core. |
| 26 | UC12 | Cập nhật dịch vụ/bảng giá | `/admin/finance` → AdminFinanceView tab prices | Một phần: sửa giá | Core cập nhật fields đã đặc tả, hệ số/phòng liên quan; không sửa snapshot cũ. Nếu chỉ sửa giá thì phải ghi rõ phạm vi thu hẹp, không nhận là full CRUD. |
| 27 | UC64 | Cấu hình giờ và slot | `/admin/settings` → `views/admin/AdminSettingsView.vue` | Có UI mock | Core ca/khung ngày/sức chứa; không giảm dưới số đã giữ, không làm lịch/booking hiện hữu vô hiệu. loai_phong.so_luong_phong là bổ sung cần có chỗ nhập tối thiểu hoặc seed được duyệt. |
| 28 | FR01 | Đăng nhập/phân quyền nội bộ | `/login`, auth store, router guards, permissions.js | Có UI mock | Core Auth ADMIN/STAFF; Backend bỏ phân quyền workGroup, xác thực assignment cho chăm sóc. Token/mật khẩu demo không phải bảo mật production. |

## 3. UI ngoài bảng: phạm vi đã xác nhận và phần giữ demo

| Màn hình/tính năng | Mapping | Xử lý đề xuất |
|---|---|---|
| Trang chủ, About, liên hệ | Không phải UC riêng; CTA dẫn UC10/14/15 | Giữ nội dung tĩnh/hardcode; không API CMS. |
| `/admin/overview`, `/staff/overview` | Tổng hợp dữ liệu các UC16/24/35/53; không phải UC độc lập | Giữ UI; aggregate từ dữ liệu core đã cấp quyền hoặc demo có nhãn; không API/dashboard backend mới chỉ để phục vụ trang trí. Không hiển thị số mock như số vận hành thật. |
| `/admin/dashboard` thống kê ngày/tháng/năm | Ngoài bảng 28 | Đã chốt aggregate nhẹ từ booking/invoice có sẵn hoặc demo có nhãn; không phân hệ analytics riêng. |
| Đánh giá ở tracking và AdminFinance | UC56 được nhắc sau UC35, BR09; ngoài bảng 28 | Ngoại lệ core đã duyệt: POST /api/tracking/reviews (đúng SĐT/tracking, COMPLETED, một lần) và GET /api/admin/reviews (Admin). Không ticket/approval/refund review. |
| Khiếu nại/ticket, phê duyệt hoàn tiền | Ngoài bảng 28 | Không phát triển API; bỏ/ẩn đường ghi live, có thể giữ demo có nhãn. Admin xử lý đánh giá thấp ngoài hệ thống theo D12. |
| Yêu cầu thêm dịch vụ từ Public, hàng chờ duyệt | Ngoài bảng 28 dưới dạng approval riêng | Không bắt khách lên web duyệt. Thao tác thêm do STAFF sau trao đổi trực tiếp; UI approval cũ giữ demo hoặc cắt sau khi thống nhất tích hợp. |
| `/admin/staff` tạo/khóa tài khoản và đổi role | Ngoài bảng 28; dữ liệu tài khoản hỗ trợ FR01/UC50 | D18 cho phép ADMIN ↔ STAFF; mock đã triển khai và bảo vệ Admin hoạt động cuối. Chưa mở CRUD tài khoản Backend đầy đủ. Không nối workGroup vào Auth target. |
| `/staff/settings`, `/admin/account` | Ngoài FR01 nếu gồm đổi mật khẩu/hồ sơ | Giữ demo hoặc thu gọn; không mặc định thêm API đổi mật khẩu/reset/email. |
| QR tạm tính ngay khi đặt | Không phải xác nhận thanh toán UC35 | Không nhận cọc online; QR thanh toán thật ưu tiên hóa đơn chốt. Nếu vẫn hiển thị QR ước tính phải ghi rõ không yêu cầu chuyển tiền trước. |

## 4. Kết luận đối chiếu

Có frontend giúp xác định điểm vào, không đủ chứng minh 28 mục đã hoàn tất. Các khoảng thiếu ưu tiên: UC02/07 hồ sơ cũ nội bộ, UC18 dời lịch, UC19 hủy lịch, UC11 thêm dịch vụ/combo; UC08/12/30 cần làm rõ mức đầy đủ. D03/D06/D11 còn đòi hỏi chỉnh validation/quyền và chọn lịch nhận/trả trong lần tích hợp sau.

Không cắt UI, đổi quyền hay gọi API mới trong lượt lập mapping này. Q01–Q08 và thiết kế T01–T09 đã xác nhận. Xem [API contract dự thảo](../design/api-contract.md) và [traceability](../design/requirements-traceability.md) để nối mapping này với triển khai sau khi được cho phép.

## 5. Delta frontend/mock ngày 18/10/2026

Đợt kế hoạch mục 7 đã duyệt và triển khai: UC17 có lý do/xác nhận từ chối dùng chung hàng chờ/cuối chi tiết và notification; UC23 có policy vaccine mặc định tắt, nhân viên xác minh khi bật; UC41 chọn ca từ lịch có thứ/ngày; UC50 thêm cấu hình nghỉ tiếp nhận trước mở form, vẫn có ca HOTEL_CARE. Tracking thêm gửi lại mã theo email snapshot qua outbox mock, Admin xem tại settings. Loại lông được thêm riêng, tình trạng lông giữ nguyên.

Các khoảng thiếu UC02/07/18/19/11 và mức đầy đủ UC08/12/30 vẫn là backlog. WorkGroup và period/published cũ vẫn dùng trong mock, chưa phải mô hình quyền/lịch Backend mục tiêu. Không coi từ chối PENDING là hoàn tất UC19; không coi outbox mock là SMTP đã chạy. Xem [progress](../design/frontend-update-progress.md) để biết lệnh kiểm chứng đã thực hiện.
