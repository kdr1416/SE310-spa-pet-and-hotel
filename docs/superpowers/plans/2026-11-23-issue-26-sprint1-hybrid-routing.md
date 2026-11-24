# Issue #26 Phase 1 — Allowlist Hybrid Routing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Không triển khai khi người dùng mới yêu cầu lập plan.

**Goal:** Gỡ G6, nối 15 operations Sprint 1 ở tầng transport/adapter, giữ demo fallback tách khỏi dữ liệu live và bảo vệ regression frontend.

**Architecture:** Vue services tiếp tục gọi `callOperation(action, payload, isPublic)`. Allowlist tĩnh chọn adapter→Axios hoặc mock repository; auth giữ identity live và demo context riêng. Adapter stateless chỉ đổi DTO/projection, không gọi E23 ngầm khi thu tiền, không tự suy slot/assignment/đồng thuận.

**Tech Stack:** Vue 3, Pinia, Axios hiện có, Vitest + jsdom + Vue Test Utils hiện có; backend .NET 10 giữ nguyên.

**Spec:** [Bản APPROVED 2026-11-23](../specs/2026-11-23-issue-26-sprint1-hybrid-routing-design.md), đặc biệt mục 4, 5, 6, 8, 9 (chiến lược hai Phase). Các ghi chú lịch sử còn nói “chưa chốt actor” không ghi đè quyết định C-01 đã duyệt. Plan này chỉ thực hiện **Phase 1**.

## Global Constraints

- Dùng duy nhất `IS_MOCK_ENABLED = import.meta.env.VITE_USE_MOCK_API !== "false"`; giữ `VITE_API_BASE_URL` có sẵn, không thêm env flag.
- 15 operations: E01/E02/E03/E04/E08/E10a/E11/E12/E15/E17/E18/E19/E23/E24/E26. E01 gọi từ auth store; adapter xử lý 14 operations còn lại và các alias UI. E14 ngoài Phase 1.
- TDD bắt buộc: thêm test vào `frontend/tests/unit/sprint1HybridRouting.spec.js` → chạy RED vì hành vi thiếu → code nhỏ nhất → GREEN → refactor. Không dùng import missing hoặc fixture lỗi làm bằng chứng RED.
- Không sửa backend, migration, dependency, template/form hoặc CI. Ngoại lệ script duy nhất ngoài năm core files: `BookingNotifications.vue` để thực hiện I-05 đã duyệt; không sửa template.
- Request/response tuân thủ `docs/design/api-contract.md` mục 1–5; tham chiếu nghiệp vụ D03/Q07, D04/Q01, D09, D11/Q07, D12, D13, D15 và amendment tách E26/E19.
- Live errors giữ envelope backend; không fallback mock khi HTTP/network thất bại, không tự retry mutation.
- Sinh requestId khi thiếu bằng `payload.requestId = payload.requestId || crypto.randomUUID()`; giữ key đã cấp trên cùng payload qua retry. Caller tạo object mới không có key sẽ là command mới; không hứa exactly-once cho trường hợp đó.
- C-02 thiếu slot thật trả `MISSING_SLOT_MAPPING`, không dùng maCa/ID hai ca cố định như maKhungGio. M-02 page size 100 không chứng minh đã tải toàn bộ >100 đơn; giữ metadata.
- Bảo toàn tất cả thay đổi/untracked hiện có; không stage cả thư mục bằng `git add .`, không sửa bản spec đã được người dùng chỉnh.

## Review Focus

1. Live employee ID trùng demo Admin ID hoặc đổi role qua reload: identity/quyen live phải không bị ghi đè bởi seed session (Task 2, 6).
2. Fallback action ngoài allowlist nhận bookingId trùng ID seed: chặn trước mock nếu nguồn booking không phân biệt được (Task 6).
3. FormData bị JSON Content-Type và error envelope 400 bị nhầm ModelState: kiểm tra interceptor thật, không chỉ spy Axios post (Task 1).
4. Retry sau timeout phát requestId mới hoặc thiếu slot ở service con: giữ key và slot ID đúng, không gửi HTTP khi mapping thiếu (Task 4).
5. Workspace trả >100 records, đọc phát update loop hoặc listener còn chạy sau unmount: giữ pagination metadata, event chỉ cho mutation và debounce cleanup (Task 3, 6, 7).

