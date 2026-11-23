# KẾ HOẠCH TRIỂN KHAI SPRINT 2 — DEV B (CARE, PHÂN CÔNG, COMBO `E61` & NGOẠI LỆ PET)

**Phụ trách:** Dev B  
**Phạm vi:** 3 Issues (`#18`, `#19`, `#20`) — **7 Operations mới** (`E14, E16, E61, E21, E47, E48, E49`) + **Gỡ chốt chặn Sprint 1** (`G4`: mở rộng `E15` Check-in cho Combo/Hotel, guard `E17 READY` cho dịch vụ phát sinh/Combo, và `E19` khép đơn `M2-04/M2-05`).  
**Tổng ước tính:** ~16 giờ code + unit/integration test.  
**Kế hoạch tổng thể:** [sprint-2-plan.md](sprint-2-plan.md)  
**Nguồn chuẩn (Single Source of Truth):**
- [docs/design/api-contract.md](../../design/api-contract.md) (mục 2, 3, 5)
- [docs/design/system-design.md](../../design/system-design.md) (mục 5, 6.2, 6.3, 7.3, 7.4)
- [docs/requirements/scope-and-decisions.md](../../requirements/scope-and-decisions.md) (`D03/Q07, D06/Q08, D07/Q06, D11/Q07, M2-04, M2-05, M2-07`)
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

