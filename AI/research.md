# Research — TaskManager

## Project Overview
TaskManager is a modular .NET 9 system for managing projects and tasks.
Architecture consists of:
- **DAL** — Entity Framework Core data access.
- **BLL** — Business logic with domain services.
- **WebAPI** — RESTful interface for external clients.
- **Notifier** — Background worker using RabbitMQ and SignalR for real-time updates.
- **Client** — User interface consuming WebAPI and SignalR.
- **Tests** — Unit and Integration tests.

Technologies: C#, EF Core, MediatR, SignalR, RabbitMQ, Serilog, Docker, Grafana.

## Current Observations
- WebAPI publishes task events into RabbitMQ.
- Notifier listens and sends live updates via SignalR.
- Logging handled through Serilog; configuration via `appsettings`.
- Solution is layered, clean, and testable.

## Risks / Questions
- Message delivery reliability in Notifier (retries? DLX?).
- Exception handling and monitoring of failed messages.
- Scaling — how multiple Notifier instances coordinate.
- Long-term data consistency between DB and message queues.

## Related Code Areas
- `/DAL/Context/`
- `/BLL/Services/`
- `/Notifier/`
- `/WebAPI/Controllers/`
- `/Client/Services/`
- `Tests/`

## Summary
Before implementing any feature, clarify:
- How it works.
- How it should workd.
- How failures are detected.
- What max retry count and delay should be.
- How to track dead-letter messages.
- How to handle exception.
- What kind of interface should be and what properties.

## Local verification findings — 2026-10-13

Meticulous owner-authorized testing reproduced scope caches surviving refreshed
roles/membership, missing privacy headers after Response.Clear, stale admin
directory claims, nonempty project DELETE500, native SQL status deadlock500,
project/user permission changes at write boundaries, Identity concurrency500,
and clipped form glyphs at200% text enlargement. New behavioral regressions
precede the corrections. SQL migration fixtures contain historical executed
tasks, so preservation assertions compare their initial rows instead of
assuming an empty table. Playwright wrapping-label text includes a textarea's
initial content; actual edit scenarios must use its label prefix reliably.

The current Windows environment provides native SQL Server, Chrome, .NET10 and
portable RabbitMQ; Docker is absent. Controlled AI/email responses are explicit
test fixtures. Live Azure/model/provider gates remain deferred by the owner.
See TESTS.md and the current thorough-local-verification trace for commands,
exact results, coverage, isolated-resource cleanup and remaining limits.

## UI reference — 2026-10-15

Read the supplied qa-interview.com reference in the browser and inspected its
rendered screenshot. It uses a near-black canvas, charcoal sidebar/panels, thin
neutral borders, yellow call-to-action and active-nav markers, monospace small
labels/counters and ordinary readable body text. Adapt these visual properties to
TaskManager's repeated work flows, retaining its own brand/data/navigation.
System fonts and local SVG stroke icons suffice; no asset/dependency service or
external screenshot/source upload is required. Current all-feature JSX has no
inline color literals, so shared CSS tokens can migrate the whole product.

## Board dragging — 2026-10-19

The Board already has a state-only versioned PATCH command with CSRF, pending
guards, reload and focus recovery. Reuse it for drag drops. Browser Pointer Events
and pointer capture support mouse, touch and pen without adding a dependency;
edge scrolling is scoped to the Board and hovered column. Existing status selects
provide a single-pointer alternative. Synthetic Playwright verifies actual mouse
and CDP touch input; no SQL write is needed for regression verification.

## Repository layout — 2026-10-20

Merged main22104dc is the pre-refactor baseline. Existing .NET identities and
migrations survive moves to src/backend and tests/backend. React unit tests stay
colocated; Playwright suites/configs move to tests/frontend with their own npm
workspace. Root npm lock migration changed no existing dependency version or
integrity. Full-stack discovery/content roots, reporter cwd, Docker COPY/allowlist,
CI and private native launcher paths must agree with the new layout. Updated
maintained commands are in docs/repository-layout.md and TESTS.md; historical
execution evidence retains its original paths.
