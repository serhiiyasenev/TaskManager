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
