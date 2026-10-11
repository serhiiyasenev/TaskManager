# Step 25 AI rendered UI evidence

**14/14 Playwright cases passed, with 0 failures, skips or flaky results** in
installed Chrome against the actual production React build. The two CSS
viewports were 1440x900 and 390x844. [ai-report.json](ai-report.json) records the
run, observations and 11 selected full-page screenshots.

Responses are explicitly synthetic API fixtures. This gate verifies rendered
UI and real frontend transport behavior; it does not prove live Azure output,
Identity cookies, server authorization, SQL transactions or receipt retention.
The page visibly identifies the fixture provider as a test mode.

## Observed workflows

- Proposals remain unchecked; human edits and source quotations stay separate.
- Explicit partial commit locks materialized rows and uses the returned version
  for a later batch with a new key.
- A lost committed response freezes edits. An identical body and key recover
  the receipt with fresh frontend CSRF; double click sends one initial commit.
- Loading, empty, malformed-response, service-outage and unavailable-provider
  states retain safe explanations and manual work.
- Commit HTTP 403 hides current member/project options, refreshes session data
  and retains human edits while saving is disabled.
- Labelled controls, distinct review/table landmarks, visible keyboard focus
  and contained mobile table scrolling were checked.

Rendered review found raw internal explanation codes, duplicate landmark names
and an overflowing confirmation badge. The final run verified human Ukrainian
explanations, distinct table naming and scoped badge wrapping. English copy is
prepared; a full English rendered workflow was not run. Expiry, conflict,
validation, deletion and draft resume remain in the component-test scope.
The earlier [step 24 report](report.json) records its separate shared-interface
contrast, touch-target and reduced-motion checks; this AI subset did not repeat
those measurements.

## Reproduce

After the normal frontend build, from `frontend`:

```powershell
node node_modules/@playwright/test/cli.js test ui-ai.spec.ts
```

Playwright manages loopback Vite preview port 4173 and disposable Chrome contexts;
no personal browser profile is reused. Raw output stays in ignored
`frontend/test-results`. Committed fixtures use invented actors and notes.

From the repository root, the installed ECC
`frontend-design-direction/scripts/check-evidence.js` was run against
`docs/modernization/ui-evidence/ai-report.json`.

It exited **0** with the exact output:

```text
Evidence record complete; visual correctness still requires rendered inspection.
```

The checker validates report completeness and local artifact files. Screenshot
inspection and interaction assertions supply the rendered observations.
