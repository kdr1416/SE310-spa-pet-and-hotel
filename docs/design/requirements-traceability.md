# Traceability và lộ trình triển khai — Spa & Hotel

**Phiên bản:** 0.1, 17/10/2026. **Trạng thái:** yêu cầu và T01–T09 đã duyệt; mapping API theo [contract dự thảo](api-contract.md). Chưa triển khai Backend. Bảng này giúp chứng minh phạm vi báo cáo và chọn kiểm thử có giá trị, không chứng minh code đã đạt yêu cầu.

**Nguồn:** [SRS 1.1](../requirements/SRS_He_Thong_Quan_Ly_Spa_Thu_Cung_AI_Readable.md), [mapping frontend](../requirements/frontend-use-case-mapping.md), [AC01–AC16](system-analysis.md), [thiết kế dữ liệu](system-design.md). Giữ mã 27 UC + FR01; UC56 là ngoại lệ riêng đã duyệt.

## 1. Mapping phạm vi core

BR dưới đây dùng số của BR-01…BR-18. AC là kịch bản đã định nghĩa trong phân tích; V là tiêu chí bổ sung ở mục 2. Mã E thuộc API contract; tra tên bảng/field chính xác ở Data Dictionary, không tự tạo bảng từ tên module.

| Mục | BR | API | Bảng chính | UI / điểm cần nối | Nghiệm thu |
|---|---|---|---|---|---|
| UC01 Tạo khách | 01,17 | E04–E06 | khach_hang, don_dat_lich | Form booking Public/nội bộ | AC12,13 |
| UC02 Tra khách theo SĐT | 17 | E06 | khach_hang, thu_cung | Thiếu lookup nội bộ, bổ sung tối thiểu | V01 |
| UC06 Tạo pet | 02,17 | E04,E05 | thu_cung, chi_tiet_don | Form nhiều pet; không gộp tên | AC13,V01 |
| UC07 Danh sách pet | 17 | E06,E08,E10 | thu_cung, chi_tiet_don | Chọn pet cũ nội bộ; Tracking chỉ pet của đơn | V01 |
| UC08 Cập nhật pet/cân nặng | 18 | E07,E15,E18 | thu_cung, chi_tiet_don, nhat_ky_cham_soc | Hồ sơ/check-in/ghi chú | V02 |
| UC10 Xem dịch vụ/giá | 04,05,06 | E02 | dich_vu, combo, chi_tiet_combo, he_so_can_nang, loai_phong | /services và booking form | V03 |
| UC14 Đặt lịch | 01–06,17 | E03–E05 | don_dat_lich, chi_tiet_don, khach_hang, thu_cung, ho_so_tiem_chung, khung_gio_dat_lich, loai_phong | /booking, booking mới nội bộ | AC01–04,12,13; V04 |
| UC15 Tra đơn | 01,17 | E08 | don_dat_lich, chi_tiet_don | /tracking | V05 |
| UC16 Danh sách/chi tiết hẹn | 02,10 | E10 | don_dat_lich, chi_tiet_don | /staff/bookings, /admin/bookings | V06 |
| UC17 Duyệt/từ chối | 03,12 | E11,E12 | don_dat_lich, khung_gio_dat_lich | Pending queue | AC05,V07 |
| UC18 Dời lịch | 02,03,06 | E13 | don_dat_lich, khung_gio_dat_lich, loai_phong | Thiếu form đổi ca/ngày | AC14,V08 |
| UC19 Hủy/no-show | 03,12,13 | E09,E12 | don_dat_lich, khung_gio_dat_lich | Tracking và booking detail thiếu nút hủy phù hợp | AC05–07,V09 |
| UC23 Check-in | 04,10,11,18 | E14–E16 | chi_tiet_don, thu_cung, dang_ky_ca, he_so_can_nang | Booking detail, chọn người ca APPROVED | AC07,08; V02,03 |
| UC24 Cập nhật chăm sóc | 07,10 | E17,E38 | chi_tiet_don, nhat_ky_cham_soc | /staff/care-tasks | AC08,V10 |
| UC25 Nhật ký/ảnh | 07,18 | E17,E18 | nhat_ky_cham_soc | Care form → private Storage | AC08,V10,11 |
| UC26 Tracking tiến độ | 01,07,17 | E08 | chi_tiet_don, nhat_ky_cham_soc | /tracking, progress/logs | V05,11 |
| UC30 Bàn giao/hoàn tất | 08,16 | E19,E26 | chi_tiet_don, don_dat_lich, hoa_don | Cần thao tác bàn giao pet độc lập | AC09,10 |
| UC31 Chốt hóa đơn | 04,08,15 | E23,E25 | hoa_don, ct_hoa_don, phu_phi, dich_vu_phat_sinh | Booking detail, snapshot/thu hồi nháp | AC09–11,V03 |
| UC32 Phụ phí/dịch vụ thêm | 03,08,14 | E20–E22 | phu_phi, dich_vu_phat_sinh, khung_gio_dat_lich | Nội bộ thêm dịch vụ; không public approval | AC11,16; V12 |
| UC34 Xem hóa đơn | 08,17 | E08,E24 | hoa_don, ct_hoa_don | Tracking/booking detail/finance | V05,13 |
| UC35 Thu tiền | 08,15,16 | E24,E26 | hoa_don, chi_tiet_don, don_dat_lich | QR thật + nhân viên xác nhận | AC10,11; V13 |
| UC41 Đăng ký ca | 11 | E27,E28 | dang_ky_ca, ca_lam_viec | ScheduleWorkspace | V14 |
| UC50 Duyệt/xếp ca | 10,11 | E27,E29 | dang_ky_ca, nhan_vien, ca_lam_viec | /admin/staff-schedule, bỏ publish riêng | V14 |
| UC53 Lịch/pet phụ trách | 10,11 | E27,E38 | dang_ky_ca, chi_tiet_don | Staff dashboard/care-tasks | AC08,V14 |
| UC11 Thêm dịch vụ/combo | 04,05 | E30,E31 | dich_vu, combo, chi_tiet_combo | Thiếu form tạo catalog | V15 |
| UC12 Cập nhật catalog/giá | 04–06 | E30–E33 | dich_vu, combo, chi_tiet_combo, he_so_can_nang, loai_phong | /admin/finance có sửa giá một phần | V03,15 |
| UC64 Giờ/slot | 03,06,11,13 | E33,E34 | ca_lam_viec, khung_gio_dat_lich, loai_phong | /admin/settings | V09,16 |
| FR01 Auth/phân quyền | 10,11,17 | E01; bảo vệ mọi E nội bộ | nhan_vien, dang_ky_ca, chi_tiet_don | Login/guards; bỏ workGroup authorization | AC08,15; V06 |
| UC56 Đánh giá — ngoại lệ | 09 | E35,E36 | danh_gia, don_dat_lich | Tracking → Admin reviews; STAFF không xem | AC15 |