## File structure và interfaces chung

| File | Trách nhiệm |
|---|---|
| `frontend/tests/unit/sprint1HybridRouting.spec.js` (create) | Các nhóm transport/session/permissions/routing/DTO/fallback/events và mount listener bằng jsdom; không cần test dependency mới |
| `frontend/src/services/api/httpClient.js` | Bearer, multipart boundary, unwrap AxiosResponse, bảo toàn error envelope |
| `frontend/src/stores/auth.js` | Normalize Session + aliases; persist/init/logout live và demo session |
| `frontend/src/utils/permissions.js` | Permission live trực tiếp + mapping capability UI; giữ policy mock seed |
| `frontend/src/services/api/sprint1Adapter.js` (create) | Allowlist metadata, request DTOs/FormData, response projection, validation |
| `frontend/src/services/api/operationClient.js` | Dispatch live/mock, guard dữ liệu, event/storage signal |
| `frontend/src/components/shared/BookingNotifications.vue` | Debounce listener 300 ms và cleanup timer trong script |

Chốt interface adapter: export `supportsSprint1Action(action): boolean`, `isSprint1Mutation(action): boolean`, `callSprint1Operation(action, payload = {}): Promise<ApiEnvelope>`. `callOperation` và `service` giữ signature hiện tại. ApiEnvelope là `{ success, data, message, ...metadata }`, không AxiosResponse, không bọc hai lần. Adapter dùng httpClient import hiện có, không tạo instance/cache phụ.

Các helper `normalizeLiveUser(rawUser)`, `syncDemoSession(user)` để nội bộ auth.js; `toBookingDto`, `toCheckinDto`, `projectBooking` là helper nội bộ adapter, không dựng public abstraction mới. Local validation reject `{ success:false, data:null, message, errorCode, errors:[{field,message}] }`, không gọi HTTP/mock và không tạo traceId giả.

## Task 0: Chuẩn bị nhánh và baseline (không sửa code)

**Files:** đọc AGENTS.md, `.codex/instructions.md`, `.github/CONTRIBUTING.md`, spec approved; không sửa tài liệu người dùng.

- [ ] Kiểm tra `git status --short`, `git branch --show-current`, HEAD. Nếu có CLAUDE.md mới thì đọc; nếu vẫn không tồn tại ghi rõ, không tự tạo.
- [ ] Fetch `origin`, tạo `feat/issue-26-frontend-sprint1-hybrid-wiring` từ develop mới nhất theo GitHub workflow; dùng worktree khi cần giữ working tree hiện có. Không checkout/rebase đè các file plan/spec đang sửa; không tự stash thay người dùng. Nếu remote mới thay canonical contract, đọc phần liên quan trước khi code.
- [ ] Chạy `npm --prefix frontend test -- --run` và `npm --prefix frontend run build`, ghi số test thực tế. 56 tests là baseline đã từng quan sát, không hardcode số cuối cùng. Không thêm package nếu node_modules đã sẵn.
- [ ] Đưa Issue #26 In Progress khi công cụ GitHub sẵn; chưa truy cập được thì ghi phần chưa thực hiện, không tuyên bố đã đổi board.

## Task 1: Transport đúng envelope và multipart (M-01)

**Files:** modify httpClient.js; create test file nêu trên. **Contract:** mọi operation, nhất là E01/E04/E15/E17/E18. **Rules:** D09/D12, errors mục 1 contract.

**Interfaces:** default httpClient tiếp tục trả ApiEnvelope; named export IS_MOCK_ENABLED giữ nguyên.

