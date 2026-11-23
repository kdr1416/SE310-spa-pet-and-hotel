# Issue #26 Phase 2 — Spa UI Integration Implementation Plan

> **Execution:** Use superpowers:executing-plans, theo lựa chọn mới nhất của người dùng. Tự triển khai tuần tự trong chat, TDD và self-review mỗi task; không implementer/reviewer subagent từng task. Một final whole-branch review theo executing-plans. Người dùng đã yêu cầu cả plan và triển khai; không dừng hỏi giữa các task. Không self-merge.

**Goal:** Khép luồng Spa live từ chọn slot tới trả pet, bổ sung E64/E62/E38 và giữ demo isolation.

**Architecture:** Giữ pragmatic .NET Controller→service/DbContext, read endpoints không ghi DB, assignment guard trước paging/media signing. Vue dùng Phase 1 httpClient và allowlist mới phase2Adapter; store query có metadata/stale-response guard. Các live form nhỏ dùng style/controls hiện có, mock UI tiếp tục chạy.

**Tech Stack:** .NET10/EF/Npgsql/xUnit/WebApplicationFactory; Vue3/Pinia/Axios/Vitest/jsdom/TestUtils. Không dependency/schema/env flag mới.

**Spec:** [Approved Phase 2](../specs/2026-11-23-issue-26-phase2-spa-ui-design.md). Nhánh `feat/issue-26-phase2-spa-ui` ban đầu được dựng từ Phase 1 + develop `cb25111` (#18/#19). PR #38 đã squash merge vào develop tại `141312a`; Phase 2 đã được rebase riêng lên base này trước khi push theo yêu cầu người dùng. Code backend/frontend giữ nguyên so với bản đã kiểm chứng trước rebase; PR chỉ chứa thay đổi Phase 2.

**Integration gate:** Phase 1 từ PR #38 đã có trên develop; Phase 2 cần PR review/CI và merge riêng. Người dùng đã yêu cầu push và tạo PR Phase 2, chưa yêu cầu merge hoặc deploy. Browser smoke #27 chỉ nghiệm thu bản test chứa đủ hai phase và môi trường #25 sẵn sàng. Có thể dùng preview của nhánh chứa đủ code để test trước merge.

## Global Constraints

- Chỉ Spa thường live; Hotel/Combo/E05/E16UI/E61UI/phụ phí/OTP ngoài scope. Không mock mutation trên entity live, không suy ID/assignment/đồng thuận.
- Duy nhất IS_MOCK_ENABLED hiện có; không thay auth protocol/migration/deployment. API amendment phải vào canonical docs trước backend code (user đã duyệt).
- E64 GET `/api/booking-options?ngay=...`: BookingOptionsQuery/BookingOptions/BookingShiftOption đúng spec3.1; ngày VN hôm nay..+90 inclusive; active shifts leftjoin slots; read-only, không counters/employee/private data.
- E62 CHECKIN_PERFORM đọc active FUR_CONDITIONS, malformed/missing503. E38 CARE_UPDATE lọc STAFF assignment server, PageBookingPet, active4states + bookingIN_PROGRESS, date/paging/sort đúng spec3.3.
- E14 dùng ca thao tác hiện tại từ E64 ngàyVN, không ca hẹn giả. E17/E18 mutations tái kiểm tra guardserver.
- requestId gắn logicalcommand caller; timeout/503 chưa rõ kết quả giữ key/data/File, input đổi thì reload/commandmới; no autoretry. E26≠E19; invoiceId≠bookingId≠petDetailId.
- Strict TDD mỗi slice: viết test → RED intendedfail → minimal GREEN → regression → review/commit. Tests mới theo feature, không tăng file Phase1 thêm hàng nghìn dòng.
- Giữ diff focus, style hiện có; labels/error-summary/status/busy/keyboard, responsive grids. Không tự tạo defaults consent/health/actualmoney; không render missingprice thành0.
- PostgreSQL test cluster mới loopback-only dùng binaries đã cài, data trong ignored plan scratch, không đụng Neon/database hiện có. Fixture chỉ migrate/drop database nó tạo; không schema migration mới.

## Review Focus

1. Ca ở hai ngày có ID khác, đóng/nghỉ/nullslot và ranh giới VNmidnight (Task1/4).
2. Staff CARE_UPDATE không có BOOKING_APPROVE vẫn vào careview, không leak task/ảnh người khác (Task2/3/5).
3. Response search cũ/metadata/requestId bị mất khi view reload hay người dùng đổi input (Tasks3–7).
4. Booking IN_PROGRESS với petREADY, invoicePAID và petDONE là các state khác, không parentREADY/auto-handover (Task6/7).
5. Mock mode không bị live-read side effect, read no notificationloop, role/context retained, giá thiếu không0 (Tasks3–8).

## Task 0: Contract amendment, isolated baseline và test database

**Files:** spec approved; docs/design/api-contract.md, requirements-traceability.md; docs/plans/sprint-1-core-plan.md + sprint-2/sprint-2-plan.md; docs/requirements/scope-and-decisions.md amendment chú thích; không rewrite lịch sửM2.

- [x] Ghi nhận user approval; registry thêm E64 + DTOdictionary/guards; PageBookingPet giải thích E38states/dates/sort. E38/E62 đưaSprint2: Sprint1=15, Sprint2=32(29+E38+E62+E64), Sprint3=24(24-E38+E63;E62/E63 trước đây bị bỏ sót), tổng71; xác minh code list unique, không đếm guard table.
- [x] Nhánh mới từ c990c45 + merge origin/develop, không merge develop/PR38. Tạo plan-scoped ledger/preflight/briefs. Commit canonicaldocs+plan trước code.
- [x] Init cluster riêng `.superpowers/sdd/<plan>/postgres-data` bằng PostgreSQL binaries hiện có; trust chỉ localhost/127.0.0.1 trên port55439 nếu trống, không đổi service cũ. pg_ctl log scratch; env PETSPA_TEST_POSTGRES chỉ command process, không credential thực.
- [x] Chạy frontendtests/build và backendbuild/fulltests trên baseline kết hợp; lưu pass/fail thật. DB fixture tạo database tên GUID, không mutate cluster khác.

## Task 1: E64 booking options và E62 fur conditions

**Files:** create Core/DTOs/Booking/BookingOptions.cs; Api/Services/Booking/BookingOptionsService.cs; Api/Controllers/Booking/BookingOptionsController.cs; Api/Services/Care/FurConditionsService.cs; Api/Controllers/Care/FurConditionsController.cs; Program.cs DI; tests Booking/BookingOptionsEndpointTests.cs và Care/FurConditionsEndpointTests.cs. Root các path backend/src/PetSpa.* và backend/tests/PetSpa.Tests.

**Interfaces:** service `GetAsync(DateOnly ngay, CancellationToken)` trả ApiResponse<BookingOptions>; fur `GetAsync(int actorId,CancellationToken)` ApiResponse<IReadOnlyList<FurConditionItem>>. DTO fields/types spec3.1, FurConditionItem reuse đã có. TimeProvider hiện có.

- [x] RED WebApplicationFactory GET newroutes: valid differentday IDs, missing/closedslot, inactiveca, holiday, sort, queryinvalid/past/+91, todayVN khácUTC; GET không thay counters/rowcounts và no privateJSON. E62 noJWT401/revoked403, activeonly, missing/malformed503, no writes. Assert expected200/400/403/503 against current404 before code.
- [x] Run `dotnet test backend/PetSpa.sln -m:1 --filter "FullyQualifiedName~BookingOptionsEndpointTests|FullyQualifiedName~FurConditionsEndpointTests"`; RED phải endpointbehavior khôngfixtureconfig.
- [x] Implement focused services/controllers/DI. Query AsNoTracking, activeca+leftjoin; moNhanKhach effective holidayflag, không tính sức chứa là bảo đảm. Permission từ DB, generic503 no rawexceptions.
- [x] FocusedGREEN + backendfulltests once; commit `feat(api): expose booking options and active fur conditions`.

## Task 2: E38 authorized paged care tasks

**Files:** create Core/DTOs/Care/PageBookingPet.cs; Api/Services/Care/CareTasksService.cs; Api/Controllers/Care/CareTasksController.cs; Program.cs DI; tests Care/CareTasksEndpointTests.cs. Reuse CareProjection/IFileStorageService, no edits #18mutations.

**Interfaces:** `ListAsync(BookingQuery query,int actorId,CancellationToken): ApiResponse<PageBookingPet>`, PageBookingPet Items/Trang/KichThuocTrang/TongSo. Endpoint maps statuses400/401/403/503; same envelope conventions.

- [x] RED tests STAFFA≠STAFFB, onlycurrentassignment, active4states+bookingIN_PROGRESS, ADMINscope, revokedpermission403, noJWT401, statesbad400, dateinvalid/over90, page1/2/count/sort, media signing onlypageauthorized and nopathleak. Include IDs differentfromemployee and samepetnames to prove no name matching.
- [x] Run focused `--filter FullyQualifiedName~CareTasksEndpointTests` RED404 then implement serverwhere beforecount/page, AsNoTracking/projection/sign onlyauthorizedpage.
- [x] GREEN + backendfulltests; commit `feat(care): list authorized paged care tasks`.

## Task 3: Phase2 adapter/services/store/query/permissions

**Files:** create frontend/src/services/api/phase2Adapter.js; extend operationClient.js (readrouting only), bookingService.js/catalogService.js; stores/workspace.js; router/index.js/AppLayout.vue only for no BOOKING_APPROVE careStaff access; services/mock/mockOperations.js newreadcases. Tests unit/phase2Routing.spec.js, unit/phase2Workspace.spec.js, components/Phase2Navigation.spec.js.

**Interfaces:** `supportsPhase2Action`, `callPhase2Operation`; actions getBookingOptions/getAssignmentCandidates/getFurConditions/listCareTasks. Shared httpClient unchanged, ApiEnvelope intact. workspace state bookingsQuery/bookingsPage and careQuery/carePage; `loadBookings(query={})`, `loadCareTasks(query={})` return envelope/metadata, stale sequences separate. E10a explicit valid BookingState (defaultsPENDING), not unsupported allstates. E38 defaultsnostate and uses PetState.

- [x] RED request method/query/URL for4reads, no event/fallbackonerror, E14requiresrealmaCa; mocknewreads use demo state, notHTTP. Store out-of-order response ignored, filter resetpage1, total125≠items.length100, care never invokesE10a. Router care-onlyStaff home `/staff/care-tasks`, inaccessible links hidden; no selfredirectloop to overviewrequiringBOOKING_READ.
- [x] Run `npm --prefix frontend test -- --run tests/unit/phase2Routing.spec.js tests/unit/phase2Workspace.spec.js tests/components/Phase2Navigation.spec.js` RED then implement. Preserve Phase1 test227 and existingmockfixtures, no broad env flags.
- [x] GREEN/fullfrontend; commit `feat(frontend): route Phase 2 reads and retain workspace pagination`.

## Task 4: Public Spa booking with real slots

**Files:** BookingForm.vue (conditional live delegation), new components/booking/LiveSpaBookingForm.vue, CustomerBookingView.vue copy/states if necessary, bookingRules.js onlylivecompatible validation; tests components/LiveSpaBookingForm.spec.js. Mocktemplate unchanged except wrapper/guardneeded.

**Interfaces:** live component emit created(BookingUIprojection), usescatalog/getBookingOptions/checkAvailability/createCustomerBooking; date/selectedMaCa/maKhungGio distinct. Requestsequence guard; tomorrowdate neverreuseyesterdaysslot. Nohotel/comboinputlive.

- [x] RED mount entercontact/pet/service/date; options maCa1/slot701 and day2slot899; E03demand matches everyservice; closed/null/pastca disabled; responseold ignored; needsavailability but newstate invalidatesit. Submit sends canonical slot+requestId+files correct. 409 preservesform/reloads; SMTPfailed success keeps tracking; timeoutretry samekey+File; priceempty “Chưa có giá”.
- [x] Run focused RED; implement using current `.field/.label/.btn` style and labelederrors/status/keyboard; old onMounted/watchprice logic must not run for delegatedlive form. Watch pets/date/service invalidateavailability; E04 stillfinalguard, nofalsecapacitylabels.
- [x] GREEN/fullfrontend/build; commit `feat(booking): submit live Spa bookings with verified slots`.

## Task 5: Staff care tasks and logs

**Files:** StaffCareTasksView.vue conditionalliveview; new components/care/LiveSpaCareTasks.vue; tests components/LiveSpaCareTasks.spec.js. Optional composable `useSpaCommand` only if reusedTask6/7: define `runCommand(kind,targetId,fields,send)` stablekeyperlogicalpayload, clearonsuccess, retainsretry; no globalcache/autoretry. Prefer small localhelpers if reuse not concrete.

- [x] RED careStaff onlyquyenCARE_UPDATE receivesE38 page noBOOKING_APPROVE; neverfiltersnames/employeeids; WAITINGSTART/PAUSEDRESUME, PAUSErequiredreason, READYpositivephoto, logsseparatekey/permissionPHOTO_UPLOAD; 403/409reload, rowremovedclosesdialog, selectedsnapshotpreservedwhilebusy, pageold ignored.
- [x] FocusedRED then implement E38canonicalBookingPet view no fakeparentbooking/customer. E17/E18 service exports as needed, onlypetDetailId, explicitaction, signedmedia viaStoredPhoto. Multirowbusy/disabled and fielderrors; unsupportedHotel/Combo tasks blockedwithmessage notmutation.
- [x] GREEN/fullfrontend; commit `feat(care): operate live assigned Spa tasks and logs`.

## Task 6: Live check-in form and booking workspace pagination

**Files:** BookingWorkspace.vue query/status/pagecontrols/livecomponentselector; new components/booking/LiveSpaBookingDetail.vue CHECKIN+detailpart; BookingDetailDialog.vue conditionaldelegation ONLY if wrapper safer; tests components/LiveSpaCheckin.spec.js, components/LiveBookingWorkspace.spec.js. No2differentlivewrappers for samepath; pickfocused one and document.

**Interfaces:** LiveSpaBookingDetail props booking, emit changed; UIcheckin usesE64(todayVN)→currenttimeca→E14, E62 andE15. Invoicepart addedTask7. Existingbookingaliases intact; don'tread pet.input/vaccine or nhanVienId if absent, show servername/unavailable.

- [x] RED checkout metadata readca notoldappointment, overlapsaskchoose/noapprovedcandidateblock; formneedspositivephoto,health,weight,furcodes,employee,radiochoice+THUGONreason,explicitconsent,optionalpapercheckbox notautotrue. SendCheckin multipart targetpetID notbookingID; secondpetcheckinbookingIN_PROGRESS allowed, permissionCHECKIN_PERFORM independentBOOKING_APPROVE. Error400fieldmaps,403/409reload whilekeepinput.
- [x] E10a statusdropdownexplicit/PENDINGdefault; afterconfirm switchCONFIRMED, checkinIN_PROGRESS; dialogsnapshot notlostjustbecause filteredpage changes. page>100 totalhonest, calendar showsloadedrange; notifications count E10aPENDING.tongSo notlength (modify BookingNotifications.vue/test accordingly).
- [x] FocusedRED→minimalGREEN; old mockBookingDetail behavior unchanged. Disable unsupported livebuttons updateBooking/addSurcharge/Hoteloperations, no callingmock. Fullsuite/build; commit `feat(checkin): collect live Spa intake inputs and page bookings`.

## Task 7: Invoice checkout and explicit handover

**Files:** extend LiveSpaBookingDetail.vue; new small components/booking/LiveSpaInvoice.vue ifneeded; tests components/LiveSpaSettlement.spec.js. Keep phasesE23/E26/E19 separate; no backendmoneychanges.

- [x] RED IN_PROGRESSbooking with allSpaREADYpets showsE23 ifINVOICE_CREATE (notparentREADY); canonicaldongTien/QRfrombackend, qrnullmessage, no mockQR. E24reloadfailurepreservesnapshot. Collectpositiveinteger amountsactual entered, netpreview, bankreference. invoice902≠booking101≠pet301, wrongamount409preservesform/key, timeoutmanualretry samepayload, noE23autocall.
- [x] PAID keepspetREADY until explicit perpet E19checkbox+PET_HANDOVER; lastpetonlyreloadparentCOMPLETED, disabledunpaid, noprecheckedconsent, samekey onuncertainretry. Correct actionpermissions, fielderrors/focus, sensitiveinputsnotlogged.
- [x] FocusedRED→GREEN andfullsuite/build; commit `feat(billing): settle Spa invoices and hand over pets explicitly`.

## Task 8: Integration, finalreview and handoff

**Files:** tests integration/phase2SpaWorkflow.spec.js; backend/tests/PetSpa.Tests/Flows/Phase2SpaUiFlowTests.cs if backendflowcoveragegap; docs/plans/sprint-2 main/devhandoffs; no newtestframeworkdependency.

- [x] Testfeatureintegration RED expectedmissingintegrationbehavior beforefix if defectsfound, coversPublicE64→E03/E04→E11→E14/E62/E15→E38/E17/E18→E23/E24/E26→E19→E08; ordinarySpa2pets, errors/idempotency/photoownership/cross-tab/regression included. Backendflow actualHTTP/Postgres with testmedia/email doubles; don't call this realSupabaseSMTP smoke.
- [x] Run dotnetbuild/fulltest usingisolatedcluster, npmfullsuite/build, diffcheck, UI/browsercheck desktop+mobile if available. If localbrowser unavailable report actualblocker; use existing browserautomation only, no installingPlaywright/dependencies. APIHTTPintegration can still beverified.
- [x] Final wholebranch reviewer (mostcapable) getsactualdiff+ledger; oneconsolidatedfixwave then scopedrereview. Preserve earlierPhase1 ruling/limits, updatephase2acceptanceevidence and operationscountassert.
- [x] Stoptestserver with pg_ctl fast onceunused; do not recursively delete users/worktrees or resetDBs. Preservebranch/worktree and testsreports neededforhandoff. No auto merge, no push/PR until user instruction covers exact destination/payload; earlierPR38authorization doesnotimplymergingit.

## Self-review / coverage

P2-01 Task1/4; P2-02 Task4/8; P2-03 Task6; P2-04 Task1/6; P2-05 Task2/3/5; P2-06 Task5/8; P2-07 Task7/8; P2-08 Task7/8; P2-09 Task3/6; P2-10 Tasks3–8. Sharedfiles operationClient/services/store andliveDetail are sequentialowners, neverconcurrentwriters. Staffcare noBOOKING_APPROVE redirects addressedTask3. E10a backendPENDINGdefault forcesexplicitstatusfilter notfakeallquery. Supabase/SMTP realexternalvalidation remainsdistinctfromlocalfullHTTPtests.

## Execution result — 2026-11-23

Triển khai inline bằng executing-plans trên `feat/issue-26-phase2-spa-ui`; không implementer/reviewer từng task. Một final whole-branch review và scoped re-review; các lỗi còn lại từ re-review được tái hiện RED rồi sửa, không tuyên bố reviewer đã duyệt lại các sửa cuối.

- Backend: `dotnet test backend/PetSpa.sln -m:1 --no-build` — **425 passed, 0 failed**, PostgreSQL cô lập localhost:55439. Build đã chạy trong focused test trước đó; không schema/migration mới.
- Frontend: `npm --prefix frontend test -- --run` — **272 passed, 0 failed**; `npm --prefix frontend run build` — PASS. `git diff --check` — PASS.
- Registry: **71 rows, 71 unique operations**, không trùng mã.
- Luồng HTTP hai pet trong `backend/tests/PetSpa.Tests/Flows/SpaFlowTests.cs` dùng E64/E14/E62/E38 cùng E03/E04/E11/E15/E17/E18/E23/E24/E26/E19/E08. Media/email là test double. Frontend integration chạy component với adapter/httpClient thật và transport double.
- Rà soát đã sửa: cache theo principal/grants và đổi phiên cross-tab; retry giữ đúng lệnh/key; ca đúng ranh giới TimeOnly; E24/E26 race; invoice REVOKED; ảnh thay thế không hợp lệ; assignment conflict; transient DB 503; notification pagination/query lifecycle; snapshot sau bàn giao cuối. Test RED/GREEN ghi trong plan-scoped ledger.
- Baseline deviation: frontend baseline 227 PASS; full backend baseline bị gián đoạn do runner/DLL lock và sandbox loopback, không ghi nhận là PASS. Full regression sau Task1 là 413, sau Task2 là 422, cuối cùng 425. Backend commands sau chẩn đoán chạy tuần tự và được cấp quyền loopback đúng phạm vi.
- Browser: form public live/offline ở 1280px và 390px không tràn ngang; viewport đã reset. **Chưa chạy authenticated browser smoke hoặc Supabase/SMTP thật.** Theo quyết định người dùng ngày 2026-11-23, live acceptance này được bàn giao cho [#27 – Task 14, Step 3b](../../plans/sprint-2/sprint-2-plan.md), sau khi #25 – Task 12 cung cấp môi trường backend/Neon/Supabase/SMTP và tài khoản test sẵn sàng. Trạng thái #26 là implementation hoàn tất, live acceptance pending; không coi lượt browser offline đã nghiệm thu phần đăng nhập. Hotel/Combo/cọc/zero-due UI vẫn ngoài scope đã duyệt.
- Đã dừng PostgreSQL/Vite riêng của tác vụ; giữ báo cáo ignored và worktree. Không push, tạo PR mới, merge PR38 hoặc thay đổi checkout develop của người dùng.
