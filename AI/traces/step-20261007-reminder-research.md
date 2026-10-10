# Reminder reliability repair — research and plan

The owner requested the reliability fix and pull request on 2026-10-07.
Read AGENTS.md, plan.md, research.md, decisions.md and AI_GUIDE.md before implementation.

Findings: the scheduler publishes before saving task markers; publisher confirms are disabled; Notifier consumes with autoAck. A process crash can duplicate publication, and a consumer failure can lose a notification.

Plan: transactional outbox with per-occurrence IDs, optimistic concurrency and expiring dispatch claims; confirmed mandatory publication; manual consumer acknowledgment; regression tests and a documented at-least-once contract. Keep existing REST and SignalR contracts. Bounded retry/DLX and production monitoring remain later roadmap work.

Environment: no local .NET SDK. Compile and execute tests through the PR workflow; do not claim unexecuted tests passed. Research and accepted ADR files remain unchanged.
