# Plan — TaskManager

## GOAL
- Implement a robust retry and Dead-Letter Exchange (DLX) mechanism in the Notifier service.
- Deliver reminder notifications and AI-assisted reviews for task deadlines.

## Constraints
- Do not modify external REST endpoints.
- Preserve existing public API contracts and backward compatibility.
- Keep current test suite passing.
- Apply minimal changes to existing configuration structure.
- Avoid expanding configuration surface beyond reminder/queue needs.

## Unified Step-by-Step Roadmap

| # | Step | Acceptance Criteria |
|---|------|---------------------|
| 1 | Research RabbitMQ retry & DLX patterns | `research.md` updated with selected approach |
| 2 | Extend Notifier configuration for DLX/retry queues | `appsettings.json` contains retry settings |
| 3 | Implement retry logic with limited attempts | Simulated failure triggers retries then DLX |
| 4 | Add DLX consumer/logger | Failed messages recorded with metadata |
| 5 | Integrate metrics & monitoring | Grafana / App Insights counters available |
| 6 | Write integration & E2E tests | All message-flow scenarios verified |
| 7 | Human review & plan approval | `plan.md` accepted and merged |
| 8 | Implement iteratively, updating `traces/` | Each step compacted and logged |
| 9 | Observe production behaviour | Metrics show retry/DLX counts |
| 10 | Confirm each step | The user approves this manually |
| 11 | ✅ Add reminder data model, validation, and migration | Tasks include reminder fields; DTOs/validators cover offsets and due date presence |
| 12 | ✅ Implement reminder scheduling and messaging | Hosted service publishes reminder and overdue envelopes to RabbitMQ with correlation IDs |
| 13 | ✅ Update Notifier and clients for reminders | Notifier emits typed notifications; console lists/updates reminder state |
| 14 | ✅ Add Consensia AI Reviewer workflow | Workflow runs on PRs using Consensia action with guarded secrets and required permissions |
| 15 | ✅ Validate docs and tests | README reflects reminder features and automation; regression tests stay green |

## Reminder reliability repair — requested 2026-10-07

Scope authorized by the repository owner: fix reminder failure/reprocessing behavior and prepare a PR. Preserve REST endpoints and notification payload compatibility.

| # | Step | Acceptance Criteria |
|---|------|---------------------|
| 16 | ✅ Persist reminders in a transactional outbox | Task notification IDs and pending envelopes commit together; competing scans cannot enqueue the same occurrence |
| 17 | ✅ Dispatch with expiring claims and broker confirms | Failed or interrupted sends remain retryable; retries preserve the message ID; sent timestamps follow broker confirmation |
| 18 | ✅ Acknowledge consumption after processing | SignalR failures leave the message available for redelivery |
| 19 | ✅ Validate recovery and document guarantees | Relational rollback/concurrency, restart, failure and acknowledgment tests; at-least-once boundary documented |
| 20 | ✅ Present the flagship application | README contains the problem, runnable local setup, demo sequence, architecture rationale and explicit limitations |

## Product modernization — authorized 2026-10-11

### GOAL

Deliver the owner's accepted TaskManager implementation brief (2026-10-11): secure team task management, Ukrainian React UI, notes-to-draft-to-commit AI, durable private notifications and reproducible separate Azure web/API/notifier deployment.

The owner explicitly requested implementation of the supplied brief and use of agents/ECC in the current chat. This authorizes the following plan and additive API/UI/configuration work, superseding the earlier reminder-only scope constraints. Preserve existing JWT console routes, integer entity keys, existing users/password hashes, SQL data, old migration files and outbox guarantees. Do not merge PRs automatically. Accepted `research.md`/`decisions.md` remain unchanged; current findings are in `docs/modernization/baseline.md`, proposed new decisions in `AI/adr/003-product-modernization.proposed.md`.

### Active step

