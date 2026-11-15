# Hợp đồng API — PetSpa & Hotel

**Phiên bản:** 1.0, 03/11/2026. **Trạng thái:** APPROVED / API CONTRACT LOCKED — nhóm trưởng đã duyệt gói sáu tài liệu theo M2-15. Không đồng nghĩa API đã triển khai hoặc tests đã pass.
**Nguồn:** [SRS v3.0](../requirements/SRS_He_Thong_Quan_Ly_Spa_Thu_Cung_AI_Readable.md), [quyết định M2](../requirements/scope-and-decisions.md#m2-decisions), [thiết kế hệ thống](system-design.md). Tài liệu này thay draft 0.1/0.2; không hỗ trợ alias tùy ý cho DTO cũ.

## 1. Quy ước schema, HTTP và lỗi

Base /api. JSON business fields tiếng Việt không dấu camelCase; giữ requestId, trackingCode, challengeId, proof, accessToken và envelope kỹ thuật. ID int>0; money là số nguyên VND trong numeric(14,0), weight là số kg >0 có tối đa 2 số thập phân. date=YYYY-MM-DD, time=HH:mm:ss, instant=ISO8601 có offset; lưu timestamptz, ngày nghiệp vụ Asia/Ho_Chi_Minh. Size=S/M/L/XL. BookingState/PetState/InvoiceState đúng bảng trạng thái trong system-design.

Trong bảng DTO: field không ? bắt buộc; ? được bỏ, không tự cho phép null; response ghi |null nếu có thể null. [] là mảng, [a..b] là số phần tử; enum phân cách /. text mặc định <=2000 ký tự nếu không ghi giới hạn. secret là token opaque, không log/persist rõ phía server. PATCH phải có >=1 business field và không chấp nhận trường lạ; không nhận actor/state/giá do client áp đặt.

Success: {success:true,data:<response DTO>,message:string}. Error: {success:false,data:null,message:string,errorCode:string,errors:[{field:string,message:string}],traceId:string}. Không 200 khi lỗi nghiệp vụ. PageX={items:X[],trang:int,kichThuocTrang:int,tongSo:int}; stable sort thời gian+ID. Bộ lọc nhiều ngày giới hạn đề xuất 90 ngày; pageSize<=100.

| HTTP | errorCode và ý nghĩa |
|---|---|
| 400 | VALIDATION_FAILED, INVALID_DATE_RANGE, OTP_INVALID: sai field/enum/OTP. |
| 401 | UNAUTHENTICATED: thiếu/hết hạn JWT. |
| 403 | FORBIDDEN, PROOF_SCOPE_INVALID: thiếu permission, assignment hoặc proof sai scope. |
| 404 | NOT_FOUND, TRACKING_NOT_FOUND: ID không có; pair tracking sai/nonexistent cùng thông báo. |
| 409 | INVALID_TRANSITION, CAPACITY_EXCEEDED, INVOICE_LOCKED, IDEMPOTENCY_CONFLICT, ASSIGNMENT_UNAVAILABLE, DEPOSIT_EXPIRED, AMOUNT_MISMATCH, REVIEW_EXISTS: xung đột state/data. |
| 413 / 415 | FILE_TOO_LARGE / UNSUPPORTED_FILE. |
| 429 | RATE_LIMITED: kèm Retry-After, không dùng 423 riêng cho khóa tracking. |
| 503 | STORAGE_UNAVAILABLE, TEMPORARY_FAILURE: integration lỗi trước commit; SMTP sau booking commit không đổi success thành 503. |

Mỗi operation áp các lỗi chung thích hợp: 400 cho query/body/path sai; 401/403 nội bộ; 404 khi target không tồn tại; 409 cho command không đạt guard; 413/415 multipart; 429 Public/login/OTP/tracking; 503 hạ tầng chưa commit. Các lỗi nghiệp vụ cụ thể ở mục 5. Không để SQL/stack trace ra response.

Mọi mutation nghiệp vụ có requestId phải lưu fingerprint/kết quả hoặc kiểm tra unique và state dưới transaction. Cùng key+cùng dữ liệu trả 200 bản đã ghi, khác dữ liệu 409. Sau commit nhưng mất response vẫn retry cùng key, không thu/hoàn/gửi mail lại mù. Các key cho command ngoài booking lưu trong audit/kết quả có cấu trúc và unique theo actor/operation/requestId, không chỉ log text. Không có requestId ở login, OTP verify, tracking lookup/resend vì không tạo giao dịch tiền.

## 2. Registry từng operation

Path {id}, {petId}, {componentId}, {shiftId} là id>0; {date} là date. GET request DTO là query, None không body/query nghiệp vụ; còn lại JSON trừ multipart nêu mục 4. Status là lần đầu thành công; mọi create idempotent lặp hợp lệ trả 200. Không có route “quét hết hạn” Public; BackgroundService gọi service nội bộ theo cùng protocol.

E03 đổi GET draft thành POST cùng /api/availability để truyền nhu cầu nhiều pet/dịch vụ bằng body, tránh query nhạy cảm/phức tạp; thay đổi này đã được duyệt trong baseline M2-15. E32 giữ đường dẫn weight-coefficients để truy vết draft nhưng payload giờ là khoảng size chuẩn, không nhân hệ số vào giá trực tiếp; tên route được giữ và đã duyệt theo M2-15.

| ID | Method | Path | Permission/actor | UC | Request | Response data | Status |
|---|---|---|---|---|---|---|---|
| E01 | POST | /api/auth/login | Public | FR01 | Login | Session | 200 |
| E02 | GET | /api/catalog | Public | UC10 | None | Catalog | 200 |
| E03 | POST | /api/availability | Public | UC14 | AvailabilityQuery | Availability | 200 |
| E04 | POST | /api/bookings | Public+OTP nếu bật | UC14 | BookingCreate | Booking | 201 |
| E05 | POST | /api/internal/bookings | BOOKING_CREATE | UC14 | BookingCreate | Booking | 201 |
| E06 | GET | /api/internal/customers/lookup | CUSTOMER_LOOKUP | UC02/07 | CustomerQuery | Customer[] | 200 |
| E07 | PATCH | /api/internal/pets/{petId} | CUSTOMER_LOOKUP | UC08 | PetEdit | Pet | 200 |
| E08 | POST | /api/tracking/lookup | Public pair | UC15/26/34 | TrackingPair | Tracking | 200 |
| E09 | POST | /api/tracking/cancel | Public pair | UC19 | TrackingCancel | Booking | 200 |
| E10a | GET | /api/internal/bookings | BOOKING_APPROVE | UC16 | BookingQuery | PageBooking | 200 |
| E10b | GET | /api/internal/bookings/{id} | BOOKING_APPROVE | UC16 | None | Booking | 200 |
| E11 | POST | /api/internal/bookings/{id}/confirm | BOOKING_APPROVE | UC17 | Command | Booking | 200 |
| E12 | POST | /api/internal/bookings/{id}/cancel | BOOKING_APPROVE | UC17/19 | Cancel | Booking | 200 |
| E13 | POST | /api/internal/bookings/{id}/reschedule | BOOKING_RESCHEDULE | UC18 | Reschedule | Booking | 200 |
| E14 | GET | /api/internal/assignment-candidates | CHECKIN_PERFORM | UC23B | ShiftQuery | Employee[] | 200 |
| E15 | POST | /api/internal/booking-pets/{id}/check-in | CHECKIN_PERFORM | UC23 | Checkin | BookingPet | 200 |
| E16 | PUT | /api/internal/booking-pets/{id}/assignment | CHECKIN_PERFORM | UC23B | Assignment | BookingPet | 200 |
| E17 | POST | /api/internal/booking-pets/{id}/care-actions | CARE_UPDATE | UC24 | CareAction | BookingPet | 200 |
| E18 | POST | /api/internal/booking-pets/{id}/care-logs | PHOTO_UPLOAD | UC25 | CareLogCreate | CareLog | 201 |
| E19 | POST | /api/internal/booking-pets/{id}/handover | PET_HANDOVER | UC30 | Handover | BookingPet | 200 |
| E20 | POST | /api/internal/booking-pets/{id}/extra-services | INVOICE_CREATE | UC32 | ExtraService | OrderService | 201 |
| E21 | POST | /api/internal/extra-services/{id}/actions | CARE_UPDATE hoặc PET_CANCEL_LINE | UC24/32 | ServiceAction | OrderService | 200 |
| E22a | POST | /api/internal/bookings/{id}/fees | INVOICE_CREATE | UC32 | FeeCreate | Fee | 201 |
| E22b | PATCH | /api/internal/fees/{id} | INVOICE_CREATE | UC32 | FeeEdit | Fee | 200 |
| E22c | POST | /api/internal/fees/{id}/cancel | INVOICE_CREATE | UC32 | Reason | Fee | 200 |
| E23 | POST | /api/internal/bookings/{id}/invoices | INVOICE_CREATE | UC31 | Command | Invoice | 201 |
| E24 | GET | /api/internal/invoices/{id} | INVOICE_CREATE | UC34 | None | Invoice | 200 |
| E25 | POST | /api/internal/invoices/{id}/withdraw | INVOICE_REVOKE | UC31 | Reason | Invoice | 200 |
| E26 | POST | /api/internal/invoices/{id}/checkout | INVOICE_COLLECT | UC35 | Collect | Invoice | 200 |
| E27 | GET | /api/internal/work-schedule | SCHEDULE_REGISTER | UC53 | ScheduleQuery | ShiftRegistration[] | 200 |
| E28 | POST | /api/internal/shift-registrations | SCHEDULE_REGISTER | UC41 | ShiftRegister | ShiftRegistration | 201 |
| E29 | POST | /api/admin/shift-registrations/{id}/decision | SCHEDULE_APPROVE | UC50 | ShiftDecision | ShiftRegistration | 200 |
| E30a | POST | /api/admin/services | SYSTEM_CONFIG | UC11 | ServiceWrite | Service | 201 |
| E30b | PATCH | /api/admin/services/{id} | SYSTEM_CONFIG | UC12 | ServicePatch | Service | 200 |
| E31a | POST | /api/admin/combos | SYSTEM_CONFIG | UC11 | ComboWrite | Combo | 201 |
| E31b | PATCH | /api/admin/combos/{id} | SYSTEM_CONFIG | UC12 | ComboPatch | Combo | 200 |
| E32 | PUT | /api/admin/weight-coefficients | SYSTEM_CONFIG | UC64 | SizeWrite | SizeBand[] | 200 |
| E33 | PATCH | /api/admin/room-types/{id} | SYSTEM_CONFIG | UC64 | RoomPatch | RoomType | 200 |
| E34a | GET | /api/admin/capacity-settings | SYSTEM_CONFIG | UC64 | None | Settings | 200 |
| E34b | PUT | /api/admin/shifts/{id} | SYSTEM_CONFIG | UC64.1 | ShiftWrite | Shift | 200 |
| E34c | PUT | /api/admin/slots/{date}/{shiftId} | SYSTEM_CONFIG | UC64.2 | SlotWrite | Slot | 200 |
| E35 | POST | /api/tracking/reviews | Public pair | UC56 | ReviewCreate | Review | 201 |
| E36 | GET | /api/admin/reviews | REVIEW_READ | UC56 | PageQuery | PageReview | 200 |
| E37 | GET | /api/internal/overview | BOOKING_APPROVE | UC16/35 | DateQuery | Overview | 200 |
| E38 | GET | /api/internal/care-tasks | CARE_UPDATE | UC53 | BookingQuery | PageBookingPet | 200 |
| E39 | POST | /api/otp/challenges | Public | UC14/OTP | OtpSend | OtpChallenge | 202 |
| E40 | POST | /api/otp/verify | Public | UC14/OTP | OtpVerify | OtpProof | 200 |
| E41 | POST | /api/verified-pets/lookup | Public proof EXISTING_PETS | UC07/14 | VerifiedPetQuery | Pet[] | 200 |
| E42 | POST | /api/internal/deposits/{id}/receipts | DEPOSIT_CONFIRM | Cọc | ReceiptCreate | Deposit | 201 |
| E43 | GET | /api/internal/deposits | DEPOSIT_CONFIRM | Cọc | BookingQuery | PageDeposit | 200 |
| E44 | POST | /api/admin/deposits/{id}/refunds | DEPOSIT_REFUND | Hoàn cọc | RefundCreate | Refund | 201 |
| E45 | POST | /api/admin/refunds/{id}/confirm | DEPOSIT_REFUND | Hoàn cọc | RefundConfirm | Refund | 200 |
| E46 | GET | /api/admin/refunds | DEPOSIT_REFUND | Hoàn cọc | BookingQuery | PageRefund | 200 |
| E47 | POST | /api/admin/booking-pets/{id}/cancel | PET_CANCEL_LINE | UC19B | PetCancel | BookingPet | 200 |
| E48 | POST | /api/admin/booking-pets/{id}/early-handover | EARLY_HANDOVER_APPROVE | UC30 | Reason | BookingPet | 200 |
| E49 | POST | /api/internal/booking-pets/{id}/return-cancelled | PET_HANDOVER | UC19B | Command | BookingPet | 200 |
| E50 | POST | /api/tracking/resend | Public | UC15/D19 | TrackingResend | Acknowledged | 202 |
| E51 | POST | /api/admin/tracking/unlock | TRACKING_UNLOCK | UC15 | Unlock | Acknowledged | 200 |
| E52 | POST | /api/admin/booking-pets/{id}/photo-exception | PHOTO_EXCEPTION_APPROVE | UC24 | Reason | PhotoException | 201 |
| E53 | PUT | /api/admin/employees/{id}/permissions | PERMISSION_MANAGE | FR01 | PermissionsWrite | Employee | 200 |
| E54 | POST | /api/admin/shift-windows | SCHEDULE_APPROVE | UC41 | ShiftWindowWrite | ShiftWindow | 201 |
| E55 | POST | /api/admin/shift-registrations/{id}/transfer | SCHEDULE_APPROVE | UC50 | ShiftTransfer | ShiftRegistration | 200 |
| E56 | PUT | /api/admin/holidays/{date} | SYSTEM_CONFIG | UC64.4 | HolidayWrite | Holiday | 200 |
| E57 | PUT | /api/admin/extension-settings | SYSTEM_CONFIG | Extension | ExtensionWrite | ExtensionSettings | 200 |
| E58 | POST | /api/admin/bookings/{id}/vaccination-policy | SYSTEM_CONFIG | M2-08 | VaccinationOverride | Booking | 200 |
| E59 | POST | /api/internal/bookings/{id}/late-fee-waiver | INVOICE_COLLECT | M2-03 | FeeWaiver | Acknowledged | 201 |
| E60 | PUT | /api/admin/employees/{id}/role | PERMISSION_MANAGE | D18 | RoleWrite | Employee | 200 |
| E61 | POST | /api/internal/order-services/{id}/components/{componentId}/complete | CARE_UPDATE | UC24/19B | Command | OrderService | 200 |

## 3. Dictionary DTO

### 3.1 Request và kiểu lồng nhau

| DTO | Trường và kiểu |
|---|---|
| None | Không body/query ngoài path. |
| Command | requestId:uuid |
| Reason | requestId:uuid; lyDo:text(1..1000) |
| Login | tenDangNhap:text(1..50); matKhau:text(1..128) |
| PageQuery | trang?:int>=1=1; kichThuocTrang?:int[1,100]=20 |
| DateQuery | tuNgay:date; denNgay:date |
| BookingQuery | trang?:int>=1=1; kichThuocTrang?:int[1,100]=20; tuNgay?:date; denNgay?:date; trangThai?:code |
| CustomerQuery | soDienThoai:text(10 chữ số) |
| ShiftQuery | ngay:date; maCa:id |
| ScheduleQuery | tuNgay:date; denNgay:date; maNhanVien?:id (Admin-only) |
| AvailabilityQuery | maKhungGio:id; ngayTra:date; thuCung:PetDemand[] |
| PetDemand | maLoaiPhong?:id; dichVu:ServiceSelection[] |
| ServiceSelection | maDichVu?:id; maCombo?:id (XOR); maKhungGio:id |
| BookingCreate | requestId:uuid; lienHe:Contact; maKhungGio:id; ngayTra:date; thuCung:PetSelection[1..20]; ghiChu?:text(0..2000); bookingProof?:secret; petProof?:secret; maKhachHang?:id (nội bộ hoặc proof scope) |
| Contact | hoTen:text(1..150); soDienThoai:text(10 chữ số); email:email |
| PetSelection | maThuCung?:id; tenThuCung?:text(1..150); loaiPet?:CHO/MEO; giong?:text(0..150); canNangKhaiBao:weight; loaiLong?:text(0..100); ghiChuDiUng?:text(0..2000); maLoaiPhong?:id; dichVu:ServiceSelection[]; vaccinationFileKey?:text; maHoSoTiemChung?:id |
| PetEdit | requestId:uuid; tenThuCung?:text(1..150); giong?:text(0..150); canNang?:weight; ghiChuDiUng?:text(0..2000); loaiLong?:text(0..100); ngaySinh?:date |
| TrackingPair | soDienThoai:text(10 chữ số); trackingCode:text(8) |
| TrackingCancel | soDienThoai:text(10 chữ số); trackingCode:text(8); requestId:uuid; lyDo:text(1..1000) |
| Cancel | requestId:uuid; loaiHuy:CUSTOMER_CANCELLED/STAFF_REJECTED/NO_SHOW/STAFF_CANCELLED; lyDo:text(1..1000) |
| Reschedule | requestId:uuid; maKhungGio:id; ngayTra:date; lyDo:text(1..1000) |
| Checkin | requestId:uuid; canNangThucTe:weight; tinhTrangLong:BINH_THUONG/ROI_NHE/ROI_NANG/VE_RAN; maNhanVien:id; ghiChuSucKhoe:text(1..2000); quyetDinhGia:SIZE_THUC_TE/THU_GON; xacNhanKhach:true; noiDungThuGon?:text(1..1000); maHoSoTiemChung?:id; xacNhanGiayHopLe?:bool |
| Assignment | requestId:uuid; maNhanVien:id; lyDo:text(1..1000) |
| CareAction | requestId:uuid; hanhDong:START/PAUSE/RESUME/READY; ghiChu?:text(1..2000); maNgoaiLeAnh?:id |
| CareLogCreate | requestId:uuid; ghiChu?:text(1..2000); canNangTheoDoi?:weight |
| Handover | requestId:uuid; xacNhanDaNhan:true |
| ExtraService | requestId:uuid; maDichVu?:id; maCombo?:id (XOR); maKhungGio:id; lyDo:text(1..1000) |
| ServiceAction | requestId:uuid; hanhDong:COMPLETE/CANCEL; lyDo?:text(1..1000) |
| FeeCreate | requestId:uuid; tenPhuPhi:text(1..150); soTien:money>0; lyDo:text(1..1000); maChiTietDon?:id |
| FeeEdit | requestId:uuid; tenPhuPhi?:text(1..150); soTien?:money>0; lyDo:text(1..1000) |
| Collect | requestId:uuid; phuongThuc:CASH/BANK_TRANSFER; soTienThucNhan:money; soTienDaTraLai:money; thamChieu?:text(1..150); bangChungTraThua?:text(1..2000) |
| ShiftRegister | requestId:uuid; ngay:date; maCa:id |
| ShiftDecision | requestId:uuid; quyetDinh:APPROVE/REJECT; lyDo?:text(1..1000) |
| ServiceWrite | requestId:uuid; tenDichVu:text(1..150); loaiDichVu:text(1..40); thoiLuongPhut:int>0; yeuCauTiemChung:bool; bangGia:SizePrice[4]; moTa?:text; dangHoatDong:bool |
| ServicePatch | requestId:uuid; tenDichVu?:text(1..150); thoiLuongPhut?:int>0; yeuCauTiemChung?:bool; bangGia?:SizePrice[4]; moTa?:text; dangHoatDong?:bool |
| ComboWrite | requestId:uuid; tenCombo:text(1..150); maDichVu:id[1..50]; bangGia:SizePrice[4]; moTa?:text; dangHoatDong:bool |
| ComboPatch | requestId:uuid; tenCombo?:text(1..150); maDichVu?:id[1..50]; bangGia?:SizePrice[4]; moTa?:text; dangHoatDong?:bool |
| SizeWrite | requestId:uuid; khoangSize:SizeBand[4] |
| RoomPatch | requestId:uuid; tenPhong?:text(1..150); giaMoiDem?:money; soLuongPhong?:int>=0; dangHoatDong?:bool |
| ShiftWrite | requestId:uuid; tenCa:text(1..150); gioBatDau:time; gioKetThuc:time; sucChuaPet:int>=0; tranPhut:int>=0; dangHoatDong:bool |
| SlotWrite | requestId:uuid; moNhanKhach:bool; tongSlot:int>=0; tranPhut:int>=0 |
| ReviewCreate | soDienThoai:text(10 chữ số); trackingCode:text(8); requestId:uuid; soSao:int[1,5]; binhLuan?:text(0..2000); maChiTietDon?:id; maDichVuTrongDon?:id |
| OtpSend | requestId:uuid; mucDich:BOOKING/EXISTING_PETS; soDienThoai:text(10 chữ số); email:email |
| OtpVerify | challengeId:uuid; maOtp:text(6 chữ số) |
| VerifiedPetQuery | petProof:secret |
| ReceiptCreate | requestId:uuid; soTien:money>0; phuongThuc:CASH/BANK_TRANSFER; giaoDichAt:instant; maGiaoDich?:text(1..150); nganHang?:text(1..100); ghiChu?:text |
| RefundCreate | requestId:uuid; soTien:money>0; lyDo:text(1..1000) |
| RefundConfirm | requestId:uuid; phuongThuc:CASH/BANK_TRANSFER; hoanAt:instant; thamChieu:text(1..150); bangChungFileKey?:text |
| PetCancel | requestId:uuid; lyDo:text(1..1000); soDemDaO:int>=0; maThanhPhanDaLam:id[] |
| TrackingResend | soDienThoai:text(10 chữ số); email:email; ngayHen?:date |
| Unlock | requestId:uuid; soDienThoai:text(10 chữ số); ip:text; lyDo:text(1..1000) |
| PermissionsWrite | requestId:uuid; quyen:text[]; lyDo:text(1..1000) |
| ShiftWindowWrite | requestId:uuid; tuNgay:date; denNgay:date; moAt:instant; dongAt:instant |
| ShiftTransfer | requestId:uuid; ngay:date; maCa:id; lyDo:text(1..1000) |
| HolidayWrite | requestId:uuid; nghiNhanKhach:bool; lyDo:text(1..1000) |
| ExtensionWrite | requestId:uuid; otpEmail:bool; cocHotel:bool; giuCho:bool |
| VaccinationOverride | requestId:uuid; maDichVuTrongDon:id[]; yeuCau:bool; lyDo:text(1..1000); xacNhanDaThongBao:true |
| FeeWaiver | requestId:uuid; soTienDuKien:money; lyDo:text(1..1000) |
| RoleWrite | requestId:uuid; vaiTro:ADMIN/STAFF; lyDo:text(1..1000) |

### 3.2 Response data và kiểu lồng nhau

Các response scalar/object không có trường tùy ý ngoài bảng. Các kiểu dùng lại Contact/SizePrice/SizeBand dùng đúng schema chung; PageBooking/PageReview/PageBookingPet/PageDeposit/PageRefund dùng quy tắc PageX mục 1.

| DTO | Trường và kiểu |
|---|---|
| Acknowledged | daTiepNhan:true; thongBao:text |
| Session | accessToken:secret; expiresIn:int>0; nhanVien:Employee |
| Employee | maNhanVien:id; tenNhanVien:text; tenDangNhap:text; vaiTro:ADMIN/STAFF; quyen:text[]; dangHoatDong:bool |
| Pet | maThuCung:id; tenThuCung:text; loaiPet:CHO/MEO; giong:text hoặc null; canNang:weight; ngaySinh:date hoặc null; loaiLong:text hoặc null; ghiChuDiUng:text hoặc null |
| Customer | maKhachHang:id; hoTen:text; soDienThoai:text; email:email; thuCung:Pet[] |
| Catalog | dichVu:Service[]; combo:Combo[]; loaiPhong:RoomType[]; khoangSize:SizeBand[] |
| SizeBand | maSize:S/M/L/XL; tuKg:number; denKg:number hoặc null |
| SizePrice | maSize:S/M/L/XL; donGia:money |
| Service | maDichVu:id; tenDichVu:text; loaiDichVu:text; thoiLuongPhut:int; yeuCauTiemChung:bool; bangGia:SizePrice[]; moTa:text hoặc null; dangHoatDong:bool |
| Combo | maCombo:id; tenCombo:text; thanhPhan:Service[]; bangGia:SizePrice[]; moTa:text hoặc null; dangHoatDong:bool |
| RoomType | maLoaiPhong:id; tenPhong:text; giaMoiDem:money; soLuongPhong:int; dangHoatDong:bool |
| Availability | maKhungGio:id; slotConLai:int; phutConLai:int; phong:RoomAvailability[]; coTheDat:bool; lyDo:text hoặc null |
| RoomAvailability | maLoaiPhong:id; ngay:date; suatConLai:int |
| Booking | maDon:id; trackingCode:text; trangThai:BookingState; batDauDuKien:instant; ketThucDuKien:instant; lienHe:Contact; tongTamTinh:money; thuCung:BookingPet[]; tienCoc:Deposit hoặc null; hoaDon:Invoice hoặc null; emailDelivery:SENT/FAILED |
| BookingPet | maChiTietDon:id; maThuCung:id; tenThuCung:text; trangThai:PetState; canNangChot:weight hoặc null; sizeThucTe:Size hoặc null; sizeChot:Size hoặc null; maLoaiPhong:id hoặc null; tenNhanVienPhuTrach:text hoặc null; dichVu:OrderService[]; nhatKy:CareLog[]; nhanSom:bool; banGiaoAt:instant hoặc null; traLaiAt:instant hoặc null |
| OrderService | maDichVuTrongDon:id; maDichVu:id hoặc null; maCombo:id hoặc null; maKhungGio:id; tenDichVu:text; trangThai:CHO_THUC_HIEN/HOAN_TAT/HUY; sizeChot:Size hoặc null; giaChot:money hoặc null; thanhPhan:Component[] |
| Component | maThanhPhan:id; maDichVu:id; tenDichVu:text; trangThai:CHO_THUC_HIEN/HOAN_TAT/HUY; hoanTatAt:instant hoặc null |
| CareLog | maNhatKy:id; loaiNhatKy:text; ghiChu:text hoặc null; anhUrl:url hoặc null; canNangTheoDoi:weight hoặc null; tenTacGia:text; ghiNhanAt:instant |
| Fee | maPhuPhi:id; maDon:id; maChiTietDon:id hoặc null; tenPhuPhi:text; soTien:money; lyDo:text; huyAt:instant hoặc null |
| Invoice | maHoaDon:id; soHoaDon:text; maDon:id; thayTheHoaDon:id hoặc null; trangThai:InvoiceState; lapAt:instant; nguoiLap:text; khach:Contact; ngayHen:date; dongTien:InvoiceLine[]; tongTien:money; cocKhauTru:money; conPhaiTra:money; paidAt:instant hoặc null; thuHoiAt:instant hoặc null; qr:Qr hoặc null |
| InvoiceLine | maDongHoaDon:id; loaiKhoan:SPA/HOTEL/PHU_PHI; maChiTietDon:id hoặc null; maDichVuTrongDon:id hoặc null; maPhuPhi:id hoặc null; tenPet:text hoặc null; tenKhoan:text; size:Size hoặc null; soLuong:int>0; donGia:money; thanhTien:money; dienGiai:text hoặc null |
| Qr | imageUrl:url; soTien:money; noiDungChuyenKhoan:text |
| Deposit | maTienCoc:id; maDon:id; trangThaiYeuCau:CHO_THU/DA_DAP_UNG/HET_HAN/HUY; coSoTinh:money; tyLe:number; soTienYeuCau:money; hetHanAt:instant; thucNhan:money; khauTru:money; giuLai:money; danhHoan:money; daHoan:money; hoanCoc:Refund[] |
| Refund | maHoanCoc:id; maTienCoc:id; soTien:money; lyDo:text; trangThai:CHO_HOAN/DA_HOAN; hoanAt:instant hoặc null; thamChieu:text hoặc null |
| Tracking | maDon:id; trackingCode:text; trangThai:BookingState; batDauDuKien:instant; ketThucDuKien:instant; tongTamTinh:money; thuCung:BookingPet[]; hoaDon:Invoice hoặc null; tienCoc:Deposit hoặc null; hanhDongChoPhep:text[] |
| Shift | maCa:id; tenCa:text; gioBatDau:time; gioKetThuc:time; sucChuaPet:int; tranPhut:int; dangHoatDong:bool |
| Slot | maKhungGio:id; ngay:date; maCa:id; moNhanKhach:bool; tongSlot:int; slotDaDat:int; phutDaGiu:int; tranPhut:int |
| ShiftRegistration | maDangKy:id; maNhanVien:id; ngay:date; maCa:id; trangThai:PENDING/APPROVED/REJECTED; lyDoDieuChuyen:text hoặc null |
| ShiftWindow | maDot:id; tuNgay:date; denNgay:date; moAt:instant; dongAt:instant |
| Holiday | ngay:date; nghiNhanKhach:bool; lyDo:text |
| ExtensionSettings | otpEmail:bool; cocHotel:bool; giuCho:bool; phienBan:int |
| Settings | ca:Shift[]; loaiPhong:RoomType[]; ngayNghi:Holiday[]; extension:ExtensionSettings |
| PhotoException | maNgoaiLe:id; maChiTietDon:id; lyDo:text; duyetAt:instant |
| OtpChallenge | challengeId:uuid; hetHanAt:instant; guiLaiSau:int |
| OtpProof | proof:secret; mucDich:BOOKING/EXISTING_PETS; hetHanAt:instant |
| Review | maDanhGia:id; maDon:id; soSao:int; binhLuan:text hoặc null; anhUrl:url[]; maChiTietDon:id hoặc null; maDichVuTrongDon:id hoặc null; taoAt:instant |
| Overview | ngay:date; soDonCho:int; soPetTrongTiem:int; soPetSanSang:int; doanhThu:money hoặc null |
## 4. Multipart, ownership và projection

E04/E05: multipart/form-data gồm payload JSON BookingCreate và file được từng pet tham chiếu bằng vaccinationFileKey. Tên/loài của pet mới bắt buộc; pet cũ chỉ dùng ID sau proof đúng hồ sơ hoặc xác minh quầy. Mọi pet phải có dịch vụ hoặc Hotel. ServiceSelection XOR dịch vụ/combo, ca hợp lệ. Không gộp pet theo tên; khi SĐT dùng chung mà chưa xác định master an toàn thì tạo/giữ thông tin người đặt theo snapshot và yêu cầu quầy xử lý liên kết, không trả master cũ.

E15: payload Checkin + file anhTiepNhan bắt buộc. E17: payload CareAction + anhThanhPham bắt buộc khi READY, trừ maNgoaiLeAnh Admin duyệt hợp lệ. START kế thừa ảnh check-in, PAUSE có lý do. E18: payload CareLogCreate + anh tùy chọn, phải có nội dung hoặc ảnh. E35: payload ReviewCreate + anh[] tùy chọn. E45: payload RefundConfirm + file bằng chứng nếu cần, không nhận arbitrary object path.

Đề xuất JPEG/PNG/WebP <=5MiB/file, <=10file/request; validate signature/decode, server sinh đường dẫn. File upload ngoài SQL transaction; kiểm tra lại permission/state trước commit metadata. File orphan vẫn private và được dọn; không giữ SQL lock khi chờ upload/xóa.

Booking và Tracking dùng projection Public-safe: không password/JWT, object path, tên đăng nhập, ghi chú nội bộ, audit/override nội bộ hay danh sách đơn khác. CareLog chỉ nội dung được phép xem của đúng booking. Deposit/Refund không lộ tài khoản ngân hàng hoặc chứng từ có dữ liệu nhạy cảm; response chỉ tham chiếu hiển thị đã được che phù hợp. Catalog không có dữ liệu khách. Internal customer lookup exact SĐT, redact query khỏi log. Overview.doanhThu chỉ Admin, người khác null.

## 5. Guard và HTTP lỗi theo nhóm

| Operation | Guard và lỗi nghiệp vụ cụ thể |
|---|---|
| E03–05 | Ngày nghỉ nhận/trả, nhân sự APPROVED đủ quyền care, hai trần pet/phút, phòng từng đêm; không đủ 409 CAPACITY_EXCEEDED. Proof thiếu khi yêu cầu 403; giá/state/tracking client gửi 400. Booking và mọi con commit nguyên tử; retry requestId đã commit đọc kết quả trước kiểm tra proof đã dùng/hết hạn; SMTP lỗi success kèm emailDelivery FAILED. |
| E08/09/35 | Xác minh lại pair từng request, không dùng bookingId bỏ xác minh; sai 404 TRACKING_NOT_FOUND, bị khóa 429. E09 chỉ >=2h trước ca, trước mọi check-in. E35 COMPLETED trong 30 ngày, một lần, source đúng đơn; trùng 409 REVIEW_EXISTS. |
| E11/12/13 | Cọc đủ nếu yêu cầu mới duyệt; từ chối chỉ PENDING; no-show sau cuối ca+30 phút, chưa nhận, không liên hệ. Hủy muộn <2h Admin-only dù có BOOKING_APPROVE. Dời chỉ PENDING/CONFIRMED trước mọi check-in; hợp nhất khóa tài nguyên, lỗi giữ lịch cũ. |
| E15/16 | Pet NOT_ARRIVED trong đơn CONFIRMED hoặc IN_PROGRESS do pet khác; chưa invoice hiệu lực. Assignment có ca APPROVED hiện tại; 409 ASSIGNMENT_UNAVAILABLE. Cân/size theo bảng cam kết, THU_GON cần nội dung; không đồng thuận chưa check-in. Giấy hợp lệ khi policy snapshot yêu cầu, actor server. |
| E17/18/61 | Assignment hiện tại + ca phù hợp; Admin override actor. PAUSED chỉ RESUME; READY cần phần việc xong và ảnh/ngoại lệ. Thành phần hoàn tất tạo dấu thời điểm, không giả hoàn tất cả combo. 403 khi actor sai, 409 khi state sai. |
| E19/48/49 | Bình thường invoice PAID rồi mới DONE; E48 Admin thực hiện duyệt+bàn giao READY trong cùng transaction và còn pet thật sau thao tác. Chặn hai request nhận hết. E49 chỉ pet CANCELLED, ghi traLaiAt sau kiểm tra quyết toán/ngoại lệ hợp lệ; không đổi DONE. |
| E20/21/22 | Chưa invoice hiệu lực, pet chưa DONE; Spa thêm ở WAITING/IN_PROGRESS/PAUSED, dùng size chốt; giữ cả phút và suất ca thực hiện. CANCEL cần PET_CANCEL_LINE/Admin theo nhánh và phần việc chưa hoàn tất; không hủy phần đã làm để nhả lại phút. COMPLETE cần care. Phí gỡ rối phải có ROI_NANG hoặc ảnh chứng minh. |
| E23/25 | Một invoice hiệu lực; pet không hủy READY/DONE và pet hủy đã quyết toán. Toàn pet hủy vẫn invoice phần đã làm. Invoice không rỗng; zero due được cọc bao phủ vẫn invoice/PAID có xác nhận phân bổ. Thu hồi chỉ UNPAID; giữ lines, ẩn QR; bản mới maHoaDon mới và thayTheHoaDon. |
| E26 | soTienThucNhan-soTienDaTraLai đúng conPhaiTra; cọc phân bổ hợp lệ, tiền thừa đã trả có dấu vết. Sai 409 AMOUNT_MISMATCH, giữ UNPAID. Chỉ PAID, không DONE. Retry không thu lại; không giao dịch thu 0 khi cọc đã đủ. |
| E28/29/54/55 | Đợt đang mở, không chồng ca; APPROVED là công bố. Đổi ca đã duyệt chỉ Admin có lý do; không phá assignment hoặc giảm phút dưới cam kết. |
| E30–34/56/57 | Giá đủ 4 size, combo đúng thành phần, khoảng size chuẩn, không giảm công suất dưới reservation. Giá/policy mới không đổi snapshot đơn cũ. Ngày nghỉ đóng nhận/trả mới, giữ trực Hotel đang ở. cocHotel=true yêu cầu giuCho=true. |
| E39/40/41 | Email OTP mới chỉ BOOKING, EXISTING_PETS phải email master đúng hồ sơ. Không tìm thấy vẫn E39 trả challenge giả/thông báo chung, không tiết lộ master. Wrong OTP 400; hết hạn/rate 429 hoặc 400 OTP_INVALID không tiết lộ hồ sơ. Proof TTL/scope kiểm tra server, dùng cho lookup chỉ đúng hồ sơ; booking tiêu thụ proof một lần và gắn requestId. |
| E42–46 | Nhân viên đối soát thực nhận; thiếu không đổi deadline, đủ trước hạn mới đáp ứng; hết hạn không hồi sinh đơn. Tiền muộn vẫn ghi thực nhận và tạo nghĩa vụ hoàn. Khoản dành hoàn gồm CHO_HOAN; không khấu trừ/giữ/hoàn vượt thực nhận; sai 409 AMOUNT_MISMATCH. E45 chỉ Admin sau thực trả, không trả lại lần nữa khi retry. |
| E47 | Admin, chưa invoice hiệu lực, chưa nhận sớm; soDemDaO và thành phần đã làm phải khớp bằng chứng/server, không tin giá client. Tính giá lẻ snapshot cho combo dở, Hotel đêm đã ở; nhả phần chưa dùng, giữ dấu pet đang ở thực tế. |
| E50/51 | Resend chỉ email snapshot các đơn chưa kết thúc theo D19; luôn 202 cùng message, không trả mã/list. Mất mã đơn hủy còn hoàn thì quầy hỗ trợ. Unlock Admin reset scope cụ thể, audit; không bypass pair. |
| E52/53/60 | Admin-only; ngoại lệ ảnh gắn pet/lý do trong audit. Đổi quyền/role giữ >=1 Admin hoạt động, không tự mở CRUD tài khoản/reset password. Thay quyền có hiệu lực request tiếp theo. |
| E58/59 | Đổi vaccine đơn cũ có lý do và xác nhận thông báo; không sửa snapshot lịch sử invoice/giá. Miễn phí đón trễ sau PAID ghi audit, không tạo invoice phụ hoặc thu thêm. |

## 6. Ví dụ và quy tắc ánh xạ

Ví dụ response lỗi: {"success":false,"data":null,"message":"Không đủ công suất ca đã chọn.","errorCode":"CAPACITY_EXCEEDED","errors":[],"traceId":"server-generated"}.

Ví dụ Tracking request: {"soDienThoai":"0912345678","trackingCode":"TRK-8X2A"}. Mã 8 ký tự theo SRS; server sinh ngẫu nhiên, unique, không suy ra từ ID/SĐT.

DB can_nang_chot -> JSON canNangChot; size_chot ->sizeChot; so_dien_thoai ->soDienThoai; don_dat_lich.id ->maDon; chi_tiet_don.id ->maChiTietDon; hoa_don.id ->maHoaDon, mã hiển thị ->soHoaDon. Không đổi CT thành chứng từ; ct_hoa_don là chi tiết hóa đơn snapshot.

QR chỉ invoice UNPAID hiệu lực và conPhaiTra>0; nội dung mã đơn/mã invoice, không SĐT/email. Deposit QR chỉ yêu cầu còn hiệu lực; hết hạn ẩn và hướng dẫn quầy, không tự xác nhận. Không Public command PAID/DA_HOAN.

## 7. Cửa khóa

68 operations thuộc baseline được nhóm trưởng duyệt, không chứng minh endpoints đã tồn tại. Route bổ sung E39–61, E03 POST và ngữ nghĩa E32 đã được chấp nhận trong M2-15. Các tên DTO mới là quyết định thiết kế đã duyệt, không phải tên được suy diễn là có sẵn trong SRS.

Tracking khóa từ lần sai thứ 5 theo checklist mới nhất. NFR <500ms đo xử lý API/DB sau khi nhận đủ request; truyền file và SMTP đo riêng theo M2-14, không đổi emailDelivery sang QUEUED. Gói đã được duyệt và khóa theo M2-15; kiểm chứng OpenAPI/contract tests khi triển khai, không tự thay đổi route/DTO/status. Không chạy xUnit/Vitest cho bản tài liệu, không tuyên bố concurrency/latency đã kiểm chứng.
