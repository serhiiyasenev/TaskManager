# Board drag and drop — 2026-10-19

User requested Jira-like movement between status columns.

- Extracted TaskBoard with dedicated 44px drag handles, Pointer Events/capture,
  6px activation threshold, drag preview, highlighted column and drop hints.
- Added horizontal Board edge scroll and vertical hovered-column edge scroll.
- Added Space/Enter pickup/drop, arrows/Home/End target selection, Escape/Tab
  cancellation, focus recovery and localized UK/EN instructions/announcements.
- Retained status selects as an alternative; links and ordinary page scrolling
  remain independent of the drag handles.
- Reused existing state-only PATCH, expectedVersion/CSRF, pending guards and
  error behavior. Cards move after server confirmation; rejected drops keep source.
- Added no dependencies, schema/API changes or migrations.

Verification:

- Production TypeScript/Vite build and ESLint pass. Existing 500kB bundle advisory
  remains; compressed JS approximately165kB.
- 15/15 focused component tests pass, including both locales, versioned body,
  legacy assignments, cancellation, conflict and duplicate/pending protection.
- 32/32 Playwright desktop/mobile cases pass with zero skips/retries: new mouse,
  distant-column auto-scroll, real CDP touch, keyboard/focus/cancellation plus
  existing Board/filter/history/contrast/200% text/navigation regressions.
- Synthetic preview screenshots inspected at1440x900 and390x844. Native signed-in
  admin Board updated and rendered with new instructions/handles; canceled preview.
  Automated state writes use invented API fixtures rather than local SQL.

Private evidence: `.local/board-drag-20261019/` (build/lint/browser logs and desktop/
mobile highlighted-target screenshots), `.local/board-drag-unit.log`.
No commit/push/deployment. Current local dist is live. Request complete.
