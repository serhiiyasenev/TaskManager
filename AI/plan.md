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
