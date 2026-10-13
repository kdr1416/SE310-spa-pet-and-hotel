# Internal Dashboard Implementation Plan

> Trạng thái: đã được duyệt và triển khai 2026-10-13; kiểm chứng rút gọn theo yêu cầu người dùng.
> Ngày: 2026-10-13.
> Khi triển khai: dùng executing-plans, làm theo từng bước có kiểm chứng; không quét lại toàn bộ codebase.

**Goal:** Bổ sung tổng quan vận hành theo nhóm công việc và tạo phân cấp thị giác rõ hơn cho nội bộ.

**Architecture:** Tổng quan đọc dữ liệu qua các service/store hiện có, tổng hợp bằng hàm thuần và liên kết đến các workspace hiện hữu. Không thay nghiệp vụ booking, chăm sóc, tài chính hoặc lịch nhân viên. AppLayout tiếp tục là shell nội bộ; appearance vẫn dùng MASTER chung.

**Tech Stack:** Vue 3, Pinia, Vue Router, Tailwind, PrimeVue, Lucide; Vitest/Vue Test Utils hiện có. Chưa có nhu cầu thêm dependency.

**Spec:** Phân tích và thiết kế trong tài liệu này; tham chiếu `design-system/petspa/MASTER.md` và `../architecture/codebase-map.md`.

## 1. Hiện trạng đã kiểm tra

- `/admin/dashboard` → AdminDashboardView: thống kê theo ngày/tháng/năm, 3 chỉ số và bảng giao dịch. Đây là màn hình thống kê tài chính, chưa phải tổng quan vận hành.
- `/staff/dashboard` → StaffDashboardView → ScheduleWorkspace: lịch tuần, đăng ký ca và trạng thái đăng ký. Chưa có tổng quan việc cần làm.
- AppLayout chưa có menu Tổng quan; landing sau login đang trỏ vào hai màn hình trên.
- Screenshot `internal-preview.jpg`: nền, sidebar, bộ lọc, KPI và bảng gần như cùng tông sáng, mọi panel có trọng lượng tương tự. Thiếu một điểm nhấn chính và nhịp phân cấp giữa thông tin quan trọng và thông tin phụ.
- Nguồn hiện có: workspace store/getWorkspace, financeService và scheduleService; CareTasks đã lọc theo nhanVienId và chi tiết checkedIn. Kiểm tra lại payload/quyền chính xác khi triển khai.
- Đã kiểm tra source regression tests hiện có; chưa có bằng chứng về test riêng cho dashboard tổng quan mới. Không chạy lại application tests ở bước lập plan.

## 2. Phạm vi và quyết định đề xuất

Giữ nguyên Customer, font Nunito/Plus Jakarta Sans và bảng màu MASTER. Có thể điều chỉnh bố cục dashboard mới và phân cấp của shell nội bộ; không đổi bố cục/form/logic các workspace nghiệp vụ trong đợt này.

**Đề xuất route:** thêm `/admin/overview` và `/staff/overview`, menu Tổng quan đứng đầu, trở thành điểm vào sau login. Giữ `/admin/dashboard` là Thống kê và `/staff/dashboard` là Lịch làm việc; giữ các redirect cũ và khả năng mở bookmark. Người dùng đã duyệt thay đổi điều hướng này cùng plan.

Không tách role mới. Dùng ADMIN/STAFF, workGroup/capability hiện tại. Không tạo API mới, tự đổi hợp đồng live, migration hay reset mock data. Nếu dữ liệu cần thiết chưa có trong response đúng quyền thì thu hẹp widget hoặc xin quyết định bổ sung; không dùng legacy mockData để lấp số.

## 3. Tổng quan theo người dùng