28 — Local phase ACCEPTED. The owner explicitly deferred Azure deployment on 2026-10-12. Native Windows SQL/RabbitMQ: 860 backend, 87 frontend unit, 52 UI and 6 full-stack browser tests passed; exact database/queue/server/broker/private-key cleanup verified. Current code at 93b737c also passed all six PR CI workflows, including Linux SQL/Rabbit full-stack and six non-root container images. TaskManager_Codex_20261012 retains eight migrations and 19 original tasks. Azure deployment, actual cloud adapters and live model evaluation remain DEFERRED; nine actual Azure probes/access routes are pending. No whole-project deployed/live acceptance is claimed. Evidence and replay instructions are in the step28 trace and native Windows runbook.

| # | Step | Acceptance criteria |
|---|------|---------------------|
| 21 | ✅ Baseline and compatibility audit | SHA/default branch/status, auth/data/endpoints/tools, real restore/build/tests/coverage and external blockers recorded |
| 22 | ✅ Reproducible .NET 10 runtime | Stable SDK/packages pinned, Release build/tests/audit, multi-stage non-root images/Compose, SQL-aware readiness; real Linux container smoke PASS run 38166615774 |
| 23 | ✅ Authentication and data isolation | Identity cookie/JWT, confirmation/recovery/password/logout/me, CSRF/rate limits/filesystem key ring/SMTP; scoped data/private hub, owned teams/invites/race guards; real SQL lifecycle/migration PASS run 38166615743 |
| 24 | ✅ Web host and Ukrainian React workflows | Same-origin YARP, responsive accessible auth/dashboard/projects/teams/task list/board/forms, real API operations, loading/empty/error/session/concurrency states, typed validated API and pinned npm lockfile; SQL/backend495, frontend44/browser24 and five-image HTTPS container CI PASS at cb524a5 |
| 25 | ✅ AI drafts and transactional commit | Official configured Azure SDK, unavailable/fake separation, structured source-backed drafts, reference-date/timezone resolution, restricted assignees, expiry/version/revalidation, idempotency/unique constraints/atomic task+outbox commit; failure tests and30UA/EN cases; real SQL629/browser38/five-image CI PASS at d717ffc (live model evaluation requires actual deployment/auth/budget in28) |
| 26 | ✅ Durable notifications and worker transports | SQL inbox/dedup before broker ack, private authenticated realtime/reconnect, bounded retry/DLQ, RabbitMQ+Service Bus adapters, scheduler moved out of API, stable IDs/leases/confirms preserved;769realSQL/Rabbit,87unit/52browser/five-image CI PASS at15aa4d3 |
| 27 | ✅ Azure infrastructure and delivery gates | Bicep/resources/MI/runtime SDKs, least-privilege SQL bootstrap, OIDC and bounded migration/candidate/smoke/traffic/rollback scripts/runbook;860realSQL/Rabbit,87unit/52browser,siximages,Windows/Linux offline tooling CI PASS at79b6e3d. Actual ARM what-if/deploy/provider authentication remain explicit live gates in28 |
| 28 | ✅ Local end-to-end verification; Azure DEFERRED | Native Windows 860 backend/87 unit/52 UI/6 actual SQL-Rabbit browser cases, audits/coverage/cleanup and reproducible runbook; all six CI workflows passed at93b737c. Azure deployment and real model/provider checks explicitly deferred by owner; no fixture evidence substitutes for them |

Implementation evidence and restart instructions belong in `AI/traces/`. This is the sole roadmap; modernization documents link here rather than maintaining a parallel plan. Legacy reminder/DLX work is incorporated into step 26; production observation/approval rows 9–10 require actual production evidence.

## Local administration follow-up — authorized 2026-10-13

The owner explicitly requested that admin@example.com see all tasks, teams,
projects and related workspace records in the existing local database.

1. ✅ Grant the existing admin role to the confirmed local account ID 11, preserving
   its recovered password and all workspace ownership/assignment data.
2. ✅ Use system-wide task/project scope on the admin overview and add a read-only
   administrator user directory through the existing authorized API.
3. ✅ Verify global API/UI counts against SQL and retain ordinary-member isolation,
   including refreshed role/membership changes. Evidence belongs in the local
   administration trace; Azure remains deferred.

## Interface languages — authorized 2026-10-13

