# KẾ HOẠCH TRIỂN KHAI SPRINT 2 — DEV A (BOOKING, HOTEL, KHÁCH HÀNG & TRACKING)

**Phụ trách:** Dev A  
**Phạm vi:** 3 Issues (`#15`, `#16`, `#17`) — **7 Operations mới** (`E05, E06, E07, E09, E10b, E13, E50`) + **Gỡ 2 chốt chặn Sprint 1** (`G2`: mở rộng `E03/E04` cho Hotel/Combo/Cọc 30%; `G3`: mở rộng `E11/E12` cho Hotel/Cọc).  
**Tổng ước tính:** ~16 giờ code + unit/integration test.  
**Kế hoạch tổng thể:** [sprint-2-plan.md](sprint-2-plan.md)  
**Nguồn chuẩn (Single Source of Truth):**
- [docs/design/api-contract.md](../../design/api-contract.md) (mục 2, 3, 4, 5)
- [docs/design/system-design.md](../../design/system-design.md) (mục 4.7, 5, 6.1, 7.10, 7.11, 8)
- [docs/requirements/scope-and-decisions.md](../../requirements/scope-and-decisions.md) (`D04, D05, D06, D07, D10, D13, D14, D15, D16, D17, D19, Q01, Q05, M2-01, M2-06, M2-11, M2-13`)
- [backend/README.md](../../../backend/README.md) và [.github/CONTRIBUTING.md](../../../.github/CONTRIBUTING.md)

## Handoff foundation Issue #14 (2026-11-22)

