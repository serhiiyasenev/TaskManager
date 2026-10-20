# Readability, compact Dashboard and mobile filters —2026-10-17

Owner accepted the four concrete polish items proposed in chat. Prior UK/EN,
authorization, versioned Board writes and local HTTP/HTTPS remain in scope.
ECC design/polish workflows and two existing agents were used for copy and
browser regressions; earlier uncommitted work was preserved.

## Changes

- Dashboard statistics are compact summaries, with tasks appearing earlier.
  Responsive columns follow available width and enlarged text. Task titles,
  deadlines, supporting labels, section links and pagination are more readable.
- Mobile advanced filters start collapsed. Search, Mine/Overdue, Apply, Clear
  and list/Board choice remain available. A labelled44px keyboard toggle exposes
  aria-controls/expanded; nondefault advanced choices have a visible count.
  Desktop controls remain visible. Drafts survive collapse, language and resize.
- Ukrainian month names retain lowercase; only the weekday's initial is raised.
  Intl.PluralRules selects correct UK/EN task-count forms, including21/22/111.
  Focused plural regressions cover fractions and locale changes too.

## Verification and evidence

- Lint: PASS, zero warnings. TypeScript/Vite: PASS; existing size advisory remains.
- Complete Vitest:180/180 in20 files,181.66s.
- Render/capture gate:16/16 at1440x900,1024x900,768x900,390x844,320x900, plus200%
  text with mobile filters both collapsed and expanded; all six content states.
- Final maintained browser gate:80/80, zero failures/skips/retries; see ignored
  `ui-final.log` and `ui-summary.json`.
- Initial browser run caught6px Dashboard overflow at320px/200% text. Responsive
  rem-based statistic columns and caption wrapping repair the actual failure;
  its original log is retained as `ui.log`.
- Root inspected desktop Dashboard, mobile collapsed Board and320px/200% expanded
  filter captures. Actual signed-in HTTP admin page was also reviewed: correct
  lowercase month, five rendered `3 завдання` project counts and original totals.
  The expired administrator session was renewed using the existing private local
  credentials; no password or other account setting changed.

Private local evidence: `.local/ui-refinement-20261017/`, including
`design-contract.md`, `evidence.json`, `capture.log`, `unit.log`, `lint.log`,
`build.log`, `ui-final.log`, screenshots and the final browser JSON report.
Synthetic UI fixtures do not establish SQL/Identity/model-provider behavior.
Actual admin inspection is separately described above. No backend code changed
for this refinement; previous backend test counts are not reported as fresh.

No dependencies, cloud deployment, commit or push. New dist is served directly by
the existing local host; business data and persistent credentials were retained.
The ECC evidence checker passes.37 historical tracked screenshots were restored
after archiving generated renders; earlier English dashboard artifacts retained.
