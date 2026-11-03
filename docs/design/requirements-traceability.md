# Traceability M2 — PetSpa & Hotel

**Phiên bản:** 1.0 — APPROVED (M2-15), 03/11/2026. Mapping phương án [API](api-contract.md) và [thiết kế](system-design.md); không chứng minh code/tests đã tồn tại. SRS v3.0, FR01 và M2-01–M2-15 là baseline. UC19B/UC23B giữ mã nhánh gốc, không dùng số lượng tiêu đề để tự suy ra scope mới.

## 1. Từng operation và kiểm chứng

| Operation | UC/Extension | Module | Bảng chính | Kiểm chứng cần có |
|---|---|---|---|---|
| E01 | FR01 | Identity | nhan_vien/quyen, otp_challenge, bao_ve_tracking | M2-T04 |
| E02 | UC10 | Booking | khach_hang/thu_cung, don_dat_lich/chi_tiet_don, reservation | M2-T01 |
| E03 | UC14 | Booking | khach_hang/thu_cung, don_dat_lich/chi_tiet_don, reservation | M2-T01 |
| E04 | UC14 | Booking | khach_hang/thu_cung, don_dat_lich/chi_tiet_don, reservation | M2-T01 |
| E05 | UC14 | Booking | khach_hang/thu_cung, don_dat_lich/chi_tiet_don, reservation | M2-T01 |
| E06 | UC02/07 | Booking | khach_hang/thu_cung, don_dat_lich/chi_tiet_don, reservation | M2-T01 |
| E07 | UC08 | Booking | khach_hang/thu_cung, don_dat_lich/chi_tiet_don, reservation | M2-T01 |
| E08 | UC15/26/34 | Booking | khach_hang/thu_cung, don_dat_lich/chi_tiet_don, reservation | M2-T01 |
| E09 | UC19 | Booking | khach_hang/thu_cung, don_dat_lich/chi_tiet_don, reservation | M2-T01 |
| E10a | UC16 | Booking | khach_hang/thu_cung, don_dat_lich/chi_tiet_don, reservation | M2-T01 |
| E10b | UC16 | Booking | khach_hang/thu_cung, don_dat_lich/chi_tiet_don, reservation | M2-T01 |
| E11 | UC17 | Booking | khach_hang/thu_cung, don_dat_lich/chi_tiet_don, reservation | M2-T01 |
| E12 | UC17/19 | Booking | khach_hang/thu_cung, don_dat_lich/chi_tiet_don, reservation | M2-T01 |
| E13 | UC18 | Booking | khach_hang/thu_cung, don_dat_lich/chi_tiet_don, reservation | M2-T01 |
| E14 | UC23B | Booking | khach_hang/thu_cung, don_dat_lich/chi_tiet_don, reservation | M2-T01 |
| E15 | UC23 | Care | chi_tiet_don, nhat_ky_cham_soc, dịch vụ/thành phần | M2-T05 |
| E16 | UC23B | Care | chi_tiet_don, nhat_ky_cham_soc, dịch vụ/thành phần | M2-T05 |
| E17 | UC24 | Care | chi_tiet_don, nhat_ky_cham_soc, dịch vụ/thành phần | M2-T05 |
| E18 | UC25 | Care | chi_tiet_don, nhat_ky_cham_soc, dịch vụ/thành phần | M2-T05 |
| E19 | UC30 | Care | chi_tiet_don, nhat_ky_cham_soc, dịch vụ/thành phần | M2-T05 |
| E20 | UC32 | Billing/Care | dịch vụ/giá, hoa_don/ct_hoa_don, thu/cọc/hoàn, audit | M2-T02/M2-T06 |
| E21 | UC24/32 | Billing/Care | dịch vụ/giá, hoa_don/ct_hoa_don, thu/cọc/hoàn, audit | M2-T02/M2-T06 |
| E22a | UC32 | Billing/Care | dịch vụ/giá, hoa_don/ct_hoa_don, thu/cọc/hoàn, audit | M2-T02/M2-T06 |
| E22b | UC32 | Billing/Care | dịch vụ/giá, hoa_don/ct_hoa_don, thu/cọc/hoàn, audit | M2-T02/M2-T06 |
| E22c | UC32 | Billing/Care | dịch vụ/giá, hoa_don/ct_hoa_don, thu/cọc/hoàn, audit | M2-T02/M2-T06 |
| E23 | UC31 | Billing/Care | dịch vụ/giá, hoa_don/ct_hoa_don, thu/cọc/hoàn, audit | M2-T02/M2-T06 |
| E24 | UC34 | Billing/Care | dịch vụ/giá, hoa_don/ct_hoa_don, thu/cọc/hoàn, audit | M2-T02/M2-T06 |
| E25 | UC31 | Billing/Care | dịch vụ/giá, hoa_don/ct_hoa_don, thu/cọc/hoàn, audit | M2-T02/M2-T06 |
| E26 | UC35 | Billing/Care | dịch vụ/giá, hoa_don/ct_hoa_don, thu/cọc/hoàn, audit | M2-T02/M2-T06 |
| E27 | UC53 | Scheduling/Billing | ca/đăng ký, phòng, cấu hình/giá | M2-T03 |
| E28 | UC41 | Scheduling/Billing | ca/đăng ký, phòng, cấu hình/giá | M2-T03 |
| E29 | UC50 | Scheduling/Billing | ca/đăng ký, phòng, cấu hình/giá | M2-T03 |
| E30a | UC11 | Billing/Care | dịch vụ/giá, hoa_don/ct_hoa_don, thu/cọc/hoàn, audit | M2-T02/M2-T06 |
| E30b | UC12 | Billing/Care | dịch vụ/giá, hoa_don/ct_hoa_don, thu/cọc/hoàn, audit | M2-T02/M2-T06 |
| E31a | UC11 | Billing/Care | dịch vụ/giá, hoa_don/ct_hoa_don, thu/cọc/hoàn, audit | M2-T02/M2-T06 |
| E31b | UC12 | Billing/Care | dịch vụ/giá, hoa_don/ct_hoa_don, thu/cọc/hoàn, audit | M2-T02/M2-T06 |
| E32 | UC64 | Scheduling/Billing | ca/đăng ký, phòng, cấu hình/giá | M2-T03 |
| E33 | UC64 | Scheduling/Billing | ca/đăng ký, phòng, cấu hình/giá | M2-T03 |
| E34a | UC64 | Scheduling/Billing | ca/đăng ký, phòng, cấu hình/giá | M2-T03 |
| E34b | UC64.1 | Scheduling/Billing | ca/đăng ký, phòng, cấu hình/giá | M2-T03 |
| E34c | UC64.2 | Scheduling/Billing | ca/đăng ký, phòng, cấu hình/giá | M2-T03 |
| E35 | UC56 | Billing/Care | dịch vụ/giá, hoa_don/ct_hoa_don, thu/cọc/hoàn, audit | M2-T02/M2-T06 |
| E36 | UC56 | Billing/Care | dịch vụ/giá, hoa_don/ct_hoa_don, thu/cọc/hoàn, audit | M2-T02/M2-T06 |
| E37 | UC16/35 | Booking | khach_hang/thu_cung, don_dat_lich/chi_tiet_don, reservation | M2-T01 |
| E38 | UC53 | Care | chi_tiet_don, nhat_ky_cham_soc, dịch vụ/thành phần | M2-T05 |
| E39 | UC14/OTP | Identity | nhan_vien/quyen, otp_challenge, bao_ve_tracking | M2-T04 |
| E40 | UC14/OTP | Identity | nhan_vien/quyen, otp_challenge, bao_ve_tracking | M2-T04 |
| E41 | UC07/14 | Identity | nhan_vien/quyen, otp_challenge, bao_ve_tracking | M2-T04 |
| E42 | Cọc | Billing/Care | dịch vụ/giá, hoa_don/ct_hoa_don, thu/cọc/hoàn, audit | M2-T02/M2-T06 |
| E43 | Cọc | Billing/Care | dịch vụ/giá, hoa_don/ct_hoa_don, thu/cọc/hoàn, audit | M2-T02/M2-T06 |
| E44 | Hoàn cọc | Billing/Care | dịch vụ/giá, hoa_don/ct_hoa_don, thu/cọc/hoàn, audit | M2-T02/M2-T06 |
| E45 | Hoàn cọc | Billing/Care | dịch vụ/giá, hoa_don/ct_hoa_don, thu/cọc/hoàn, audit | M2-T02/M2-T06 |
| E46 | Hoàn cọc | Billing/Care | dịch vụ/giá, hoa_don/ct_hoa_don, thu/cọc/hoàn, audit | M2-T02/M2-T06 |
| E47 | UC19B | Billing/Care | dịch vụ/giá, hoa_don/ct_hoa_don, thu/cọc/hoàn, audit | M2-T02/M2-T06 |
| E48 | UC30 | Care | chi_tiet_don, nhat_ky_cham_soc, dịch vụ/thành phần | M2-T05 |
| E49 | UC19B | Care | chi_tiet_don, nhat_ky_cham_soc, dịch vụ/thành phần | M2-T05 |
| E50 | UC15/D19 | Identity | nhan_vien/quyen, otp_challenge, bao_ve_tracking | M2-T04 |
| E51 | UC15 | Identity | nhan_vien/quyen, otp_challenge, bao_ve_tracking | M2-T04 |
| E52 | UC24 | Care | chi_tiet_don, nhat_ky_cham_soc, dịch vụ/thành phần | M2-T05 |
| E53 | FR01 | Identity | nhan_vien/quyen, otp_challenge, bao_ve_tracking | M2-T04 |
| E54 | UC41 | Scheduling/Billing | ca/đăng ký, phòng, cấu hình/giá | M2-T03 |
| E55 | UC50 | Scheduling/Billing | ca/đăng ký, phòng, cấu hình/giá | M2-T03 |
| E56 | UC64.4 | Scheduling/Billing | ca/đăng ký, phòng, cấu hình/giá | M2-T03 |
| E57 | Extension | Scheduling/Billing | ca/đăng ký, phòng, cấu hình/giá | M2-T03 |
| E58 | M2-08 | Billing/Care | dịch vụ/giá, hoa_don/ct_hoa_don, thu/cọc/hoàn, audit | M2-T02/M2-T06 |
| E59 | M2-03 | Billing/Care | dịch vụ/giá, hoa_don/ct_hoa_don, thu/cọc/hoàn, audit | M2-T02/M2-T06 |
| E60 | D18 | Identity | nhan_vien/quyen, otp_challenge, bao_ve_tracking | M2-T04 |
| E61 | UC24/19B | Care | chi_tiet_don, nhat_ky_cham_soc, dịch vụ/thành phần | M2-T05 |