## 2. Tiêu chí bổ sung để nghiệm thu phần chưa phủ bởi AC

| Mã | Tình huống / kết quả cần quan sát |
|---|---|
| V01 | Public nhập SĐT cũ không nhận danh sách pet; nội bộ lookup có Auth, chỉ chọn pet đúng chủ; hai pet trùng tên không tự gộp. |
| V02 | Check-in lưu cân nặng thực tế và cập nhật cân nặng hiện tại; sửa hồ sơ/ghi cân nặng nhật ký sau đó không sửa snapshot giá/lịch sử. |
| V03 | Spa/combo nhân đúng dải cân và làm tròn VND; Hotel chỉ giá đêm × đêm; tổng hợp cộng hai phần. Không có dải cân phải báo lỗi. Catalog đổi giá không đổi invoice cũ. |
| V04 | Cùng requestId/payload gửi đồng thời hoặc retry sau timeout chỉ tạo một đơn và giữ một lần; khác payload trả conflict. Không để booking nửa chừng khi một pet hết phòng. |
| V05 | Sai cặp Tracking lỗi chung; đúng cặp chỉ xem một đơn và ảnh/invoice của đơn đó; rate limit thử sai và hết hạn signed URL không lộ bucket. |
| V06 | Thiếu JWT/role/resource bị chặn trực tiếp ở API, không chỉ ẩn menu; danh sách phân trang, STAFF không nhận dữ liệu Admin-only. |
| V07 | Confirm lặp không giữ chỗ thêm; STAFF_REJECTED chỉ PENDING, STAFF_CANCELLED chỉ CONFIRMED; CANCELLED giữ reason và loại đúng. |
| V08 | Dời lịch sau một pet check-in bị chặn; đổi ca/thay loại tài nguyên hợp lệ không đếm chính booking hai lần. |
| V09 | No-show chỉ từ sau cuối ca 30 phút và chưa check-in; Hotel-only hủy không tăng slot Spa. Đổi giờ ca không sửa deadline snapshot của đơn cũ. |
| V10 | START thiếu ảnh/ghi chú hoặc READY thiếu ảnh/phần việc chưa xong bị chặn; retry không tạo trạng thái thiếu ảnh; PAUSE/RESUME giữ lịch sử. |
| V11 | File quá lớn/sai loại hoặc actor sai bị chặn; Storage lỗi không chuyển trạng thái. Tracking thấy mọi ảnh nhật ký đúng đơn bằng quyền đọc có hạn. |
| V12 | Hai extras cùng pet/cùng ca chỉ giữ một suất; khác ca giữ thêm; hủy một extra không nhả suất còn được dùng; không thêm khi root READY/DONE hoặc invoice hiệu lực. |
| V13 | QR đúng tiền/mã đơn không tự tạo PAID; chỉ nhân viên xác nhận đã đủ tiền. Invoice UNPAID bị thu hồi không còn hiển thị thanh toán như invoice hiệu lực. |
| V14 | Đăng ký PENDING không dùng phân công; APPROVED hiện lịch ngay; đổi người ngày tiếp theo hợp lệ và không thay tác giả log cũ; không thu hồi ca làm assignment hiện tại vô hiệu. |
| V15 | Admin tạo combo chứa dịch vụ hợp lệ; grooming theo BR-05; STAFF không sửa catalog. isActive không xóa lịch sử đã dùng. |
| V16 | Không giảm slot dưới lượng giữ hoặc phòng dưới peak đã cam kết; các ca/dải cân không chồng lấn theo thiết kế; giá trị âm bị chặn. |