| Người dùng | Chỉ số chính | Khối làm việc | Lối đi nhanh |
|---|---|---|---|
| Admin | Lịch nhận hôm nay, đơn đang chờ duyệt, pet đang chăm sóc, tiền thu trừ hoàn trong hôm nay | Lịch tiếp nhận hôm nay; yêu cầu chưa xử lý mà Admin có quyền xem; xu hướng 7 ngày | Lịch booking, tài chính, lịch nhân viên |
| Quầy | Lịch nhận hôm nay, đơn đang chờ duyệt, pet chưa check-in trong lịch đã xác nhận hôm nay, đơn READY chờ bàn giao | Danh sách lịch theo ca và việc cần tiếp nhận/bàn giao | Booking mới, danh sách lịch hẹn, lịch làm việc |
| Spa | Pet được phân công đang WAITING/IN_PROGRESS/READY trong đơn chưa kết thúc | Pet cần thao tác tiếp theo; ca published hôm nay của chính mình | Thực hiện dịch vụ, lịch làm việc |

Các con số phải ghi rõ đơn vị và khoảng thời gian. Chỉ số hôm nay dùng ngày Việt Nam và ngày nhận lịch; đơn chờ duyệt là backlog còn mở, không mặc định chỉ tính đơn tạo hôm nay. Pet đếm theo chi tiết đơn, đơn đếm theo booking. READY không đồng nghĩa đã thanh toán. Thu ròng tính theo ledger hôm nay, không theo giá tạm tính.

Staff không thấy doanh thu, reviews hoặc thông tin quản trị không được cấp quyền. Tổng quan Spa phải áp dụng cùng điều kiện nhiệm vụ với CareTasks, không đếm pet của người khác. Lịch chưa published không được đưa vào khối lịch chính thức.

Các nút tắt ưu tiên điều hướng đến màn hình hiện có; không thực hiện check-in/thu tiền/chuyển trạng thái trực tiếp trên overview. Nếu workspace chưa hỗ trợ mở một đơn từ query, trước mắt liên kết đến workspace tương ứng, không tạo nút giả.

## 4. Hướng thị giác

Theo Impeccable: tăng sức biểu đạt bằng chính hệ thống hiện có, làm nổi bật một vùng rồi giảm độ nổi của các vùng phụ. Giữ màu/font đã được người dùng chọn.

- **Vùng đầu trang:** một dải teal đậm lấy từ supporting teal của MASTER, chữ trắng, tiêu đề công việc/ngày và một CTA cam đúng quyền. Đây là điểm neo chính, không dùng ảnh stock hay gradient trang trí.
- **KPI:** số liệu có độ đậm rõ, nhãn ngắn, icon Lucide; dùng nền cam nhạt hoặc teal nhạt có chọn lọc. Không tô cả bốn card bốn màu hoặc tất cả đều có shadow lớn.
- **Nội dung:** khối việc cần xử lý nổi hơn; lịch/bảng và biểu đồ ở surface trắng để đọc lâu. Tiêu đề section có khoảng cách và phụ đề rõ; giảm cảm giác mọi thứ đều là một card giống nhau.
- **Sidebar/header:** giữ cấu trúc đang có; active item cam nhạt/cam đậm, chia nhóm menu hợp lý bằng khoảng cách, tên người/nhóm công việc dễ nhận biết. Không thêm thông báo chưa có nguồn dữ liệu.
- **Màu trạng thái:** giữ success/error/warning và kèm nhãn/icon, không dùng màu làm tín hiệu duy nhất.
- **Motion:** hover/focus nhẹ; không animation đếm số, carousel hay chuyển động nền.

UI/UX Pro Max: kết quả chart phù hợp dữ liệu theo thời gian gợi ý line chart. Đề xuất một đường số đơn nhận theo 7 ngày cho Admin, dùng màu MASTER, kèm số liệu/bảng đọc được; không thêm thư viện chart chỉ cho 7 điểm. Không dùng donut hoặc phần trăm tăng trưởng trang trí. Với dữ liệu trống, hiển thị đúng 0/empty state; lỗi tải không được giả thành 0.

Tìm kiếm UX ban đầu không trả về hướng dashboard đủ cụ thể; phần phân cấp trên là nhận định trực tiếp từ source/screenshot và nguyên tắc Impeccable, không xem kết quả tìm kiếm là yêu cầu sản phẩm.

