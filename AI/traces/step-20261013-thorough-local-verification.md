# Thorough local verification — 2026-10-13

## Authorization and scope

The owner requested meticulous testing of the complete local application, with
ECC and agents. `verification-loop`, `test-collaboration`, C# testing and the
existing E2E/security workflows were applied. Three agents independently
investigated security/scope, runtime/concurrency and delivery/browser risks.
Root coordinated all builds, isolated resources, RED/GREEN runs and fixes.
This request does not authorize cloud deployment or commit/push. Earlier local
language/admin/Board changes and the owner's accepted workflow removal remain.

## Reproduced defects and corrections

1. **Former-scope private pages survived session refresh.** Four actual
   App/SessionProvider/fetch/schema regressions failed. Protected Outlet is keyed
   by user ID, team and canonical roles; fresh scope discards old caches, requests
   and forms. Locale is excluded, preserving bilingual edits.
2. **Unhandled500 cleared privacy headers.** Actual HTTP pipeline fault proved
   no-store missing. Privacy and correlation headers are registered OnStarting;
   safe generic ProblemDetails and headers now survive Response.Clear.
3. **Users directory trusted stale admin claims.** Two HTTP regressions returned
   200 after persisted role loss. GetUsersAsync requires fresh database admin.
4. **Nonempty project deletion returned500.** Author/admin regressions prove409
   and unchanged project/tasks; empty authorized deletion still returns200.
   Initial visibility/author checks precede dependency checks. Native SQL FK547
   remains a conflict fallback for dependency races.
5. **Native SQL status competitions returned200/500.** Both independent-connection
   tests reproduced this. Tasks recognize typed SQL1205 throughout bounded EF
   exception wrappers; the loser rereads the winner's version and returns409.
   Existing two attempts and rollback are preserved; unrelated errors do not retry.
6. **Project writes committed after membership/admin loss at the command
   boundary.** Four real HTTP/SQLite faults returned200. Serializable transaction
   plus final fresh caller-scope validation now rolls back complete writes as409.
7. **Revoked admins could change/delete foreign accounts during writes.** Two
   boundary regressions returned200. Account writes use the same final-scope
   transaction guard. Four positive scenarios preserve admin/member profile
   updates and dependency-free admin/self deletion. Explicit self deletion uses
   original caller identity because its row is absent after DELETE.
8. **Identity concurrency failure became500.** Actual stamp change at UPDATE
   proved this with a valid self-profile request. Typed EF concurrency escapes
   the core catch to the transaction guard and returns409 with full rollback.
   Diagnostics never print stamps or credentials.
9. **200% enlarged text was clipped in fixed-height controls.** Two Chrome
   glyph/content-box regressions failed. Input/select height now grows with font
   size and keeps the normal44px alignment. Both regressions and narrow keyboard
   Board scrolling pass.

Additional verification defect: a910-pass TRX contained a Coverlet
EndOfStream warning and no coverage attachment. The installed collector URI is
now explicit in .runsettings, and native verification rejects missing collector
attachments/warnings or skipped/failed backend counts. A fresh repeat generated
current coverage successfully; no previous coverage file certifies the final run.

## Final executed evidence

| Gate | Result / boundary |
| --- | --- |
| Current .NET solution and test host Release build | PASS; pinned packages, vulnerability warnings treated as errors. Existing nullable/analyzer warnings remain. |
| Complete backend, actual native SQL Server and RabbitMQ | **910/910 PASS**, failed0, notExecuted0; all migrations, real independent SQL writes, broker/confirm/delivery regressions. |
| Fresh backend coverage | **89.17% lines (5420/6078),75.58% branches**. Current collector attachment exists, no collector warning. |
| Entire frontend with V8 coverage | **19 files,178/178 PASS**; statements86.41%, branches78.82%, functions84.23%, lines93.95%. All configured gates pass unchanged. |
| Frontend ESLint, TypeScript and Vite | PASS. Final dist index-B4plYCYI.js / index-8N0nxAiv.css. Existing547.52KB Vite chunk advisory remains. |
| Synthetic desktop/mobile Chrome UI | **58 scenarios verified**:56 passed in the complete run; two were interrupted by host/network suspension, then isolated2/2 recheck passed. No product assertion was weakened. Includes320/820 keyboard containment and200% text enlargement. |
| Actual SQL/API/gateway/cookie/CSRF/private-hub/Rabbit desktop/mobile browser | **12/12 PASS**, skipped0, flaky0; source/partial commits, removal/expiry, fallback/recovery, departed-assignee Board, bilingual two-tab conflicts, filtering/pageSize1/CRUD and nonempty deletion. |
| Offline Azure delivery/security fixtures | **26/26 PASS**, no cloud calls. |
| Full-stack launcher/process safety fixtures | **9/9 PASS**, synthetic native children only. |
| Native Bicep contract compilation | Three foundation/jobs/apps templates compile and match canonical contracts with pinned0.48.1. |
| Compiled notification permission cases | Positive contract plus four corrupted fixtures PASS (**5 cases**). |
| Azure workflow actionlint | PASS, pinned1.7.12; no cloud authentication. |
| NuGet vulnerability check | No known vulnerabilities reported for11 projects, including transitive dependencies. |
| npm vulnerability follow-up,2026-10-14 | Owner approved registry metadata transfer; `npm audit --audit-level=low --json` exited0: **0 known vulnerabilities** across321 audited dependencies. No dependency changes. |
| Restored actual local owner app | Admin login/API PASS:20 tasks,7 projects,7 teams,14 users, foreign task/admin analytics readable. |
| Actual local bilingual browser | Desktop1440/mobile390 PASS: auth, all admin directories, retained forms/errors, separate AI language, public-only storage and sign-out/reload. Initial cold/concurrent probe timed out; isolated phase-sanitized replay passed. |
| Actual local geometry | UK/EN at1440/1024/768/390 (**8 configurations**) PASS: controls/counters, bounded columns, keyboard Board and no document overflow. |

