# Plan — TaskManager

## GOAL
Implement a robust retry and Dead-Letter Exchange (DLX) mechanism in the Notifier service.

## Constraints
- Do not modify external REST endpoints.
- Preserve backward compatibility.
- Keep current test suite passing.
- Apply minimal changes to existing configuration structure.

## Step-by-Step Roadmap

| # | Step | Acceptance Criteria  |
|---|------|----------------------|
| 1 | Research RabbitMQ retry & DLX patterns | `research.md` updated with selected approach |
| 2 | Extend Notifier configuration for DLX/retry queues | `appsettings.json` contains retry settings |
| 3 | Implement retry logic with limited attempts | Simulated failure triggers retries then DLX |
| 4 | Add DLX consumer / logger | Failed messages recorded with metadata |
| 5 | Integrate metrics & monitoring | Grafana / App Insights counters available |
| 6 | Write integration & E2E tests | All message-flow scenarios verified |
| 7 | Human review & plan approval | `plan.md` accepted and merged |
| 8 | Implement iteratively, updating `traces/` | Each step compacted and logged |
| 9 | Observe production behaviour | Metrics show retry/dlx counts |
|10 | Confirm each step | The user approves this manually |