## 5. Các bước triển khai sau khi duyệt

### Bước 1 — Dữ liệu tổng quan có test

- [x] Kiểm tra getWorkspace/getFinance/schedule response và quyền hiện có bằng CodeGraph exact files.
- [x] Tạo `frontend/src/utils/dashboardSummary.js` và `frontend/tests/unit/dashboardSummary.spec.js`.
- [x] Viết test trước: ngày Việt Nam, ngày nhận khác ngày tạo, booking nhiều pet, backlog, READY chưa trả tiền, ledger hoàn tiền, task của nhân viên khác, đơn đã kết thúc và lịch unpublished.
- [x] Chạy test đỏ đúng lý do; triển khai hàm thuần nhỏ nhất rồi chạy xanh. Không thay công thức tài chính hiện có.

### Bước 2 — Overview và điều hướng theo quyền

- [x] Tạo `views/admin/AdminOverviewView.vue`, `views/staff/StaffOverviewView.vue` và các component cần dùng chung trong `components/dashboard/` (OverviewBanner, MetricCard, WorkQueue, TrendChart khi cần).
- [x] Dùng service/store + useAction hiện tại, loading/error/retry/empty; lỗi từng nguồn không làm giả số liệu của nguồn đó.
- [x] Thêm overview routes/menu; cập nhật điểm vào trong LoginView, router redirect/guard fallback và các nơi đang chọn home. Giữ capability guards, không thay role model.
- [x] Thêm `tests/components/DashboardOverview.spec.js`: đúng widget/quyền từng nhóm, lối đi nhanh, không lộ tài chính cho Staff, empty/error và reload.
- [x] Kiểm tra default home bằng browser cho Admin/Quầy/Spa; giữ suite regression hiện có. Không thêm route unit suite theo quyết định giảm kiểm chứng.

### Bước 3 — Phân cấp thị giác nội bộ

- [x] Thêm các class/component dashboard có tên rõ nghĩa; chỉ bổ sung CSS nội bộ cần thiết trong main.css. Không dùng wrapper theme thứ hai và không đổi CSS toàn cục làm Customer lệch mẫu.
- [x] Giữ sidebar, header, tables/dialogs và vùng thao tác đọc được; không tăng trang trí ở các màn hình nghiệp vụ.
- [x] Ghi page override cho dashboard trong design-system/petspa/pages nếu được duyệt, cập nhật MASTER để cho phép bố cục overview và codebase-map sau implementation.

### Bước 4 — Kiểm chứng và bàn giao

- [x] Chạy `npm run test`, `npm run build` trong frontend.
- [x] Browser gộp một lượt: Admin desktop1440/mobile375, Quầy/Spa mobile375; kiểm tra điểm vào, quyền hiển thị, overflow và console. Không chạy lại walkthrough nghiệp vụ hoặc đủ bốn breakpoint theo yêu cầu người dùng.
- [x] Đối chiếu KPI với workspace/ledger; đổi ngày hoặc reload phải cập nhật đúng. Chart không có dữ liệu giả và dữ liệu có thể đọc không cần hover.
- [x] Kiểm tra Customer không bị thay đổi ngoài ý muốn; lưu screenshot nội bộ và cập nhật progress.

## 6. Điểm cần người dùng duyệt

1. Bổ sung Tổng quan riêng cho Admin/Staff và đặt làm điểm vào; giữ Thống kê/Lịch làm việc hiện có.
2. Hướng nội bộ: banner teal đậm + CTA cam, KPI nhấn màu có chọn lọc, vùng làm việc trắng dễ đọc.
3. Phạm vi đợt đầu: overview và shell nội bộ; các workspace nghiệp vụ chỉ hưởng component styling chung, chưa thiết kế lại từng form/table.

Đã triển khai overview/routes và page override. Không thay API, role hay dữ liệu; xem frontend-update-progress.md để biết kiểm chứng thực tế.


