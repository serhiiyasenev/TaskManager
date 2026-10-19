# Dark UI and daily usability —2026-10-15/16

## Request and delivery

The owner supplied qa-interview.com and a screenshot, asked for a more polished
TaskManager in that style, then asked for maximum convenience and functionality.
The reference was inspected read-only in a temporary browser tab. Its dark
canvas, charcoal surfaces, restrained monospace details and yellow emphasis were
adapted to TaskManager's actual workflows. No reference content/assets were copied.

ECC frontend-design-direction and make-interfaces-feel-better guided the design.
Existing agents provided the dashboard markup, maintained theme/usability browser
regressions, independent visual critique and code review. The root integrated and
verified the result in the shared checkout.

## Changes

- One token system across Dashboard, Tasks/list/Board, Projects, Teams, Users,
  Notifications, AI notes and account screens. Near-black canvas, charcoal panels,
  amber primary actions with dark text, readable normal/muted/status/error text,
  consistent local stroke SVGs, modest borders and shadows.
- More readable daily text, consistent44px controls, aligned dashboard statistics
  and rows, responsive forms and clearer error/disabled/read-only/focus states.
  Native date inputs use a dark color scheme. Reduced motion remains respected.
- Compact mobile navigation with an overflow cue; active items reveal themselves
  horizontally on direct routes, language changes and resize. Header labels remain
  accessible when visually hidden. Language values grow with text enlargement.
- Task filter inputs now follow browser Back/Forward. Unapplied edits survive
  view/paging changes. Clear is available with active filters and retains the
  Board/list choice and pageSize. Empty-state recovery uses the same action.
- Successful versioned Board PATCH announces the confirmed task/state, restores
  keyboard context after a move and respects a newer focus chosen during a request.
  Loaded cards stay mounted during a same-query refresh so a different card's
  newer focus survives; held PATCH and held refresh regressions cover this case.
  Out-of-range filtered pages recover after their final card moves. Switching to
  another project's team clears a foreign assignee while preserving explicit Mine
  and same-team choices. Board overdue dates now use the same semantic color as
  the list. No API/authorization contract was broadened.

The final independent code review also identified delayed unread badges widening
an already-revealed mobile item. Reveal now tracks the exact rendered badge
count, with a delayed1024-unread fixture measuring both link and badge bounds.
The reviewer confirmed both final fixes address their reported causes.

The initial render review found a320px/200% checkbox overflow and cramped login
footer spacing; both were repaired. An independent agent's mobile overflow cue
and footer observations were incorporated. Existing business content, UK/EN,
public-only locale persistence and the authorization-scoped Outlet key remain.

## Executed verification

| Gate | Current result / boundary |
| --- | --- |
| Frontend lint | PASS, zero warnings; final scoped/whole run logs below |
| TypeScript/Vite production build | PASS;175 modules; existing bundle-size advisory remains |
| Vitest |178/178 in19 files; final2026-10-16 run167.90s |
| Final affected unit rerun |26/26 across Board, workflows and security boundaries after the final two review fixes |
| Maintained Playwright UI |78/78, desktop1440x900 and mobile390x844, one worker, no retries/skips; includes10 new theme/usability cases executed in both projects |
| Additional render capture |16/16;1440x900,1024x900,768x900,390x844,320x900, long content/200% text and all six content states |
| Actual native admin session/API |PASS;20 tasks,7 projects,7 teams,14 users; foreign task/admin analytics readable |
| Actual native bilingual journeys |PASS at1440x900 and390x844; account/admin/directories, field errors and edits, locale persistence, logout and public-only storage |
| Actual native UI geometry |PASS in UK/EN at1440/1024/768/390; aligned controls/counters, bounded Board columns, keyboard scrolling and document containment |

Root visually inspected the five target sizes plus Board, login, AI review,
loading/empty/error/partial/success/permission captures. Screenshot evidence and
design contract are in ignored `.local/ui-theme-20261015/`, including
`evidence.json`, `design-contract.md`, `capture.log`, `ui-summary.json`,
`ui-regression.log`, `frontend-tests.log`, `frontend-build.log` and sanitized native
phase logs. The ECC evidence checker validates record completeness and local file
containment; it does not replace visual judgment. No manual screen-reader or
native browser zoom certification is claimed.

The78 browser cases use deliberately invented HTTP/socket data. They do not prove
real SQL/Identity/transport/model behavior; actual native read-only checks are
listed separately. No backend source changed for this UI request, so the prior
910 backend/12 full-stack verification is not presented as a fresh run. Live
Azure/OpenAI remain deferred; the UI preserves the actual availability notice.

## Local result and preservation

The running native HTTPS host serves the new `frontend/dist` at
`https://localhost:7151`. Owner SQL data and credentials were preserved. No cloud
deployment, commit or push was made. Current test-generated images were archived
under `.local/ui-theme-20261015/regression-evidence-20261016`;37 known-clean tracked
historical PNGs were restored and36 generated files moved there. The two earlier
English dashboard images and all earlier uncommitted source work were retained.

The only temporary approval-service usage-limit failure affected a read-only
native locale check on2026-10-15. After the service window reset it was retried on
2026-10-16 and passed; no verification gap remains from that failure.

## Follow-up

The owner can review the local UI after refreshing the page. The existing Vite
bundle-size advisory is a future performance improvement, not a failed build.
