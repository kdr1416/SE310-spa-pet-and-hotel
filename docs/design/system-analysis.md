# Phân tích hệ thống — PetSpa & Hotel

**Phiên bản:** 2.0 — APPROVED (M2-15), 03/11/2026. Phân tích M2 tham chiếu [SRS v3.0](../requirements/SRS_He_Thong_Quan_Ly_Spa_Thu_Cung_AI_Readable.md) và [quyết định M2](../requirements/scope-and-decisions.md#m2-decisions). Bản này thay phân tích 1.1; không duy trì thêm một bảng state/quyền cạnh tranh với thiết kế chính.

## 1. Ranh giới và tác nhân

Một cơ sở PetSpa & Hotel; khách không tài khoản; nhân viên được phân quyền chức năng; Admin cấu hình/duyệt ngoại lệ. Public chỉ truy cập hồ sơ cũ sau proof đúng scope, hoặc một booking bằng SĐT+tracking. Bank transfer đối soát thủ công; SMTP và Storage là dịch vụ ngoài transaction DB.

## 2. Phân biệt các khái niệm

- Hồ sơ pet khác pet tham gia booking; nhiều dịch vụ không tạo nhiều trạng thái bàn giao.
- Reservation khác tiền đã chuyển; hết hạn chỗ không xóa lịch sử tiền.
- Giá tham khảo/bảng cam kết lúc đặt khác giá chốt cân/size tại quầy và invoice snapshot.
- PAID là tài chính, DONE là thực nhận; CANCELLED không chứng minh pet đã rời tiệm.
- Một invoice hiệu lực, nhiều bản thu hồi lịch sử; cọc khấu trừ không phải giao dịch nhận tiền mới.

## 3. Nguồn chi tiết duy nhất

| Nội dung | Nguồn |
|---|---|
| Quy tắc nghiệp vụ và amendment | SRS và scope-and-decisions M2-01–14 |
| Kiến trúc/module/quyền | system-design mục 1–3 |
| ERD/dictionary/delete | system-design mục 4 |
| Công suất/locking/giá | system-design mục 5 |
| Trạng thái/liên thực thể | system-design mục 6 |
| DFD/activity và lỗi nhánh | system-design mục 7–8 |
| Method/path/DTO/status | api-contract |
| UC–operation–bảng–kiểm chứng | requirements-traceability |

## 4. Giới hạn phê duyệt

Nhóm trưởng đã duyệt gói sáu tài liệu, gồm các chi tiết vật lý/route, theo M2-15. Xem trạng thái và giới hạn kiểm chứng tại system-design mục 9. Không phát sinh code/migration trong lượt này.
