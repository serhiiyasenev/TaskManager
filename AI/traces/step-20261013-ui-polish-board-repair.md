# Interface alignment and Board repair — 2026-10-13

Owner request: modest visual polish/alignment, especially the AI form and task
filters, plus repair of the Board failure shown in the supplied screenshots.
Scope is standard because the minimal safe fix adds a public status-only
contract; the owner explicitly authorized repair in this chat and AI/plan.md.
ECC make-interfaces-feel-better and orch-fix-defect guided the work. Three agents
owned styles, Board frontend regressions and backend security regressions/review.
No dependency, migration, assignment repair, password change or Azure deployment.

## Actual defect and repair

Board submitted a full PUT for a status gesture. The existing full update checks
that the performer currently belongs to the project's team, even for admin.
Historical/departed assignments therefore failed a status gesture. A private
actual local probe confirmed task17 returns HTTP400 Error.Validation.PerformerId
with its current version; the version/state remained unchanged. Stale probes for
tasks1/20 correctly returned409, ruling out a model-binding mismatch.

Added PATCH /api/tasks/{id}/state with only State and ExpectedVersion. Missing
state/undefined enum is rejected; a current body version or strong If-Match is
required for browser and bearer callers. The service updates only State,
FinishedAt and terminal occurrence IDs. It preserves assignment, names, dates,
reminder settings/sent timestamps and other business fields. Matching no-op state
preserves the original version/FinishedAt. Existing Serializable transaction,
fresh role/team/project check, optimistic concurrency, rollback and deadlock
handling apply; full PUT assignment validation remains unchanged.

Frontend Board sends exactly state/expectedVersion through the existing
cookie/CSRF transport. Independent pending sets/ref guards prevent duplicate
updates and one card's completion unlocking another card. Failed writes preserve
the previous card/status; explicit refresh obtains current data/version.

## Visual changes

| Before | After |
|---|---|
| AI field grid stretched the Response language select beside timezone help | Field tracks align at the top; input/select/buttons have 44px height, help remains below its control |
| Filters, checkboxes and view switch wrapped in one uneven row | Responsive field grid and a separate aligned action toolbar |
| Header language label displaced the select from Sign out | Inline language label/control in workspace, same-height controls; tablet header wraps without document overflow |
| Board headings had default margins and uneven counters | Centered headings/count pills with tabular numbers, consistent card borders/radii and subtle shadows |
| Many tasks stretched empty columns across a very tall page | Bounded column height, independent vertical scrolling, named keyboard-scroll Board region with contained horizontal overflow |

Teal/Segoe UI identity, Ukrainian/English catalogs, user content, focus rings,
reduced motion and AI response-language/availability semantics are preserved.

## Evidence

- Backend regression: valid 19-case HTTP RED **15 FAIL /4 PASS**, then **19 GREEN**.
  Initial Release attempt was an infrastructure failure due to live Windows DLL
  locks, not a valid RED. Stopped only the owned supervisor before the real run.
- Backend target including existing DataIsolation and TaskAuthorizationRace:
  **63/63 PASS**, no skips. Actual SQLite transactions/HTTP factory; test-only
  principals are explicit, not represented as actual cookie/SQL evidence.
- Frontend Board real request/fetch RED **6 FAIL /4 PASS**, then **10/10 GREEN**,
  including UK/EN, CSRF, exact narrow payload, independent pending cards,
  unchanged-state guard and safe400/403/409/428 outcomes.
- Entire frontend: **18 files, 174/174 PASS**. ESLint and TypeScript/Vite build
  pass. Final dist: index-D9FY0xWf.js, index-DGc4PL0L.css. The existing Vite bundle
  advisory remains non-blocking.
- Desktop/mobile UI regression **54/54 PASS**. After final tablet/column CSS
  corrections, the six directly affected dashboard/Board/language cases also
  pass. UI fixtures are explicitly synthetic.
- Actual SQL/API/browser suite **8/8 PASS**, zero skips/flakes, run
  ca5db88ad4834b1596e65ea507f2f553. New Board case creates real owner/member accounts,
  assignment and invitation/removal, proves old PUT400 without mutation, changes
  status through actual rendered PATCH, verifies row movement/field preservation,
  stale409 and departed-member read/write404. SQL/programs/cookies/CSRF are real;
  AI/email/transport adapters are explicitly controlled for this run.
- Owned database taskmanager_e2e_test_ca5db88ad4834b1596e65ea507f2f553 was dropped;
  all owned servers stopped and cleanup verified. No test touched the product DB.
- Native application restarted with preserved key ring/credentials. Fresh real
  admin login/API still reads **20 tasks, 7 projects, 7 teams, 14 users**.
- Actual native UI geometry and visual inspection pass in both languages at
  **1440, 1024, 768, 390px**. This caught tablet header overflow before final
  delivery; it was repaired and rechecked. Header/AI controls align at44px,
  Board titles/counters align, columns are bounded and document width fits.
  Private captures and replay helper are in .local/native-app. A first cold
  login check timed out during concurrent browser load; isolated rerun verified
  actual200 and redirect with a bounded15s expectation, no private values logged.

## Delivery and replay

The running local HTTPS application at https://localhost:7151 serves the final
dist. Refresh the browser to load the new Board gesture and layout. Source
changes remain local/uncommitted, and existing business data is preserved.

Replay frontend unit/lint/build and npm run e2e. For backend use
`dotnet test Tests/Tests.csproj --configuration Release --no-restore -m:1 --filter "FullyQualifiedName~TaskStateBoardTests|FullyQualifiedName~DataIsolationTests|FullyQualifiedName~TaskAuthorizationRaceTests"`.
Stop the owned native supervisor first when rebuilding its loaded Release DLLs.
The existing scripts/fullstack/Run.ps1 Controlled mode includes the new Board
case and owns its disposable DB; private connection inputs remain out of Git.

Preserve previous historical screenshot evidence; current generated captures are
retained privately under .local/board-ui-evidence. The two English examples from
the language follow-up remain in the repository. No live Azure/model/provider
acceptance is claimed.

## ECC self-evaluation

Overall **4.8/5**: accuracy5 (actual old400 and guarded success/denial verified),
completeness5 (requested polish/Board plus retained languages/data), clarity4
(scope/evidence are explicit, but existing dense JSX could be formatted for easier
review), actionability5 (final build is running), conciseness5 (small visual pass
and a narrow command reuse existing architecture). No axis is3 or below. Minor
maintenance improvement: format dense existing JSX when it is next changed.
The user can verify the delivered interface and Board directly.
