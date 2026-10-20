# Step 21 — baseline (2026-10-11)

Owner explicitly accepted the supplied TaskManager brief and requested agents/ECC. Starting clean `main` at `eb5dd4c58c4c9f96635a627b3f01605ad53e5079`; connector verifies default `main` and repository access. Branch `feature/ai-step-21-modernization` follows repository naming. Three agents read/audited security, runtime and delivery; no baseline production changes.

Loaded governance/accepted ADRs/latest reminder traces. Expanded the sole `AI/plan.md` with authorized steps 21–28; preserved accepted research/decisions and created proposed ADR003. Findings and actual commands: `docs/modernization/baseline.md`.

Restore with NU1901–NU1904 errors: PASS. Release build: PASS, 0 errors/31 existing warnings. Existing suite with repository coverage settings: 260 passed/0 failed/0 skipped (4 s). Artifacts: ignored `TestResults/baseline/`. Docker/Azure CLI absent; SQL/broker/container/live-AI/deployment not validated.

ECC tdd-workflow/security-review/verification-loop read by coordinator; agents additionally used dotnet-patterns/production-audit/deployment-patterns. No runtime ECC dependency added.

Next action: step 22 — pin verified stable SDK/packages/tool, build non-root images/Compose, add failing readiness HTTP tests (SQL unhealthy 503, healthy 200, liveness independent), checkpoint RED then fix/tag/verify GREEN. Security migration metadata/demo seed issues remain for a safe forward migration in step 23. Do not weaken coverage/audit or edit old migrations. Then proceed to authenticated caller/data isolation, browser Identity flow and protected hub.
