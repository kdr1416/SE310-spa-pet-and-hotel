# PetSpa Design System — Master

**Project:** PetSpa & Hotel  
**Category:** Boutique Pet Spa & Hotel  
**Updated:** 2026-10-13  
**Approved Direction:** Vibrant Warm & Pastel Color-blocking (Hoàng hôn ấm & Pastel sinh động)

## 1. Quyết định & Phạm vi Thiết kế

Hệ thống thiết kế PetSpa được hoàn thiện dựa trên phản hồi thực tế từ người dùng, khắc phục hoàn toàn sự lạnh lẽo/đơn sắc của nền trắng xám trước đây, mang lại phong cách ấm áp, đáng yêu, chuẩn nhận diện Boutique Pet Spa:
- **Nhận diện chính:** Cam hoàng hôn rực rỡ (`#EA580C` - `#F97316` - `#F59E0B`), kết hợp nền kem ngà ấm áp (`#FAF8F5`).
- **Phân cấp Color-blocking:** 4 sắc thái pastel ấm áp phân loại trạng thái nghiệp vụ (Cam đào, Vàng bơ, Xanh bạc hà, Xanh pastel).
- **Phạm vi áp dụng:** Đồng bộ xuyên suốt từ Cổng khách hàng (Customer), Màn hình đăng nhập (Login), đến Không gian làm việc nội bộ (Staff & Admin).
- **Nguyên tắc kỹ thuật:** Không hardcode mã màu hex trực tiếp trong component; sử dụng 100% Design Tokens ngữ nghĩa khai báo tập trung trong `tailwind.config.js` và `main.css`.

---

## 2. Bảng Tokens Màu Sắc Ngữ Nghĩa (Semantic Color Tokens)

### 2.1. Brand Accent & Action (Cam Hoàng Hôn)

| Token Tailwind | Giá trị HEX | Mục đích sử dụng |
|---|---|---|
| `primary-50` / `accent-50` | `#FFF7ED` | Nền chip, badge vai trò, hover nhẹ |
| `primary-100` / `accent-100` | `#FFEDD5` | Nền badge trạng thái, viền nhẹ |
| `primary-200` / `accent-200` | `#FED7AA` | Viền thẻ nổi bật, border input focus |
| `primary-500` / `accent-500` | `#EA580C` | Icon squircle, brand logo, điểm nhấn chính |
| `primary-600` / `accent-600` | `#C2410C` | Nút CTA chính, text nhấn mạnh tương phản cao |
| `primary-700` / `accent-700` | `#9A3412` | CTA hover state, text link đậm |
| `primary-900` / `accent-900` | `#7C2D12` | Tiêu đề trên nền cam nhạt |

### 2.2. Pastel Color-blocking (Chỉ số & Khối trạng thái)

| Token Tailwind | Nền Card | Viền Thẻ | Icon Badge | Ngữ nghĩa nghiệp vụ |
|---|---|---|---|---|
| `pastel-orange` | `#FFF8F3` | `border-orange-200` | `#EA580C` | Lịch nhận hôm nay / Pet được giao / Quan trọng |
| `pastel-amber` | `#FFFDF0` | `border-amber-200` | `#F59E0B` | Đơn chờ duyệt / Chờ bắt đầu / Cảnh báo cần chú ý |
| `pastel-emerald` | `#F0FBF5` | `border-emerald-200` | `#10B981` | Đang chăm sóc / Hoàn tất / Check-in thành công |
| `pastel-blue` | `#F0F7FF` | `border-blue-200` | `#3B82F6` | Thu ròng / Sẵn sàng bàn giao / Tiện ích mở rộng |

### 2.3. Nền Canvas & Bề Mặt Thẻ (Surfaces & App)

| Token Tailwind | Giá trị HEX | Mục đích sử dụng |
|---|---|---|
| `app-bg` | `#FAF8F5` | Nền canvas toàn bộ ứng dụng (kem ngà ấm, dịu mắt) |
| `app-surface` | `#FFFFFF` | Nền thẻ panel, form, header, modal |
| `app-subtle` | `#FFFDF9` | Nền thẻ phụ, item hover |
| `app-border` | `#F0E9DF` | Đường viền ngăn cách mặc định (ấm áp, không dùng xám lạnh) |

---

## 3. Gradients & Shadow Tokens

### 3.1. Background Gradients

- **`bg-sunset`:** `linear-gradient(135deg, #ea580c 0%, #f97316 45%, #f59e0b 100%)`  
  *Dùng cho: Hero Banner, nút CTA chính, active navigation bar.*
- **`bg-warm-page`:** `linear-gradient(135deg, #faf8f5 0%, #fffbf7 50%, #fff6ec 100%)`  
  *Dùng cho: Màn hình Login, Landing hero background.*
- **`bg-attention`:** `linear-gradient(135deg, #fff7ed 0%, #ffedd5 100%)`  
  *Dùng cho: Khối Việc cần chú ý, thông báo quan trọng.*

### 3.2. Elevation & Warm Shadows

- **`shadow-card`:** `0 4px 16px -2px rgba(120, 53, 15, 0.04)` (Thẻ mặc định).
- **`shadow-warm-sm`:** `0 2px 8px rgba(234, 88, 12, 0.06)` (Badge, chip, nút phụ).
- **`shadow-warm-md`:** `0 8px 24px -4px rgba(234, 88, 12, 0.12)` (Nút CTA, thẻ hover).
- **`shadow-warm-lg`:** `0 14px 36px -4px rgba(234, 88, 12, 0.22)` (Hero banner, modal nổi).
- **`shadow-glow`:** `0 0 20px rgba(234, 88, 12, 0.25)` (Điểm sáng thương hiệu).

---

## 4. Typography & Bo Góc (Radii)

- **Heading:** Nunito, trọng số 700, 800, 900.
- **Body & Controls:** Plus Jakarta Sans, trọng số 400, 500, 600, 700.
- **Squircle Radii:**
  - `rounded-xl` (`12px`): Nút bấm, ô input, menu item.
  - `rounded-2xl` (`16px` - `18px`): Squircle icon badge, thẻ card tiêu chuẩn.
  - `rounded-squircle` (`22px`): Thẻ KPI MetricCard, Banner chính, Attention card.
  - `rounded-3xl` (`24px` - `28px`): Khối thẻ lớn (Login card, Hero card).
  - `rounded-full`: Pill badges, avatar status dot.

---

## 5. Quy Chuẩn Kỹ Thuật (Engineering Rules)

1. **Tuyệt đối không hardcode hex code trong template `.vue`:**
   - ❌ Sai: `class="bg-[#fff8f3] border-[#f0e9df] shadow-[0_4px_16px...]"`
   - ✅ Đúng: `class="bg-pastel-orange border-app-border shadow-card"`
2. **Không lạm dụng `!important` hoặc `!` Tailwind prefix:**
   - Cần cấu trúc CSS tầng components/utilities trong `main.css` rõ ràng để kế thừa tự nhiên.
3. **Bảo toàn 100% logic nghiệp vụ:**
   - Các thuộc tính thẻ root (như `<article class="overview-metric">`) cần giữ nguyên để bảo đảm các bài kiểm thử Vitest luôn PASS.
4. **Độ tương phản (Accessibility Contrast):**
   - Chữ trắng chỉ đặt trên nền gradient cam sẫm (`bg-sunset`) hoặc badge cam (`bg-primary-500`/`600`).
   - Trên nền kem nhạt, tiêu đề bắt buộc dùng `text-slate-900` hoặc `text-orange-950`.