- [ ] **RED tests:** nhóm `transport`: Axios custom adapter thành công trả envelope một lần, JSON giữ JSON, FormData không bị JSON Content-Type, Bearer lấy đúng storage và tôn trọng Authorization explicit. Dùng AxiosError giả lập HTTP để response interceptor thật chạy; custom adapter không tự reject chỉ vì status 400. Assertion mẫu:

```js
expect(result).toEqual(backendEnvelope);
expect(result.data.data).toBeUndefined();
expect(rejected).toMatchObject({ errorCode: 'VALIDATION_FAILED', traceId: 'test-trace', errors: fieldErrors });
```

- [ ] Chạy `npm --prefix frontend test -- --run tests/unit/sprint1HybridRouting.spec.js -t transport`; xác nhận RED do metadata bị mất hoặc FormData header sai.
- [ ] **GREEN:** trong request interceptor xóa JSON Content-Type khi data là FormData (AxiosHeaders API), để browser tạo boundary. Error handler ưu tiên ApiResponse chuẩn, giữ backend message kể cả 401/403; ModelState dictionary xử lý riêng; raw/network errors có fallback rõ. 401 giữ cleanup/event và xóa demo session để phiên cũ không tiếp tục dùng sau token mất hiệu lực.
- [ ] Test thêm 400/401/403/404/409/413/415/429/503/network; errors array không bị flatten mất `{field,message}`. 401 unauthorized event đúng một lần, không log secret.
- [ ] Chạy lại nhóm transport GREEN, commit chỉ httpClient.js + test file: `fix(frontend): preserve API errors and multipart transport`.

## Task 2: Session live, demo context và permissions (C-01/C-04)

**Files:** auth.js, permissions.js, test file. **Contract:** E01 Session/Employee; FR01 D03/Q07. **Depends:** Task 1.

**Interfaces:** auth.login/initAuth/logout/can giữ API; normalizeLiveUser bổ sung contract fields và aliases. Demo ID chỉ nằm `sessionStorage.petspa_mock_session`, không bao giờ thay id/token/user live.

- [ ] **RED tests:** nhóm `session|permissions`: Session canonical và legacy, live STAFF có quyền BOOKING_APPROVE nhưng không workGroup, inactive=false, backend STAFF có ID=1 nhưng demo actor phải 2. Assertion:

```js
expect(auth.user).toMatchObject({ maNhanVien: 1, id: 1, vaiTro: 'STAFF', isActive: true });
expect(JSON.parse(sessionStorage.getItem('petspa_mock_session'))).toEqual({ id: 2 });
expect(auth.can('BOOKING_MANAGE')).toBe(true); // quyen: ['BOOKING_APPROVE']
expect(auth.can('CARE_UPDATE')).toBe(false);
```

