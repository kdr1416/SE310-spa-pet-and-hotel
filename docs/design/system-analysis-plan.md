# Kế hoạch hoàn thiện phân tích và thiết kế hệ thống

**Ngày:** 17/10/2026. **Trạng thái:** baseline Q01–Q08, D01–D14 và thiết kế T01–T09 đã duyệt. API contract và traceability bản 0.1 đã viết để rà soát; chưa triển khai code/migration.

**Mục tiêu:** bộ tài liệu tiếng Việt đủ dùng báo cáo đồ án và làm đầu vào triển khai .NET Web API/EF Core/PostgreSQL/Supabase Storage; tận dụng frontend Vue hiện có.

**Đầu vào:** [scope-and-decisions.md](../requirements/scope-and-decisions.md), [mapping 28 mục](../requirements/frontend-use-case-mapping.md), SRS 1.1 và frontend thực tế. [ERD/Data Dictionary gốc](references/erd-original-reference.md) giữ làm bản đối chiếu không chuẩn tắc. Không dùng bố cục UI để suy ra schema một cách máy móc.

## Thứ tự và điểm xác nhận

| Bước | Sản phẩm tài liệu | Nội dung và điều kiện hoàn tất |
|---|---|---|
| 1. Chốt baseline — đã xong | scope-and-decisions.md + frontend-use-case-mapping.md | Q01–Q08 và ngoại lệ UC56 đã được người dùng chốt. Account seed, không published/payment gateway/refund/ticket. |
| 2. Chuẩn hóa yêu cầu — đã viết | Cập nhật SRS hiện có, không tạo SRS thứ hai | SRS 1.1: 27 UC + FR01, UC56, BR/NFR, luồng/ngoại lệ trọng yếu; sửa Public privacy, Hotel, lịch và thanh toán. |
| 3. Phân tích nghiệp vụ — baseline đã xác nhận | docs/design/system-analysis.md | Context/Use Case, quyền, bất biến, state/activity và 16 kịch bản giấy. CANCELLED + loại lý do và dời trước check-in đã duyệt D13/D14. |
| 4. Kiến trúc và dữ liệu — 1.0 đã duyệt | docs/design/system-design.md | Modular monolith, 20 bảng (19 nhóm cũ + dịch vụ phát sinh), Data Dictionary, ràng buộc/index, khóa/hủy/dời/checkout và Sequence. T01–T09 đã được người dùng xác nhận; chưa chạy migration. |
| 5. Giao tiếp — 0.1 chờ rà soát | docs/design/api-contract.md; phần Sequence trong system-design.md | Route/DTO/error/quyền/idempotency; upload, Tracking, invoice/payment và email fallback. Contract cần được duyệt trước triển khai. |
| 6. Traceability/lộ trình — 0.1 đã viết | docs/design/requirements-traceability.md | Đủ 28 mục + UC56 → BR/entity/API/UI → AC và tiêu chí bổ sung; NFR và sáu chặng E2E. Chưa phải kế hoạch thao tác code chi tiết hoặc lệnh triển khai. |

## Nguyên tắc thiết kế cần bảo vệ

- Không tạo API chỉ vì UI đã có nút. Một UC có thể dùng nhiều thao tác hoặc là bước trong transaction, không cố mỗi UC một endpoint.
- Giữ sức chứa PENDING; từ chối/hủy/no-show không trả hai lần. Spa và phòng theo đêm không dùng lẫn một bộ đếm.
- Hotel đếm pet/phòng từng đêm [nhận, trả), không COUNT booking overlap. Thuật toán Q02 và cơ chế lock/concurrency trong T01–T09 đã duyệt; cần kiểm chứng bằng PostgreSQL khi triển khai.
- Một khách có nhiều booking; tracking thuộc booking. Tra cứu Public không mở hồ sơ/hóa đơn/ảnh của đơn khác. Request public không ghi đè hồ sơ cũ.
- STAFF có thể làm nhiều công việc; điều kiện người thực hiện chăm sóc nằm ở assignment và ca hợp lệ. Không thiết kế RECEPTION/GROOMER thành role ngầm.
- APPROVED đồng thời là lịch đã công bố; không published riêng. ADMIN được thao tác vận hành nhưng vẫn phải tuân thủ bất biến.
- Invoice snapshot/PAID có ràng buộc và chống lặp thao tác. QR không thay việc nhân viên xác nhận đã nhận tiền.
- Supabase Storage lưu ảnh; secret/service credentials ở Backend, không nhúng vào frontend hay tài liệu. Quyền xem mọi ảnh nhật ký của một booking không đồng nghĩa công khai toàn bộ bucket.
- Không tự thêm payment webhook, refund/ticket platform, chi nhánh, mã phòng vật lý, bảng lịch sử sức khỏe riêng hoặc framework khác.

## Cách làm việc và kiểm chứng tài liệu

Mỗi chặng: trình bày phần hiểu/đề xuất và lý do → người dùng xác nhận → ghi tài liệu. Gộp câu hỏi có liên quan, tránh hỏi lựa chọn nhỏ có thể ghi giả định. Chỉ dùng CodeGraph cho module cần đối chiếu, không đọc lại toàn codebase.

Kiểm chứng gọn: kiểm tra đủ 28 mã, cross-reference UC/BR/entity/API, chạy vài kịch bản giấy cho phòng trùng đêm/multi-pet/partial check-in/no-show/payment lặp, kiểm tra Mermaid khi có sơ đồ. Không chạy application tests/build cho thay đổi chỉ tài liệu. Test code chỉ được lên lịch khi người dùng cho phép triển khai.

Kế hoạch này không yêu cầu phê duyệt lại 12 quyết định người dùng đã trả lời; chỉ cần xử lý các mục mở/ngoại lệ và duyệt sản phẩm ở từng chặng. Không thay đổi frontend hoặc Backend trong giai đoạn phân tích.
