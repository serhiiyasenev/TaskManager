# TaskManager UI contract — step 24

This is a design/test contract for the sole roadmap in `AI/plan.md`, not another
implementation plan. Existing product references are the real task states,
reminder fields, scoped team/project APIs and confirmed-email Identity flows.

## User job and hierarchy

Team members need to find their next task, update its state and understand the
deadline. Start on the working dashboard after login. Primary action: create a
task in an accessible project. The list/board switch preserves filters. Team
owners can manage invitations; ordinary members see safe read/leave actions.

Use a calm, compact operations interface: ink text, warm neutral canvas, white
surfaces, teal primary actions and amber/red deadline cues with text labels.
Navigation, page heading, filters and content have clear hierarchy. A restrained
vertical project accent and overdue date rail are the recurring visual detail.
No decorative imagery or marketing hero is needed for this data-oriented tool.
System fonts keep Cyrillic/Latin support and avoid remote font dependencies.

Target viewports: **390x844 and 1440x900**. Mobile navigation and forms remain
usable at 320px; tables may scroll within their own named region. Board columns
keep readable widths with horizontal scrolling. All controls have labels,
visible keyboard focus and adequate touch targets. Status uses text as well as
color; reduced motion disables unnecessary transitions.

## Behavior and content states

| State | Trigger | Visible response and next action |
| --- | --- | --- |
| Loading | Fetch/mutation in progress | Stable content skeleton or progress text; submit disabled only while pending |
| Empty | No teams/projects/tasks or no matching filters | Explain what is absent; create permitted entity or clear filters |
| Error | Network/API/validation failure | Ukrainian explanation, safe retry, field errors and correlation ID when supplied |
| Partial | One dashboard section fails | Keep successful sections visible; retry the failed section |
| Success | A mutation commits | Confirmation plus refreshed server data; links to the affected object |
| Permission/session | 401/403, revoked membership, restricted action | Session-expired sign-in flow or access explanation; no unauthorized cached directory |
| Conflict | Stale task version | Preserve unsaved input, load current record and explicitly retry after review |

## Surfaces and trust contract

- Auth routes: register/confirmation/login/forgot/reset; sign-out, profile/password.
  Tokens appear only as recovery-link input; no browser token storage. API cookie
  is HttpOnly/Secure; requests stay same origin and mutations send antiforgery.
- Dashboard: current user's tasks, deadlines, project/team context.
- Projects: accessible lists/details and real create/edit/delete with author/admin
  controls; teams and members from scoped server DTOs.
- Tasks: list and board, search/project/state/assignee/deadline filters, sorting,
  pagination, validated forms and accessible status changes. Existing states:
  ToDo, InProgress, Done, Canceled. Explicit local input converted to UTC.
- Teams: create, detail/members, owner invitations/revoke/remove, voluntary leave;
  copyable invitation links and confirmed-email acceptance. No fake send status.
- AI Notes and notification inbox are completed with their real contracts in
  steps 25/26; never fabricate tasks or notifications as production content.

React + TypeScript + Vite + Tailwind + React Router + Zod; exact stable versions
verified from official npm registry on 2026-10-11 and fixed in package-lock.
Node 24.18.0/npm 12.2.0 match the available runtime. Ukrainian messages use a
translation dictionary with English support prepared. Zod validates server
responses; authorization is always enforced on the server.

The separate ASP.NET/YARP web host serves built assets and proxies API/hub paths;
it has no DAL/business DB reference. Forwarded headers are trusted only from
configured proxies/networks. Local browser transport uses HTTPS. Rendered
Playwright captures/state/accessibility evidence must be collected before passing
the ECC frontend-design-direction finish gate; source inspection is insufficient.

## Primary documentation checked

- [React](https://react.dev/learn)
- [Vite](https://vite.dev/guide/)
- [Tailwind with Vite](https://tailwindcss.com/docs/installation/using-vite)
- [React Router](https://reactrouter.com/start/declarative/installation)
- [YARP](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/getting-started?view=aspnetcore-10.0)
