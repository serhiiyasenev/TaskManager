# Test registry

This registry maps product risks to maintained test assets. Exact executed
results and unresolved gates belong in `AI/traces/`, not in stale test counts.
Test inputs and authentication artifacts stay in ignored `.local/` directories.

## Coverage map

| ID | Product risk / acceptance | Maintained assets | Evidence boundary |
| --- | --- | --- | --- |
| TEST-AUTH | Cookie login, confirmation/recovery, session/stamp expiry, CSRF, origin, bearer selection, private routes | `tests/backend/Integration/BrowserAuthentication*`, `ThoroughSecurityTests.cs`, `src/frontend/src/features/auth/*test*`, `tests/frontend/fullstack/journeys.spec.ts` | Actual Identity/HTTP in disposable SQLite; full-stack journeys additionally use native SQL and HTTPS cookies. Mail/model responses are controlled. |
| TEST-SCOPE | Admin directory; scoped tasks/projects/teams/users; IDOR; scope changes during loads and writes; profile concurrency conflicts | `DataIsolationTests.cs`, `TaskAuthorizationRaceTests.cs`, `ProjectAuthorizationRaceTests.cs`, `UserAuthorizationRaceTests.cs`, `UserWriteTransactionTests.cs`, `UserConcurrencyConflictTests.cs`, `src/frontend/src/lib/SecurityBoundaries.test.ts`, admin/dashboard tests | Fresh database rights and transactional rollback; frontend scope fixtures use actual App/session/transport with invented HTTP responses. Command-boundary faults do not claim concurrent SQL lock evidence. |
| TEST-TASK | CRUD/filter/pagination; narrow Board status; required version and stale conflicts; assignments/deadlines unchanged | `TaskStateBoardTests.cs`, `ThoroughTaskStateTests.cs`, `SqlServerTaskStateConcurrencyTests.cs`, `src/frontend/src/features/work/*test*`, `tests/frontend/fullstack/{board,thorough}.spec.ts` | Native SQL competitions use independent connections and real migrations; browser cases use actual API/SQL. Synthetic UI tests alone do not prove backend behavior. |
| TEST-PROJECT | Nonempty deletion returns409 and preserves data; empty deletion works; rights checked before mutation | `ProjectDeletionConstraintTests.cs`, `ProjectAuthorizationRaceTests.cs`, `tests/frontend/fullstack/thorough.spec.ts` | Real SQLite HTTP and native SQL/browser; foreign-key enforcement remains authoritative. |
| TEST-NOTIFY | Durable outbox/inbox, occurrence suppression, duplicate/idempotent processing, confirm/lease races, private live hub/reconnect | `tests/backend/Notifications/`, `ThoroughTaskStateTests.cs`, `src/frontend/src/features/notifications/*test*`, `tests/frontend/e2e/ui-inbox.spec.ts`, `tests/frontend/fullstack/journeys.spec.ts` | Actual native Rabbit gate exercises broker confirms/transport; UI socket fixtures are explicitly synthetic. Azure Service Bus is a separate live gate. |
| TEST-ADMIN-INBOX | Current admin role, cross-team read-only overview, recipient read isolation, denied-cache/late-response clearing, filters and bilingual display | `tests/backend/Notifications/NotificationAdminInboxTests.cs`, `src/frontend/src/features/notifications/AdminInbox.test.tsx`, `tests/frontend/e2e/ui-admin-inbox.spec.ts` | Actual SQLite HTTP role/DB checks; browser fixtures prove UI behavior. Global mode is explicitly refreshed and never writes recipient read markers. |
| TEST-AI | Strict provider/output validation, source review, selected/partial commits, immutable receipts, no duplicate/unauthorized saves, retention, unavailable/manual fallback | `tests/backend/AI/`, frontend AI tests, `tests/frontend/e2e/ui-ai.spec.ts`, full-stack journeys | Controlled provider responses prove contracts and persistence, not live model quality, quota or Azure identity. |
| TEST-LANG | Ukrainian/English copy/errors/dates; canonical Kyiv; persistent public locale; fields/private state preserved during language choice | Frontend locale/date/timezone tests; `tests/frontend/e2e/ui.spec.ts`; `tests/frontend/fullstack/thorough.spec.ts` | Rendered UI, actual cross-tab storage event and real API/version conflicts. Only public locale is persisted in browser storage. |
| TEST-READABILITY | Task-count plural forms, month casing, compact statistics, readable dates and accessible mobile filter disclosure/draft retention | `src/frontend/src/features/work/WorkPlural.test.tsx`, `tests/frontend/e2e/ui-theme.spec.ts`, `tests/frontend/e2e/ui-usability.spec.ts`, `tests/frontend/e2e/taskFilterControls.ts` | Browser measurements and controlled UI fixtures; actual signed-in local admin copy is checked separately. |
| TEST-UI | Long content, focus/labels, reduced motion, desktop/mobile/tablet/narrow containment, keyboard Board scroll,200% text enlargement | `tests/frontend/e2e/ui*.spec.ts`, particularly `ui-thorough.spec.ts` | Installed Chrome with explicit synthetic API fixtures. Text enlargement is not browser chrome zoom or a manual assistive-technology certification. |
| TEST-COMPACT | Compact admin header, accessible language choice and readable dashboard statistics at390/684/760px;320px with200% text | `tests/frontend/e2e/ui-compact-header.spec.ts` | Synthetic admin/browser layout evidence; no product data or authentication changes. |
| TEST-THEME | Charcoal/amber text/status/control contrast, accessible dark account forms, focus,200% text and readable navigation | `tests/frontend/e2e/ui-theme.spec.ts` | Browser-computed sRGB compositing, keyboard and layout assertions with invented data; local rendered inspection complements automated measurements. |
| TEST-USABILITY | Back/Forward filter synchronization, draft preservation, Clear retaining view/pageSize, save announcements/focus, filtered-page recovery, team/assignee selection and active mobile nav | `tests/frontend/e2e/ui-usability.spec.ts` | Rendered React/browser history and held HTTP fixture responses; no real authorization, SQL or business-data writes are claimed. |
| TEST-DELIVERY | Immutable release/config contracts, least privilege roles, migration/rollback sequence, timeout/process cleanup and live opt-in | `scripts/azure/tests/`, `scripts/fullstack/tests/`, native Bicep compilation/actionlint | Offline native tooling/controlled fixtures; cloud deployment and real external services require separately authorized inputs. |
| TEST-DEPS | Known vulnerable direct/transitive packages | Locked NuGet/npm dependencies; `dotnet list src/backend/TaskManager.sln package --vulnerable --include-transitive --format json`; frontend `npm audit --audit-level=low --json` | Registry advisory checks reflect their execution date and known advisories. npm metadata transfer was explicitly owner-approved2026-10-14. This does not certify absence of unknown vulnerabilities. |
| TEST-LOCALHTTP | Explicit Development loopback HTTP, exact origin/peer checks and unchanged secure cookie defaults | `tests/backend/Integration/DevelopmentHttpTests.cs`, existing browser authentication/gateway/origin tests | Local HTTP is configured explicitly; production defaults remain HTTPS. Actual owner HTTP login/readiness is separate native evidence. |
| TEST-DRAG | Mouse/touch/keyboard Board movement, edge scrolling, cancellation, versioned state saves and pending/focus recovery | `src/frontend/src/features/work/TasksBoard.test.tsx`, `tests/frontend/e2e/ui-board-drag.spec.ts` | Real browser mouse/CDP touch gestures with invented HTTP fixtures; existing state API/native SQL gates establish persistence separately. |

