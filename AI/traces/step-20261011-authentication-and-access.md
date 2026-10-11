# Step 23 — authentication and caller-scoped data

## Authorization and scope

Owner accepted the supplied implementation brief and explicitly requested ECC and
agents. This continues the sole `AI/plan.md`; accepted research/decisions and old
migrations remain unchanged. Root coordinates shared builds/tests/Git. ECC
security-review, tdd-workflow and verification-loop guide implementation; agents
own Identity, data access and private notifications/console respectively.

## Implemented checkpoints

- Existing Identity<int>: confirmed email, browser cookie, CSRF, login/logout/me,
  forgot/reset/change, lockout/rate limits, validated PublicOrigin and SMTP adapter.
  JWT issuer/audience/lifetime remain available for native console clients.
- Product-specific persistent filesystem key ring shared by API/Notifier; real
  restart validation and security-stamp session revocation. Unsupported Blob
  provider fails closed until the real Azure adapter is implemented in step 27.
- Required production caller/access services scope business/analytics queries to
  current SQL membership and verified administrator role. Scalar write DTOs avoid
  entity graph/admin mass assignment; server sets project author.
- Owned-team onboarding and email-bound expiring invitations; SQL CAS validates
  membership and token versions. Legacy teams retain nullable owner and data.
- Authenticated private hub, no arbitrary client broadcast or query JWT; Origin
  guard. SQL-recipient lookup and ACK after SignalR send retained. Durable inbox,
  membership revalidation and bounded retry/DLQ remain step 26.
- Console environment URLs, in-memory session/login/logout and private SignalR;
  normalized 401 session expiry and blank deadline preservation.
- Runtime seed removed. Incremental SecureLegacyAccounts fingerprints only known
  demo credentials, revokes seeded admin and locks those users; preserves hashes,
  users and business rows. AddOwnedTeamInvitations adds tables/nullable owner.
- Safe ProblemDetails and correlation IDs; query-bearing framework request logs
  suppressed, API responses no-store/no-referrer. Mailpit added to Compose.

## TDD and evidence

RED checkpoints include authentication/hub exposure, 38 data isolation cases,
ProblemDetails, 22 onboarding cases and console workflow defects (commits recorded
in branch history). Auth 21 cases include real loopback SMTP and filesystem/SQLite
restart; onboarding 22 and isolation 38 passed after implementation.

Command:
`dotnet test Tests/Tests.csproj --configuration Release --no-restore --settings .runsettings --logger trx;LogFileName=auth-access-green.trx --results-directory ./TestResults/auth-access-green -v quiet`

Latest complete run: **393 passed, 0 failed, 1 skipped, 394 total, 45 seconds**.
Coverage: **89.81% lines (2990/3329), 74.33% branches (999/1344)**. Console is now
included in collection; no production code/test exclusion or audit gate weakened.
Locked restore with NU1901–4 warning-as-error PASS. Incremental migration scaffold
PASS. Logs/TRX/coverage are under ignored TestResults; Docker/SQL not present.
The SQL Server migration test is explicitly skipped without
`TASKMANAGER_TEST_SQL_SERVER`; CI now provisions real SQL Server. It checks prior
schema data/human admin preservation and additive ownership migration.

## Open checkpoint and next action

Security review after GREEN found an inviter-rights/rename race between permission
read and commit, including the already-member acceptance path. Runtime RED: all
three injected revocations incorrectly returned 200, checkpoint `87f233a`.
Serializable transactions, conditional receiver/consume writes and guarded rename
now close it; deadlock retries are limited to two attempts. Affected 63 tests PASS.
Final full run (`TestResults/step23-final`): **396 passed, 0 failed, 1 skipped,
397 total, 39 seconds**; **89.52% lines (3018/3371), 73.37% branches (995/1356)**.
New nullable annotations have their own enabled context; analyzers stay enabled.

Real Linux/SQL Server CI at `e0d14c1de827edaecbd04b2129055ee0679a4d31`:
[run 160](https://github.com/serhiiyasenev/TaskManager/actions/runs/38165790980)
**394 passed, 0 failed, 0 skipped, 14 seconds**, including upgrade from prior
outbox schema through both new migrations/data/human credential preservation.
This run predates race hardening and proves schema upgrade, not SQL locking races.
A disposable real-SQL business lifecycle gate is being added to prove current
conditional UPDATE translation/rollback. Prepared container smoke builds four
non-root targets and runs migration/SQL/HTTP/SMTP/console checks; real execution
still requires its CI result. Do not equate preparation with successful images.

The owner's remote `fff5efa` removed only Consensia workflow. Auto-review initially
blocked merging a CI deletion; the owner explicitly confirmed preserving it.
Root merged without rewriting history and pushed `e0d14c1`. Never restore it.

Next action: include real-SQL lifecycle test and CI smoke, run local focused gate,
commit/push, inspect runner result, then advance to React/YARP step 24. Keep
remaining runtime gates explicit. UI design contract and verified registry
versions are prepared; no frontend runtime is delivered yet.

Draft PR #51 targets main from feature/ai-step-21-modernization. Preserve its
single branch/dependency chain; no auto-merge. Restart from this trace, plan and
latest working tree. Later stages (React/gateway, AI, inbox/worker, Azure) remain
required and are not represented as delivered.
