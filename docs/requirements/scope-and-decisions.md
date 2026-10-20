# Phạm vi và quyết định nghiệp vụ — baseline đã chốt

**Ngày cập nhật:** 18/10/2026. **Trạng thái:** người dùng đã xác nhận D01–D14, Q01–Q08, T01–T09 và bổ sung D15–D19 ở mục 5. Phần bổ sung mới ưu tiên khi khác baseline cũ; frontend/mock mục 7 đã được duyệt và triển khai. Chưa triển khai Backend/migration; xem progress cho kiểm chứng và giới hạn.

**Mục đích:** lưu quyết định và nguồn thay đổi. [SRS](SRS_He_Thong_Quan_Ly_Spa_Thu_Cung_AI_Readable.md) là bản yêu cầu chuẩn hóa; [system-analysis.md](../design/system-analysis.md) là phân tích nghiệp vụ để duyệt trước thiết kế vật lý/API. Không phải bằng chứng Backend đã được triển khai.

## 1. Phạm vi và công nghệ

- Một cơ sở Spa & Hotel, khoảng 30–50 booking/ngày.
- Phạm vi core: 27 UC + FR01 trong SRS mục 3.1, thêm ngoại lệ UC56 đánh giá tối thiểu gồm `POST /api/tracking/reviews` và `GET /api/admin/reviews`.
- Giữ frontend Vue hiện có. Backend: .NET Web API, EF Core, PostgreSQL; ảnh dùng Supabase Storage; email MailKit/SMTP thật.
- Auth chỉ ADMIN và STAFF. STAFF làm được cả quầy và chăm sóc; người thực hiện chăm sóc phải là người được gán hiện tại. ADMIN được thao tác vận hành như STAFF và được xử lý chăm sóc ngoài assignment của chính mình; các điều kiện nghiệp vụ vẫn phải được tuân thủ.
- Seed 1 Admin và 3–4 Staff; không API CRUD tài khoản nhân viên. Overview/thống kê tính từ dữ liệu core hoặc truy vấn tổng hợp nhẹ, không phân hệ analytics riêng.
- Ngoài phạm vi: payment gateway/webhook, đặt cọc, giảm giá/voucher, hoàn tiền tự động/phức tạp, ticket khiếu nại, mã chuồng vật lý, bảng lịch sử sức khỏe riêng. Các UI ngoài phạm vi giữ demo có nhãn hoặc cắt gọn khi tích hợp, không tự nối API.
- Phiên bản .NET/EF/PostgreSQL, hosting và cấu hình hạ tầng cụ thể sẽ được lựa chọn ở thiết kế kỹ thuật, chưa tự xác nhận thay người dùng.

## 2. Nhật ký quyết định đã xác nhận

