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
read and commit, including the already-member acceptance path. Agents are adding
deterministic regression tests before transaction hardening. Root must run and
checkpoint RED, authorize the fix, rerun affected checks/full gate, then mark step
23 complete and advance to step 24. Do not treat the current GREEN as proof of
this new race. Step 22 container execution remains external until CI smoke runs.

Draft PR #51 targets main from feature/ai-step-21-modernization. Preserve its
single branch/dependency chain; no auto-merge. Restart from this trace, plan and
latest working tree. Later stages (React/gateway, AI, inbox/worker, Azure) remain
required and are not represented as delivered.
