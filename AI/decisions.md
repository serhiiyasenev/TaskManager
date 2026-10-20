# Architecture Decision Records (ADRs) — TaskManager

## ADR 001 — Use RabbitMQ DLX + Retry for Message Resilience
**Context**  
Notifier sometimes fails to process messages due to transient errors.

**Decision**  
Adopt RabbitMQ retry queue mechanism (3 attempts) and a Dead-Letter Exchange (DLX) for unprocessable messages.

**Status**  
Accepted (2025-10-10)

**Consequences**  
- Adds retry/dlx configuration in Notifier.
- Requires logging failed messages.
- Slightly increases queue complexity.
- Enables reliable message delivery under load.

---

## ADR 002 — Introduce ACE-FCA Process Artifacts
**Context**  
AI-assisted coding and automation require structured context management.

**Decision**  
Include `AGENTS.md`, `research.md`, `plan.md`, `decisions.md`, and `traces/` to formalize workflow.

**Status**  
Accepted

**Consequences**  
- Improves reproducibility of agent work.
- Enables human review at higher abstraction level.
- Requires consistent updates of artifacts.

## ADR 003 — Revalidate caller scope before committing project/account writes

**Context**
Actual command-boundary regressions show that initial membership/admin checks
alone allow writes after rights change. Independent native SQL competitions
also reproduce EF-wrapped deadlocks returning500 instead of409.

**Decision**
Use a caller-scoped Serializable transaction for project/account writes and
re-read persisted caller rights before commit. Roll back the complete mutation
on access loss; keep existing initial visibility/author/self guards. Preserve
explicit self-deletion using its original authenticated identity because its
own row is absent at the final scope read. Typed SQL1205 and EF concurrency
conflicts become409, without treating arbitrary failures as retryable. Tasks
retain their existing bounded two-attempt versioned transaction. Direct legacy
repository tests keep the null-access constructor path; HTTP DI resolves the
required access/context.

The frontend remounts protected pages when user ID, team or canonical roles
change, discarding former-scope caches, requests and forms. Locale changes do
not alter this key, so bilingual editing remains continuous. API no-store,
no-referrer and correlation headers are applied OnStarting and survive error
response clearing. Input/select height grows with text size while retaining
the normal44px layout.

**Status**
Accepted within the owner's thorough local test/fix request. Verified behavior
and external gates are recorded separately in the trace and TESTS.md.

## ADR 004 — Group application sources and tests in a single repository

**Decision (owner-authorized 2026-10-20)**
Use src/backend for the existing .NET project boundaries, src/frontend for React,
tests/backend for .NET tests and tests/frontend for browser suites. Retain frontend
unit tests beside their features. Keep root solution/toolchain/Compose/CI entry
points; tools, scripts, contracts, infrastructure and documentation have dedicated
directories. A private root npm workspace and one lock govern the frontend and
browser-test packages. Preserve all existing dependency versions, assembly names,
project identities, namespaces, interfaces, migrations and security defaults.

**Rationale**
Separate product source, executable test suites and operational tools while
keeping root-level build commands discoverable. Native .NET solution folders and
npm workspaces provide the organization without an additional orchestration stack.
Browser tests can resolve the same hoisted tools/contracts without copying schemas
or maintaining a second dependency graph. Rollback is the inverse path mapping;
no schema/data migration is part of this change.

## ADR 005 — Read-only administrator notification overview

Owner-authorized2026-10-20: add GET /api/notifications/all and an admin-only
bilingual mode. Require both AdminOnly policy and fresh database-backed admin
scope. Return retained notifications joined to existing tasks/projects/recipients
across teams, with recipient identity and their read status. Reassigned-recipient
history remains inspectable; current task/project names and links are returned.
The existing personal inbox, read command, hub routing and unread badge stay
personal. No global broadcast, new persisted read markers or migration is needed.
Admin overview is explicitly refreshed and read only; denied requests clear UI
visibility of cached private data, including during retries. This avoids changing
another person's read state simply because an administrator reviewed delivery.

### ADR004 amendment — canonical backend solution,2026-10-20

At the owner's request, relocate the single solution to
src/backend/TaskManager.sln. Its11 project identities/configurations remain stable:
seven backend applications/libraries, one backend test project and three .NET
tools. External tests/tools use relative paths; virtual src/backend solution
folders are removed because the solution is already at that boundary. Keep root
commands explicit and update CI/native-runner references to this sole solution.
