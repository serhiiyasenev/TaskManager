# Ukrainian and English interface — 2026-10-13

The owner requested a selectable Ukrainian/English interface. The current chat
also authorizes ECC and agent work. Azure deployment remains deferred.

## Implementation

- Added one shared reactive locale store using React's external-store subscription.
  It defaults to Ukrainian, validates uk/en, updates html.lang and persists only
  the public taskmanager.ui-locale preference. Unavailable storage still permits
  switching; cross-tab updates and invalid preferences have tested fallbacks.
- Added an accessible, labeled selector to public account pages and the workspace
  header. Mobile controls have a 44px minimum touch target.
- Reused existing paired TypeScript catalogs and added shared/work catalogs for
  remaining UI copy. Auth, teams, dashboard, projects, tasks, admin users, AI notes
  and inbox use the selected language, including safe errors, validation, status
  labels, dates, pagination and accessibility labels.
  Final source inspection also caught the browser tab title; it now switches with
  the interface and is asserted in the portable and actual browser checks.
- Kept existing API payloads, enum values, auth/CSRF/role scope and UTC conversions.
  Locale changes do not enter loader keys or reset forms/private data. Only known
  application-generated stored notices pass through copy translation. User names,
  task/project content, notes and source evidence retain their original language.
  The explicit AI response language and draft locale remain separate UI state;
  pending commits and identical outcome-recovery requests retain body/key.
- Three ECC agents owned disjoint work/admin, auth/team and AI/inbox feature files.
  Shared localization received an additional read-only privacy/access review with
  no concrete regression found. No dependency, backend or migration change.

## Verification

- Meaningful feature RED preceded GREEN: work 7 failures; auth/team 11 failures
  and two parity passes; AI/inbox six failures and one parity pass; initial core
  three failures and one pass. Windows sandbox Vite temporary-cache ENOENT runs
  were infrastructure failures, not product assertion failures. Sequential workers
  with repository-owned temporary storage completed the actual full regression.
- Complete frontend regression: **17 files, 164/164 PASS** (126 existing + 38 new).
  Includes paired key/placeholder parity, both-language rendering, stored errors,
  private invitation and input retention, filters, AI response-language separation,
  pending/idempotent saves, inbox state, blocked storage and cross-tab selection.
- Existing desktop/mobile UI regression: **52/52 PASS**. Updated the keyboard
  login path to verify the new selector first, then the original email/password
  controls. Existing focus/reduced-motion/long-content cases pass in both sizes.
- New portable bilingual Playwright case: **2/2 PASS** (desktop/mobile), covering
  actual shell selector, unchanged password/form values, translated validation,
  refresh/sign-out persistence and public-only browser storage. UI fixtures remain
  explicitly synthetic. English examples are in the UI evidence screenshots.
- ESLint and TypeScript/Vite build pass. Final Vite build emitted index-DMVxnYi6.js (547.39kB)
  and index-DImbmAUA.css. The existing bundle-size advisory is non-blocking.
- Actual local SQL-backed app tested in disposable Chrome contexts at 1440x900
  and 390x844. Real recovered admin login, global navigation/directories, task
  detail, unsaved reminder validation, profile and notes state, separate response
  language, inbox and logout all pass. Browser storage contains only selected
  locale; no credentials, cookies/tokens, notes or draft content were logged.
  Product business data was not mutated by the language checks.
- Visual inspection confirms the selector fits in desktop and mobile headers.
  Full-page Chrome stitching initially displayed an off-viewport skip link in a
  private capture; normalized viewport screenshots confirmed unobstructed controls.
  The final title correction passed the eight shared locale tests, both focused
  browser cases and both actual local desktop/mobile checks again.

## Replay and delivery

From frontend, run `npm test -- --maxWorkers=1`, `npm run lint`, `npm run build`,
`npm run e2e`. The focused bilingual case is
`npm run e2e -- --grep "interface language preserves"`.
On this owner's local machine, ignored `.local/native-app/VerifyLocaleWorkspace.cjs`
replays the actual-browser check using the private existing credential file; it
does not print credentials or save business records. Private screenshots remain
under .local. Preserve earlier step24/25/26 screenshot evidence; current complete
synthetic captures are retained under .local/locale-ui-evidence.

The running HTTPS web host serves the new dist at https://localhost:7151.
Source and trace changes remain local, uncommitted. No Azure/live-model acceptance
or new cloud deployment is claimed.

## ECC self-evaluation

Overall **4.4/5** across five axes; deliver the verified local interface.

| Axis | Score | Evidence and specific improvement |
|---|---:|---|
| Accuracy | 5 | 164 unit, 54 unique UI cases and actual desktop/mobile checks pass; claims distinguish fixtures from real local runtime. |
| Completeness | 4 | Selector, all app copy, persisted preference and retained forms are covered. Existing count templates still use fixed plural nouns; numeral-aware wording is a small copy improvement. |
| Clarity | 4 | Interface and AI response selectors have distinct labels. Several existing JSX render sections remain dense single lines; formatting would improve source review. |
| Actionability | 5 | Built interface is already served locally; the user can choose either language immediately. |
| Conciseness | 4 | One locale store and existing catalogs avoid another dependency. Similar validation wording remains in some feature catalogs and could be consolidated during later maintenance. |

Minor improvements by user impact: numeral-aware count copy, then consolidating
duplicated validation wording. No axis is 3 or below; no corrective rework gate.
Self-check: the user can verify the requested selector and persistence directly;
the assessment is consistent with the measured checks, with no live cloud claim.
