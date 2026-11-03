## 📌 Mô tả thay đổi (Summary)
<!-- Tóm tắt ngắn gọn thay đổi: tính năng mới, sửa lỗi, refactor hay tài liệu? -->

## 🔗 Liên kết Issue
Closes #

## 📋 Truy vết Hợp đồng & Nghiệp vụ (Traceability)
- **API Contract liên quan:** `docs/design/api-contract.md` (Liệt kê endpoint hoặc DTO bị ảnh hưởng, ví dụ: `E04 POST /api/bookings`)
- **Quyết định nghiệp vụ áp dụng:** `docs/requirements/scope-and-decisions.md` (Liệt kê ID: D01-D19, Q01-Q08, M2-01–M2-15)

## 🧪 Bằng chứng Kiểm thử (Verification)
- [ ] Backend: Đã chạy `dotnet test backend/PetSpa.sln` thành công cục bộ (kèm số test pass: ...)
- [ ] Frontend: Đã chạy `npm run test` trong `frontend` thành công cục bộ (56/56 tests)
- [ ] Build: Đã chạy `dotnet build backend/PetSpa.sln` không có lỗi và không có warning mới (0 errors, 0 warnings)
- [ ] Bao phủ test: Đã có ít nhất 1 test cho Happy Path và 1 test cho Sad Path (bắt lỗi nghiệp vụ/validation)

## 🛡️ Checklist An toàn & Chất lượng
- [ ] **Đã Rebase mới nhất:** Đã chạy `git fetch origin && git rebase origin/develop` trước khi push.
- [ ] **Schema Freeze:** Không tự ý sửa đổi Entity, DbContext hoặc tạo migration mới (Schema 33 bảng đã đóng băng; nếu cần sửa phải qua quy trình mở khóa schema riêng).
- [ ] **Không commit secrets:** Không chứa connection string thật, API key, mật khẩu, JWT secret trong code hoặc appsettings.
- [ ] **Đúng ranh giới:** Không tự ý đổi tên trường DTO khác với tài liệu `api-contract.md`.
- [ ] **Tối giản:** Không thêm thư viện ngoài hoặc tầng abstraction khi chưa thảo luận với nhóm.
- [ ] **Tự kiểm tra:** Đã đọc lại toàn bộ git diff của chính mình trước khi tạo PR.