| ID | Quyết định chính thức | Lưu ý triển khai |
|---|---|---|
| D01 / Q04 | Core 28 mục gốc + UC56 tối thiểu. | Giữ mã gốc, không đổi UC56 thành một UC trong 28 để che mở rộng. Mapping không có nghĩa UI đã đầy đủ. |
| D02 / Q05 | .NET/EF/PostgreSQL/Supabase Storage; email bắt buộc, SMTP thật và fallback. | Tracking luôn trả về JSON và màn hình kết quả; SMTP lỗi không rollback booking đã lưu. Không đặt secret trong repo. |
| D03 / Q07 | ADMIN/STAFF; bỏ phân quyền workGroup RECEPTION/SPA ở Backend; Admin có toàn quyền vận hành. | STAFF chăm sóc đúng assignment, server kiểm tra; Admin override quyền actor không được bỏ ảnh bắt buộc, sức chứa hoặc khóa hóa đơn. |
| D04 / Q01 | PENDING giữ sức chứa. Từ chối/hủy/no-show trả đúng tài nguyên đã giữ một lần. | Hotel-only không trừ slot Spa. Slot Spa chỉ đếm pet có Spa, không đếm dòng add-on. |
| D05 / Q02 | loai_phong.so_luong_phong; 1 pet Hotel = 1 suất phòng, từng đêm [nhận, trả). | Các đơn PENDING/CONFIRMED/IN_PROGRESS/READY giữ phòng. Khóa loại phòng trong transaction khi kiểm tra/đặt; không đếm số booking đơn thuần. |
| D06 / Q03, Q08 | Lịch nhận/trả dự kiến nằm ở booking cha; mọi pet qua đêm phải có loại phòng. Được bàn giao thực tế từng pet sớm. | Q08 mới nhất thay thế cấm bàn giao lẻ trước đó. Không phát sinh hóa đơn hoặc thu tiền riêng từng pet. |
| D07 / Q06 | Spa/Combo Spa × hệ số cân nặng; Hotel = giá/đêm × số đêm; kết hợp cộng hai phần. | Check-in chốt theo cân thực tế. Trả sớm không tính lại tiền; đón trễ thêm phụ phí trước snapshot. |
| D08 / Q06, Q08 | Một hóa đơn hiệu lực, UNPAID khóa thay đổi; được hủy nháp UNPAID để tạo lại; PAID bất biến. | Không thu tiền trước khi mọi pet READY/DONE. Xác nhận đủ tiền + hoàn tất bàn giao là một transaction. |
| D09 / Q05 | Chỉ tra một đơn bằng SĐT + tracking_code thuộc don_dat_lich; bỏ PIN và khach_hang.ma_tracking_hash. | Không Public lookup hồ sơ/lịch sử chỉ bằng SĐT. Không cho QR/khách tự đánh dấu PAID. |
| D10 / Q05 | Public map customer ngầm theo SĐT, không ghi đè tên/email cũ, không gộp pet theo tên. Nội bộ xác minh miệng rồi chọn pet cũ. | Thông tin người đặt/email nhận kết quả của lần đặt phải gắn booking, không làm thay đổi hồ sơ master. Check-in lưu cân thực tế từng lần và cập nhật cân hiện tại tại thu_cung. |
| D11 / Q07 | APPROVED đồng thời là ca đã công bố; không cờ published/bước publish độc lập. | Gán/chuyển người phải có ca APPROVED đúng ngày/ca thao tác; được chuyển người khi pet chưa DONE, kể cả sang ca/ngày kế tiếp của Hotel. |
| D12 / Q04 | Ảnh đầu/cuối bắt buộc; mọi ảnh nhật ký của booking xem được qua Tracking hợp lệ. Đánh giá một lần/COMPLETED, chỉ Admin đọc. | 1–2 sao: Admin liên hệ ngoài hệ thống; không ticket khiếu nại. Storage không được công khai toàn bộ chỉ vì khách được xem ảnh của một đơn. |
| D13 | Booking từ chối/no-show vẫn là CANCELLED; dùng loai_huy và ly_do_huy. | Bộ mã: CUSTOMER_CANCELLED, STAFF_REJECTED, NO_SHOW, STAFF_CANCELLED. Mọi đường hủy dùng cùng logic nhả sức chứa, không thêm enum booking REJECTED/NO_SHOW. |
| D14 | UC18 chỉ PENDING/CONFIRMED và chưa bất kỳ pet nào check-in thực tế. | Dời lịch nguyên tử; nếu không đủ tài nguyên lịch mới thì lịch cũ được giữ nguyên. Không chỉ kiểm tra trạng thái cha mà bỏ qua tiếp nhận từng pet. |

## 3. Chi tiết Q01–Q08 đã đóng

### Q01 — No-show, hủy và slot

- Hết ca 30 phút, chưa bất kỳ pet nào check-in: có thể hủy vì no-show; Staff/Admin có nút đánh dấu. Không bắt buộc background job; lazy evaluation hoặc lệnh dọn quá hạn đều được.
- Khách chỉ tự hủy PENDING/CONFIRMED khi còn ít nhất 2 giờ trước đầu ca; phải xác minh đúng SĐT/tracking.
- STAFF/Admin được hủy PENDING/CONFIRMED, bắt buộc ly_do_huy; không dùng hủy đơn để xóa pet đã check-in một phần. Phải kiểm tra dấu vết tiếp nhận thực tế, không chỉ dựa vào trạng thái cha.
- Hotel-only không giữ slot Spa. SPA/COMBO_SPA_HOTEL chỉ giữ số pet có Spa ở ca ngày bắt đầu.
- Đã duyệt D13: từ chối/no-show đều lưu CANCELLED kèm loại lý do; REJECTED chỉ là quyết định duyệt, không thêm booking state. Chọn cột loai_huy riêng thay vì prefix trong ghi chú để lọc/báo cáo rõ ràng; thiết kế chi tiết vẫn trình duyệt.
- Đã duyệt D14: dời lịch chỉ PENDING/CONFIRMED và chưa pet nào check-in; thay đổi lịch/tài nguyên cùng transaction, không làm mất lịch cũ nếu lịch mới đầy.

### Q02 — Hotel

- Kiểm tra từng đêm d: số suất phòng đang giữ + số pet muốn đặt của loại đó <= so_luong_phong.
- Dòng add-on của cùng pet không giữ thêm suất phòng. Phải tránh đếm lại cùng pet/booking khi mô hình có nhiều dòng dịch vụ.
- Không tự tính lại ngày/tiền khi trả sớm hoặc trễ vài tiếng. Trả sớm giữ giá cam kết; đón trễ xử lý bằng phụ phí. Chưa xây dựng hệ thống tự gia hạn lưu trú/quản lý overstay nhiều ngày.
- Khi booking còn hiệu lực, công suất theo khoảng đã đặt; không tự giảm từng đêm chỉ vì pet đã DONE sớm. Nếu mọi pet đã DONE và booking COMPLETED thì không còn là đơn active theo công thức đã chốt.