Đọc [spec #14](../../superpowers/specs/2026-11-22-issue-14-sprint2-foundation-design.md) và [implementation plan](../../superpowers/plans/2026-11-22-issue-14-sprint2-foundation.md) trước khi consume foundation.

- Writer chuẩn bị `slotIds`/`roomTypeIds` liên quan (mảng rỗng nếu không có), truyền named args `targetKhungGioIds`/`targetRoomTypeIds` khi gọi expiry. Dọn đủ hold quá hạn tại cutoff qua nhiều batch nếu cần, tối đa 20 booking mỗi transaction. Cả hai filters null là global sweep dành worker; filter explicit rỗng là no-op. Hai tập IDs dùng OR.
- Cleanup commit độc lập trước transaction chính. Nếu hold khác vừa hết hạn trong lúc writer chờ lock, thả/rollback transaction chính rồi dọn với cutoff mới; không khóa slot/room rồi quay lại khóa booking khác. Sau cleanup nạp lại entity/counters; không dùng graph cũ đang track như dữ liệu mới.
- `now >= HetHanAt` là hết hạn. E42 giữ booking → slots → rooms → deposit trước `ExpireLockedBookingDepositAsync`, không SaveChanges/transaction lồng trong helper. Nếu receipt đến sau HET_HAN, E42 vẫn ghi nhận và bổ sung CHO_HOAN cho phần mới thu chưa dành hoàn dưới cùng booking lock, không hồi sinh reservation.
- Hủy toàn bộ pet qua E47 giữ `LoaiHuy == null`, không tự phạt toàn đơn. Nhánh trước check-in (PENDING/CONFIRMED, thực tế mọi CheckInAt null) có thể CANCELLED khi không còn invoice UNPAID dù vẫn còn xử lý hoàn; nhánh M2-05 đang IN_PROGRESS vẫn chờ quyết toán/hoàn/trả đủ. Caller cập nhật pet state trước terminal helper; settlement tính target theo nightsStayed dù state pet đổi trước hay sau lời gọi.
- Hotel tính theo từng đêm: pet chưa check-in bị hủy nhả toàn bộ; đã check-in chưa trả vẫn giữ khoảng cam kết; đã trả chỉ nhả đêm chưa dùng, vẫn tính đêm đã ở khi booking còn active. DONE sớm không tự nhả phòng. Booking terminal không còn giữ phòng.
- Booking/Tracking dùng `DepositDto?`. Public mặc định refund.thamChieu=null; internal chỉ thấy tham chiếu đã che. Nạp đủ graph dưới transaction của use case; calculator đường đọc không tạo write-lock.
- Foundation helper không bị giới hạn số dòng. Ước lượng #14 là 16–24 giờ tập trung, không phải cam kết hoàn tất sáng Ngày 1; lịch các làn phụ thuộc phải tính lại theo thời điểm gate #14 thực sự đạt. Đo SLA <500ms, không suy ra SLA từ batch size.


---

## Handoff Issue #26 (2026-11-23)

**Cập nhật Phase 2 đã duyệt:** Spa thường dùng E64 `/api/booking-options?ngay=...` để lấy `maCa` và `maKhungGio` riêng theo ngày; không suy ID từ nhãn ca. UI giữ pagination `E10a` và trạng thái tường minh (mặc định `PENDING`). `emailDelivery: FAILED` vẫn là đặt lịch thành công, giữ tracking code. Hotel/Combo UI tiếp tục chờ phần #15; không mở scope Spa hiện tại. Xem [plan Phase 2](../../superpowers/plans/2026-11-23-issue-26-phase2-spa-ui.md).

- **Phase 1 (#26):** `E03` cung cấp availability; nhãn ca Phase 1 chỉ là dữ liệu hiển thị của adapter. `E04` cần `maKhungGio` thật hoặc ánh xạ đã xác minh và truyền cùng ID vào từng `ServiceSelection.maKhungGio`. `shiftId`/`maCa` định danh ca hiển thị. Khi thiếu ánh xạ, adapter trả `MISSING_SLOT_MAPPING` trước HTTP.
- **Phase 2 (Spa đã duyệt; Hotel/Combo nối sau):** Handoff contract `E03/E04` từ #15 và `BookingResponse`/`PageBooking` cho luồng UI. Form Spa lấy slot động từ E64 đã duyệt, không suy ID từ nhãn ca. Phân trang UI dùng `trang`, `kichThuocTrang`, `tongSo` từ `E10a`. Phase 1 mặc định lấy trang 1 với `kichThuocTrang=100`.

Tham chiếu [thiết kế đã duyệt](../../superpowers/specs/2026-11-23-issue-26-sprint1-hybrid-routing-design.md) và [implementation plan Phase 1](../../superpowers/plans/2026-11-23-issue-26-sprint1-hybrid-routing.md).

---

## 0. Điều Kiện Bắt Đầu & Quy Tắc Bắt Buộc

1. **Cửa khóa Ngày 1 (Chờ Issue #14 merge):** Chỉ tạo nhánh tính năng sau khi Tech Lead đã merge **Issue #14 (Foundation)** vào `origin/develop`. Luôn đồng bộ sạch trước khi cắt nhánh:
   ```powershell
   git fetch origin
   git checkout develop
   git rebase origin/develop
   ```
2. **Những gì Issue #14 đã chuẩn bị sẵn cho Dev A dùng ngay (không tự viết lại):**
   - DTOs trong `PetSpa.Core/DTOs/Booking/CustomerPetDto.cs` (`CustomerDto`, `PetDto`, `CustomerQuery`, `PetEdit`, `RescheduleRequest`, `TrackingCancel`, `TrackingResend`, `AcknowledgedResponse`) và `DepositDto` trong `BookingResponse.TienCoc`.
   - `BookingProjection.cs` đã ánh xạ sẵn `TienCoc`, `ThanhPhan` combo, `MaLoaiPhong`, `NhanSom`, `BanGiaoAt`, `TraLaiAt`.
   - `HotelCapacityCalculator` (`CheckRoomAvailabilityAsync`, `LockRoomTypesAsync`) và `SpaCapacityCalculator.CheckAsync(..., readAsOfNow)`.
   - `IDepositExpiryService` (`ExpireOverdueDepositsAsync`) và `IEmailSender.SendTrackingResendAsync`.
   - `DbInitializer` đã seed sẵn 2 `LoaiPhong` và 1 `Combo` (giữ `EXTENSION_SETTINGS` mặc định `false` $\rightarrow$ các test Hotel/Cọc tự bật `cocHotel: true, giuCho: true` trong fixture của test).
3. **Quy tắc cứng không được phá vỡ:**
   - **Schema Freeze:** Không sửa Entity/Configuration/Migration.
   - **Thứ tự khóa chống Deadlock:** `don_dat_lich` $\rightarrow$ `khung_gio_dat_lich` (ID tăng dần) $\rightarrow$ `loai_phong` (ID tăng dần) $\rightarrow$ `dang_ky_ca` (ID tăng dần) $\rightarrow$ `tien_coc`.
   - **JSONB Giá Snapshot:** `DichVuTrongDon.BangGiaSizeSnapshot` và `ThanhPhanDichVuTrongDon.GiaLeSizeSnapshot` luôn serialize theo chuẩn `Dictionary<SizeBand, decimal>` với `JsonSerializerDefaults.Web`.
   - **Idempotency & Replay:** Replay hợp lệ trả HTTP `200 OK` (kể cả `E05` lần đầu `201`), sai payload cùng `requestId` trả `409 IDEMPOTENCY_CONFLICT`, `TraceId` cắt tối đa 40 ký tự.
   - **Kiểm chứng trước khi mở PR:** `dotnet build backend/PetSpa.sln -m:1` (`0 errors, 0 warnings`) và `dotnet test backend/PetSpa.sln -m:1` với `PETSPA_TEST_POSTGRES` pass 100%, bao gồm test thu hồi quyền FR01 (`403 FORBIDDEN`).

---

## 1. Issue #15: Mở Rộng `E03/E04` Cho Hotel, Combo & Cọc 30% (G2) + Booking Tại Quầy `E05`

- **Nhánh Git:** `feat/issue-15-hotel-combo-counter-booking`
- **Phụ thuộc:** Issue #14 đã merge.
- **Ước tính:** 6h
- **Endpoints:**
  - `E03`: `POST /api/availability` (Public)
  - `E04`: `POST /api/bookings` (Public + OTP nếu bật)
  - `E05`: `POST /api/internal/bookings` (Permission `BOOKING_CREATE`)

### Files cần sửa
- Modify: `backend/src/PetSpa.Api/Services/AvailabilityService.cs`
- Modify: `backend/src/PetSpa.Api/Services/Booking/BookingCreateService.cs`
- Modify: `backend/src/PetSpa.Api/Controllers/Booking/BookingCreateController.cs`
- Test: `backend/tests/PetSpa.Tests/AvailabilityEndpointTests.cs`
- Test: `backend/tests/PetSpa.Tests/Booking/BookingCreateEndpointTests.cs`

### Chi tiết nghiệp vụ & Guard cần cài đặt
1. **Quy tắc ngày nhận/trả & tổ hợp pet (dùng chung cho `E03`, `E04`, `E05`):**
   - Mỗi pet phải có ít nhất 1 dịch vụ Spa/Combo (`DichVu.Count > 0`) **HOẶC** có `MaLoaiPhong != null` (`M2-L02`).
   - Nếu trong đơn có **ít nhất 1 pet** có `MaLoaiPhong != null`: bắt buộc `NgayTra > intake.Ngay` (`SoDemChot = NgayTra.DayNumber - intake.Ngay.DayNumber >= 1` cho pet có phòng; các pet chỉ Spa trong cùng đơn hỗn hợp `M2-06` có `MaLoaiPhong == null, SoDemChot = null`). Mọi `service.MaKhungGio` phải nằm trong `[intake.Ngay, NgayTra]` và không trước giờ bắt đầu ca tiếp nhận ở ngày đầu.
   - Nếu **không có pet nào** có `MaLoaiPhong != null`: bắt buộc `NgayTra == intake.Ngay`. Sai trả `400 VALIDATION_FAILED` (hoặc `400 INVALID_DATE_RANGE` khi `NgayTra < intake.Ngay`).
2. **Mở rộng `E03` (`AvailabilityService` — Đường Đọc):**
   - **Tuyệt đối không gọi** write-lock `ExpireOverdueDepositsAsync`.
   - Truyền `readAsOfNow: clock.GetUtcNow()` vào `SpaCapacityCalculator.CheckAsync` và `HotelCapacityCalculator.CheckRoomAvailabilityAsync` để câu truy vấn đọc tự trừ slot/phút và phòng của các đơn `PENDING` quá hạn cọc 30 phút.
   - Hỗ trợ `MaCombo != null` XOR `MaDichVu != null`: thời lượng combo = tổng `ThoiLuongPhut` của các thành phần trong `ChiTietCombo` (mỗi thành phần đếm 1 lần).
   - Pet Hotel-only (`DichVu` rỗng) không tốn slot Spa (`0` slot, `0` phút), nhưng `intake.Ngay` và `NgayTra` không được rơi vào `NgayNghi` (`D17`, đêm ở giữa được đi qua ngày nghỉ).
   - Điền mảng `AvailabilityResponse.Phong` cho từng đêm $d \in [\text{intake.Ngay}, \text{NgayTra})$; nếu thiếu phòng bất kỳ đêm nào trả `409 CAPACITY_EXCEEDED`.
3. **Mở rộng `E04` & Thêm `E05` (`BookingCreateService` & `BookingCreateController` — Đường Ghi):**
   - Trước khi mở transaction chính, gọi `await depositExpiry.ExpireOverdueDepositsAsync(clock.GetUtcNow(), null, ct, targetKhungGioIds: slotIds, targetRoomTypeIds: roomTypeIds)` (transaction riêng) để giải phóng `SlotDaDat`/`PhutDaGiu` vật lý của đơn `PENDING` quá hạn cọc.
   - Nhận `multipart/form-data` (JSON `"payload"` + files khớp `pet.VaccinationFileKey`) hoặc `application/json`.
   - `E05` kiểm tra JWT + quyền `BOOKING_CREATE` trong DB, gán `DonDatLich.TaoBoiId = actorId`, cho phép chọn `maKhachHang` & `maThuCung` cũ (xác minh `thu_cung.khach_hang_id == maKhachHang`) mà không cần `bookingProof`/`petProof`.
   - Trong transaction chính: khóa `request_id` advisory lock $\rightarrow$ `khung_gio_dat_lich` $\rightarrow$ `loai_phong` $\rightarrow$ `dang_ky_ca`.
   - Với dòng `MaCombo`: serialize `BangGiaSizeSnapshot` (`Dictionary<SizeBand, decimal>`) và sinh đầy đủ `ThanhPhanDichVuTrongDon` kèm `GiaLeSizeSnapshot` của từng dịch vụ lẻ trong combo, `TrangThai = CHO_THUC_HIEN`.
   - Với pet Hotel: lưu `LoaiPhongId`, `GiaPhongCamKet = loaiPhong.GiaMoiDem`, `SoDemChot >= 1`.
   - `TongTamTinh = tổng Spa/Combo theo size khai báo + tổng (GiaPhongCamKet * SoDemChot)`.
   - Nếu đơn có $\ge 1$ pet Hotel và `EXTENSION_SETTINGS` đang bật `cocHotel == true && giuCho == true`: tạo `TienCoc` với `CoSoTinhSnapshot = TongTamTinh`, `TyLe = 0.30m`, `SoTienYeuCau = Math.Round(TongTamTinh * 0.30m, 0, MidpointRounding.AwayFromZero)`, `HetHanAt = now.AddMinutes(30)`, `TrangThaiYeuCau = CHO_THU`.

### Checklist TDD bắt buộc (DoD)
- [ ] `E03`: Test Combo, Hotel nhiều đêm, pet Hotel-only không tốn slot Spa, lưu trú đi qua ngày nghỉ (`D17`), và test `E03` thấy cả slot Spa + phòng Hotel trống ngay sau khi đơn `PENDING` quá hạn 30 phút (không gọi endpoint ghi).
- [ ] `E04`: Test đơn hỗn hợp (1 pet Hotel 2 đêm + Combo, 1 pet chỉ Spa theo `M2-06`) khi bật `cocHotel & giuCho` sinh `tienCoc` 30% và `ThanhPhanDichVuTrongDon`; test `MaLoaiPhong != null` mà `NgayTra == intake.Ngay` trả `400`; test `E04` tự thu hồi chỗ đơn quá hạn 30 phút.
- [ ] `E05`: Test Lễ tân (`BOOKING_CREATE`) đặt cho khách/pet cũ trả `201`, replay cùng `requestId` trả `200`, thu hồi quyền `BOOKING_CREATE` trả `403 FORBIDDEN`.
- [ ] Chạy pass: `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~AvailabilityEndpointTests|FullyQualifiedName~BookingCreateEndpointTests" -m:1`.

---

## 2. Issue #16: API Tra Cứu Khách Hàng (`E06`) & Cập Nhật Hồ Sơ Thú Cưng (`E07`)

- **Nhánh Git:** `feat/issue-16-customer-pet-management`
- **Phụ thuộc:** Issue #14 đã merge (độc lập hoàn toàn với #15, có thể làm trước hoặc sau #15).
- **Ước tính:** 4h
- **Endpoints:**
  - `E06`: `GET /api/internal/customers/lookup?soDienThoai=...` (Permission `CUSTOMER_LOOKUP`)
  - `E07`: `PATCH /api/internal/pets/{petId}` (Permission `CUSTOMER_LOOKUP`)

### Files cần tạo / sửa
- Create: `backend/src/PetSpa.Api/Services/Booking/CustomerLookupService.cs`
- Create: `backend/src/PetSpa.Api/Controllers/Booking/CustomersController.cs`
- Modify: `backend/src/PetSpa.Api/Program.cs` *(đăng ký `CustomerLookupService`)*
- Test: `backend/tests/PetSpa.Tests/Booking/CustomerPetEndpointTests.cs`

### Chi tiết nghiệp vụ & Guard cần cài đặt
1. **`E06` (`GET /api/internal/customers/lookup`):**
   - Kiểm tra quyền `CUSTOMER_LOOKUP` từ DB. Validate `soDienThoai` đúng 10 chữ số ASCII (sai trả `400 VALIDATION_FAILED`).
   - Exact match `khach_hang.so_dien_thoai == soDienThoai && deleted_at == null`, kèm `thu_cung` (`deleted_at == null`), sắp xếp ổn định theo `Id`.
   - Không log `soDienThoai` ra logger (`api-contract.md` mục 4).
2. **`E07` (`PATCH /api/internal/pets/{petId}`):**
   - Kiểm tra `CUSTOMER_LOOKUP` từ DB và idempotency theo `(actorId, "PET_EDIT", request.RequestId)` trong `audit_log`.
   - Bắt buộc có `requestId` hợp lệ và **ít nhất 1 trường nghiệp vụ** khác null (`tenThuCung`, `giong`, `canNang`, `ghiChuDiUng`, `loaiLong`, `ngaySinh`); nếu không có trường nào hoặc sai định dạng/giới hạn trả `400 VALIDATION_FAILED`.
   - Cập nhật hồ sơ master `ThuCung`, không làm thay đổi dữ liệu snapshot (`TenPetSnapshot`, `CanNangKhaiBao`) trên các `ChiTietDon` lịch sử (`D10/Q05`).

### Checklist TDD bắt buộc (DoD)
- [ ] `E06`: Test exact SĐT trả `Customer[]` kèm `thuCung`, SĐT không có trả `[]`, sai định dạng trả `400`, thu hồi quyền `CUSTOMER_LOOKUP` trả `403`.
- [ ] `E07`: Test patch thành công `200`, giữ nguyên snapshot `ChiTietDon` cũ, replay cùng `requestId` trả `200`, đổi payload cùng `requestId` trả `409 IDEMPOTENCY_CONFLICT`, body rỗng field nghiệp vụ trả `400`.
- [ ] Chạy pass: `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~CustomerPetEndpointTests" -m:1`.

---

## 3. Issue #17: Chi Tiết Đơn (`E10b`), Dời Lịch (`E13`), Hủy Tracking (`E09`), Gửi Lại Tracking (`E50`) & Mở Rộng `E11/E12` (G3)

- **Nhánh Git:** `feat/issue-17-booking-reschedule-cancel-tracking`
- **Phụ thuộc:** Issue #15 đã merge.
- **Ước tính:** 6h
- **Endpoints:**
  - `E10b`: `GET /api/internal/bookings/{id}` (Permission `BOOKING_APPROVE`)
  - `E11` (mở rộng): `POST /api/internal/bookings/{id}/confirm` (Permission `BOOKING_APPROVE`)
  - `E12` (mở rộng — G3): `POST /api/internal/bookings/{id}/cancel` (Permission `BOOKING_APPROVE`, Admin-only nếu `< 2h`)
  - `E13`: `POST /api/internal/bookings/{id}/reschedule` (Permission `BOOKING_RESCHEDULE`)
  - `E09`: `POST /api/tracking/cancel` (Public pair SĐT + trackingCode)
  - `E50`: `POST /api/tracking/resend` (Public)

### Files cần sửa
- Modify: `backend/src/PetSpa.Api/Services/Booking/BookingManagementService.cs`
- Modify: `backend/src/PetSpa.Api/Controllers/Booking/BookingManagementController.cs`
- Modify: `backend/src/PetSpa.Api/Services/Booking/TrackingService.cs`
- Modify: `backend/src/PetSpa.Api/Controllers/Booking/TrackingController.cs`
- Test: `backend/tests/PetSpa.Tests/Booking/BookingManagementEndpointTests.cs`
- Test: `backend/tests/PetSpa.Tests/Booking/TrackingEndpointTests.cs`

### Chi tiết nghiệp vụ & Guard cần cài đặt
1. **`E10b` (`GET /api/internal/bookings/{id}`):** Kiểm tra `BOOKING_APPROVE`, trả `BookingResponse` đầy đủ (kèm `TienCoc`, `HoaDon`), không có trả `404 NOT_FOUND`.
2. **Mở rộng `E11` (`ConfirmAsync`):** Dưới khóa `don_dat_lich`, kiểm tra nếu `booking.TienCocDonDatLich != null` mà `TrangThaiYeuCau != DA_DAP_UNG` (hoặc `TrangThaiYeuCau == CHO_THU && HetHanAt <= now`) thì trả `409 INVALID_TRANSITION`.
3. **Mở rộng `E12` (G3) & Thêm `E09` (`POST /api/tracking/cancel`):**
   - Gỡ chốt chặn hủy đơn có Hotel/Cọc khi đơn ở `PENDING`/`CONFIRMED` và chưa có pet nào check-in (`CheckInAt == null`).
   - `E12`: Nếu `LoaiHuy is CUSTOMER_CANCELLED or STAFF_CANCELLED` và `BatDauDuKien - now < 2h`, chỉ `Role.ADMIN` được hủy (`STAFF` trả `403 FORBIDDEN`). Nếu `NO_SHOW`, bắt buộc `now >= MocCuoiCa + 30m` và lý do bắt đầu bằng `"Khách không liên hệ"` (chưa đủ 30m trả `409 INVALID_TRANSITION`).
   - `E09`: Xác minh cặp `(soDienThoai, trackingCode)` qua cơ chế khóa 5 lần sai/15 phút của `TrackingService`; bắt buộc `BatDauDuKien - now >= 2h` (`< 2h` trả `409 INVALID_TRANSITION`). Idempotency trong `audit_log` với `(ActorId: null, ActorLoai: "GUEST", HanhDong: "TRACKING_CANCEL", RequestId)`.
   - Khi hủy đơn (`E12`/`E09`): khóa `don_dat_lich` $\rightarrow$ `khung_gio_dat_lich` $\rightarrow$ `loai_phong` $\rightarrow$ `tien_coc`. Nhả `SlotDaDat` và `PhutDaGiu` đúng 1 lần.
   - Xử lý `TienCoc`: nếu còn `CHO_THU` thì đổi sang `HUY` (`KetThucAt = now`). Nếu `ThucNhan > 0`:
     - Hủy hợp lệ $\ge 2$h (`E09`, `E12`) hoặc `STAFF_REJECTED`: tự động tạo `HoanCoc` (`CHO_HOAN`) cho `ThucNhan - DanhHoan`. **Bắt buộc gán `TaoBoiId = actorId ?? booking.DuyetBoiId ?? latestReceipt.XacNhanBoiId ?? booking.TaoBoiId!.Value`** để không lỗi PostgreSQL FK `23503` khi `E09` không có `actorId`!
     - Hủy muộn `< 2h` hoặc `NO_SHOW` (với cọc đã `DA_DAP_UNG`): phạt giữ cọc (`GiuLai = ThucNhan - DanhHoan`, không tạo `HoanCoc`).
4. **`E13` (`POST /api/internal/bookings/{id}/reschedule`):**
   - Kiểm tra `BOOKING_RESCHEDULE` + idempotency. Gọi `depositExpiry.ExpireOverdueDepositsAsync(now, null, ct, targetKhungGioIds: slotIds, targetRoomTypeIds: roomTypeIds)` trước khi mở transaction chính.
   - Guard (`D14`): chỉ `PENDING`/`CONFIRMED` và chưa có pet nào check-in; kiểm tra quy tắc `NgayTra` như Task 2.
   - Khóa hợp nhất `don_dat_lich` $\rightarrow$ `khung_gio_dat_lich` (cũ + mới tăng dần) $\rightarrow$ `loai_phong` $\rightarrow$ `dang_ky_ca` mới. Kiểm tra công suất (truyền `excludeBookingId: booking.Id`). Lỗi rollback giữ nguyên lịch cũ (`409 CAPACITY_EXCEEDED`); hợp lệ thì chuyển slot/phút và cập nhật lịch/số đêm (giữ nguyên `TienCoc` đã phát hành).
5. **`E50` (`POST /api/tracking/resend`):**
   - Rate limit chống gửi dồn theo IP/SĐT. Tìm đơn có `SoDienThoaiSnapshot == request.SoDienThoai`, `EmailSnapshot == request.Email` (case-insensitive), trạng thái chưa kết thúc (`PENDING/CONFIRMED/IN_PROGRESS`, lọc `NgayHen` nếu có). Nếu có $\ge 1$ đơn thì gửi qua `email.SendTrackingResendAsync`. Luôn trả `202 Accepted` cùng thông báo chung (`AcknowledgedResponse`).

### Checklist TDD bắt buộc (DoD)
- [ ] Test `E10b` trả `200`/`404`; test `E11` chặn `409` khi cọc chưa `DA_DAP_UNG`.
- [ ] Test `E09` hủy đơn có cọc $\ge 2$h tạo `HoanCoc` (`CHO_HOAN`) không lỗi FK `TaoBoiId`; test `E09` `< 2h` trả `409`; test `E12` bởi `STAFF` `< 2h` trả `403`, bởi `ADMIN` `< 2h` hoặc `NO_SHOW` giữ cọc (`giuLai == thucNhan`).
- [ ] Test `E13` dời lịch nguyên tử thành công, dời vào ca đầy trả `409 CAPACITY_EXCEEDED` và giữ lịch cũ, dời sau check-in trả `409 INVALID_TRANSITION`.
- [ ] Test `E50` gửi email cho đơn chưa kết thúc, không gửi cho đơn `COMPLETED`/`CANCELLED`, cả hai trả `202`.
- [ ] Chạy pass: `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~BookingManagementEndpointTests|FullyQualifiedName~TrackingEndpointTests" -m:1`.
