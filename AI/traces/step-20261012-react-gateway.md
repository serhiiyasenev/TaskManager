# Step 24 — React workflows and same-origin gateway

Date: 2026-10-12. Branch: `feature/ai-step-21-modernization`; draft PR
[51](https://github.com/serhiiyasenev/TaskManager/pull/51). Sole roadmap:
`AI/plan.md`. Accepted research/decisions remain unchanged. Preserve the owner's
explicitly approved Consensia workflow deletion and all old migration files.

## Delivered sources

- Ukrainian React/TypeScript/Vite/Tailwind: cookie auth, confirmation/recovery,
  profile/password, scoped dashboard/projects/teams, invitations/membership,
  task list/board, filters/sorting/paging, UTC forms/reminders, conflicts and
  session expiry. Validated same-origin Zod transport obtains fresh CSRF tokens;
  no JWT browser storage or synthetic production data.
- Database-free ASP.NET Core/YARP WebHost and shared Hosting: final DI
  configuration, explicit destinations/public origin, trusted proxy/hop/host
  limits, HTTPS and WebSocket forwarding, private health probes, SPA/asset/API
  separation and query-token privacy. Readiness requires built UI and bounded
  API readiness; liveness does not fetch dependencies.
- Additive SQL task paging/filters scope before count, GUID optimistic version,
  browser 428/409 and strong If-Match; legacy bearer routes remain compatible.
  Serializable writes recheck caller/project/assignee after saving before
  commit; denial rolls back task/version/outbox changes. New migration backfills
  existing task versions without changing business data. Real SQL lifecycle
  gate covers pagination, escaping, UTC/null ordering and concurrency.
- Node 24.18.0/npm 12.2.0 and exact stable lockfile; frontend lint/types/unit
  coverage/browser CI. Five non-root images, HTTPS Compose profile, local
  certificate/up/smoke scripts and extended disposable Linux container gate.

## TDD and verification

Behavioral RED checkpoints are in branch history: gateway routes/configuration,
late forwarding, upstream readiness, typed CSRF response, stale session fetch,
forms, auth/team UI, task paging/version, six revoked-access write races and
malformed confirmation IDs. New UI assignee/full conflict review RED is
`59ae782`; gateway upstream-readiness RED is `6fd2c3e`.

- Gateway/forwarding: **42 passed / 0 failed / 0 skipped**, real loopback Kestrel
  HTTP and HTTPS WebSocket with test-only pinned certificate validation.
- Full backend: **492 passed / 0 failed / 3 skipped / 495 total, 29 seconds**.
  `TestResults/step24/step24.trx`; `.runsettings` collects production code,
  excluding only generated files/migrations. The three real-SQL gates explicitly
  skip locally because SQL Server is absent; actual CI must execute them.
  Coverage: **89.25% lines (3339/3741), 74.28% branches (1254/1688)**.
- Frontend strict production build and lint PASS. **44 unit tests passed**.
  Vitest coverage thresholds:
  lines/statements 80%, branches/functions 70%; current line coverage **90.36%**,
  statements **80.46%**, branches **70.78%**, functions **76.24%**. No production
  feature is excluded to meet the thresholds.
- Actual installed Chrome, disposable contexts, desktop 1440x900/mobile390x844:
  **24 passed**, covering rendered loading/empty/error/partial/success/permission/
  session/conflict, long content, keyboard/focus, contrast/reduced motion and UTC
  forms. This uses explicitly intercepted synthetic API responses. It proves UI
  behavior, not live Identity/SQL/AI end-to-end; full-stack E2E is step28.
- Docker is absent locally. Earlier four-image CI evidence at7e11f5f remains
  valid only for steps22–23; new five-image/HTTPS and current real-SQL/frontend
  CI results are pending. Do not mark this step complete until these pass.

## Exact continuation

Finish rendered evidence and ECC finish checker; commit only synthetic screenshots
and concise machine-readable summary. Push this branch to existing draft PR51;
inspect real backend/SQL, frontend and five-image container runs. Fix failures,
record exact head/run evidence, then mark24 complete and advance25.

Agents: delivery_audit owns gateway/HTTPS/container; runtime_audit owns task
version/paging/atomic rights checks; security_audit owns auth/team UI and rendered
QA; root owns shared builds/tests/Git and remaining frontend. Sources are frozen
for step24 verification. Step25 agents have read-only contract preparation only.
AI drafts, durable inbox/worker transports, Azure delivery and full-stack/live
verification are required future steps and are not delivered yet.
