# TaskManager modernization baseline — 2026-10-11

## Checkout

- Repository: https://github.com/serhiiyasenev/TaskManager; default branch `main` verified using the GitHub connector and `origin/HEAD`.
- Starting SHA: `eb5dd4c58c4c9f96635a627b3f01605ad53e5079`; clean working tree before work.
- Implementation branch: `feature/ai-step-21-modernization`, based on that SHA. Owner explicitly authorized the supplied implementation plan and agents/ECC.
- Sole execution plan: [AI/plan.md](../../AI/plan.md), steps 21–28. Progress/restart state: [AI/traces](../../AI/traces). Accepted research/ADRs preserved; additive proposal: [ADR 003](../../AI/adr/003-product-modernization.proposed.md).

## Actual structure and contracts

Six .NET 10 projects: DAL (EF/SQL/Identity entities), BLL (services/validation/JWT/RabbitMQ), WebAPI (controllers/reminder scheduler), Notifier (RabbitMQ consumer/SignalR), Client (console), Tests (xUnit/Moq/InMemory/SQLite).

Routes: `/api/Users/Register`, `/api/Users/Login`, `/api/{Projects,Teams,Tasks}` and `{id}`, `/api/Tasks/AddExecutedTask`, `/api/Tasks/{id}/reminder`, `/api/Analytics/*`. Default API version is 1.0; no version URL segment. Notifier hub: `/chathub`. Task states: `ToDo`, `InProgress`, `Done`, `Canceled`. Entity keys are integers; a user has optional `TeamId`, projects have `TeamId` and `AuthorId`, tasks have `ProjectId` and `PerformerId`.

No React/frontend lockfile, gateway, product AI, inbox, Dockerfiles, Bicep, Azure deployment scripts or OIDC deployment exist at baseline. AI folder documents development governance, not runtime AI.

## Auth/data/security findings

- IdentityCore, password hash reuse, UserManager/SignInManager, role `admin`, lockout and configurable bootstrap exist. Browser cookies, antiforgery, confirmation/recovery and external Data Protection key ring are absent.
- Projects/Teams/Tasks/Analytics and user update endpoints lack authentication; services do not scope access by caller. User `TeamId` and entity graphs are client writable. JWT issuer/audience checks disabled; a development signing configuration is present in tracked settings (never use it in production).
- Unconditional EF model seeds contain demo credential hashes and an admin role assignment. Production remediation must preserve business rows and human-created accounts, add an incremental migration and require explicit development seed.
- Notifier has no auth middleware, accepts client broadcast and sends private reminders through `Clients.All`.
- SQL health check lacks the `ready` tag selected by readiness; `/health/ready` currently checks zero dependencies. Public `/health` exposes detailed diagnostics.

## Data and delivery compatibility

Tracked migrations: Init (20250817194942), RemoveLegacySeededAdmin (20260414145000), AddTaskReminders (20260416223117), AddNotificationOutbox (20261007093000). The legacy-admin migration lacks discoverable metadata/designer; confirm actual EF migration discovery before deciding a forward repair. Preserve old files/data; validate new incremental upgrades with real SQL Server where available.

PR #49's outbox behavior exists in the checkout: transactional occurrence/envelope persistence, stable IDs, concurrency tokens, expiring claims, confirm-before-sent, ack-after-processing. Consumer still lacks durable dedup/inbox and bounded retry/DLQ; broadcast+ack can duplicate on crash. Keep at-least-once semantics and existing recovery tests.

## Local tools and actual checks

Windows PowerShell; .NET SDK `10.0.401`, .NET/ASP.NET runtime `10.0.12`, Node `24.18.0`, npm `12.2.0`. `dotnet --info` prints installed versions but exits with a sandbox Service Control Manager permission exception; this does not prevent elevated build/test. `docker` and `az` are absent from PATH/standard install locations. `gh auth status` reports no CLI login; GitHub connector is available and reports repository push/admin rights.

- `dotnet restore TaskManager.sln -warnaserror:NU1901,NU1902,NU1903,NU1904`: PASS. Initial sandbox invocation exited 1 without output; standard approved execution restored all six projects with audit enabled.
- `dotnet build TaskManager.sln --configuration Release --no-restore -warnaserror:NU1901,NU1902,NU1903,NU1904`: PASS, 0 errors, 31 warnings (existing nullable and two xUnit assertions).
- `dotnet test TaskManager.sln --settings .runsettings --results-directory ./TestResults/baseline --logger "trx;LogFileName=baseline.trx" --no-build --configuration Release`: PASS, 260 passed, 0 failed, 0 skipped; runner duration 4 seconds. TRX and Cobertura stored under ignored `TestResults/baseline/`.
- Cobertura baseline: 2014/2104 lines covered (95.72%), 326/390 branches (83.58%). These are collected backend coverage values with the existing migration/generated-code exclusions; they do not cover an absent frontend or real infrastructure.

The 260 tests in the previous #49 trace are historical. Current tests use mocks/InMemory and SQLite; do not claim SQL Server, broker, container, real-AI or Azure verification from these results. Existing coverage exclusions/gates must not be weakened.

## Version references

Stable SDK 10.0.401/runtime 10.0.12 verified against [Microsoft downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). Health filtering design follows [ASP.NET Core health checks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0). Browser auth requires [ASP.NET Core antiforgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0). Verify NuGet patch availability before changing package pins.

## External limitations

No Docker runtime means container/real-SQL runs remain unverified until a suitable runner is available. No Azure CLI/authenticated subscription/deployment values, email sender or model quota is available. These do not block code/fixtures/UI/Docker/Bicep preparation. Ask for missing deployment parameters/budget once validated deployment artifacts and resource list are ready. No deployment, migration to a user's database, live AI inference or cloud spend has occurred.
