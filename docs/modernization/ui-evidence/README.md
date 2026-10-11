# Step 24 rendered UI evidence

**24/24 Playwright cases passed, 0 failures/skips/flaky results**, using the actual
built React App in installed Chrome. CSS viewports were 1440x900 and 390x844;
screenshots are full-page captures. The machine-readable result and selected
synthetic screenshots are listed in [report.json](report.json).

API responses are deliberately synthetic fixtures. This run verifies frontend
rendering, interaction and transport calls. Real API/SQL authorization,
transactions, CSRF validation and Identity cookies require the separate HTTP
tests and step 28 UI + API + test SQL E2E.

## Observed checks

- Keyboard login, visible focus and skip-to-main navigation; labelled controls
  and associated registration validation.
- Long Ukrainian content at both sizes; document overflow checks and contained
  task table/board scrolling.
- Measured status/error text contrast, task-form touch targets and actual
  reduced-motion CSS behavior.
- Task list/board filters and versioned status mutation; task form UTC conversion.
- Loading, empty, outage/retry, partial dashboard, create success, revoked owner
  rights, ended session and stale-edit review/retry.

Rendered review found and verified fixes for the missing session-expiry banner,
small mobile checkbox labels and the auth layout. Screenshot capture now starts
at document top to avoid stitching an off-viewport fixed skip link into a
full-page image. Raw Playwright output stays in ignored `frontend/test-results`;
the committed report contains no local user-profile paths.

## Reproduce

From `frontend`, after the normal production build:

```powershell
node node_modules/@playwright/test/cli.js test
```

Playwright manages a loopback Vite preview on port 4173 and disposable browser
contexts. The default channel is installed `chrome`; `TASKMANAGER_UI_BROWSER`
can select a browser installed by CI. No personal browser profile is reused.
The Windows sandbox blocked loopback on this host, so the same local CLI was
run through the approved execution mechanism without changing firewall settings.

Run the installed ECC `frontend-design-direction/scripts/check-evidence.js`
against `docs/modernization/ui-evidence/report.json` from the repository root.
Checker completed with exit code **0** and the exact output:

```text
Evidence record complete; visual correctness still requires rendered inspection.
```

The checker validates completeness and files; the rendered observations remain
the review evidence.

## Source references

- [UI contract](../ui-contract.md)
- [Playwright browser channels](https://playwright.dev/docs/browsers#google-chrome--microsoft-edge)
- [Playwright configuration](https://playwright.dev/docs/test-configuration)
- [Controlled API mocking](https://playwright.dev/docs/mock)
