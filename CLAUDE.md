# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run Commands

```bash
# Build
dotnet build src/backend/TaskManager.sln --configuration Release

# Run all tests with coverage (Cobertura format)
dotnet test src/backend/TaskManager.sln --settings .runsettings --configuration Release

# Run a single test class
dotnet test tests/backend/Tests.csproj --filter "FullyQualifiedName~TasksServiceIntegrationTests"

# Run a single test method
dotnet test tests/backend/Tests.csproj --filter "FullyQualifiedName~TasksServiceIntegrationTests.GetTaskByIdAsync_ExistingId_ReturnsTask"

# Run the system (three separate terminals)
dotnet run --project src/backend/WebAPI
dotnet run --project src/backend/Notifier
dotnet run --project src/backend/Client

# Prepare and verify both npm workspaces from the repository root
npm ci --ignore-scripts
npm run contracts:check
npm run lint
npm run typecheck
npm run test:coverage
npm run build:frontend
npm run test:e2e

# Docker with Compose v2, Linux x86-64 containers (PowerShell)
./scripts/local-init.ps1
./scripts/local-up.ps1
./scripts/local-smoke.ps1
# Optional console profile
./scripts/local-console.ps1
# Stop services while retaining data volumes
./scripts/local-down.ps1
```

Docker builds use pinned .NET 10 images and run published application binaries as
the non-root `app` user. Compose runs a migration bundle before WebAPI startup,
keeps bootstrap admin disabled, and supplies credentials from ignored `.env`.
See `docs/local-development.md` for ports, retained volumes and the precise
verification status. Docker execution requires a Docker-capable host.

## Architecture

The .NET 10 applications and libraries live in `src/backend/`; the React app
and its colocated unit tests live in `src/frontend/`. .NET suites are in
`tests/backend/`, while Playwright suites/configuration are in `tests/frontend/`.
The root npm workspace owns the single lockfile. See
`docs/repository-layout.md` for the complete map and execution commands.

- **WebAPI** -- ASP.NET Core REST API. Controllers under `src/backend/WebAPI/Controllers/`, global exception handling via middleware (`src/backend/WebAPI/Middleware/`), Swagger with JWT Bearer auth. Admin bootstrap is runtime-configurable via `BootstrapAdmin` settings in `src/backend/WebAPI/appsettings*.json` and disabled by default (no hardcoded default credentials).
- **BLL** -- Business logic services, AutoMapper profiles (`src/backend/BLL/Mapping/`), FluentValidation validators (`src/backend/BLL/Validators/`), configuration options classes (`src/backend/BLL/Configuration/`). Services return `Result<T>` for operation outcomes.
- **DAL** -- EF Core with SQL Server. `TaskContext` extends `IdentityDbContext<User, IdentityRole<int>, int>`. Generic repository pattern: `IRepository<T>` / `EfCoreRepository<T>`. Entities: User, Team, Project, Task, ExecutedTask. Migrations in `src/backend/DAL/Migrations/`.
- **Notifier** -- Background worker that consumes RabbitMQ messages and broadcasts to clients via SignalR (`ChatHub`).
- **Client** -- Console app that connects to WebAPI (HTTP) and Notifier (SignalR) for real-time updates.
- **WebHost** -- ASP.NET Core YARP gateway for the built React application, API and authenticated hub.
- **Hosting** -- Shared hosting, cloud configuration and Data Protection support.

**Message flow:** WebAPI publishes to RabbitMQ -> Notifier consumes -> SignalR pushes to Client.

## Key Patterns

- **Repository pattern** with generic `IRepository<T>`, split into `IReadRepository<T>` and `IWriteRepository<T>`
- **Options pattern** for configuration: `RabbitMqOptions`, `JwtOptions`, `PaginationOptions`
- **JWT authentication** via ASP.NET Core Identity with role-based authorization ("AdminOnly" policy)
- **Polly** resilience policies for retry with exponential backoff
- **API versioning** via Asp.Versioning (default v1.0)
- **Correlation ID** tracking via `X-Correlation-ID` header middleware

## Test Structure

Tests use **xUnit** + **Moq** + **MockQueryable.Moq**. Located in `tests/backend/`:
- `tests/backend/Unit/` -- Unit tests with mocked dependencies
- `tests/backend/Integration/` -- Integration tests using `DatabaseFixture` with EF Core InMemoryDatabase

`DatabaseFixture` seeds a known dataset (teams, users, projects, tasks). CI runs tests via `.github/workflows/run-tests.yml` and posts coverage to PRs.

## AI Governance (ACE-FCA)

The `AI/` directory contains governance artifacts: `plan.md` (roadmap), `research.md` (findings), `decisions.md` (ADRs), and `traces/` (action logs). See `AI/AGENTS.md` for the context management and compaction policy that AI agents should follow when performing multi-step work in this repo.
