# Repository guide

Read [AI/AGENTS.md](AI/AGENTS.md) for the project workflow and
[docs/repository-layout.md](docs/repository-layout.md) for directory ownership.
Run commands from the repository root.

- Application projects: `src/backend/`; browser app: `src/frontend/`.
- Canonical .NET solution: `src/backend/TaskManager.sln` (includes tests/tools).
- .NET suites: `tests/backend/`; Playwright suites: `tests/frontend/`.
- Keep React unit tests beside the components they exercise.
- Use the root npm workspace and its single lockfile: `npm ci --ignore-scripts`.
- Preserve .NET project identities, API contracts, migration history and private
  local configuration when changing paths.
- Generated outputs, credentials and test artifacts stay outside tracked source.

Verification commands and the separate native SQL/Rabbit prerequisites are in
[TESTS.md](TESTS.md). Record actual results and limitations in `AI/traces/`.