- **Phase 1 (#26):** Adapter `E15` đóng gói multipart theo contract và từ chối trước HTTP nếu thiếu input bắt buộc. `E19` yêu cầu xác nhận thực nhận và được gọi riêng sau khi hóa đơn `PAID`. Phase 1 có thể hoàn tất trước #18, với `getWorkspace.users` rỗng.
- **Phase 2 (UI Form Integration, chưa gán số Issue):** Sau khi #18 merge, bàn giao contract `E14` (`ngay`, `maCa`, `CHECKIN_PERFORM`, danh sách ứng viên hợp lệ) để form check-in gửi `maNhanVien` đã chọn vào `E15`. Form cần `anhTiepNhan`, `ghiChuSucKhoe`, `quyetDinhGia`, `xacNhanKhach`. UI có thao tác bàn giao `E19` và xác nhận thực nhận riêng sau `PAID`.

Tham chiếu [thiết kế đã duyệt](../../superpowers/specs/2026-11-23-issue-26-sprint1-hybrid-routing-design.md) và [implementation plan Phase 1](../../superpowers/plans/2026-11-23-issue-26-sprint1-hybrid-routing.md).

---

## 0. Điều Kiện Bắt Đầu & Quy Tắc Bắt Buộc

1. **Cửa khóa Ngày 1 (Chờ Issue #14 merge):** Chỉ tạo nhánh tính năng sau khi Tech Lead đã merge **Issue #14 (Foundation)** vào `origin/develop`. Luôn đồng bộ trước khi cắt nhánh:
   ```powershell
   git fetch origin
   git checkout develop
   git rebase origin/develop
   ```
2. **Những gì Issue #14 đã chuẩn bị sẵn cho Dev B dùng ngay (chạy test độc lập 100% không phải chờ Dev C):**
   - DTOs dùng chung: `ShiftQuery`, `AssignmentRequest`, `ServiceActionRequest`, `PetCancelRequest`, `ReasonCommand`, `BookingCommand`.
   - `IBillingSettlementHelper` **và class thực thi `BillingSettlementHelper` đã đăng ký DI sẵn trong `Program.cs`:**
     - `SettleCancelledPetInMemory(booking, pet, completedComponentIds, nightsStayed, now)` — chốt giá lẻ thành phần combo đã làm (`GiaLeSizeSnapshot`), đặt phần chưa làm sang `HUY` (`GiaChot = 0`), cập nhật `SoDemDaO` và `TongTamTinh`.
     - `TryTransitionBookingTerminalState(booking, now)` — kiểm tra điều kiện khép đơn sang `COMPLETED` (`M2-04`) hoặc `CANCELLED` (`M2-05`).
   - `BookingProjection.cs` đã map sẵn `ThanhPhan` combo, `MaLoaiPhong`, `NhanSom`, `BanGiaoAt`, `TraLaiAt`, `TienCoc`.
   - `DbInitializer` đã seed sẵn 2 `LoaiPhong` và 1 `Combo` (`Combo Tắm Cắt Toàn Diện`).
3. **Quy tắc cứng không được phá vỡ:**
   - **Schema Freeze:** Không sửa Entity/Configuration/Migration.
   - **Thứ tự khóa chống Deadlock:** `don_dat_lich` $\rightarrow$ `khung_gio_dat_lich` (ID tăng dần) $\rightarrow$ `loai_phong` (ID tăng dần) $\rightarrow$ `dang_ky_ca` (ID tăng dần) $\rightarrow$ `tien_coc` / `hoa_don`.
   - **JSONB Giá Snapshot:** `BangGiaSizeSnapshot` và `GiaLeSizeSnapshot` dùng chuẩn `Dictionary<SizeBand, decimal>` (`JsonSerializerDefaults.Web`).
   - **Mã lỗi `409` thống nhất:** Trạng thái sai hoặc đơn đã có hóa đơn hiệu lực trả `409 INVALID_TRANSITION`; nhân viên được gán thiếu quyền `CARE_UPDATE` hoặc thiếu ca `APPROVED` trả `409 ASSIGNMENT_UNAVAILABLE`.
   - **Kiểm chứng trước khi mở PR:** `dotnet build backend/PetSpa.sln -m:1` (`0 errors, 0 warnings`) và `dotnet test backend/PetSpa.sln -m:1` với `PETSPA_TEST_POSTGRES` pass 100%, bao gồm test thu hồi quyền FR01 (`403 FORBIDDEN`).

---

## 1. Issue #18: Ứng Viên Ca (`E14`), Phân Công Lại (`E16`) & Mở Rộng Check-in (`E15`) Cho Combo/Hotel (G4)

- **Nhánh Git:** `feat/issue-18-care-assignment-checkin-combo`
- **Phụ thuộc:** Issue #14 đã merge.
- **Ước tính:** 5h
- **Endpoints:**
  - `E14`: `GET /api/internal/assignment-candidates?ngay=...&maCa=...` (Permission `CHECKIN_PERFORM`)
  - `E16`: `PUT /api/internal/booking-pets/{id}/assignment` (Permission `CHECKIN_PERFORM`)
  - `E15` (mở rộng — G4): `POST /api/internal/booking-pets/{id}/check-in` (Permission `CHECKIN_PERFORM`)

### Files cần sửa
- Modify: `backend/src/PetSpa.Api/Services/Care/CheckinService.cs`
- Modify: `backend/src/PetSpa.Api/Services/Care/CareOperation.cs`
- Modify: `backend/src/PetSpa.Api/Controllers/Care/BookingPetsController.cs`
- Modify: `backend/src/PetSpa.Core/Interfaces/Care/ICheckinService.cs`
- Test: `backend/tests/PetSpa.Tests/Care/CheckinEndpointTests.cs`
- Test: `backend/tests/PetSpa.Tests/Care/AssignmentEndpointTests.cs`

### Chi tiết nghiệp vụ & Guard cần cài đặt
1. **`E14` (`GET /api/internal/assignment-candidates`):**
   - Kiểm tra `CHECKIN_PERFORM` từ DB. Validate `ngay` và `maCa > 0` (ca không tồn tại trả `404 NOT_FOUND`).
   - Trả `Employee[]` gồm các nhân viên `IsActive == true`, có quyền `CARE_UPDATE` trong `NhanVienQuyen`, và có `DangKyCa` trạng thái `APPROVED` tại `(ngay, maCa)`, sắp xếp theo `Id`.
2. **`E16` (`PUT /api/internal/booking-pets/{id}/assignment`):**
   - Kiểm tra `CHECKIN_PERFORM` từ DB + idempotency qua `CareOperation`.
   - Khóa `don_dat_lich` $\rightarrow$ `khung_gio_dat_lich` $\rightarrow$ `dang_ky_ca` `FOR UPDATE` (chống write-skew với `E55` điều chuyển ca).
   - Guard: Đơn chưa có hóa đơn hiệu lực (`TrangThai != REVOKED`), pet đang ở `WAITING`, `IN_PROGRESS`, `PAUSED`, hoặc `READY` (chưa `DONE`, chưa `CANCELLED`).
   - Nhân viên được gán (`request.MaNhanVien`) **luôn bắt buộc** active, có quyền `CARE_UPDATE` và có ca `APPROVED` (kể cả khi Admin là người gọi `E16`; nếu không đạt trả `409 ASSIGNMENT_UNAVAILABLE`).
   - Cập nhật `pet.NhanVienId = request.MaNhanVien`, **giữ nguyên `NhanVienId` tác giả của mọi `NhatKyChamSoc` cũ** (`D11/Q07`), ghi `AuditLog` kèm `request.LyDo`.
3. **Mở rộng `E15` (`CheckinService.cs` — G4):**
   - Hỗ trợ pet có dòng `ComboId != null`: đọc giá theo size từ `line.BangGiaSizeSnapshot` (hoặc fallback bảng `BangGiaSize` theo `ComboId` + `SizeId`), chốt `line.SizeChot` và `line.GiaChot`.
   - Hỗ trợ pet Hotel (`pet.LoaiPhongId != null`, kể cả pet Hotel-only `0` dòng `DichVuTrongDon`): chốt `pet.GiaPhongChot = pet.GiaPhongCamKet ?? pet.LoaiPhong.GiaMoiDem`, `pet.SoDemChot = pet.SoDemChot ?? Math.Max(1, HcmDate(booking.KetThucDuKien).DayNumber - HcmDate(booking.BatDauDuKien).DayNumber)` (quy đổi `Asia/Ho_Chi_Minh`, **không** dùng `.Date` UTC trực tiếp).

### Checklist TDD bắt buộc (DoD)
- [ ] `E14`: Chỉ trả kỹ thuật viên có `CARE_UPDATE` + ca `APPROVED` (loại lễ tân và ca `PENDING`/`REJECTED`).
- [ ] `E16`: Đổi người phụ trách khi pet `WAITING`/`IN_PROGRESS` thành công `200`, giữ nguyên `tenTacGia` nhật ký cũ; chặn `409 INVALID_TRANSITION` khi pet đã `DONE`; chặn `409 ASSIGNMENT_UNAVAILABLE` khi nhân viên mới không có ca `APPROVED`.
- [ ] `E15`: Check-in thành công pet đặt Combo và pet đặt Hotel-only, chốt đúng `GiaChot`, `GiaPhongChot`, `SoDemChot`, cập nhật `TongTamTinh` khi đổi size cân thực tế.
- [ ] Chạy pass: `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~AssignmentEndpointTests|FullyQualifiedName~CheckinEndpointTests" -m:1`.

---

## 2. Issue #19: Hoàn Tất Thành Phần Combo (`E61`), Action Dịch Vụ Phát Sinh (`E21`) & Guard `E17 READY`

- **Nhánh Git:** `feat/issue-19-combo-component-extra-service-actions`
- **Phụ thuộc:** Issue #14 đã merge.
- **Ước tính:** 5h
- **Endpoints:**
  - `E61`: `POST /api/internal/order-services/{id}/components/{componentId}/complete` (Permission `CARE_UPDATE`)
  - `E21`: `POST /api/internal/extra-services/{id}/actions` (Permission `CARE_UPDATE` cho `COMPLETE`, `PET_CANCEL_LINE` cho `CANCEL`)
  - Cập nhật Guard `E17 READY` trong `CareService.cs`.

### Files cần tạo / sửa
- Modify: `backend/src/PetSpa.Api/Services/Care/CareService.cs`
- Modify: `backend/src/PetSpa.Core/Interfaces/Care/ICareService.cs`
- Create: `backend/src/PetSpa.Api/Controllers/Care/OrderServicesController.cs`
- Test: `backend/tests/PetSpa.Tests/Care/OrderServiceActionsEndpointTests.cs`

### Chi tiết nghiệp vụ & Guard cần cài đặt
1. **`E61` (`POST /api/internal/order-services/{id}/components/{componentId}/complete`):**
   - Kiểm tra `CARE_UPDATE` + đúng assignment hiện tại và ca `APPROVED` của pet (Admin được override assignment/ca).
   - Guard: Đơn đang `IN_PROGRESS`, chưa có hóa đơn hiệu lực, pet đang `IN_PROGRESS`, dòng `DichVuTrongDon` (`id`) là Combo (`ComboId != null`) đang `CHO_THUC_HIEN`, thành phần `componentId` thuộc đúng dòng đó và đang `CHO_THUC_HIEN`.
   - Chuyển `component.TrangThai = HOAN_TAT`, `component.HoanTatAt = now`.
   - **Chỉ khi toàn bộ** thành phần không bị hủy của combo đó đều đã `HOAN_TAT` thì mới chuyển dòng cha `DichVuTrongDon.TrangThai = HOAN_TAT`, `HoanTatAt = now`.
2. **`E21` (`POST /api/internal/extra-services/{id}/actions`):**
   - `id` là `DichVuTrongDon.Id` có `LaPhatSinh == true`, đơn chưa có hóa đơn hiệu lực, pet chưa `DONE`/`CANCELLED`, dòng đang `CHO_THUC_HIEN`.
   - **`hanhDong == COMPLETE`:** Quyền `CARE_UPDATE` + đúng assignment/ca (Admin override). Chuyển `DichVuTrongDon.TrangThai = HOAN_TAT`, `HoanTatAt = now` (nếu là combo phát sinh còn thành phần `CHO_THUC_HIEN` thì đánh dấu `HOAN_TAT` luôn các thành phần đó).
   - **`hanhDong == CANCEL`:** Quyền `PET_CANCEL_LINE` (Admin) + bắt buộc `lyDo` (`1..1000` ký tự). Chỉ hủy khi dòng đang `CHO_THUC_HIEN` và **chưa có thành phần nào `HOAN_TAT`**. Khóa `khung_gio_dat_lich`, hoàn trả `PhutDaGiu` (và chỉ giảm `SlotDaDat` đi 1 nếu pet không còn bất kỳ dịch vụ active/hoàn tất nào khác trong cùng `KhungGioId`), trừ `GiaChot` khỏi `booking.TongTamTinh`, đặt `TrangThai = HUY`, `HuyAt = now`, `LyDo = request.LyDo`.
3. **Cập nhật Guard `E17` (`hanhDong == READY` trong `CareService.cs`):**
   - Nếu pet còn bất kỳ dòng `DichVuTrongDon` nào là Combo (`ComboId != null` hoặc có thành phần con) **HOẶC** dịch vụ phát sinh (`LaPhatSinh == true`) đang ở `CHO_THUC_HIEN`, chặn chuyển `READY` và trả `409 INVALID_TRANSITION`. Chỉ dịch vụ đơn đặt ban đầu (`!ComboId.HasValue && !LaPhatSinh`) mới được tự động đánh dấu `HOAN_TAT` khi bấm `READY`.

### Checklist TDD bắt buộc (DoD)
- [ ] `E61`: Hoàn tất 1/2 thành phần combo $\rightarrow$ thành phần 1 `HOAN_TAT`, dòng cha vẫn `CHO_THUC_HIEN`, `E17 READY` bị chặn `409`; hoàn tất nốt thành phần 2 $\rightarrow$ dòng cha `HOAN_TAT`, `E17 READY` thành công.
- [ ] `E21 COMPLETE`: Đúng kỹ thuật viên trả `200`, sai người trả `403`; chưa hoàn tất dịch vụ phát sinh thì `E17 READY` bị chặn `409`.
- [ ] `E21 CANCEL`: Admin (`PET_CANCEL_LINE`) hủy thành công khi chưa làm, nhả `PhutDaGiu` và giảm `TongTamTinh`; chặn `409` nếu đã làm xong hoặc đã có thành phần `HOAN_TAT`.
- [ ] Chạy pass: `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~OrderServiceActionsEndpointTests|FullyQualifiedName~CareActionsEndpointTests" -m:1`.

---

## 3. Issue #20: Hủy Riêng Thú Cưng (`E47`), Nhận Sớm (`E48`), Trả Pet Hủy (`E49`) & Cập Nhật `E19`

- **Nhánh Git:** `feat/issue-20-pet-cancel-early-handover-return`
- **Phụ thuộc:** Issue #18, #19 đã merge *(PR rủi ro cao — yêu cầu $\ge 2$ người duyệt)*.
- **Ước tính:** 6h
- **Endpoints:**
  - `E47`: `POST /api/admin/booking-pets/{id}/cancel` (Permission `PET_CANCEL_LINE`)
  - `E48`: `POST /api/admin/booking-pets/{id}/early-handover` (Permission `EARLY_HANDOVER_APPROVE`)
  - `E49`: `POST /api/internal/booking-pets/{id}/return-cancelled` (Permission `PET_HANDOVER`)
  - `E19` (cập nhật): `POST /api/internal/booking-pets/{id}/handover` (Permission `PET_HANDOVER`)

### Files cần sửa
- Modify: `backend/src/PetSpa.Api/Services/Care/HandoverService.cs`
- Modify: `backend/src/PetSpa.Core/Interfaces/Care/IHandoverService.cs`
- Modify: `backend/src/PetSpa.Api/Controllers/Care/BookingPetsController.cs`
- Test: `backend/tests/PetSpa.Tests/Care/PetExceptionsEndpointTests.cs`

### Chi tiết nghiệp vụ & Guard cần cài đặt
1. **`E47` (`POST /api/admin/booking-pets/{id}/cancel`):**
   - Quyền `PET_CANCEL_LINE` (Admin-only). Khóa `don_dat_lich` $\rightarrow$ `khung_gio_dat_lich` $\rightarrow$ `loai_phong` $\rightarrow$ `tien_coc`.
   - Guard: Đơn ở `CONFIRMED`/`IN_PROGRESS`, chưa có hóa đơn hiệu lực, pet chưa `DONE`/`CANCELLED` và `pet.NhanSom == false`. Nếu đơn đã có pet khác nhận sớm (`NhanSom == true`) và đây là pet thực tế cuối cùng còn ở tiệm khi chưa `PAID`, chặn `409 INVALID_TRANSITION`.
   - Đối chiếu bằng chứng server:
     - `request.MaThanhPhanDaLam` phải khớp chính xác tập ID các `ThanhPhanDichVuTrongDon` đã `HOAN_TAT` trên server (lệch trả `409 INVALID_TRANSITION`).
     - Nếu `LoaiPhongId == null` hoặc pet chưa check-in (`NOT_ARRIVED`): `request.SoDemDaO` phải bằng `0`. Nếu có Hotel và đã check-in: `0 <= request.SoDemDaO <= (pet.SoDemChot ?? 0)`.
   - Nhả công suất Spa chưa dùng: hoàn trả `PhutDaGiu` của các dịch vụ/thành phần chưa làm (`CHO_THUC_HIEN`); chỉ giảm `SlotDaDat` đi 1 nếu pet **không có bất kỳ dịch vụ hay thành phần nào đã `HOAN_TAT`** trong `KhungGioId` đó (`system-design.md` dòng 376).
   - Gọi `billingSettlement.SettleCancelledPetInMemory(booking, pet, completedIds, request.SoDemDaO, now)`.
   - Đặt `pet.TrangThaiChamSoc = CANCELLED`, `pet.HuyBoiId = actor.Id`, `pet.HuyAt = now`, `pet.LyDoHuy = request.LyDo.Trim()`. Nếu `pet.CheckInAt == null` thì gán luôn `pet.TraLaiAt = now`.
   - Gọi `billingSettlement.TryTransitionBookingTerminalState(booking, now)`.
2. **`E48` (`POST /api/admin/booking-pets/{id}/early-handover`):**
   - Quyền `EARLY_HANDOVER_APPROVE` (Admin-only). Khóa `don_dat_lich` `FOR UPDATE`.
   - Guard: Đơn `IN_PROGRESS`, pet `READY`, và sau thao tác **vẫn còn ít nhất 1 pet khác không bị hủy đang ở tiệm** (`other.Id != pet.Id && other.TrangThaiChamSoc != CANCELLED && other.TrangThaiChamSoc != DONE && other.CheckInAt != null`). Nếu không còn pet nào (hoặc 2 request `E48` chạy song song cho 2 pet cuối khi chưa `PAID`) thì trả `409 INVALID_TRANSITION`.
   - Cập nhật `TrangThaiChamSoc = DONE`, `NhanSom = true`, `NhanSomBoiId = actor.Id`, `NhanSomLyDo = request.LyDo.Trim()`, `BanGiaoAt = now`, `BanGiaoBoiId = actor.Id`. Đơn giữ `IN_PROGRESS`.
3. **`E49` (`POST /api/internal/booking-pets/{id}/return-cancelled`) & Cập nhật `E19`:**
   - Quyền `PET_HANDOVER`. Khóa `don_dat_lich` $\rightarrow$ `tien_coc` $\rightarrow$ `hoa_don`.
   - `E49`: Cho phép khi đơn ở `CONFIRMED`/`IN_PROGRESS`/`COMPLETED`, pet ở `CANCELLED` và `pet.TraLaiAt == null`. Nếu `booking.TongTamTinh > 0` phải đã có hóa đơn `PAID` (nếu `TongTamTinh == 0` không được có hóa đơn `UNPAID` treo). Ghi `pet.TraLaiAt = now`, `pet.BanGiaoBoiId = actor.Id`.
   - Cuối cả `E19` và `E49`: gọi `billingSettlement.TryTransitionBookingTerminalState(booking, now)` (đảm bảo ở `E19`, nếu trong đơn có pet bị hủy riêng chưa `E49` thì đơn vẫn giữ `IN_PROGRESS` cho đến khi `E49` trả nốt pet đó mới sang `COMPLETED`).

### Checklist TDD bắt buộc (DoD)
- [ ] `E47`: Hủy dở 1/2 thành phần combo chốt đúng giá lẻ `GiaLeSizeSnapshot`, nhả phút chưa dùng nhưng không giảm `SlotDaDat` đã tiêu thụ; gửi `maThanhPhanDaLam` lệch trả `409`.
- [ ] `E48`: Nhận sớm 1 pet trong đơn 2 pet thành công (`nhanSom: true`, `DONE`, đơn vẫn `IN_PROGRESS`); gọi `E48` (hoặc song song) cho pet thứ 2 khi chưa `PAID` bị chặn `409`; gọi `E47` hủy pet thứ 2 sau khi pet 1 nhận sớm bị chặn `409`.
- [ ] `E49` & `E19`: Đơn toàn pet hủy (`M2-05`) khép sang `CANCELLED` sau khi quyết toán + hoàn cọc + `E49`; đơn 1 pet bình thường + 1 pet hủy (`M2-04`) chỉ sang `COMPLETED` khi cả `E19` và `E49` đều xong.
- [ ] Chạy pass: `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~PetExceptionsEndpointTests|FullyQualifiedName~HandoverEndpointTests" -m:1`.
