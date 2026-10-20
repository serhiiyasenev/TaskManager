# Administrator notification overview and structure PR —2026-10-20

Owner confirmed individual notifications work and authorized All notifications
for admins plus a PR containing the entire new repository structure. Branch:
codex/repository-layout-admin-notifications, from main22104dc. Existing structure
and compact-layout local work carried forward; two preexisting untracked synthetic
dashboard images remain excluded. Private runtime/credentials remain ignored.

ECC security-review/git-workflow applied. Backend/contract and frontend-test
agents worked in separate files; an independent agent reviewed access, privacy,
DTO generation and CI/Docker/workspace paths without actionable findings.

Implemented GET /api/notifications/all: AdminOnly policy and current database role
required; bounded paging/filter/order/counts; current task/project/recipient joins
across teams; retained reassignment history; recipient ID/display name and their
read timestamp. Orphans omitted. Separate strict schemas generated from the
canonical OpenAPI with exact-five-name generator allowlist. No schema migration.

Admin-only bilingual My/All mode uses independent scoped component and explicit
refresh. Personal provider, unread badge, live hub routing and mark-read remain
personal. Admin rows have no mutation controls. Scope changes unmount/abort;
401/403 hides cached rows and refreshes identity. Test agent identified denied
cache resurrection during retry; a persistent block until fresh success fixes
403→pending→503 and has direct regression coverage. Personal copy now describes
assigned tasks clearly.14 focused unit cases pass;4 focused admin browser cases
pass;22 new backend executions included in full native suite. Further gate results
and PR identity follow below.

Verification: locked restore and Release solution build PASS (existing nullable/
package-pruning warnings remain); canonical generation, both workspace type/lint
checks PASS. Native backend946/946, no skips; actual SQL/Identity/gateway/Rabbit
browser12/12 run92daf5514233441bb1890f582f89e924 with owned host/database/queue
and broker/listener cleanup PASS. Frontend199/199, coverage statements83.84%,
branches76.48%, functions83.03%, lines90.75%; original thresholds retained.
All workflow actionlint PASS. Final full UI/local-smoke/PR results follow.

Final local gates:100/100 synthetic desktop/mobile UI pass (no retries), alongside
946 backend/199 unit/12 actual full-stack cases. Updated local admin API/UI smoke
passes against product DB:4 global notifications with named recipients; no read
markers or personal unread counts changed by overview inspection. HTTP localhost
is ready. Final staged artifact boundary passed; no credentials/generated outputs
or preexisting untracked PNGs are included. No schema/migration changes. Docker
execution awaits GitHub Linux container gate; Azure remains deferred.
ECC self-check: access/privacy tests and independent review support correctness;
all local gates pass, paths are documented, changes are reviewable. Remaining
limit is actual container execution, to be observed in PR CI. PR publish follows.
