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