The owner requested an interface selector for Ukrainian and English. Apply a
shared reactive locale to the existing TypeScript copy maps, extend untranslated
UI copy, and retain user-entered data, API/AI locale contracts and access rules.
Default to Ukrainian, persist only the public locale preference, and provide
the selector on account pages and in the authenticated workspace. Verify
locale-key parity, translated validation/errors/date labels, state-preserving
switching, refresh persistence and actual desktop/mobile UI before updating the
local dist. Azure remains deferred.

Completed locally: shared selector and all feature copy are reactive; 164 frontend
unit tests, 52 existing UI cases and two new bilingual browser cases passed.
Actual SQL-backed desktop/mobile checks passed and the updated dist is served at
https://localhost:7151. Evidence and replay commands are in
`traces/step-20261013-interface-languages.md`.

## Interface alignment and Board repair — authorized 2026-10-13

The owner requested a modest visual polish/alignment pass and a fix for the
Tasks Board validation error shown in the current local app. Retain the teal
visual identity and Ukrainian/English copy. Reproduce the failing status update
against its actual API contract, repair it with a regression test, and align
form controls, filters, buttons, headers and board cards across desktop/mobile.
Preserve authorization, optimistic concurrency, existing business data and AI
availability boundaries. Verify the final local build in the actual browser.
Azure remains deferred; evidence belongs in the follow-up trace.

Completed locally: Board now uses a narrow versioned status command; unrelated
fields and historical assignments remain intact. Controls/headers/filters/cards
are aligned, tablet overflow is corrected, and long Board columns scroll within
bounded heights. Verification: 63 backend/access tests, 174 frontend tests,
54 UI cases and eight actual SQL/API/browser cases passed; final geometry passed
in both languages at 1440/1024/768/390px. See
`traces/step-20261013-ui-polish-board-repair.md` for scope and evidence.

## Thorough local verification — authorized 2026-10-13

The owner explicitly requested meticulous testing of the whole application.
Run the full backend/frontend suites with coverage, real SQL lifecycle/concurrency
and Rabbit delivery, actual desktop/mobile/browser user flows, role/session/CSRF
and IDOR checks, bilingual/responsive/accessibility/error boundaries and offline
delivery tooling. Add meaningful regressions for uncovered risks, fix discovered
defects, and distinguish unavailable Docker/live AI/Azure gates from executed
evidence. Own and clean up disposable test resources; preserve the product DB,
credentials and previous local changes. No commit, push or cloud deployment is
part of this verification request. Record exact results and remaining limits.

Completed locally:910 backend tests with actual SQL/Rabbit and fresh89.17% line
coverage,178 frontend tests with93.95% line coverage,58 UI scenarios and12 actual
desktop/mobile SQL/API/browser journeys verified. Nine reproduced product defect
categories were corrected with regressions; collector evidence handling was
repaired. Offline delivery/tooling gates pass, exact owned cleanup is verified,
and the restored owner app preserves all business checksums/counts. Docker/live
Azure/model gates remain deferred. On2026-10-14 the owner approved npm registry
metadata transfer; npm audit completed with0 known vulnerabilities across321
dependencies. See `traces/step-20261013-thorough-local-verification.md` and TESTS.md.

## Dark charcoal/amber UI — authorized 2026-10-15

The owner supplied qa-interview.com and its screenshot as a visual reference and
requested a more polished TaskManager in this style. Apply a coherent dark
canvas/surface/control token system, warm amber primary actions and active
navigation, restrained monospace metadata, consistent stroke icons and more
legible spacing/hierarchy across the authenticated workspace and account pages.
Keep current Ukrainian/English strings, rights/session scope and narrow versioned
Board writes. Improve dashboard semantics/row alignment with actual data only.
Check loading/empty/error/partial/success/permission states, normal/status/control
contrast, keyboard focus, long text and200% enlargement at1440/1024/768/390/320px.
Run affected frontend/unit/lint/build/browser checks and a final native SQL-backed
read-only owner UI review. Preserve business data and earlier uncommitted work;
publish the local dist and capture local evidence, with no cloud or Git publish.

## Usability refinement — authorized 2026-10-16