The first real browser run revealed outdated test assumptions rather than a
product regression: fresh revoked membership correctly removes private AI review
rows, so the assertion now requires source/form removal and verifies forbidden
HTTP/no materialization. Wrapped textarea initial text is included in Playwright
label matching; edit tests use the actual label prefix. A SQL assertion initially
expected migrated historical execution tables to be empty; it now compares all
pre-existing delivery/history rows. Corrected tests retain the intended guarantees.

## Resource and data preservation

Final browser run **aca77814a41d432e8f17590aa9666723**; native broker run
**549a591d39cc45cf96cef94da27d9cfd**. Matching runner/native results are PASS.
Database taskmanager_e2e_test_aca77814a41d432e8f17590aa9666723 was dropped; owned
servers stopped, broker queues deleted and all temporary broker listeners removed.
Earlier failed browser/collector runs also verified cleanup. No service/registry
or profile Erlang cookie changes occurred.

Coverlet restored all six checked product DLLs byte-for-byte against compiler
outputs. The owner's native supervisor is running again, phase ready, with its
original database/key ring/credentials. Before/after task/project/team counts
and business checksums match exactly; user count remains14. Password recovery
helpers were not rerun and no business test writes targeted the owner database.

37 historical tracked screenshots were restored after privately archiving current
captures;36 generated screenshots were moved under ignored verification evidence.
The prior two English examples remain. Logs, TRX, coverage summaries, temporary
credentials and actual native screenshots stay in ignored .local/TestResults
paths. Public tests use finite diagnostics; full-stack raw auth values/context
screenshots are not published. Source diff whitespace check passes.

## Limits and replay

Docker is absent, so no current local container/Linux execution is claimed.
Live Azure deployment/identity/email/Service Bus and actual Azure OpenAI quality,
quota/cost/model evaluation remain explicitly deferred. Controlled provider/email
tests verify contracts and persistence; they do not certify live services.

Automatic approval review initially rejected npm audit because it transmits
project dependency metadata to npm registry. The owner explicitly approved this
transfer on2026-10-14. The exact audit then completed with exit0 and all severity
counts zero;321 dependencies were audited. No alternate upload/bypass or package
changes were used. Follow-up evidence is .local/verification-20261014/npm-audit.json.

Use TESTS.md for maintained risk IDs, assets and commands. Private replay evidence
is under .local/verification-20261013 and the matching .local/fullstack run IDs.
Final TRX is TestResults/fullstack-native-rabbit-backend/test_results.trx; fresh
coverage is b8dedea8-5163-4a8b-a542-9a426efce059/coverage.cobertura.xml below that
directory. Browser summary is frontend/test-results/fullstack-summary.json.
The owner's app is available at https://localhost:7151; refresh to load current
assets. All source/test/documentation changes remain local, uncommitted.

## ECC self-evaluation

Accuracy5: current exact run IDs/counts and coverage, synthetic/live boundaries,
failed assumptions and collector error are explicit. Completeness4: thorough
authorized local testing/fixes finished; external platform/model gates remain
unavailable. The npm permission gap was closed by the2026-10-14 approved audit.
Clarity5 and actionability5: registry, replay evidence
and running owner app are provided. Conciseness4: detailed trace is intentional
for this requested meticulous audit. No axis is3 or below.
