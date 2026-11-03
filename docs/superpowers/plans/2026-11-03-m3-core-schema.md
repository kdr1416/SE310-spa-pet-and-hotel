# M3 Core Schema — execution record

Spec: user-provided M3 task; system-design 4.6/4.10/4.11/6; M2-01–15.
Scope: 15 enums, 33 entities and configurations, Npgsql DbContext, local InitialCoreSchema, offline model tests.
No new endpoints, remote DB connection, database update, secrets or frontend changes.

1. [x] RED: DatabaseModelTests against missing real context.
2. [x] GREEN model: entities/enums, per-entity configurations, DI + design-time factory.
3. [x] RED/GREEN migration: migration script test fails until InitialCoreSchema is generated.
4. [x] Verify Core build (0 warnings), solution build/test, existing frontend suite; final independent review.

Ruling: ChiTietCombo uses composite PK per latest M3 request, overriding generic identity convention in M2.
Ruling: existing EF Design 10.0.14 / Npgsql 10.0.3 stay unchanged; local dotnet-ef 10.0.14 needed.
Ruling: use real Npgsql model with no connection string rather than EF InMemory; no provider/DB is contacted.
Ruling: implement on isolated codex/m3-core-schema branch in the authorized workspace; no auto-commit/push.
Tests verify schema constraints only; cross-row state/ownership/locking remain service-layer work.

RED: 18 model tests failed on missing context. Sandbox test-host requires elevated localhost permission; use -m:1 for MSBuild.
Ruling: persist CanNangKhaiBao and ReadyAt on pet line to preserve required booking weight/state timestamps from contract; document the mapping rather than silently drop input.

Model: 17 metadata checks GREEN; migration test RED with no migration; generated InitialCoreSchema offline.
Ruling: exclude EF-owned __EFMigrationsHistory from CREATE TABLE count; it is not a 34th domain entity.
Ruling: pin EF Relational 10.0.14 explicitly because EF Design private assets left tests resolving runtime 10.0.4; no new framework/provider.

Anonymous idempotency: RED on absent null-safe unique index; fixed with second partial UNIQUE(hanh_dong,request_id) WHERE actor_id IS NULL AND request_id IS NOT NULL, then regenerated initial migration offline.
Independent final review dispatched read-only per requesting-code-review skill; no per-task agents.

Final review: one Important finding (runtime key mismatch), no Critical/Minor. Regression test failed expected local DefaultConnection vs null; fixed Program to existing DefaultConnection. No appsettings values read/printed or modified.
Review exclusions: service-layer ownership/state/locking, JSON validation, normalization/timestamp updates, auth workflows and actual PostgreSQL execution are outside this foundation task.

Final verification: Core build 0 warnings/0 errors; solution build 0 warnings/0 errors; xUnit 22/22 (including original health); Vitest 56/56. Final review Important key mismatch fixed with RED→GREEN and full suite green; no Critical/Minor outstanding. No remote database connection/update, commit or push.
Ruling costs: composite PK follows latest M3 task (changing later needs migration); declared weight/ReadyAt preserve contract data (if unwanted, remove via reviewed migration); real Npgsql metadata avoids InMemory gaps (does not verify server execution); local EF tool and Relational version pin align existing Design runtime (future upgrade must align); work remains on codex branch in authorized workspace (no separate filesystem worktree); EF history excluded only from application-table count (history table remains generated normally).
