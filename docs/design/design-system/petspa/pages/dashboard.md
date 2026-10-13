# Internal Overview Page Specification

**Approved:** 2026-10-13 (Cập nhật định hướng Hoàng hôn & Pastel). Áp dụng cho `/admin/overview` và `/staff/overview`.

## 1. Hero Banner Hoàng Hôn (`.overview-banner`)
- Nền sử dụng token gradient hoàng hôn `bg-sunset` (`#ea580c` → `#f97316` → `#f59e0b`).
- Bo góc squircle `rounded-squircle` (22px), đổ bóng ấm `shadow-warm-lg`.
- Tiêu đề chữ trắng tinh, badge thông tin vai trò dạng kính mờ (`bg-white/15 border-white/30 text-white`).
- Nút CTA chuyển sang thẻ trắng chữ cam đậm tương phản cao (`bg-white text-primary-700 shadow-warm-md hover:bg-orange-50`).
- Widget "Trạng thái ngày" bên phải là thẻ trắng nổi khối `bg-white/95 text-slate-800 shadow-md`.

## 2. Thẻ Chỉ Số Pastel Color-blocking (Metric Cards)
- Bố cục 4 cột trên desktop (`xl:grid-cols-4`), 2 cột trên tablet (`sm:grid-cols-2`).
- 4 thẻ mang 4 sắc thái pastel ấm độc lập (`variant`):
  1. `orange`: Nền `bg-pastel-orange`, viền `border-orange-200`, icon badge `bg-primary-500 text-white`.
  2. `amber`: Nền `bg-pastel-amber`, viền `border-amber-200`, icon badge `bg-amber-500 text-white`.
  3. `emerald`: Nền `bg-pastel-emerald`, viền `border-emerald-200`, icon badge `bg-emerald-500 text-white`.
  4. `blue`: Nền `bg-pastel-blue`, viền `border-blue-200`, icon badge `bg-blue-500 text-white`.
- Thẻ bắt buộc giữ thẻ root là `<article class="overview-metric">` để đảm bảo suite test Vitest.
- Micro-interaction: Khi hover nhấc nhẹ -3px kèm bóng đổ êm ái cùng tone màu.

## 3. Khu vực Nghiệp vụ & Chú ý
- **Việc cần chú ý (`.overview-attention`):** Gradient cam đào ấm `bg-attention`, 2 thẻ hàng đợi nổi bật số đếm to màu cam.
- **Danh sách tiếp nhận & nhiệm vụ:**
  - Badge ca trực (`Ca 1`, `Ca 2`) dùng squircle cam nhạt `bg-orange-50 text-primary-700 border-orange-200/80`.
  - Icon pet dùng squircle cam nổi bật `bg-primary-500 text-white`.
  - Empty state dùng viền đứt nét ấm và icon calendar cam dịu mắt.
- **Phân quyền hiển thị:**
  - Admin: Xem nhịp tiếp nhận (TrendChart), yêu cầu tài chính/khiếu nại, thu ròng trong ngày.
  - Staff (Spa): Chỉ xem danh sách thú cưng được phân công cho mình và ca làm việc cá nhân đã công bố. Không hiển thị bất kỳ widget hay số liệu tài chính nào.
