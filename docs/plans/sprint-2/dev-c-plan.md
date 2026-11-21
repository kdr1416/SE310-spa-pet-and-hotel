# KẾ HOẠCH TRIỂN KHAI SPRINT 2 — DEV C (CỌC 30%, HOÀN CỌC, PHÁT SINH, PHỤ PHÍ & HÓA ĐƠN)

**Phụ trách:** Dev C  
**Phạm vi:** 3 Issues (`#21`, `#22`, `#23`) — **10 Operations mới** (`E42, E43, E44, E45, E46, E20, E22a, E22b, E22c, E25`) + **Gỡ chốt chặn Sprint 1** (`G5`: mở rộng `E23` lập hóa đơn cho Hotel/Combo/Khấu trừ cọc/Zero-due và cập nhật `E26` khép đơn `M2-05`).  
**Tổng ước tính:** ~16 giờ code + unit/integration test.  
**Kế hoạch tổng thể:** [sprint-2-plan.md](sprint-2-plan.md)  
**Nguồn chuẩn (Single Source of Truth):**
- [docs/design/api-contract.md](../../design/api-contract.md) (mục 2, 3, 4, 5)
- [docs/design/system-design.md](../../design/system-design.md) (mục 4.3, 4.6, 4.7, 4.8, 4.9 `M2-L01..L09`, 5, 6.3, 7.8, 7.9)
- [docs/requirements/scope-and-decisions.md](../../requirements/scope-and-decisions.md) (`D07, D08, Q06, T02/T05/T08, M2-01, M2-02, M2-04, M2-05, M2-06, M2-07, M2-11`)
- [backend/README.md](../../../backend/README.md) và [.github/CONTRIBUTING.md](../../../.github/CONTRIBUTING.md)

---

## 0. Điều Kiện Bắt Đầu & Quy Tắc Bắt Buộc