- [ ] Chạy focused test với `-t 'session|permissions'`, xác nhận RED từ mapping/permissions hiện tại.
- [ ] **GREEN auth:** dùng accessToken??token, nhanVien??user; normalize cả contract fields/aliases, canonical `maNhanVien` từ legacy id nếu cần, quyen từ permissions hoặc []; giữ false bằng `??`. Chỉ role ADMIN/STAFF hợp lệ active mới tạo demo context Admin→1/Staff→2. initAuth live đọc identity live rồi tái tạo demo context theo role hiện tại, không gọi currentUser mock; lỗi/unsupported/inactive không giữ demo session. logout/login thất bại xóa cả ba storage keys.
- [ ] **GREEN permissions:** raw backend permission capability kiểm tra trực tiếp quyen. Mapping UI: BOOKING_READ→BOOKING_APPROVE, BOOKING_MANAGE→BOOKING_APPROVE, CARE_UPDATE→CARE_UPDATE, SCHEDULE_SELF→SCHEDULE_REGISTER, FINANCE_MANAGE→INVOICE_CREATE hoặc INVOICE_COLLECT, STAFF_MANAGE→SCHEDULE_APPROVE hoặc PERMISSION_MANAGE, SYSTEM_MANAGE→SYSTEM_CONFIG. Đây là visibility mapping, server vẫn chặn từng endpoint. ADMIN active giữ quyền UI toàn bộ theo D03. Unknown role/inactive deny; STAFF live thiếu quyền deny, không fallback BASE_STAFF.
- [ ] Giữ canAccess/requireAccess của mock repository hoạt động: phân biệt Employee đã normalize (`maNhanVien`) với seed actors legacy (`id`, workGroup, không maNhanVien); seed actors vẫn dùng grants cũ dù env live. Không dùng cờ global env một mình để ép demo actor sang quyen rỗng. Test seed Staff 2 vẫn BOOKING_MANAGE, không CARE_UPDATE; live STAFF thiếu quyen vẫn deny.
- [ ] Test Admin→Staff qua initAuth, storage hỏng, false isActive và explicit permissions; focused GREEN rồi commit: `fix(frontend): map live sessions and permission capabilities`.

## Task 3: Allowlist read operations và projection (E02/E03/E08/E10a/E24)

**Files:** create sprint1Adapter.js; modify operationClient.js; test file. **Rules:** D04/Q01, D09, D03/Q07. **Depends:** Tasks 1–2.

**Interfaces:** ba exports adapter ở trên. operationClient dispatch allowlist trước bước đọc mock session; live private dùng Bearer, không yêu cầu demo session tồn tại. Mutation metadata là tường minh, POST lookup/availability vẫn read.

- [ ] **RED tests:** nhóm `reads`: stubEnv trước resetModules/dynamic import; test qua callOperation hiện có để RED vì G6, không import module chưa tạo. Mock httpClient methods nhưng giữ adapter thật. Canonical/alias cùng request URL tương đối base `/api`:

| Action | Request/assert |
|---|---|
| getCatalog/catalog | GET `/catalog`; chuẩn Catalog còn nguyên; alias services/combos/rooms, id/isActive, bangGia size không đổi |
| checkAvailability | POST `/availability`, đúng AvailabilityQuery; không bịa slot/nhu cầu |
| lookupTracking/lookupBooking | POST `/tracking/lookup`, pair chuẩn; `090-123 4567`→`0901234567`, loại khác không hợp lệ reject |
| listBookings/getWorkspace | GET `/internal/bookings`, params chỉ BookingQuery; workspace mặc định kichThuocTrang=100; canonical PageBooking còn nguyên |
| getInvoice | GET `/internal/invoices/902` với invoiceId=902; không dùng bookingId=101 thay thế |

- [ ] Chạy focused `-t reads`, xác nhận RED G6, rồi tạo adapter và branch live tối thiểu trong operationClient. Unknown action tiếp tục path mock cũ cho đến Task 6.
- [ ] Projection alias không mutate DTO: maDon→id, lienHe→khachHang, thuCung→chiTietDonList, pet trangThai→trangThaiChamSoc, nhatKy/anhUrl→nhatKyChamSoc/photo, Invoice id/status/amount alias lấy từ invoice thật. Không chế assignment ID/cân khai báo. Dùng Asia/Ho_Chi_Minh cho ngày; test instant `2026-11-22T18:00:00Z`→`2026-11-23`.
- [ ] getWorkspace trả `{ bookings, users:[], requests:[], trang, kichThuocTrang, tongSo }`, không claim full khi tongSo>100. Catalog aliases `shifts` dùng hai nhãn Sáng/Chiều từ mockCaLamViec có sẵn chỉ để hiển thị; availability=[] khi không có slot thật. Nếu caller có đầy đủ AvailabilityQuery, catalog có thể gắn availability từ E03; date-only không gọi E03 với ID đoán và không cấp sức chứa giả.
- [ ] Focused GREEN; assertion `expect(envelope.data.tongSo).toBe(125)` khi items=100; read không phát mutation event. Commit: `feat(frontend): route Sprint 1 reads through live adapter`.