## 2. Kịch bản nghiệm thu

| Mã | Kịch bản phải chứng minh |
|---|---|
| M2-T01 | Đơn nhiều pet, pet chỉ Spa/pet Hotel, idempotency, source ownership, tranh slot/phút/phòng cuối, dời lịch rollback, hủy/check-in đồng thời. |
| M2-T02 | Bảng giá đổi sau đặt không đổi cam kết; cân đổi size/thỏa thuận; combo dở không cộng trùng; invoice source đúng pet/size; REVOKED có mã thay thế, cọc không khấu trừ hai lần. |
| M2-T03 | Ca không nhân viên, hai trần, giảm nhân sự dưới cam kết bị chặn, ngày nghỉ vẫn chăm sóc Hotel, ca APPROVED khóa thay đổi và có lý do điều chuyển. |
| M2-T04 | OTP scope/TTL/replay, email mới không đọc pet cũ, resend không enumeration, mở khóa Admin, thu hồi permission có hiệu lực request kế tiếp, giữ một Admin. |
| M2-T05 | Ảnh tiếp nhận/ngoại lệ ảnh cuối, đúng assignment, PAUSED chỉ RESUME, PAID không DONE, hai nhận sớm đồng thời không lấy hết pet, CANCELLED có thực trả riêng. |
| M2-T06 | Cọc thiếu/thừa/đến muộn, xác nhận đụng hết hạn, refund retry không trả hai lần, tiền dư sau hủy, toàn pet hủy có/không phí, hậu PAID chỉ miễn phí đón trễ. |
| M2-T07 | Storage/SMTP lỗi trước/sau commit, file orphan private, Signed URL chỉ đúng đơn, audit/log không secrets, DELETE không cascade mất tiền. |
| M2-T08 | Đo phần xử lý API/DB <500ms sau khi nhận đủ request theo M2-14; báo cáo upload, SMTP và tổng request riêng; tải/rate limit nhiều IP/SĐT, không tuyên bố đạt khi chưa đo. |

Các kịch bản M2-L01–09 ở system-design bổ sung test dữ liệu. Ở implementation: xUnit WebApplicationFactory cho mọi endpoint, PostgreSQL thật cho concurrency/FK/locking; Vitest cho UI state và DTO adapter. Các dòng ở đây là kế hoạch kiểm chứng, không phải kết quả tests.

## 3. Cửa duyệt

Baseline M2 đã được nhóm trưởng duyệt theo M2-15, gồm 33 bảng và 68 operations. Các kịch bản ở đây là yêu cầu kiểm chứng cho implementation, không phải kết quả tests. Mermaid chưa render được ghi nhận tại design mục 9; phê duyệt tài liệu không tự yêu cầu bắt đầu M3.