### Q03 — Multi-pet

- Cùng thời điểm nhận/trả dự kiến ở bảng cha. Booking qua đêm: mọi pet có loai_phong_id, có thể có/không có Spa.
- Không trộn một pet Hotel nhiều ngày và pet chỉ Spa đón ngay; tách booking.
- Được bàn giao thực tế lẻ khi pet READY theo Q08; đây là ngoại lệ thời điểm thực tế, không tạo ngày trả dự kiến độc lập cho mỗi pet.

### Q04 — Ngoại lệ phạm vi

- UC56 tối thiểu là ngoại lệ đã duyệt; không được để mock-only nữa.
- Không CRUD tài khoản; seed. Thống kê/overview chỉ aggregate nhẹ. Bỏ ticket/refund workflow live.

### Q05 — Hồ sơ/email

- Public customer trùng SĐT: map customer ID ngầm, không trả danh sách pet, không ghi đè master. Nội bộ mới được tra SĐT và chọn pet.
- Lưu tên/email người đặt theo lần booking (snapshot hoặc ghi chú có cấu trúc, cách vật lý chốt ở thiết kế). Gửi tracking của lần đặt tới email được cung cấp cho lần đó; không tự coi đó là xác minh/chỉnh email master.
- Không gộp pet theo tên. Cân nặng lịch sử ở chi_tiet_don, sức khỏe/ảnh ở nhật ký; cân hiện tại ở thu_cung.can_nang_dang_ky.
- Email bắt buộc; gửi SMTP sau khi booking được lưu. Lỗi SMTP trả booking/tracking thành công kèm cảnh báo rõ ràng, không tạo đơn lần nữa để thử email.

### Q06 — Giá/hóa đơn

- Spa đơn/combo: giá cơ sở × hệ số; Hotel: giá theo ngày × số đêm; tích hợp: cộng hai phần.
- giam_gia = 0; không tien_coc trong tính tiền core. Dịch vụ thêm/phụ phí do nhân viên ghi sau trao đổi khách, có lý do; không yêu cầu approval web.
- Có snapshot UNPAID thì không chỉnh dòng dịch vụ/phụ phí. Có thể thu hồi nháp chưa trả tiền rồi tạo lại, vẫn chỉ một hóa đơn hiệu lực. Không sửa/xóa PAID.

### Q07 — Ca/assignment

- APPROVED là đủ, không bước published. Điều kiện gán được xét ở ngày/ca hiện tại của thao tác check-in/gán/chuyển người, không yêu cầu một nhân viên có ca suốt toàn kỳ Hotel.
- Pet chưa DONE được đổi người; mọi ảnh cũ giữ nguyên tác giả. Admin có quyền vận hành/care override; việc lọc dropdown không thay kiểm tra server.

### Q08 — Hoàn tất

- Pet: chưa check-in → WAITING → IN_PROGRESS (có ảnh đầu) → READY (có ảnh cuối) → DONE. PAUSED quay lại làm sau khi xử lý phát sinh.
- Mọi pet READY/DONE: booking READY và được tạo invoice. Pet READY có thể DONE lẻ trước khi tất cả pet xong; booking vẫn theo tiến độ những pet còn lại.
- Chỉ thu đủ tiền khi mọi pet READY/DONE, invoice còn UNPAID. Một transaction: PAID + READY còn lại thành DONE + booking COMPLETED. Nếu pet đã DONE thì giữ nguyên, không yêu cầu ảnh READY mới lần nữa.

## 4. Các chi tiết kỹ thuật đã giải quyết bằng T01–T09

Các điểm dưới đây là vấn đề đầu vào đã được giải quyết trong [thiết kế 1.0](../design/system-design.md): T03 lưu ca hẹn Hotel; T05 phụ phí trước invoice; T04 trạng thái NULL trước check-in; T02/T08 dịch vụ thêm và công suất theo ca thực hiện; T06/T09 snapshot và dấu vết. Không còn là câu hỏi chờ trả lời.

- Hotel không giữ slot nhưng vẫn cần liên kết ngày/ca hẹn để tính hạn hủy/no-show; khung_gio_id có thể là lịch hẹn chứ không tự có nghĩa đã giữ slot. Cần quy ước rõ trong ERD/contract.
- Phụ phí tồn tại trước hóa đơn: FK phu_phi.hoa_don_id NOT NULL trong Data Dictionary cũ không thể giữ nguyên máy móc.
- WAITING chỉ sau check-in: phải phân biệt dòng booking mới chưa tiếp nhận với pet đã vào hàng chờ; chốt biểu diễn trong ERD/state model.
- Mô hình pet chính và dòng dịch vụ thêm phải tránh tăng slot/phòng, tránh bàn giao/đếm trạng thái hai lần. Spa phát sinh mới ở một ngày Hotel sau ngày nhận cần quy ước công suất, không âm thầm trừ về ca quá khứ; là điểm cần nêu phương án trong thiết kế tiếp theo.
- Cần lưu vết tiếp nhận, bàn giao, hủy và xác nhận tiền; chọn field/cơ chế version hóa nháp khi thiết kế, không tự thêm nhiều phân hệ.
- Bước tiếp theo: rà soát [API contract](../design/api-contract.md) và [traceability](../design/requirements-traceability.md) trước khi cho phép triển khai. Chưa sửa source/migration; không chạy app tests cho thay đổi chỉ tài liệu.

