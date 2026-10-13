# PetSpa & Hotel Management System (SE310)

Hệ thống quản lý dịch vụ chăm sóc và khách sạn lưu trú cho thú cưng (**PetSpa & Hotel**). Dự án kết hợp cổng trải nghiệm khách hàng (Customer Portal) và phân hệ tác nghiệp nội bộ (Staff / Admin Portal) với hệ thống Design System **Vibrant Warm & Pastel Color-blocking** hiện đại, thân thiện.

---

## 🐾 Tính năng chính

### 1. Phân hệ Khách hàng (Customer Portal)
- **Trang chủ & Dịch vụ**: Giới thiệu thương hiệu, bảng giá minh bạch cho từng nhóm cân nặng và loại dịch vụ (Spa tắm/vệ sinh, Hotel lưu trú, Combo toàn diện).
- **Đặt lịch trực tuyến (Booking)**: Đặt lịch cho nhiều thú cưng trong một đơn, chọn dịch vụ riêng lẻ hoặc combo, kiểm tra slot trống và quét mã thanh toán mẫu.
- **Tra cứu tiến độ (Live Tracking)**: Tra cứu nhanh bằng Số điện thoại + Mã đặt lịch (bảo mật rate-limit), theo dõi trạng thái từng bé theo thời gian thực (Chờ tiếp nhận → Đang chăm sóc → Đã sẵn sàng → Hoàn tất) kèm ảnh chụp trực tiếp từ kỹ thuật viên.
- **Yêu cầu dịch vụ phát sinh & Đánh giá**: Duyệt phụ phí add-on, xem hóa đơn minh bạch, gửi phản hồi và khiếu nại.

### 2. Phân hệ Nhân viên (Staff Portal)
- **Lễ tân quầy (Receptionist)**: Tiếp nhận thú cưng (Check-in), kiểm tra thể trạng & cân nặng, chốt dịch vụ, phân công kỹ thuật viên phụ trách, snapshot hóa đơn và thu ngân (Cashier).
- **Kỹ thuật viên Spa (Care Technician)**: Danh sách thú cưng được phân công ca trực, cập nhật nhật ký chăm sóc, tải ảnh chụp trước/sau dịch vụ, xác nhận hoàn thành công việc.
- **Quản lý ca làm việc**: Xem lịch làm việc cá nhân đã được công bố, đăng ký ca làm việc theo tuần.

### 3. Phân hệ Quản trị viên (Admin Portal)
- **Tổng quan điều hành (Overview Workspace)**: KPI thời gian thực (Đơn chờ duyệt, Ca trực, Đang phục vụ, Thu ròng), biểu đồ xu hướng tiếp nhận 7 ngày gần nhất, danh sách việc cần xử lý gấp.
- **Điều phối lịch & Ca làm (Staff Scheduling)**: Mở đợt đăng ký, duyệt/từ chối ca, điều chuyển nhân sự, chốt và công bố lịch làm việc toàn chi nhánh, hỗ trợ in lịch chuẩn trình duyệt.
- **Tài chính & Hóa đơn (Finance)**: Quản lý biểu giá, đối soát hóa đơn, xử lý yêu cầu hoàn tiền (Refund ledger) và duyệt đánh giá dịch vụ.
- **Quản trị nhân sự & Cấu hình**: Quản lý tài khoản nhân viên, phân nhóm kỹ năng, thiết lập khung giờ và giới hạn slot tiếp nhận theo ngày.

---

## 🛠️ Công nghệ sử dụng

