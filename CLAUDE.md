# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run Commands

```bash
# Build
dotnet build TaskManager.sln --configuration Release

# Run all tests with coverage (Cobertura format)
dotnet test TaskManager.sln --settings .runsettings --configuration Release

# Run a single test class
dotnet test Tests/Tests.csproj --filter "FullyQualifiedName~TasksServiceIntegrationTests"

# Run a single test method
dotnet test Tests/Tests.csproj --filter "FullyQualifiedName~TasksServiceIntegrationTests.GetTaskById_ShouldReturnTask"

# Run the system (three separate terminals)
dotnet run --project WebAPI
dotnet run --project Notifier
dotnet run --project Client

# Docker (includes SQL Server, RabbitMQ, WebAPI, Notifier, Client)
docker-compose up
```

## Architecture

This is a .NET 10 layered solution with five runtime projects:

- **WebAPI** -- ASP.NET Core REST API. Controllers under `WebAPI/Controllers/`, global exception handling via middleware (`WebAPI/Middleware/`), Swagger with JWT Bearer auth. Default admin credentials are configured via environment-specific seeding/configuration and must not be reused across environments.
- **BLL** -- Business logic services, AutoMapper profiles (`BLL/Mapping/`), FluentValidation validators (`BLL/Validators/`), configuration options classes (`BLL/Configuration/`). Services return `Result<T>` for operation outcomes.
- **DAL** -- EF Core with SQL Server. `TaskContext` extends `IdentityDbContext<User, IdentityRole<int>, int>`. Generic repository pattern: `IRepository<T>` / `EfCoreRepository<T>`. Entities: User, Team, Project, Task, ExecutedTask. Migrations in `DAL/Migrations/`.
- **Notifier** -- Background worker that consumes RabbitMQ messages and broadcasts to clients via SignalR (`ChatHub`).
- **Client** -- Console app that connects to WebAPI (HTTP) and Notifier (SignalR) for real-time updates.

**Message flow:** WebAPI publishes to RabbitMQ -> Notifier consumes -> SignalR pushes to Client.

## Key Patterns

- **Repository pattern** with generic `IRepository<T>`, split into `IReadRepository<T>` and `IWriteRepository<T>`
- **Options pattern** for configuration: `RabbitMqOptions`, `JwtOptions`, `PaginationOptions`
- **JWT authentication** via ASP.NET Core Identity with role-based authorization ("AdminOnly" policy)
- **Polly** resilience policies for retry with exponential backoff
- **API versioning** via Asp.Versioning (default v1.0)
- **Correlation ID** tracking via `X-Correlation-ID` header middleware

## Test Structure

Tests use **xUnit** + **Moq** + **MockQueryable.Moq**. Located in `Tests/`:
- `Tests/Unit/` -- Unit tests with mocked dependencies
- `Tests/Integration/` -- Integration tests using `DatabaseFixture` with EF Core InMemoryDatabase

`DatabaseFixture` seeds a known dataset (teams, users, projects, tasks). CI runs tests via `.github/workflows/run-tests.yml` and posts coverage to PRs.

## AI Governance (ACE-FCA)

The `AI/` directory contains governance artifacts: `plan.md` (roadmap), `research.md` (findings), `decisions.md` (ADRs), and `traces/` (action logs). See `AI/AGENTS.md` for the context management and compaction policy that AI agents should follow when performing multi-step work in this repo.
