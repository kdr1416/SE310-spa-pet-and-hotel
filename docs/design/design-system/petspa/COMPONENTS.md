# PetSpa Design System — Reusable Components Guide

Tài liệu hướng dẫn triển khai các thành phần giao diện dùng chung theo đúng hệ thống Tokens của PetSpa.

---

## 1. Brand Logo Squircle (`.brand-squircle`)
- **Icon Container:** `grid h-10 w-10 (hoặc h-11 w-11) place-items-center rounded-2xl bg-sunset text-white shadow-warm-sm`
- **Icon:** `PawPrint` với stroke 1.5 - 2px.
- **Brand Typography:**
  - Tiêu đề: `font-heading text-lg font-black text-slate-900` ("PetSpa <span class="text-primary-500">&</span> Hotel")
  - Tagline phụ: `text-[10px] font-sans font-bold tracking-[.15em] text-stone-500 uppercase`

---

## 2. Header Bars

### 2.1. Cổng Khách Hàng (Customer Header - `PublicNavbar.vue`)
- **Container:** `sticky top-0 z-40 border-b border-app-border bg-white/95 backdrop-blur-md shadow-warm-sm`
- **Pill Navigation Items:** `rounded-xl px-4 py-2 text-sm font-bold text-stone-600 transition-all hover:bg-primary-50 hover:text-primary-700`
- **Active Navigation Item:** `!bg-primary-50 !text-primary-700 !font-extrabold border border-primary-200 shadow-2xs`
- **Nút CTA "Đặt lịch ngay":** `bg-sunset text-white font-heading font-extrabold px-5 py-2.5 rounded-xl shadow-warm-md hover:shadow-warm-lg hover:scale-[1.02] active:scale-[0.98]`

### 2.2. Không Gian Nội Bộ (Internal Header - `AppLayout.vue`)
- **Container:** `sticky top-0 z-30 border-b border-app-border bg-white/95 backdrop-blur-md shadow-warm-sm`
- **Badge "Nội bộ":** `rounded-full border border-primary-200 bg-primary-50 px-2.5 py-0.5 text-[10px] font-extrabold text-primary-800`
- **Thẻ Nhân Viên (User Profile Chip):** `rounded-2xl border border-primary-200 bg-gradient-to-r from-pastel-orange to-white px-3.5 py-1.5 shadow-2xs`
  - Đèn trạng thái: Chấm xanh `bg-emerald-500` kèm hiệu ứng `animate-ping`.
  - Tên: `font-heading text-xs font-black text-slate-800`.
  - Vai trò: `rounded-full bg-primary-500 px-2.5 py-0.5 text-[10px] font-black uppercase tracking-wider text-white`.
- **Nút Đăng Xuất:** `rounded-xl border border-stone-200 bg-white px-3 py-1.5 text-xs font-bold text-stone-600 hover:border-red-200 hover:bg-red-50 hover:text-red-700`

---

## 3. Sidebar Điều Hướng Nội Bộ (`AppLayout.vue`)

- **Bố cục Phân nhóm (Grouped Navigation):**
  - Section Header: `text-[10.5px] font-black uppercase tracking-wider text-stone-400 font-heading px-3 pt-3 pb-1.5`
- **Menu Items:**
  - Thường: `rounded-xl px-3.5 py-2.5 text-sm font-semibold text-stone-600 transition-all hover:bg-primary-50/70 hover:text-primary-700 hover:translate-x-1`
  - Active: `bg-sunset text-white font-black shadow-warm-md` (Icon chuyển màu trắng tinh).
- **Hộp Thông Tin ("PetSpa Hub"):** `rounded-2xl border border-primary-200 bg-gradient-to-br from-pastel-orange to-primary-100/60 p-3.5 text-xs text-stone-600 shadow-2xs`

---

## 4. Thẻ Chỉ Số KPI (`MetricCard.vue`)

- **Root Element:** Bắt buộc dùng `<article class="overview-metric ...">`
- **Cấu trúc 4 Biến Thể Pastel (Variants):**
  - `orange`: `bg-pastel-orange border-orange-200 text-orange-950` + Icon badge `bg-primary-500 text-white`
  - `amber`: `bg-pastel-amber border-amber-200 text-amber-950` + Icon badge `bg-amber-500 text-white`
  - `emerald`: `bg-pastel-emerald border-emerald-200 text-emerald-950` + Icon badge `bg-emerald-500 text-white`
  - `blue`: `bg-pastel-blue border-blue-200 text-blue-950` + Icon badge `bg-blue-500 text-white`
- **Typography:**
  - Label: `text-xs font-bold uppercase tracking-wider opacity-80`
  - Value: `mt-3.5 font-heading text-3xl font-black md:text-4xl`
  - Hint: `mt-2.5 flex items-center gap-2 text-xs font-semibold` kèm chấm tròn màu cùng tone.

---

## 5. Nút Bấm & Hành Động (Buttons)

- **Primary Button (`.btn-primary`):**
  - `inline-flex items-center justify-center gap-2 rounded-xl bg-sunset text-white font-heading font-extrabold text-sm px-4 py-2.5 shadow-warm-md hover:shadow-warm-lg hover:scale-[1.01] active:scale-[0.99] transition-all`
- **Secondary / Neutral Button (`.btn`):**
  - `inline-flex items-center justify-center gap-2 rounded-xl border border-stone-200 bg-white px-4 py-2 text-sm font-bold text-slate-700 hover:bg-stone-50 transition-colors`
- **Danger Button (`.btn-danger`):**
  - `inline-flex items-center justify-center gap-2 rounded-xl border border-red-200 bg-red-50 text-red-700 hover:bg-red-100 font-bold text-sm px-4 py-2`

---

## 6. Ô Nhập Liệu & Form Fields (`.field`, `.label`)

- **Container Label (`.label`):** `block space-y-1.5 text-xs font-bold uppercase tracking-wider text-stone-600`
- **Input (`.field`):** `w-full rounded-xl border border-stone-300 bg-stone-50/50 px-3.5 py-2.5 text-sm font-semibold text-slate-800 transition-all focus:border-primary-500 focus:bg-white focus:outline-none focus:ring-2 focus:ring-primary-500/20`

---

## 7. Màn Hình Đăng Nhập (`LoginView.vue`)

- **Nền Toàn Trang:** `bg-warm-page` kết hợp các đốm sáng mờ nhẹ (`blur-3xl`).
- **Thẻ Đăng Nhập (Login Card):** `rounded-3xl border border-primary-200 bg-white p-7 shadow-warm-lg`
- **Khối Tài Khoản Mẫu (1-Click Role Switcher):** `rounded-2xl border border-amber-200 bg-gradient-to-br from-pastel-amber to-amber-50/50 p-4 shadow-2xs`
  - 3 nút vai trò: `Quầy` (`Store`), `Spa` (`HeartHandshake`), `Admin` (`ShieldCheck`).