## 5. Bổ sung ngày 18/10/2026 — đã xác nhận yêu cầu

| ID | Quyết định người dùng đã chốt | Tác động và giới hạn |
|---|---|---|
| D15 | Yêu cầu xác minh giấy tiêm chủng là cấu hình tùy chọn theo loại dịch vụ, mặc định không bắt buộc. Khách luôn được đặt khi chưa tải giấy; khi cấu hình bắt buộc đang bật, nhân viên phải xác minh tại check-in mới được tiếp nhận. | Quyết định mới thay cả quy tắc cũ chỉ bắt buộc Hotel và yêu cầu bắt buộc upload cho mọi dịch vụ trước đó. Upload chỉ là đính kèm, không đồng nghĩa đã xác minh. Nhân viên kiểm tra ảnh hoặc giấy mang tới quầy, lưu người/thời điểm xác minh; client không được tự xác nhận thay nhân viên. Guard áp dụng ở mock service và Backend tương lai, không chỉ ẩn/hiện UI. |
| D16 | Thêm loại lông riêng, giữ tình trạng lông khi check-in. | Loại lông là đặc điểm hồ sơ/khai báo; tình trạng lông là quan sát lúc tiếp nhận. Chưa duyệt hệ số giá theo loại lông. |
| D17 | Ngày nghỉ chặn tiếp nhận Spa và nhận–trả Hotel, vẫn chăm sóc pet đang lưu trú. | Admin cấu hình tuần chưa mở đăng ký; Staff không đăng ký ca tiếp nhận đã đóng. Không đặt công suất Hotel bằng 0 hoặc vô hiệu ca chăm sóc Hotel chỉ vì nghỉ nhận khách. Khoảng lưu trú được đi qua ngày nghỉ nếu vẫn đủ phòng và có chăm sóc. |
| D18 | Admin được đổi ADMIN ↔ STAFF cho account. | Ngoại lệ mới so với account chỉ seed/không CRUD user. Không thêm role RECEPTION/SPA, không tự mở rộng reset mật khẩu hoặc toàn bộ quản lý nhân sự. |
| D19 | Gửi lại Tracking bằng email, gửi các mã booking chưa hoàn tất tới đúng email snapshot đã lưu; màn hình trả thông báo chung. | Không trả mã/danh sách booking trong Public response; không dùng email master thay email lần đặt. Đơn áp dụng PENDING/CONFIRMED/IN_PROGRESS/READY; không COMPLETED/CANCELLED. Cần chống gửi dồn và không tiết lộ email có hồ sơ. |

Người dùng cho phép thay đổi mock data để đáp ứng các yêu cầu trên. Không đồng nghĩa cho phép xóa IndexedDB đang có hoặc ghi đè đơn/ảnh người dùng đã tạo. Seed và cơ chế nâng dữ liệu phải được phân biệt khi triển khai.

Các yêu cầu UI đi kèm: lịch Spa theo suất ca; Hotel theo phòng/ngày với danh sách ngày còn chỗ và ngày nghỉ; thông báo đơn mới; màn từ chối có lý do/xác nhận; nút nhận/từ chối cuối chi tiết PENDING; bấm ca trên lịch để đăng ký; hiển thị thứ trước ngày. Các UC còn thiếu khác vẫn nằm trong backlog riêng, không tự gộp vào đợt này.

Đề xuất bảo vệ khi triển khai, chưa phải yêu cầu người dùng tự nêu: giữ ít nhất một Admin hoạt động; đổi role phải có hiệu lực ở service/phiên mock, không chỉ đổi nhãn; không đánh dấu ngày nghỉ mới làm vô hiệu booking/lịch đã cam kết. Trường hợp xung đột phải báo rõ để Admin xử lý trước.

[Kế hoạch frontend mục 7](../design/frontend-update-plan.md) mô tả phạm vi thực hiện. SRS Markdown đã ghi bổ sung; schema/API draft, Word và Figma cần đồng bộ sau khi chọn cách lưu loại lông/ngày nghỉ và hợp đồng thao tác mới. Không coi ERD 20 bảng cũ là đã phản ánh đủ D15–D19.
