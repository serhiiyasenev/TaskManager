# Repository layout

Use the repository root as the working directory for the commands below.
Directory changes preserve .NET assembly names, namespaces, API routes and the
existing database schema and migration history.

| Location | Responsibility |
| --- | --- |
| `src/backend/WebAPI` | Identity, authorized API requests and business writes |
| `src/backend/Notifier` | Notification scans, outbox delivery and private realtime |
| `src/backend/WebHost` | React hosting and YARP API/hub gateway |
| `src/backend/BLL` | Business services, validation and authorization |
| `src/backend/DAL` | EF Core entities, repositories and existing migrations |
| `src/backend/Hosting` | Shared hosting and cloud configuration |
| `src/backend/Client` | Existing JWT console client |
| `src/frontend` | React application, public assets and colocated unit tests |
| `tests/backend` | .NET unit, integration, transport and cloud fixtures |
| `tests/frontend` | Playwright synthetic UI and actual full-stack browser suites |
| `tools` | SQL bootstrap, isolated full-stack host and AI evaluation runner |
| `scripts` | Contract generation, local setup, verification and delivery |
| `contracts` | Canonical notification OpenAPI schema |
| `infra` | Deployment definitions and contracts |
| `docs` | Maintained runbooks; screenshots and diagrams in `docs/assets` |
| `AI` | Plans, decisions and historical execution evidence |

## Dependencies and verification

`global.json` pins .NET. Root `.node-version`, `.npmrc` and `package.json` pin
Node/npm behavior. Root `package-lock.json` covers `taskmanager-web` and
`@taskmanager/browser-tests`; install both workspaces together:

```powershell
npm ci --ignore-scripts
npm run contracts:check
npm run lint
npm run typecheck
npm run test:unit
npm run test:coverage
npm run build:frontend
npm run test:e2e
dotnet restore TaskManager.sln --locked-mode -warnaserror:NU1901,NU1902,NU1903,NU1904
dotnet build TaskManager.sln --configuration Release --no-restore
dotnet test TaskManager.sln --settings .runsettings --configuration Release --no-build
```

`npm run dev:frontend` starts Vite. The production UI is `src/frontend/dist`;
coverage is `src/frontend/coverage`; browser results are
`tests/frontend/test-results`. Dependencies and binaries resolve from root
`node_modules`. These generated directories are ignored.

`npm run test:fullstack` invokes the browser workspace with an already prepared
private run state. Use the [full-stack runner](fullstack-verification.md) to own
the SQL database, actual hosts, private state and verified cleanup. Ordinary UI
fixtures do not prove real SQL, authentication or delivery behavior.

Secrets and local runtime configuration stay in ignored `.local/` or explicitly
configured environment variables. Historical evidence retains the paths that
existed when it was recorded; use maintained runbooks for current commands.
