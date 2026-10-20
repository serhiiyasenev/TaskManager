# Repository restructuring — 2026-10-20

Owner authorized switch main, pull and a coherent src/backend/src/frontend/tests
structure. main22104dc is the merged PR51 tree, identical to verified d0b9fff.
Context reset: loaded AI/AGENTS, research/decisions/plan and latest CI trace before
this new scope. Large behavior-preserving refactor; accepted implementation scope
and precise mapping are recorded in AI/plan.md and ADR004.

Impact review identified solution/project references, WAF/full-stack content-root
sentinels, generated notification outputs, root npm workspace module resolution,
Vite/Playwright configs/imports/report paths, Docker allowlist/build-copy inputs,
native/CI/azure scripts and documentation/assets. No database/API/domain change.

Official references:
https://learn.microsoft.com/en-us/dotnet/core/tutorials/testing-with-cli
https://docs.npmjs.com/cli/using-npm/workspaces/
https://docs.npmjs.com/cli/commands/npm-ci/

Baseline: GitHub gates at d0b9fff all success; main product-tree comparison empty.
Two original untracked English synthetic dashboard PNGs and private ignored runtime
files remain preserved. No commit/push or cloud action is in this request.

## Delivered structure

Seven application/library projects moved to src/backend; Tests moved to
tests/backend. Solution GUIDs/configurations, namespaces, assembly names, source
logic and migration history are preserved. React lives in src/frontend with
colocated unit tests. Playwright suites/configs live in tests/frontend. One root
npm lock/workspace replaces the frontend-only install; no existing package
version or integrity changed. Root .npmrc/.node-version govern both workspaces.
README JPGs moved to docs/assets. Maintained docs, project references, content-root
discovery, generator outputs, CI, Docker inputs/allowlist and runner cwd/report
paths were updated. Generated browser evidence stays ignored; original untracked
PNGs remain untouched. Private native launcher paths were updated separately.

## Verification on the restructured tree

- Fresh root npm ci --ignore-scripts:277 packages installed; audit0 vulnerabilities.
- Locked restore: all11 .NET projects. Release solution build:0 errors; existing
  nullable/package-pruning warnings remain. No dependency/security gate weakened.
- Contract generation check, both workspace TypeScript and ESLint checks:PASS.
- Frontend unit tests:185/185 across20 files. Coverage: statements83.45%,
  branches75.62%, functions82.51%, lines90.45%; original thresholds retained.
  Windows fork runs stalled; repository-local TEMP/thread pool completed coverage
  and an independent verbose185-case run. A single-file coverage diagnostic
  intentionally failed global thresholds; its artifacts remain private.
- Production frontend build:PASS; existing >500kB chunk advisory remains.
- Synthetic desktop/mobile Playwright:92/92,0 retries. Native SQL/Rabbit backend:
  924/924,0 skipped; fresh TRX and collector evidence checked by native launcher.
- Actual native SQL/Identity/gateway/Rabbit browser cases:12/12,0 skipped/flaky.
  Run cac1871b7a10437cb89fdcb3774d8b19. Host/database/queue cleanup verified;
  broker run c22af6edde2147e586bc397616755229 records passed/listenersRemoved=true.
- Full-stack process/privacy fixtures:9/9; Azure offline fixtures:26/26; Bicep
  foundation/jobs/apps compilation+contracts and4 corrupted role fixtures:PASS.
  Agent PowerShell AST check:38 scripts PASS; root actionlint all workflows:PASS.
- git diff HEAD --check:PASS. Residual executable/maintained-document path audit:
  no obsolete paths found. Diff review confirms application logic/schema intact.
- Private local supervisor restarted successfully. HTTP7150 and HTTPS7151 actual
  browser checks:readiness, login/register rendering, assets/favicon, anonymous401,
  CSRF200 PASS. Old HTTPS-only ad-hoc cookie helper expected an obsolete cookie
  name in this explicit Development HTTP profile; maintained924-case auth/security
  gates and actual HTTP admin login pass. No product security setting changed.
- Actual local admin reads match saved counts:20 tasks,7 projects,7 teams,14 users;
  foreign-assigned tasks and admin analytics remain readable. Credentials were
  read privately without disclosure/reset. Existing product database preserved.

Docker is unavailable locally, so container execution remains unverified for this
path refactor; Docker context/COPY paths and CI were reviewed/linted. Azure/cloud
execution is deferred by owner. Work remains local on main22104dc; no commit/push.
Raw diagnostics live in ignored .local/structure-20261020.

## ECC self-evaluation

Accuracy4: current local gates pass; container execution cannot be claimed.
Completeness4: requested structure and localhost restored; Docker needs CI/host.
Clarity4: root guide and repository map agree; historical traces retain old paths.
Actionability4: root commands work; native gates still require private SQL/tools.
Conciseness4: organization uses native workspaces/solution folders; extra governance
trace detail supports review but the user-facing summary stays short. Overall4.0.
Next improvement: verify Linux container smoke on a Docker-capable host before
publishing. User expectation check: fulfilled local restructure, limits explicit.
