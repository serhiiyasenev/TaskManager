# Research — TaskManager

## Project Overview
TaskManager is a modular .NET 9 system for managing projects and tasks.
Architecture consists of:
- **DAL** — Entity Framework Core data access.
- **BLL** — Business logic with domain services.
- **WebAPI** — RESTful interface for external clients.
- **Notifier** — Background worker using RabbitMQ and SignalR for real-time updates.
- **Client** — User interface consuming WebAPI and SignalR.
- **Shared** — Common contracts, DTOs, and messaging models.

Technologies: C#, EF Core, MediatR, SignalR, RabbitMQ, Serilog, Docker.

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
- `/DAL/Context/TaskContext.cs`
- `/Notifier/Consumers/`
- `/WebAPI/Controllers/`
- `/BLL/Services/TaskService.cs`

## Summary
Before implementing message retry or DLX, clarify:
- How failures are detected.
- What max retry count and delay should be.
- How to track dead-letter messages.