## Task 4: Booking và duyệt/hủy (E04/E11/E12, C-02/C-03/M-03)

**Files:** adapter + test file. **Rules:** D04/Q01, D09/D13/D15. **Depends:** Task 3.

**Interfaces:** command helpers nội bộ tạo requestId fallback trước send, chỉ whitelist contract fields. Slot thật lấy từ payload.maKhungGio; UI caller có thể cung cấp trực tiếp maKhungGio cùng fields cũ. Không tạo registry mapping slot giả.

- [ ] **RED tests:** nhóm `commands-booking`: createBooking canonical/UI với maKhungGio=701 khác shiftId=1; hai pets dịch vụ đều mang slot=701, file vaccine đúng key. Chỉ date+shiftId phải reject MISSING_SLOT_MAPPING, không gọi post. Confirm/cancel tests qua callOperation cần fail vì adapter chưa support, không vì mock fixture.
- [ ] Run focused RED rồi implement BookingCreate projection/FormData field payload; UI customer/pets maps theo contract, File tách khỏi JSON; giữ vaccinationFileKey và File object. Service selection giữ slot explicit hợp lệ nếu caller chỉ định, chỉ kế thừa slot cha khi thiếu; XOR service/combo. Không gửi actor/state/estimated price.
- [ ] E11 confirmBooking/reviewBooking CONFIRMED: bookingId??id, body `{requestId}`. E12 cancelBooking/reviewBooking CANCELLED: body `{requestId, loaiHuy:...??'STAFF_REJECTED', lyDo:...??reason}`; thiếu lý do/decision khác reject. E04 luôn `/bookings` ở Phase 1 kể cả alias internal UI, không tự gọi E05.
- [ ] Assertion retry trên cùng payload: spy randomUUID một lần, hai lần gửi giữ cùng key; key caller cung cấp không đổi. Không retry HTTP tự động, login/tracking lookup không bị thêm requestId.
- [ ] Focused GREEN và regression reads, commit: `feat(frontend): adapt booking and approval commands`.

## Task 5: Care, hóa đơn và thu tiền adapters (E15/E17/E18/E19/E23/E26)

**Files:** adapter + test file. **Rules:** D11/Q07, D12/D15, amendment tách E26/E19. **Depends:** Task 4.

**Interfaces:** pet path dùng maChiTietDon??detailId??id cho canonical; khi có detailId thì id là bookingId không được dùng vào pet path. Invoice commands: createInvoice bookingId??id; checkoutInvoice invoiceId??maHoaDon??id canonical, nhưng confirmPayment chỉ invoiceId/maHoaDon hoặc hoaDon.maHoaDon đã caller truyền, không dùng id booking.

- [ ] **RED tests:** nhóm `commands-care|commands-billing`, đủ aliases và canonical. Chạy RED trước khi thêm handlers. Data hợp lệ lấy schema mục 3/4 contract, fake files không chứa dữ liệu thật.
- [ ] Implement E15 multipart payload + anhTiepNhan. Giữ tinhTrangLong dạng code array; string code→array khi rõ ràng, không biến nhãn “Bình thường” thành mã đoán. Phase 1 không gọi E62/dựng danh mục mới; dùng code caller cung cấp, backend xác thực FUR_CONDITIONS. Thiếu ảnh/sức khỏe/maNhanVien/quyetDinhGia/xacNhanKhach=true reject client-side. Không default quan sát/đồng thuận.
- [ ] E17 multipart CareAction + photo→anhThanhPham; action rõ ràng giữ nguyên, alias next=IN_PROGRESS cần currentState WAITING→START hoặc PAUSED→RESUME; thiếu state reject. PAUSE cần lý do, READY cần ảnh hoặc maNgoaiLeAnh explicit. E18 multipart CareLogCreate + anh tùy chọn; phải có nội dung hoặc ảnh, requestId riêng.
- [ ] E19 JSON Handover bắt buộc xacNhanDaNhan=true. E23 JSON Command chỉ tạo hóa đơn; E26 JSON Collect đủ invoiceId/phuongThuc/thực thu/trả lại, không tự tính tiền đã thực thu từ conPhaiTra. `confirmPayment({ id:101, method:'CASH' })` reject, không gọi E23 hoặc E26. Các guard transition/amount/assignment thuộc server; không giả state sau success.
- [ ] Assert FormData entries + JSON không có File; READY photo giữ identity; checkout trả Invoice PAID không sửa pet thành DONE. Test missing fields reject trước network, 409 giữ nguyên/no mock. Focused GREEN, chạy toàn bộ test file rồi commit: `feat(frontend): adapt care invoice checkout and handover contracts`.

