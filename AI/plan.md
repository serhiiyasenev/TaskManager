# Plan — TaskManager

## GOAL
Deliver reminder notifications and AI-assisted reviews for task deadlines.

## Constraints
- Preserve existing public API contracts and backward compatibility.
- Keep current test suite passing.
- Avoid expanding configuration surface beyond reminder/queue needs.

## Step-by-Step Roadmap

| # | Step | Acceptance Criteria  |
|---|------|----------------------|
| 1 | ✅ Add reminder data model, validation, and migration | Tasks include reminder fields; DTOs/validators cover offsets and due date presence. |
| 2 | ✅ Implement reminder scheduling and messaging | Hosted service publishes reminder and overdue envelopes to RabbitMQ with correlation IDs. |
| 3 | ✅ Update Notifier and clients for reminders | Notifier emits typed notifications; console lists/updates reminder state. |
| 4 | ✅ Add Consensia AI Reviewer workflow | Workflow runs on PRs using Consensia action with guarded secrets and required permissions. |
| 5 | ✅ Validate docs and tests | README reflects reminder features and automation; regression tests stay green. |