The owner asked for maximum convenience, polish and functionality. Continue the
accepted dark UI work with a focused usability repair: keep task filters in sync
with browser history, expose clear-filters while retaining Board/list selection,
announce successful state changes and restore keyboard context when cards move,
and reveal the active mobile navigation item on direct routes/language changes.
Improve everyday text/control readability without adding dependencies or changing
API/permissions/business semantics. Verify with focused history/focus/feedback
regressions, then lint/build and full frontend/browser checks. Existing native
SQL-backed read-only checks remain the actual backend boundary.

Completed locally: coherent dark/amber UI, actual-data Dashboard semantics,
readable responsive forms and focused usability repairs.178 unit and78
maintained browser scenarios pass, alongside16 rendered-capture scenarios and
actual native bilingual/admin/geometry checks. See
`traces/step-20261016-dark-ui-usability.md` and TESTS.md. New dist is served by the
existing local HTTPS host; business data/credentials and prior changes retained.

## Local HTTP — authorized2026-10-16

Owner requests additional plain HTTP access for the in-app browser, with minimal
output/token use. Add an explicit Development-only, exact configured loopback
origin/remote exception to redirects and compatible local cookie names/policies.
Keep production HTTPS unchanged, CSRF/origin checks active, both local listeners
available. Verify focused regressions and actual HTTP login/readiness, then open
http://localhost:7150 in-app. Preserve the SQL database and existing credentials.

Completed: both listeners ready;72 focused backend tests pass, actual HTTP admin
login/reads succeed and the in-app HTTP sign-in page is open. See
`traces/step-20261016-local-http.md`.

## Readability and mobile refinement — authorized2026-10-17

Owner accepted the four proposed refinements: larger supporting text/dates,
compact Dashboard statistics with tasks higher on screen, collapsible mobile
advanced filters and correct UK/EN task counts/month casing. Preserve current
API/session/permissions, filter drafts/history, desktop controls and local HTTP.
Use existing ECC design/polish workflows and agents for focused copy/tests; verify
unit/lint/build, affected browser scenarios and actual rendered desktop/mobile
before replacing local dist. Keep output and token use economical.

Completed:180 unit tests,80 browser scenarios and16 capture scenarios pass.
Native signed-in admin UI confirms the corrected copy and compact layout. Local
dist is live; see `traces/step-20261017-readability-mobile.md`.

## Favicon and sidebar language — authorized 2026-10-17

Add a local SVG favicon matching the amber check logo. Move the workspace
language selector into the bottom of the left sidebar; retain an accessible
selector in the compact mobile sidebar. Verify build, focused UI checks and
rendered local UI without changing business data.

Completed: local dist updated, lint/build and 12 focused browser checks pass;
native admin desktop and compact renders verified. See
`traces/step-20261017-sidebar-language.md`.

## Board drag and drop — authorized 2026-10-19

Add mouse/touch drag handles for moving tasks between status columns, highlighted
drop targets and edge scrolling. Provide Space/arrow/Enter/Escape keyboard controls
and localized instructions/announcements; retain status selects. Reuse versioned
PATCH state commands, pending guards, error recovery and focus restoration.
Verify focused component/browser regressions and rendered UI. Keep SQL data intact.

Completed: production build/lint, 15 Board component tests and 32 desktop/mobile
browser cases pass. Local UI updated; see `traces/step-20261019-board-drag.md`.

## Prepare Pull Request — authorized 2026-10-19

Update existing draft PR51 with the accumulated local UI, bilingual/admin,
Board/state/concurrency and Development HTTP changes. Review tracked/new source
and avoid private runtime credentials/artifacts; run final frontend gates, preserve
prior backend/live evidence boundaries, commit and push the existing head branch,
then update the PR description and attach its verified URL. Keep Azure deferred
and do not merge. GitHub CLI lacks authentication, so use the connected GitHub
tool for PR metadata; Git Credential Manager remains available for git transport.

Prepared:185 frontend tests with coverage thresholds,90 UI cases,53 focused
backend regressions, lint/build/contract checks pass. Reviewed source staged;
see `traces/step-20261019-pr-preparation.md` for publication target and boundaries.