Đây là tiêu chí thiết kế, chưa phải kết quả test. Khi viết code, chọn test tự động cho công thức/guard/transaction và một lượt demo E2E; không cần lặp hàng chục bước browser sau mỗi thay đổi nhỏ.

## 3. Traceability phi chức năng

| NFR | Quyết định đáp ứng | Cách xác minh khi có Backend |
|---|---|---|
| P1/P2/P3 | Query có lọc/phân trang/index; ảnh thumbnail/quyền đọc hợp lệ; không giữ SQL lock lúc SMTP/Storage | Đo trên môi trường và dataset ghi rõ: query <500ms, Tracking 3–5 thumbnail <2s, 10 người dùng; chưa có benchmark. |
| S1/S2/S3 | Hash password an toàn, mã Tracking ngẫu nhiên unique, JWT ADMIN/STAFF + resource guards | Test đăng nhập/quyền trực tiếp; xem cấu hình hash và generator, không đưa secret vào output. |
| S4/S5 | Cặp SĐT/mã trong body, lỗi chung/rate limit, Public DTO, bucket private | V01,V05,V06,V11; kiểm tra response/log không chứa hồ sơ khác hoặc privileged key. |
| C1/C2 | Thứ tự lock, unique/index, requestId, resource delta | AC01–05,14; V04,V12 với PostgreSQL thật để kiểm tra concurrency, không thay bằng EF in-memory. |
| R1 | Snapshot và PAID bất biến, checkout transaction | AC09–11; cố gây lỗi giữa checkout phải rollback toàn bộ. |
| R2/R3 | Validate file, upload ngoài SQL lock, email fallback và QR manual | AC12; V11,V13. SMTP fail không mất tracking hoặc giữ chỗ lần hai. |
| U1 | Giữ UI hiện có; bổ sung điều khiển còn thiếu khi nối API | Kiểm tra ngắn ở mobile 375px và desktop cho các form thay đổi, không audit lại toàn frontend. |

## 4. Lộ trình code dự kiến — chưa phải lệnh triển khai

Chỉ bắt đầu sau khi contract được rà soát và người dùng cho phép code. Mỗi chặng phải có dữ liệu demo đủ đi qua luồng đó; không dựng mọi controller trước rồi mới nối E2E.

| Chặng | Vùng dự kiến thay đổi | Kết quả và kiểm chứng tập trung |
|---|---|---|
| 1. Nền tảng tối thiểu | backend/src/PetSpa.Api, Application, Domain, Infrastructure; cấu hình/EF mappings/seed | Chọn runtime, tạo schema theo 20 bảng đã duyệt, seed catalog + 1 Admin/3–4 Staff. Login/Auth, catalog và availability hoạt động; kiểm tra constraints và role. Trình bày tác động trước khi chạy migration. |
| 2. Đặt và tra đơn | Booking/Capacity/Tracking, MailKit/Storage adapters; frontend service adapters và form booking | Public đặt SPA/Hotel/Combo → PENDING giữ chỗ → Tracking thật; AC01–04,12,13 và V04/V05. Thử SMTP lỗi một lần, không chạy lại liên tục. |
| 3. Vận hành trước nhận | Booking commands, customer lookup, Schedule; UI thao tác thiếu | Duyệt/hủy/dời/no-show, chọn pet cũ, đăng ký/duyệt ca; AC05–07,14 và V01/V08/V14. |
| 4. Chăm sóc | Care/Assignment/extras/fees; private image flow; care-task adapters | Check-in → ảnh đầu → chăm sóc/phát sinh → ảnh cuối; AC08, V02/V03/V10–12. |
| 5. Hóa đơn đến đánh giá | Billing/Review; QR/Tracking/finance adapters | READY/DONE → snapshot → đủ tiền/bàn giao → COMPLETED → review Admin; AC09–11,15,16/V13. Một demo xuyên suốt. |
| 6. Hoàn thiện core | Catalog/settings/overview, các UI tối thiểu còn thiếu; tài liệu thực tế | UC11/12/64 và tổng hợp nhẹ E37; V15/V16. Cập nhật codebase-map và đánh dấu UC thực sự đã chạy, đo NFR một lượt trên môi trường demo. |

Không viết API cho account CRUD, ticket, refund, deposit, public approval phát sinh hay published schedule riêng. UI tương ứng giữ mock có nhãn hoặc thu gọn khi tích hợp. Phiên bản framework/hosting còn mở, không coi đó là trở ngại để rà soát contract hiện tại.
