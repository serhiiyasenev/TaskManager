# Local development

The application targets .NET 10. Use the SDK selected by `global.json` and the
repository tool manifest. Docker uses Linux containers on x86-64 because the
SQL Server image supports that platform.

## Container setup

Docker with Compose v2 must be installed and running. These scripts do not install
tools. Allocate at least 4 GB of memory to Docker. From the repository root:

```powershell
./scripts/local-init.ps1
./scripts/local-up.ps1
./scripts/local-smoke.ps1
```

`local-init.ps1` creates an ignored `.env` with cryptographically generated local
SQL, broker and JWT credentials. It refuses to overwrite existing credentials.
Alternatively copy `.env.example` to `.env` and fill the three empty credentials.
For custom SQL passwords avoid connection-string delimiters such as semicolons.
Use `docker compose --env-file .env config --quiet` to check configuration without
printing resolved passwords. Treat `.env`, container inspection and logs as private.

Compose builds `webapi`, `notifier` and a migration bundle from the root Dockerfile.
The migration job runs once after SQL is healthy; API startup depends on its
successful completion. Application replicas do not apply migrations at startup.
The job uses the pinned repository EF tool; its build receives public design-time
configuration only. SQL credentials are provided when the job executes.

| Service | Host URL | Container address |
| --- | --- | --- |
| API / Swagger | `http://localhost:8080/swagger` | `http://webapi:8080` |
| API liveness / readiness | `http://localhost:8080/health/live`, `/health/ready` | `http://webapi:8080` |
| Notifier hub | `http://localhost:8081/chathub` | `http://notifier:8080/chathub` |
| RabbitMQ management | `http://localhost:15672` | `http://rabbitmq:15672` |
| SQL Server | `localhost,1433` | `mssql,1433` |

Ports are bound to the loopback interface. Override ports in `.env`; supply matching
`-ApiOrigin` / `-NotifierOrigin` to `local-smoke.ps1`. This is a local HTTP stack.
The production gateway, HTTPS and Azure setup are separate modernization steps.
Bootstrap admin is disabled by default. Registration creates ordinary users;
enabling an administrator requires the documented explicit bootstrap configuration.

Run the optional console client after startup:

```powershell
./scripts/local-console.ps1
```

Its Compose profile is `console`. The profile provides `Api__BaseUrl` and
`SignalR__HubUrl` for service DNS names. The legacy client originally hardcoded
localhost HTTPS URLs; its environment configuration must be implemented before
the container console can connect. The API and Notifier stack can run independently.

Stop services while retaining data:

```powershell
./scripts/local-down.ps1
```

Named volumes retain SQL data, RabbitMQ data and the application Data Protection
directory at `/home/app/.aspnet/DataProtection-Keys`. The API and Notifier mount
the product's key volume. Cookie sharing additionally needs compatible server
authentication and Data Protection configuration in the auth modernization step.
Back up existing databases before applying new migrations. Downgrading application
images does not undo database changes; review migration `Down` operations and use
a verified backup/restore procedure when a schema rollback is necessary.

The new default project name `taskmanager-local` creates distinct volumes from the
old Compose project. Existing volumes are retained. Do not attach an old RabbitMQ
3.13 volume directly to RabbitMQ 4.3: follow RabbitMQ's supported upgrade sequence
or export definitions and drain/transfer messages into a fresh broker.

## Image and configuration policy

Application images use non-root `app` (UID 1654), published binaries, SDK
`10.0.401-noble` for builds and ASP.NET/runtime `10.0.12-noble` for execution.
SQL is pinned to `2022-CU27-ubuntu-22.04`; RabbitMQ to `4.3.6-management`.
Application configuration files, `.env`, certificates, tool caches and local
outputs are excluded from Docker build context. Compose supplies environment
configuration including console logging. Local images use the `local` tag;
release pipelines must set `TASKMANAGER_IMAGE_TAG` to a commit SHA or immutable
artifact identity and deploy verified digests.

The local database uses `sa` for development. Production must use separate runtime
and migration identities with scoped SQL permissions. Notifier's current root
probe verifies HTTP availability; authenticated hub routing, durable inbox and
separate dependency readiness are later product steps. Neither probe nor a green
container proves end-to-end notification delivery.

## Verification status

On 2026-10-11, Docker was absent from the implementation environment. Image pulls,
container builds, bundle execution, SQL upgrade and the above HTTP smoke were not
run there. Static configuration review is preparation; successful deployment must
be recorded separately after running these commands on a Docker-capable host.

Official sources checked on 2026-10-11:

- [.NET Docker tutorial](https://learn.microsoft.com/en-us/dotnet/core/docker/build-container)
- [.NET 10 Ubuntu default images](https://learn.microsoft.com/en-us/dotnet/core/compatibility/containers/10.0/default-images-use-ubuntu)
- [SDK tags](https://mcr.microsoft.com/v2/dotnet/sdk/tags/list), [ASP.NET tags](https://mcr.microsoft.com/v2/dotnet/aspnet/tags/list), [runtime tags](https://github.com/dotnet/dotnet-docker/blob/main/README.runtime.md)
- [SQL Server image tags](https://mcr.microsoft.com/v2/mssql/server/tags/list)
- [RabbitMQ official image tags](https://github.com/docker-library/official-images/blob/master/library/rabbitmq)
- [EF migration bundles and deployment jobs](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying)
- [Docker Compose interpolation](https://docs.docker.com/compose/how-tos/environment-variables/variable-interpolation/)
- [Data Protection key storage](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/default-settings?view=aspnetcore-10.0)
