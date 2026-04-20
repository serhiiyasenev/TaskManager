# Plan — TaskManager

## GOAL
- Implement a robust retry and Dead-Letter Exchange (DLX) mechanism in the Notifier service.
- Deliver reminder notifications and AI-assisted reviews for task deadlines.

## Constraints
- Do not modify external REST endpoints.
- Preserve existing public API contracts and backward compatibility.
- Keep current test suite passing.
- Apply minimal changes to existing configuration structure.
- Avoid expanding configuration surface beyond reminder/queue needs.

## Unified Step-by-Step Roadmap

| # | Step | Acceptance Criteria |
|---|------|---------------------|
| 1 | Research RabbitMQ retry & DLX patterns | `research.md` updated with selected approach |
| 2 | Extend Notifier configuration for DLX/retry queues | `appsettings.json` contains retry settings |
| 3 | Implement retry logic with limited attempts | Simulated failure triggers retries then DLX |
| 4 | Add DLX consumer/logger | Failed messages recorded with metadata |
| 5 | Integrate metrics & monitoring | Grafana / App Insights counters available |
| 6 | Write integration & E2E tests | All message-flow scenarios verified |
| 7 | Human review & plan approval | `plan.md` accepted and merged |
| 8 | Implement iteratively, updating `traces/` | Each step compacted and logged |
| 9 | Observe production behaviour | Metrics show retry/DLX counts |
| 10 | Confirm each step | The user approves this manually |
| 11 | ✅ Add reminder data model, validation, and migration | Tasks include reminder fields; DTOs/validators cover offsets and due date presence |
| 12 | ✅ Implement reminder scheduling and messaging | Hosted service publishes reminder and overdue envelopes to RabbitMQ with correlation IDs |
| 13 | ✅ Update Notifier and clients for reminders | Notifier emits typed notifications; console lists/updates reminder state |
| 14 | ✅ Add Consensia AI Reviewer workflow | Workflow runs on PRs using Consensia action with guarded secrets and required permissions |
| 15 | ✅ Validate docs and tests | README reflects reminder features and automation; regression tests stay green |
