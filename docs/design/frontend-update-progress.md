# Progress — frontend-update-plan.md

## Đợt bổ sung 18/10/2026 — hoàn tất frontend/mock mục 7

- Plan/spec: frontend-update-plan.md mục 7; scope-and-decisions.md D15–D19, gồm thay đổi vaccine configurable tại check-in. Người dùng đã duyệt triển khai.
- Nhánh: `codex/frontend-booking-schedule-updates`. Làm trong checkout hiện tại để giữ nguyên tài liệu chưa commit; không tạo worktree, không commit/push hoặc thay Backend/migration. Các ghi chép 13/10 bên dưới là lịch sử, không mô tả trạng thái Git hiện tại.
- Hoàn tất 6 bước: migration cộng thêm/seed demo và capacity; ngày nghỉ/đăng ký ca; Customer booking/resend; notification/nhận–từ chối/policy vaccine; đổi role; kiểm chứng và tài liệu.
- Ruling: giữ IndexedDB version/store cũ, thêm featureVersion=2; không reset hoặc tự chèn booking vào kho đang dùng. Kho mới có seed demo riêng, unit fixtures mặc định vẫn rỗng. Room quantity chưa có mặc định 3 suất/loại và được giữ nguyên nếu đã cấu hình.
- Ruling: không đổi toàn bộ legacy workGroup/published trong đợt UI; hai sai lệch này vẫn ở backlog Backend/tích hợp. Không tự triển khai những UC thiếu ngoài đợt đã duyệt.
- Ruling: Admin chỉ cấu hình ngày nghỉ trong tuần chưa mở; cả ca chăm sóc Hotel được biểu diễn bằng purpose=HOTEL_CARE. Khóa lại form không xóa dấu đã từng mở. Tồn phòng vẫn đếm qua ngày nghỉ.
- Ruling: gửi lại mã dùng bookingEmail snapshot, không email master mới; cooldown 60s, response chung và outbox chỉ Admin. Mock không có SMTP hoặc chống abuse ở quy mô server.
- Ruling: lịch hiển thị 14 ngày; kiểm tra đặt phòng theo toàn khoảng, tối đa 366 đêm để giới hạn vòng lặp client. Đây là giới hạn mock, chưa phải yêu cầu thời gian lưu trú của Backend.
- RED: 10 test mới thất bại đúng các tính năng còn thiếu (vaccine/capacity/lý do/role/holiday/resend). GREEN: 10/10 sau phần mock. Nhóm mở rộng có 13 integration tests, bảo vệ cả migration/Blob và ảnh upload không tự xác minh.
- Phát hiện/fix: seed Spa có thể trùng ca 0 suất vào Chủ nhật khi ngày mai là thứ Hai; đã tái hiện bằng test demo store và chuyển lịch mẫu sang thứ Ba. Test seed/store sau sửa PASS.
- Kiểm chứng toàn bộ: `npm run test` PASS, 12 files / 55 tests. Sau đó thêm hiển thị ngày nghỉ chủ động cho Spa và chạy `npm run test -- tests/components/BookingForm.spec.js` PASS 4/4; không lặp full suite cho thay đổi cục bộ này.
- Build cuối: `npm run build` PASS, 1985 modules. Kiểm tra Vue SFC trực tiếp trước đó: 9 file PASS. `git diff --check` PASS. Không chạy browser/viewport audit rộng; không khẳng định đã QA trực quan toàn ứng dụng.
- Source/tài liệu liên quan: bookingAvailability.js; mockOperations/repository/seed; BookingForm/DecisionPanel/Notifications; ScheduleWorkspace; AdminStaff/Settings; Tracking; service adapters/types; codebase-map và frontend-use-case-mapping.
- Hạn chế: vẫn chỉ frontend/mock, chưa PostgreSQL/Supabase/SMTP/VietQR thật. Schema/API/Word/Figma chưa tự đồng bộ toàn bộ các field mới. Các UC thiếu ngoài đợt này vẫn giữ trong mapping.

## Lịch sử 13/10/2026

13/10/2026. Implementation approved. Workspace has no Git repository; implemented in the supplied frontend without commits, worktrees or Backend changes. This ledger remains for handoff.

## Rulings