## Task 6: Demo fallback và event isolation (C-01/I-05)

**Files:** operationClient.js + test file. **Contract:** allowlist nêu trên; mock actions theo mockOperations hiện có. **Rules:** D03/Q07, D09, tách tiền/dữ liệu live. **Depends:** Tasks 2–5.

**Interfaces:** actor chỉ đọc seed demo session; không gán auth.user bằng kết quả mock. Các live operations không cần mock actor. Không thêm entity cache/provenance registry.

- [ ] **RED tests:** nhóm `hybrid-policy`: mock mode default/true vẫn gọi mock y như cũ; live unknown action không gọi HTTP; getSettings/getSchedule/getFinance dùng seed actor 1/2; bất kỳ live failure không gọi mock. Assert login live Staff id=1 đi actor=2 cho getSchedule và auth identity vẫn STAFF.
- [ ] Chạy focused RED; implement allowlist dispatch hoàn chỉnh, mock fallback private yêu cầu demo session hợp lệ, lỗi có kiểm soát nếu thiếu/hỏng. Unknown action được mock repository báo unsupported, không biến thành success.
- [ ] **Guard nguồn dữ liệu, lựa chọn triển khai bảo thủ:** khi live mode, chặn fallback `updateBooking`, `addSurcharge`, `customerRequest`, `reviewRequest`, `resendTracking`, `updateAccount` trước mock vì các caller hiện tại có thể mang booking/pair/account live và không có provenance tin cậy. Dùng errorCode `HYBRID_ENTITY_UNSUPPORTED`; không tra seed theo ID để đoán nguồn. Những action demo độc lập (settings/schedule/catalog admin/seed staff) vẫn dùng fallback. Test bookingId/id trùng seed và public tracking pair đều bị chặn; đây là giới hạn Phase 1, không phát minh form metadata mới.
- [ ] Mutations internal live success phát event + storage key petspa_workspace_update đúng một lần; POST read, public call, error, guard failure không phát. Mock mode giữ regression, fallback settings mutation có thể phát theo hành vi mock hiện có, nhưng không thay dữ liệu live.
- [ ] Assertion lỗi message/code/traceId không đổi sau operationClient; không catch broad. Focused GREEN và toàn test file, commit: `feat(frontend): isolate hybrid demo fallback and workspace events`.

## Task 7: Debounce listener và đóng verification (I-05)

**Files:** BookingNotifications.vue script + cùng test file. **Contract:** E10a getWorkspace/BOOKING_APPROVE. **Rules:** D03/Q07; không đổi UI form, không thêm lodash.

**Interfaces:** local trailing debouncedRefresh 300 ms dùng timeout; handler workspace event và storage key dùng chung scheduler. Polling/visibility/manual refresh giữ guard hiện có; cleanup unregister đúng function và clear timeout.

