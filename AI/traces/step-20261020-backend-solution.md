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
