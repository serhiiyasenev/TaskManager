# Backend solution location —2026-10-20

Owner requested one or several backend solutions in src/backend. Keep one
canonical TaskManager.sln there for the related eleven .NET projects. This is a
location correction within the authorized restructuring PR53.

Moved the existing solution, recalculated all project paths relative to the new
location, removed redundant virtual src/backend nesting and preserved logical
tests/tools folders. Verified all11 project GUIDs, existing build configurations
and solution GUID remain unchanged; all11 referenced project files exist.

Updated root-command documentation/agent guide, run-tests workflow and native
Windows verification runner to src/backend/TaskManager.sln. Settings/results
remain repository-root-relative. No project/package/application/database changes.
Historical traces retain the actual paths used during those runs.

Official CLI reference:
https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-sln

Locked restore and Debug solution build pass (0 errors, existing28 warnings).
Actionlint all workflows passes. HTTP integration subset runs through the relocated
solution to verify ASP.NET factory/content-root discovery. Local Release runtime
remains running. Final test and publication results follow.

Final local check:57/57 HTTP integration tests pass through the relocated
solution,0 failed/skipped; TRX under ignored TestResults/solution-location.
Project membership/path/GUID/configuration assertions, locked restore, complete
Debug build, actionlint and diff whitespace pass. Existing Release localhost
remained running. Updating PR53 with a separate location-only follow-up commit.

CI3e497c4: five workflows passed; actual-local-stack failed before browser state
at api-start. Reproduced locally in Controlled mode: InvalidOperationException
with no root solution. Verified ASP.NET10.0.12 source reads MvcTestingAppManifest
relative to CWD before ConfigureWebHost and otherwise searches for a solution:
https://github.com/dotnet/aspnetcore/blob/v10.0.12/src/Mvc/Mvc.Testing/src/WebApplicationFactory.cs

Fix: Run.ps1 starts the owned .NET child from its build directory and requires
its SDK-generated manifest. Application factories still use their validated
explicit content roots; no product behavior/security/gate relaxation. Local RED
run6b2f975cf9864e4abadd6314cf32eea4 records DB dropped, servers stopped and broker
queues deleted despite the intentionally unverified overall failed setup result.
New controlled run85a9e83208274a92bf5b37aef34796ff reaches actual host readiness;
full browser/cleanup verification is running. Runner fixtures9/9 and actionlint
pass. New CI head will verify the actual Rabbit transport as well.

GREEN:12/12 actual local Controlled-mode browser cases pass with0 skips/flaky,
including verified owned host/database/queue cleanup. Product localhost remained
ready. No product code changed in the manifest correction. Publish this fix and
observe fresh Rabbit/full-stack CI; do not treat failed prior-head CI as current.