- [ ] **RED test nhóm `notifications`:** mount bằng Vue Test Utils, stub auth/router/bookingService, fake timers; đợi initial load và clear spy. Phát ba workspace events nhanh và một storage event: advance 299 ms chưa gọi, 300 ms gọi một lần; unmount trước deadline không gọi; read response không gây chu kỳ event. Chạy focused RED trước khi sửa script.
- [ ] Implement scheduler/debounce local, không sửa template. Khi scheduler chạy refresh đã loading thì giữ guard, không khởi tạo interval/listener mới.
- [ ] Focused GREEN, rồi `npm --prefix frontend test -- --run tests/unit/sprint1HybridRouting.spec.js`, `npm --prefix frontend test -- --run`, `npm --prefix frontend run build`. Ghi kết quả thật; không cố định tổng test=56.
- [ ] Chạy `git diff --check`, tự đọc diff giới hạn sáu production files + test; commit `fix(frontend): debounce hybrid workspace notifications`.
- [ ] Trước PR chạy `dotnet build backend/PetSpa.sln -m:1`, `dotnet test backend/PetSpa.sln -m:1` theo CONTRIBUTING; chỉ dùng PostgreSQL test được cấp. Nếu thiếu cấu hình, ghi chính xác command/blocker, không tick pass backend. Không migrate/seed để làm check tự xanh.
- [ ] Nếu có môi trường test được cấp: smoke live E01/E02/E03 với slot ID được xác minh và một lookup bằng dữ liệu test; commands chỉ dùng record test được phép. Ghi riêng smoke đã chạy/chưa chạy. Không yêu cầu full UI check-in/thanh toán/bàn giao để gọi Phase 1 transport hoàn tất.
- [ ] Fetch/rebase feature branch lên develop khi working tree an toàn, chạy lại checks nếu code thay đổi; mở PR base develop, `Closes #26`, traceability C-01…C-05/I-05/M-01…M-04 và evidence RED/GREEN. Lead PR cần người khác Approve, CI xanh rồi người có quyền merge; không tự approve/merge. Không đẩy file untracked ngoài scope.

## Phase 2 handoff và giới hạn nghiệm thu

- #26 Phase 1 hoàn tất khi đủ 15 operations/adapters và tests, không có G6, demo policy đúng, permissions/envelope/multipart/event đúng. Những input UI chưa có phải lỗi rõ ràng, không báo thành công giả.
- Follow-up UI Form Integration vẫn trong Sprint 2: thêm ảnh/sức khỏe/quyết định giá/xác nhận check-in (C-05), invoiceId/thực thu/trả thừa (I-02), nút bàn giao E19 (I-01), E14 sau #18 (I-03), source slot động/verified mapping và pagination UI. Chưa có số Issue follow-up thì ghi chưa gán, không tự bịa số.
- Điều chỉnh plan chính/từng Dev trong một bước docs riêng được người dùng yêu cầu hoặc thuộc PR handoff: các plan hiện tại còn mô tả UI/E14 như điều kiện kết thúc #26 cũ; liên kết bản spec approved và plan Phase 1 này làm rõ scope mới, không tự sửa spec người dùng. Không để #27 HTTP E2E bị mô tả là chủ sở hữu frontend.
- M-02 chỉ lấy trang 1 tối đa 100 nếu caller không truyền trang; giữ trang/kichThuocTrang/tongSo. Không tuyên bố đã giải quyết trường hợp >100 đơn hoặc UI end-to-end.

## Self-review của plan

Coverage: C-01 Tasks 2/6; C-02 Tasks 3/4; C-03 Tasks 4/5; C-04 Task 2; C-05 Task 5 + Phase 2; I-01/I-02 Task 5 contract + Phase 2 UI; I-03 Phase 2; I-05 Tasks 6/7; M-01 Task 1; M-02 Task 3; M-03 Task 4; M-04 Task 3. E01 Task 2; toàn bộ 14 operations khác Tasks 3–5. Mỗi thay đổi code có RED→GREEN trước commit; không triển khai hoặc chạy tests mới trong giai đoạn lập plan.