- Keep ADMIN | STAFF. Mock workGroup RECEPTION/SPA separates capabilities; final Backend role model is undecided. Legacy STAFF without a work group gets read/schedule capabilities only, avoiding redirect loops without granting payments.
- IndexedDB stores versioned mock records and image Blobs. sessionStorage stores only the mock session user ID per tab. This is a demo, not production authentication.
- Use one shared workspace store and reusable Booking/Schedule workspaces instead of many near-identical small views/stores. Views call domain services for mutations; rules run again in the shared transaction body.
- Use native IndexedDB with one state record/transaction. No Dexie dependency. Existing legacy mock/localStorage namespaces are preserved and not silently migrated/deleted.
- qrcode creates a local PETSPA_DEMO payload; never a bank transfer code. Bank details/payment policies remain unconfirmed. All new live operations explicitly reject until Backend contracts are agreed; live login keeps the existing /auth/login contract.
- Test runner upgraded to Vitest 5.0.4 (supports existing Vite 6.4), Vue Test Utils pinned to 2.4.6 for Node compatibility; jsdom 26 for component behavior tests. No runtime test dependencies.
- MASTER palette/font retained, HTML layout adapted. Orange CTA uses dark text at normal state and white on darker hover for contrast; MASTER button example updated to resolve its contradictory foreground guidance.

## Implemented milestones

1–2. Permissions/tokens/router/mock services/repository/pricing/booking rules implemented. Initial missing-module tests observed RED then GREEN. Slot counting includes pets, idempotent booking request IDs, and cancellation releases once.
3–4. Home/Services/About, one-page multi-pet booking/Hotel evidence/summary/local QR, lookup/current history/requests implemented. Component tests observed RED then GREEN for pet add/remove, failed-submit preservation and stale lookup removal.
5–6. Shared internal list/calendar/new-booking/pending queue/detail/check-in/assignment/surcharge/snapshot/payment; assigned care tasks with Blob uploads and transition conditions implemented. Multi-pet completion and paid-price immutability covered by workflow tests.
7. Staff weekly dashboard/registration, Admin periods/open/lock/review/reassign/publish/print implemented. Deadline, duplicate, overlap and published-lock tests pass.
8–9. Admin revenue/order filters, catalogue prices, invoices, complaint/refund approvals, private reviews, surcharge rules, staff creation/activation and personal/system settings implemented. Mock password derivation and account capability guards tested; no plaintext password persisted.
10. Read-only independent review via one reviewer as required by executing-plans. Four P2 findings fixed with five RED→GREEN regression tests: publish/edit overlap, care weight preservation, lookup attempt window, addon approval information. Additional validation tests protect hotel-only check-in and legacy/unknown-role redirect behavior. Source formatted for readability.

## Browser evidence

- Customer created a two-pet booking. QR displayed 390,000 VND, marked non-payable demo. Tracking found both pets after reload, proving actual IndexedDB persistence.
- Reception accepted and checked in both pets, assigning spa; spa saw only assigned tasks. Real file chooser uploads of tests/fixtures/care.png recorded initial/final Blob photos. Tracking/care showed the updated measured weight (7kg) while invoice prices stayed fixed.
- Reception created one snapshot, opening transfer QR kept UNPAID; explicit mock confirmation changed to PAID/COMPLETED. Customer could review and request refund; Admin approved 10,000 VND. Statistics displayed 390,000 collected and 380,000 net.
- Spa direct /admin/finance navigation redirected to its dashboard. Spa registered next week, Admin approved/published, spa dashboard showed the published shift.
- Home/Booking/Tracking checked at 375/768/1024/1440px: no document horizontal overflow; heading font Varela Round. Mobile menu displayed all required Customer links. Internal views checked at the same breakpoints; tables scroll inside containers.
- Browser password/account-creation actions were not automated; the browser tool requires handoff for credential changes. Password derivation is verified with generated inputs in unit tests.

## Verification and limits

Final test/build commands and counts are recorded after the final verification run. No Backend/SQL concurrency/email/cloud uploads/payment processing was tested or claimed.

npm audit --omit=dev: 0 vulnerabilities. Full audit after the compatible test update: 7 findings (2 moderate, 5 high) remain in the Tailwind 3 development dependency tree; no patched Tailwind 3 version was available in the checked registry. No forced major Tailwind upgrade was made in this UI task.

The mock's published schedules are immutable. Printing/export uses the browser print dialog, not an Excel export. Catalogue prices are mocked and contact details remain unconfirmed. Final Backend roles, OTP/PIN, bank details and refund policy are open integration decisions.

Final verification: npm run test -> 8 test files / 33 tests PASS; npm run build -> PASS (Vite 6.4.4). Browser evidence above was collected against the running dev server at 127.0.0.1:5173. Screenshot: docs/design/frontend-preview.jpg. No commits/deployment.


## Customer visual refinement — 2026-10-13

