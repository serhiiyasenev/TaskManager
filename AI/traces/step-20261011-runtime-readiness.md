# Step 22 — .NET 10 and readiness

## Changes and sources

Pinned installed/official stable SDK 10.0.401 (`global.json`, no roll-forward/preview), EF/ASP.NET/Extensions dependencies 10.0.12 in six projects and six NuGet lockfiles, EF Design private references, local EF10.0.12 and ReportGenerator5.5.11 tools. CI reads global.json, restores tools and uses locked restore with the existing NU1901–NU1904 audit gate. Sources: https://dotnet.microsoft.com/en-us/download/dotnet/10.0 ; https://www.nuget.org/packages/Microsoft.EntityFrameworkCore/10.0.12 ; https://learn.microsoft.com/en-us/dotnet/core/tools/global-json ; https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files#locking-dependencies ; https://learn.microsoft.com/en-us/ef/core/cli/dotnet .

## TDD evidence

Journey: an orchestrator must stop routing traffic when SQL is unavailable, keep liveness independent of dependencies, and receive no private SQL diagnostics.

- Tests: `Tests/Integration/ReadinessIntegrationTests.cs`. Replace only SQL dependency, retaining production registration tags; call actual HTTP pipeline.
- RED: `dotnet test Tests/Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~ReadinessIntegrationTests --logger "trx;LogFileName=readiness-red.trx" --results-directory ./TestResults/readiness-red`. 2 failed/1 passed/0 skipped: readiness200 instead of503, SQL check invocation0 instead of1. Checkpoint `5f5381e`.
- GREEN: same test target, logger/results `readiness-green`. 3 passed/0 failed/0 skipped after tagging SQL `ready` and using minimal readiness response. Checkpoint `74a1d46`.
- Test host subsequently disables unrelated reminder background work; it does not replace health checks or weaken the regression.

## Verification

- `dotnet restore TaskManager.sln --locked-mode -warnaserror:NU1901,NU1902,NU1903,NU1904`: PASS.
- `dotnet tool restore`: PASS, exact two tool versions.
- `dotnet build TaskManager.sln --configuration Release --no-restore -warnaserror:NU1901,NU1902,NU1903,NU1904 -v quiet`: PASS, 0 errors/31 existing warnings.
- `dotnet test TaskManager.sln --settings .runsettings --results-directory ./TestResults/runtime --logger "trx;LogFileName=runtime.trx" --no-build --configuration Release`: 263 passed/0 failed/0 skipped; 19s. Cobertura2014/2103 lines95.76%,326/390 branches83.58%. No exclusions/gates reduced.
- `dotnet ef migrations list --project DAL --startup-project WebAPI --configuration Release --no-build --no-connect`: confirms only Init/AddTaskReminders/AddNotificationOutbox discovered. Legacy-admin migration file is not discovered; no database was connected or changed. Forward repair required in step23, old migration files preserved.
- XML/JSON parsing and `git diff --check`: PASS. No frontend at this step, so types/lint/E2E are not yet applicable. Security findings remain tracked for step23; whole product is not deployment-ready.

## Publication and continuation

Initial push rejected by automatic approval review as external publication insufficiently authorized; the accepted brief explicitly requests small commits/draft PRs. Recheck this exact authorization before retrying; do not bypass approval. No merge is permitted.

Container artifacts reviewed: non-root multi-stage Docker targets for API/notifier/console and a single-run EF bundle; pinned .NET/SQL/broker images; no settings/secrets in build context; fresh distinct local volume namespace, key ring volume, bootstrap off, loopback ports, real SQL/HTTP probes, private unique local credentials and PowerShell local scripts. Instructions/evidence: `docs/local-development.md`. Docker runtime is absent, so actual image/container/SQL runs remain an explicit external verification gap. Console URLs still need configuration/auth in step23.

Publication authorization rechecked against accepted brief line29 and the explicit user instruction to execute it; automatic review approved the retry, and push succeeded. Draft PR may be created after the complete step22 commit. No merge.

Continue step23 auth/data-isolation, preserving outbox guarantees and old migrations. `AI/plan.md` honestly keeps container validation pending; no claim that the first container acceptance gate passed.