1. **Cửa khóa Ngày 1 (Chờ Issue #14 merge):** Chỉ tạo nhánh tính năng sau khi Tech Lead đã merge **Issue #14 (Foundation)** vào `origin/develop`. Luôn đồng bộ trước khi cắt nhánh:
   ```powershell
   git fetch origin
   git checkout develop
   git rebase origin/develop
   ```
2. **Những gì Issue #14 đã chuẩn bị sẵn cho Dev C dùng ngay:**
   - Toàn bộ DTOs trong `PetSpa.Core/DTOs/Billing/DepositDto.cs` (`DepositDto`, `RefundDto`, `PageDeposit`, `PageRefund`, `ReceiptCreate`, `RefundCreate`, `RefundConfirm`) và `FeeDto.cs` (`FeeDto`, `FeeCreate`, `FeeEdit`, `ExtraServiceRequest`, `ReasonCommand`).
   - `IDepositExpiryService` (`ExpireOverdueDepositsAsync` cho `E20`, `ExpireLockedBookingDepositAsync` dùng chung dưới khóa cho `E42`).
   - `IBillingSettlementHelper` / `BillingSettlementHelper` (`CalculateObligatedDepositAmount`, `TryTransitionBookingTerminalState`).
   - `IEmailSender.SendRefundConfirmedAsync` (đã nâng cấp `MailKit`), `IFileStorageService` (upload/delete private object), `SpaCapacityCalculator`.
3. **Quy tắc cứng không được phá vỡ:**
   - **Schema Freeze:** Không sửa Entity/Configuration/Migration.
   - **Chống AB-BA Deadlock:** Với mọi route nhận ID bảng con (`{id}` của `tien_coc` ở `E42/E44`, `hoan_coc` ở `E45`, `hoa_don` ở `E25`), **bắt buộc** truy vấn `AsNoTracking()` lấy `DonDatLichId` trước, sau đó mở transaction và khóa theo thứ tự chuẩn: `don_dat_lich` $\rightarrow$ `khung_gio_dat_lich` $\rightarrow$ `loai_phong` $\rightarrow$ `dang_ky_ca` $\rightarrow$ `tien_coc` $\rightarrow$ `hoa_don`.
   - **Bảo toàn constraint `ck_hoa_don_tong_tien` & `ck_ct_hoa_don_so_luong`:**
     - Khi thu hồi hóa đơn ở `E25` (`REVOKED`), **tuyệt đối không sửa** `TongTien`, `CocKhauTru`, `ConPhaiTra` và các dòng `CtHoaDon` lịch sử; chỉ cập nhật `TrangThai = REVOKED`, `ThuHoiBoiId`, `ThuHoiAt`, `LyDoThuHoi`.
     - Không tạo dòng `CtHoaDon` có `SoLuong == 0` (ví dụ pet Hotel hủy riêng mà `SoDemDaO == 0`).
   - **Idempotency & Replay:** Lưu replay trong `audit_log` theo `(ActorId, HanhDong, RequestId)` (cắt `TraceId` tối đa 40 ký tự). Replay hợp lệ các endpoint `201 Created` (`E42, E44, E20, E22a, E23`) trả HTTP **`200 OK`**, khác payload trả `409 IDEMPOTENCY_CONFLICT`.
   - **Kiểm chứng trước khi mở PR:** `dotnet build backend/PetSpa.sln -m:1` (`0 errors, 0 warnings`) và `dotnet test backend/PetSpa.sln -m:1` với `PETSPA_TEST_POSTGRES` pass 100%, bao gồm test thu hồi quyền FR01 (`403 FORBIDDEN`).

---

## 1. Issue #21: Thu Cọc 30% (`E42, E43`) & Hoàn Cọc (`E44, E45, E46`)

- **Nhánh Git:** `feat/issue-21-deposits-and-refunds`
- **Phụ thuộc:** Issue #14 đã merge *(PR rủi ro cao — yêu cầu $\ge 2$ người duyệt)*.
- **Ước tính:** 6h
- **Endpoints:**
  - `E42`: `POST /api/internal/deposits/{id}/receipts` (Permission `DEPOSIT_CONFIRM`)
  - `E43`: `GET /api/internal/deposits` (Permission `DEPOSIT_CONFIRM`)
  - `E44`: `POST /api/admin/deposits/{id}/refunds` (Permission `DEPOSIT_REFUND`)
  - `E45`: `POST /api/admin/refunds/{id}/confirm` (Permission `DEPOSIT_REFUND`)
  - `E46`: `GET /api/admin/refunds` (Permission `DEPOSIT_REFUND`)

### Files cần tạo / sửa
- Create: `backend/src/PetSpa.Api/Services/Billing/DepositService.cs`
- Create: `backend/src/PetSpa.Api/Controllers/Billing/DepositsController.cs`
- Modify: `backend/src/PetSpa.Api/Program.cs` *(đăng ký `DepositService`)*
- Test: `backend/tests/PetSpa.Tests/Billing/DepositsRefundsEndpointTests.cs`

### Chi tiết nghiệp vụ & Guard cần cài đặt
1. **`E42` (`POST /api/internal/deposits/{id}/receipts`):**
   - Quyền `DEPOSIT_CONFIRM`. Đọc `AsNoTracking()` lấy `DonDatLichId` từ `tien_coc.id == id` (không có trả `404`), mở transaction khóa `don_dat_lich` $\rightarrow$ `khung_gio_dat_lich` $\rightarrow$ `loai_phong` $\rightarrow$ `tien_coc`.
   - Idempotency theo `(actorId, "DEPOSIT_CONFIRM", request.RequestId)` (replay trả `200 OK`) và chống trùng `(NganHang, MaGiaoDich)` (`M2-L05`).
   - Kiểm tra quá hạn 30 phút dưới khóa (`M2-11` / `M2-L06`): nếu `deposit.TrangThaiYeuCau == CHO_THU && now > deposit.HetHanAt`, gọi `await depositExpiry.ExpireLockedBookingDepositAsync(booking, now, ct)` trong cùng transaction (chuyển đơn `CANCELLED`, cọc `HET_HAN`, `GiuLai = 0`, nhả slot/phòng).
   - Nếu `deposit.TrangThaiYeuCau is HET_HAN or HUY` (tiền đến muộn): **vẫn lưu `GiaoDichThu`** (`SoTien = request.SoTien`, `XacNhanBoiId = actorId`) và tự tạo `HoanCoc` (`CHO_HOAN`, `SoTien = thucNhan - danhHoan` gom luôn khoản nộp thiếu trước hạn nếu có, `TaoBoiId = actorId`), trả `201 Created` (`DepositDto`).
   - Nếu `CHO_THU` trong hạn: lưu `GiaoDichThu`; nếu tổng `thucNhan >= deposit.SoTienYeuCau` thì chuyển `TrangThaiYeuCau = DA_DAP_UNG`, `KetThucAt = now` (nộp thiếu giữ nguyên `CHO_THU` và không gia hạn `HetHanAt`).
2. **`E43` (`GET /api/internal/deposits`):** Quyền `DEPOSIT_CONFIRM`, phân trang `BookingQuery` (`kichThuocTrang <= 100`, lọc ngày và `TrangThaiYeuCau`), trả `PageDeposit`.
3. **`E44` (`POST /api/admin/deposits/{id}/refunds`):**
   - Quyền `DEPOSIT_REFUND` (Admin-only). Đọc `DonDatLichId`, khóa `don_dat_lich` $\rightarrow$ `tien_coc` $\rightarrow$ `hoa_don`. Idempotency theo `(actorId, "DEPOSIT_REFUND_CREATE", request.RequestId)` (replay trả `200`).
   - Bảo toàn tiền (`system-design.md` mục 4.7): gọi `obligatedAmount = billingSettlement.CalculateObligatedDepositAmount(booking)`. Kiểm tra `obligatedAmount + danhHoan (CHO_HOAN + DA_HOAN) + request.SoTien <= thucNhan`; vượt ngưỡng trả `409 AMOUNT_MISMATCH`.
   - Tạo `HoanCoc` (`RequestId = request.RequestId`, `TrangThai = CHO_HOAN`, `TaoBoiId = actorId`), trả `201 Created` (`RefundDto`).
4. **`E45` (`POST /api/admin/refunds/{id}/confirm`):**
   - Quyền `DEPOSIT_REFUND`. Hỗ trợ cả `application/json` và `multipart/form-data` (JSON `"payload"` + file tùy chọn `"bangChung"` upload qua `IFileStorageService` trước transaction, dọn orphan nếu SQL rollback, không lộ object path ra response).
   - Đọc `DonDatLichId`, khóa `don_dat_lich` $\rightarrow$ `tien_coc` $\rightarrow$ `hoan_coc` $\rightarrow$ `hoa_don`. Idempotency theo `(actorId, "DEPOSIT_REFUND_CONFIRM", request.RequestId)`.
   - Nếu `TrangThai != CHO_HOAN` trả `409 INVALID_TRANSITION`. Cập nhật `DA_HOAN`, `HoanBoiId`, `HoanAt`, `PhuongThuc`, `ThamChieu`, `BangChungPath`. Gọi `billingSettlement.TryTransitionBookingTerminalState(booking, now)`.
   - Sau commit SQL: gọi `email.SendRefundConfirmedAsync` (bắt lỗi an toàn).
5. **`E46` (`GET /api/admin/refunds`):** Quyền `DEPOSIT_REFUND`, phân trang `BookingQuery` (lọc `hoan_coc.trang_thai`), trả `PageRefund`.

### Checklist TDD bắt buộc (DoD)
- [ ] `E42`: Thu đủ trước hạn chuyển `DA_DAP_UNG`, thu thiếu giữ `CHO_THU`, cộng dồn nhiều lần (`M2-L05`), chống trùng `(nganHang, maGiaoDich)`, replay trả `200`.
- [ ] `E42` quá hạn (`M2-L06`): Nộp thiếu 100k trước hạn + 200k sau hạn $\rightarrow$ hủy đơn, `HET_HAN`, `giuLai == 0`, tự tạo `HoanCoc` (`CHO_HOAN`) đủ 300k.
- [ ] `E44`: Tạo lệnh hoàn hợp lệ `201`, chặn `409 AMOUNT_MISMATCH` khi vượt `thucNhan - obligatedAmount - danhHoan` (kể cả khi chưa lập hóa đơn).
- [ ] `E45`: Xác nhận `DA_HOAN` (cả JSON và multipart có file `"bangChung"`), replay không hoàn 2 lần; `E43/E46` phân trang đúng chuẩn.
- [ ] Chạy pass: `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~DepositsRefundsEndpointTests" -m:1`.

---

## 2. Issue #22: Thêm Dịch Vụ Phát Sinh (`E20`) & Quản Lý Phụ Phí (`E22a, E22b, E22c`)

- **Nhánh Git:** `feat/issue-22-extra-services-and-fees`
- **Phụ thuộc:** Issue #14 đã merge (có thể làm độc lập với #21).
- **Ước tính:** 5h
- **Endpoints (Tất cả yêu cầu Permission `INVOICE_CREATE`):**
  - `E20`: `POST /api/internal/booking-pets/{id}/extra-services`
  - `E22a`: `POST /api/internal/bookings/{id}/fees`
  - `E22b`: `PATCH /api/internal/fees/{id}`
  - `E22c`: `POST /api/internal/fees/{id}/cancel`

### Files cần tạo / sửa
- Create: `backend/src/PetSpa.Api/Services/Billing/ExtraChargeService.cs`
- Create: `backend/src/PetSpa.Api/Controllers/Billing/ExtraChargesController.cs`
- Modify: `backend/src/PetSpa.Api/Program.cs` *(đăng ký `ExtraChargeService`)*
- Test: `backend/tests/PetSpa.Tests/Billing/ExtraChargesEndpointTests.cs`

### Chi tiết nghiệp vụ & Guard cần cài đặt
1. **`E20` (`POST /api/internal/booking-pets/{id}/extra-services`):**
   - Trước khi mở transaction chính, gọi `await depositExpiry.ExpireOverdueDepositsAsync(clock.GetUtcNow(), null, ct)`.
   - Khóa `don_dat_lich` $\rightarrow$ `khung_gio_dat_lich` (`request.MaKhungGio`) $\rightarrow$ `dang_ky_ca`. Idempotency theo `(actorId, "EXTRA_SERVICE_CREATE", request.RequestId)` (replay trả `200`).
   - Guard: Đơn chưa có hóa đơn hiệu lực, pet đang ở `WAITING`, `IN_PROGRESS`, hoặc `PAUSED` (có `SizeChot != null`, chặn `READY`/`DONE`/`CANCELLED`/`NOT_ARRIVED`), XOR `MaDichVu` hoặc `MaCombo` (dịch vụ lẻ không được là `Cắt tỉa tạo kiểu` / `SPA_ADVANCED`), ca `MaKhungGio` hợp lệ trong khoảng ngày phục vụ của đơn.
   - Kiểm tra công suất qua `SpaCapacityCalculator`: `PetsDemand = 0` nếu pet đã có dịch vụ trong cùng `MaKhungGio`, ngược lại `1`; `MinutesDemand` = thời lượng dịch vụ/combo. Không đủ trả `409 CAPACITY_EXCEEDED`.
   - Tạo `DichVuTrongDon` (`LaPhatSinh = true`, `SizeChot = pet.SizeChot`, `GiaChot` theo `pet.SizeChot`, `TrangThai = CHO_THUC_HIEN`; nếu là combo thì tạo kèm `ThanhPhanDichVuTrongDon`), cộng `PhutDaGiu` (+ `SlotDaDat` nếu `PetsDemand == 1`) và `TongTamTinh`, trả `201 Created` (`OrderService`).
2. **`E22a` (`POST /api/internal/bookings/{id}/fees`):**
   - Khóa `don_dat_lich`. Idempotency theo `(actorId, "FEE_CREATE", request.RequestId)` (replay trả `200`).
   - Guard: Đơn chưa có hóa đơn hiệu lực, đơn đang `CONFIRMED`/`IN_PROGRESS`. Nếu có `maChiTietDon`, pet phải thuộc đơn và chưa `DONE`/`CANCELLED`.
   - **Guard Phụ phí gỡ rối lông (`api-contract.md` dòng 235):** Nếu `tenPhuPhi` chứa `"gỡ rối"` / `"go roi"` (case-insensitive), bắt buộc có `maChiTietDon` và pet phải có mã lông cho phép phụ phí gỡ rối (mặc định `"ROI_NANG"` hoặc mã có `choPhepPhuPhiGoRoi == true` trong `FUR_CONDITIONS`) **HOẶC** đã có ít nhất 1 `NhatKyChamSoc` có ảnh (`ObjectPath != null`), sai trả `409 INVALID_TRANSITION`.
   - Tạo `PhuPhi` (`SoTien > 0`, `TaoBoiId = actorId`), cộng `TongTamTinh`, trả `201 Created` (`FeeDto`).
3. **`E22b` (`PATCH /api/internal/fees/{id}`) & `E22c` (`POST /api/internal/fees/{id}/cancel`):**
   - Idempotency trong `audit_log` (`"FEE_EDIT"`, `"FEE_CANCEL"`). Guard: Đơn chưa có hóa đơn hiệu lực, `phuPhi.HuyAt == null`, pet gắn kèm (nếu có) chưa `DONE`.
   - `E22b` bắt buộc có `lyDo` và ít nhất 1 trường `tenPhuPhi` hoặc `soTien > 0`, cập nhật chênh lệch `TongTamTinh`.
   - `E22c` gán `HuyAt = now`, trừ `SoTien` khỏi `TongTamTinh`, trả `200 OK` (`FeeDto`).

### Checklist TDD bắt buộc (DoD)
- [ ] `E20`: Thêm dịch vụ đơn & combo phát sinh khi pet `IN_PROGRESS`/`PAUSED`, giữ thêm phút không tăng `SlotDaDat` nếu cùng ca, chốt giá theo `SizeChot`, replay trả `200`; chặn `409` khi có hóa đơn `UNPAID`, pet `READY`, hoặc hết phút.
- [ ] `E22a`: Tạo phụ phí gỡ rối thành công khi có `"ROI_NANG"` hoặc ảnh care log, chặn `409` khi `BINH_THUONG` không ảnh; tạo phí chung đơn (`maChiTietDon: null`).
- [ ] `E22b/E22c`: Sửa & hủy phụ phí cập nhật đúng `TongTamTinh`, chặn khi có hóa đơn `UNPAID`, replay idempotent qua `audit_log`.
- [ ] Chạy pass: `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~ExtraChargesEndpointTests" -m:1`.

---

## 3. Issue #23: Mở Rộng Hóa Đơn `E23` (Hotel/Combo/Cọc — G5), Thu Hồi Hóa Đơn `E25` & Cập Nhật `E26`

- **Nhánh Git:** `feat/issue-23-invoice-hotel-combo-withdraw`
- **Phụ thuộc:** Issue #21, #22 đã merge *(PR rủi ro cao — yêu cầu $\ge 2$ người duyệt)*.
- **Ước tính:** 5h
- **Endpoints:**
  - `E23` (mở rộng — G5): `POST /api/internal/bookings/{id}/invoices` (Permission `INVOICE_CREATE`)
  - `E25`: `POST /api/internal/invoices/{id}/withdraw` (Permission `INVOICE_REVOKE`)
  - `E26` (cập nhật): `POST /api/internal/invoices/{id}/checkout` (Permission `INVOICE_COLLECT`)

### Files cần sửa
- Modify: `backend/src/PetSpa.Api/Services/Billing/InvoiceService.cs`
- Modify: `backend/src/PetSpa.Api/Controllers/Billing/InvoicesController.cs`
- Test: `backend/tests/PetSpa.Tests/Billing/InvoicesEndpointTests.cs`

### Chi tiết nghiệp vụ & Guard cần cài đặt
1. **Mở rộng `E23` (`CreateInvoiceAsync` — G5):**
   - Khóa `don_dat_lich` $\rightarrow$ `tien_coc` $\rightarrow$ `hoa_don`.
   - Guard: Mọi pet phải ở `READY`, `DONE` hoặc `CANCELLED` (không còn `NOT_ARRIVED`/`WAITING`/`IN_PROGRESS`/`PAUSED`).
   - Sinh `CtHoaDon`:
     - **Dòng `SPA`:** Với pet không hủy, mọi dịch vụ `!= HUY` phải `HOAN_TAT` và có `GiaChot` (Combo hoàn tất ghi **1 dòng** `CtHoaDon`, không nhân trùng thành phần). Với pet `CANCELLED` (đã qua `E47`), lập dòng cho mọi `DichVuTrongDon` có `TrangThai == HOAN_TAT && GiaChot > 0` (kèm `DienGiai` các thành phần đã làm nếu combo hủy dở).
     - **Dòng `HOTEL` (`M2-L01`):** Với pet có `LoaiPhongId != null`: nếu không hủy thì `soDem = pet.SoDemChot` (`> 0`); nếu `CANCELLED` thì chỉ tạo dòng khi `pet.SoDemDaO > 0`. `ThanhTien = donGia * soDem`.
     - **Dòng `PHU_PHI` (`M2-L09`):** Mọi `PhuPhi` có `HuyAt == null`; nếu `ChiTietDonId == null` thì `TenPetSnapshot = null`, `DienGiai = $"Phí chung đơn: {fee.LyDo}"`.
   - Không lập hóa đơn rỗng (`lines.Count > 0 && total > 0`).
   - **Khấu trừ cọc & Zero-due:** `availableDeposit = Math.Max(0m, thucNhan - danhHoan)`, `cocKhauTru = Math.Min(total, availableDeposit)`, `conPhaiTra = total - cocKhauTru`. Nếu `conPhaiTra == 0`: đặt ngay `TrangThai = InvoiceState.PAID`, `PaidAt = now`, `ThuBoiId = actorId` (**không tạo `GiaoDichThu` 0 đồng**), và gọi `billingSettlement.TryTransitionBookingTerminalState(booking, now)`.
   - **Thay thế hóa đơn (`M2-L04`):** Nếu có hóa đơn `REVOKED` trước đó chưa bị bản nào khác thay thế, gán `invoice.ThayTheHoaDonId = latestRevoked.Id` (JSON field `thayTheHoaDon`).
2. **`E25` (`POST /api/internal/invoices/{id}/withdraw`):**
   - Quyền `INVOICE_REVOKE`. Đọc `DonDatLichId` từ `hoa_don.id == id`, khóa `don_dat_lich` $\rightarrow$ `tien_coc` $\rightarrow$ `hoa_don`. Idempotency theo `(actorId, "INVOICE_REVOKE", request.RequestId)`.
   - Chỉ thu hồi khi `TrangThai == InvoiceState.UNPAID` (`PAID`/`REVOKED` trả `409 INVALID_TRANSITION`).
   - Đặt `TrangThai = REVOKED`, `ThuHoiBoiId = actorId`, `ThuHoiAt = now`, `LyDoThuHoi = request.LyDo.Trim()`. **Không sửa `TongTien`, `CocKhauTru`, `ConPhaiTra` hay `CtHoaDon` cũ**; `InvoiceProjection` tự ẩn `Qr`.
3. **Cập nhật `E26` (`CheckoutInvoiceAsync`):** Sau khi chuyển hóa đơn sang `PAID`, gọi `billingSettlement.TryTransitionBookingTerminalState(booking, now)` để khép đơn `M2-05` nếu mọi pet đều đã `CANCELLED` và trả khách.

### Checklist TDD bắt buộc (DoD)
- [ ] `M2-L01`: Đơn Hotel-only lập hóa đơn có đúng dòng `HOTEL` (`soLuong = số đêm`).
- [ ] `E23`: Đơn có Combo + Phụ phí riêng pet + Phụ phí chung đơn + Cọc 30% tính đúng `tongTien`, `cocKhauTru`, `conPhaiTra`.
- [ ] `E23` zero-due: Khi cọc khả dụng $\ge$ `tongTien` $\rightarrow$ `conPhaiTra == 0`, tự chuyển `PAID`, không tạo `GiaoDichThu` 0đ, gọi `E26` bị chặn `409`.
- [ ] `E25` (`M2-L04`): Thu hồi `UNPAID` sang `REVOKED` (lưu `LyDoThuHoi`, ẩn QR, giữ nguyên `dongTien` & `cocKhauTru`), thêm được phụ phí `E22a`, lập lại `E23` có `thayTheHoaDon` trỏ bản cũ và tái phân bổ cọc đúng; chặn `E25` trên hóa đơn `PAID` (`409`).
- [ ] Chạy pass: `dotnet test backend/PetSpa.sln --filter "FullyQualifiedName~InvoicesEndpointTests" -m:1`.