## Local execution

1. Restore/build the pinned solution and frontend. From the repository root run
   `npm ci --ignore-scripts`, `npm run contracts:check`, `npm run lint`,
   `npm run typecheck`, `npm run build:frontend`,
   `npm run test:coverage -- --maxWorkers=1` and `npm run test:e2e`.
   The single root npm lockfile covers both application and browser-test workspaces.
   On Windows, point TEMP/TMP/TMPDIR to a repository-local ignored
   directory if the Vitest cache cannot use the profile temp directory.
   If fork workers stall on Windows, use
   `npm run test:coverage -- --maxWorkers=1 --pool=threads`; the same tests and
   coverage thresholds apply.
2. For the complete backend/native transport gate, build Release first, then run
   `scripts/fullstack/Invoke-WindowsRabbitVerification.ps1 -SqlMasterPath <private-master-connection-file>`.
   It runs the entire solution with `.runsettings` coverage and the real
   desktop/mobile full-stack suite. The supplied SQL connection must target
   master; GUID databases and loopback broker/processes belong to the runner.
   Free its reserved broker ports5672/43691/25691 by stopping only the task-owned
   local app. Rebuilding loaded Release DLLs also requires that app to stop.
3. For a backend-only run set `TASKMANAGER_TEST_SQL_SERVER` from a private
   master connection and opt into Rabbit only when its expected disposable
   broker is running. SQL/Rabbit skips without these inputs are **not acceptance**.
4. Run `scripts/azure/tests/Run.ps1`,
   `scripts/azure/Test-Deployment.ps1 -OfflineOnly -BicepPath <pinned-bicep>`,
   `scripts/azure/tests/CompiledRoles.ps1` and `scripts/fullstack/tests/Run.ps1`.
   Use the pinned actionlint for the Azure workflows. None deploys to Azure.

## Gates and diagnostics

- Frontend coverage gates: statements80%, lines80%, branches70%, functions70%
  as configured in `src/frontend/vite.config.ts`; never lower them to make a run pass.
- Native gate: every executed backend/browser case must pass; no silent skips,
  retries or stale successful report accepted. Actual counts come from current TRX
  and the matching finite browser summary/run ID. Confirm owned DB/queue/server
  and broker-listener cleanup separately.
- Full-stack reporter deliberately excludes raw errors, authenticated URLs,
  cookie/token/mail/form values and screenshots. Diagnose with finite case IDs
  and source locations; private runner artifacts remain out of Git.
- Product database/credentials are never test fixtures. Preserve product counts
  and business checksums before/after tests; restart the owner's local app.
- Docker/container execution, live Azure identity/email/Service Bus and live
  Azure OpenAI/model evaluation remain distinct gates when unavailable locally.
- Add a failing behavioral regression before correcting a newly reproduced
  defect. Repair invalid test assumptions, such as expecting migrated historical
  tables to be empty, without removing data-preservation assertions.