- **Frontend Framework**: [Vue 3](https://vuejs.org/) (Composition API với `<script setup>`)
- **Build Tool**: [Vite 6](https://vitejs.dev/)
- **State Management**: [Pinia](https://pinia.vuejs.org/)
- **Routing**: [Vue Router 4](https://router.vuejs.org/) (Lazy loading, async auth hydration, capability & role guards)
- **UI & Styling**:
  - [Tailwind CSS 3](https://tailwindcss.com/) (Hệ thống semantic design tokens tập trung)
  - [PrimeVue 4](https://primevue.org/) (Preset Aura tùy biến tông màu cam ấm)
  - [Lucide Vue Next](https://lucide.dev/) (Icon pack hiện đại)
  - Typography: **Nunito** (tiêu đề, số liệu) & **Plus Jakarta Sans** (nội dung, biểu mẫu)
- **Kiểm thử (Testing)**: [Vitest 5](https://vitest.dev/) (10 test suites, 40 tests bao phủ rules, workflows, regressions và components)
- **Dữ liệu & Mock Engine**: Transactional IndexedDB Engine lưu trữ trạng thái đơn, hóa đơn, lịch làm việc và hình ảnh Blob cục bộ, mô phỏng đầy đủ luồng Backend API thực tế.

---

## 📂 Cấu trúc thư mục

```text
FinalProject/
├── docs/                      # Tài liệu dự án
│   ├── architecture/          # Bản đồ kiến trúc & codebase map
│   ├── design/design-system/  # Quy chuẩn Design System (MASTER, COMPONENTS, pages)
│   └── requirements/          # Yêu cầu nghiệp vụ & cập nhật
├── frontend/                  # Mã nguồn ứng dụng Vue 3
│   ├── src/
│   │   ├── assets/            # CSS toàn cục & semantic utility layers
│   │   ├── components/        # Components tái sử dụng (booking, dashboard, tracking, ...)
│   │   ├── layouts/           # AppLayout (nội bộ) & CustomerLayout (khách hàng)
│   │   ├── services/          # API abstraction clients & Mock repository IndexedDB
│   │   ├── stores/            # Pinia stores (auth, booking, workspace)
│   │   ├── types/             # TypeScript / JSDoc contract definitions
│   │   ├── utils/             # Business rules, formatters, permissions
│   │   └── views/             # Các trang Customer, Staff, Admin, Auth
│   ├── tests/                 # Vitest test suites (unit, integration, components)
│   ├── index.html
│   ├── package.json
│   ├── tailwind.config.js
│   └── vite.config.js
├── AGENTS.md                  # Quy ước phát triển & làm việc
├── .gitignore                 # Loại trừ build, dependencies và cache
└── README.md
```

---

## 🚀 Hướng dẫn cài đặt & Chạy ứng dụng

### Yêu cầu môi trường
- [Node.js](https://nodejs.org/) version 18+ trở lên
- npm hoặc pnpm

### 1. Cài đặt dependencies
```bash
cd frontend
npm install
```

### 2. Khởi chạy môi trường phát triển (Dev Server)
```bash
npm run dev
```
Truy cập ứng dụng tại URL hiển thị trên terminal (mặc định: `http://localhost:5173`).

### 3. Tài khoản thử nghiệm (1-Click Demo Login)
Tại trang Đăng nhập nội bộ (`/login`), hệ thống hỗ trợ đăng nhập 1-click cho các vai trò:
- **Admin**: Quản trị viên toàn hệ thống
- **Quầy**: Lễ tân tiếp nhận & thanh toán
- **Spa**: Kỹ thuật viên chăm sóc & cập nhật ảnh

### 4. Chạy kiểm thử tự động (Vitest)
```bash
cd frontend
npm test -- --run
```

### 5. Build đóng gói Production
```bash
cd frontend
npm run build
```
Kết quả build được tạo tại thư mục `frontend/dist/`.

---

## 🎨 Design System
Chi tiết về hướng dẫn sử dụng Token, Bảng màu hoàng hôn ấm áp (`sunset`), Bộ thẻ KPI pastel (`pastel.*`), và quy chuẩn component được lưu trữ chi tiết tại:
👉 [`docs/design/design-system/petspa/MASTER.md`](docs/design/design-system/petspa/MASTER.md)