- Approved direction: landing.html appearance, neutral surfaces, orange emphasis, Nunito headings and Plus Jakarta Sans body. Preserve layout/content/routes/API and internal theme.
- Ruling: scope appearance overrides under CustomerLayout instead of changing global Tailwind/PrimeVue tokens — shared components serve internal screens too.
- Added customer.css; loaded Customer and existing internal fonts together. Updated only presentation classes in CustomerLayout, home and navbar; business logic unchanged.
- Verification: npm run test — 8 files, 33 tests passed. npm run build — passed, 1977 modules.
- Browser: home at 375/768/1024/1440px without horizontal overflow; mobile booking and tracking render with new fonts/field styling; mobile menu opens. CTA uses #C2410C with white text for contrast.
- Review found the services Dialog is teleported to body; added the Customer scope class to its root without changing teleport/focus behavior. Browser confirmed dialog uses Plus Jakarta Sans, white surface and orange CTA; open/close works. Tests and build rerun after the fix: both passed.

## Shared application theme — 2026-10-13

- User approved applying the Customer appearance to Staff/Admin and removing the Customer-only theme scope.
- Consolidated neutral surfaces, Nunito/Plus Jakarta Sans, orange action buttons and component styling into main.css/Tailwind. PrimeVue primary controls use #C2410C/white; internal active navigation uses subtle orange. Supporting teal progress/status meanings remain.
- Removed customer.css and customer-theme classes. CustomerLayout remains the public navigation/footer structure; routes, data and behavior unchanged.
- Verification: 33 tests / 8 files pass; build passes (1976 modules). Read-only final review found no concrete defects.
- Browser: login, Admin statistics/table/booking dialog and both Staff demo groups checked. Admin at 375/768/1024/1440px has no horizontal overflow; Staff at 1024px checked. Dialog inherits shared fonts and orange CTA without a scope class. No console errors in checked preview.
- Existing dev preview at 5173 kept stale Tailwind config. Fresh dev server at 5175 verified new generated utilities. Its mock data is separate by origin; existing 5173 data was not modified or migrated.
- Preview screenshot: internal-preview.jpg.

## Internal overview — 2026-10-13

- Implemented admin/overview and staff/overview, shared OverviewWorkspace, MetricCard/TrendChart and pure dashboardSummary. Added first menu item/default login and guard fallback; preserved old statistics/schedule routes and redirects.
- Role-specific summaries use existing booking/finance/schedule services. Spa matches own checked-in tasks in open bookings. Staff never calls finance. Partial errors keep unavailable source values as —, not 0.
- Visuals: teal banner, selective orange emphasis, quiet white content, inline accessible 7-point SVG trend with visible values. No dependency added, no mock reset or schema/API change.
- Ruling: user requested bounded verification — 4 unit cases + 2 component cases for new KPI/privacy/error behavior; existing suite/build once at completion; one desktop/mobile browser round. No repetitive 4-breakpoint matrix, separate route unit suite or full business walkthrough.
- Verification: new focused unit test first failed on missing module, then 4 cases passed. Completion suite: 39 tests / 10 files passed; build passed (1982 modules). Browser Admin at1440/375, Staff Quầy/Spa at375: default overview route, no Staff revenue widget, no horizontal overflow or console errors.
- Final review completed and its two findings fixed; implementation uses no Git worktree/commits because workspace is not a Git repository.
- Preview: localhost:5175; screenshot internal-overview-preview.jpg. Live Backend remains unimplemented; overview uses current integration boundary and reports service errors if mock is disabled.
- Final review caught two defects; focused regressions reproduced both before fixes. Corrected partial multi-pet check-in count across eligible open arrivals; excluded permanent REVIEW records from actionable queue and linked ADDON to booking operations. Remaining requests link to finance. Reduced scope verification rerun: dashboard unit/component files only; source build rerun because source changed.

- Post-fix verification: 7 focused dashboard tests passed; final build passed. Full 39-test suite had passed before the bounded review; no redundant full-suite rerun after the localized fixes.

## Internal overview aesthetic redesign (Warm Oat & Honey Tangerine) — 2026-10-13

- User requested visual overhaul of internal overview to eliminate dark bulky teal blocks and blurry gradients, adopting a warm, friendly and crisp pet-care personality.
- Palette updated: canvas background to Warm Oat (#FAF8F5), warm card borders (#F0E9DF), crisp solid white cards, and warm honey tangerine (#EA580C/#C2410C) accents.
- Layout refinement: Replaced generic dark banner with a solid, structured Spa Salutation card with status chip and clear pawprint badge; MetricCard enhanced with Hero Metric hierarchy (warm border and squircle badge); Work Roster, Attention Hub, and TrendChart styled with warm, high-contrast, delightful pet-spa elements.
- Verification: npm run test -> 10 test files / 40 tests PASS; npm run build -> PASS (1982 modules transformed). Customer views preserved.
