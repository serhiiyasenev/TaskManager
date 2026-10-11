# Container verification

The **Container smoke / Build and verify Compose** GitHub Actions check runs on
pull requests to `main`, pushes to `main`, and manual dispatch. It uses a disposable
GitHub-hosted `ubuntu-24.04` runner with Docker, Compose and PowerShell already
installed. The event commit is checked out with an immutable action revision and
without persisted Git credentials. Workflow permissions are `contents: read`.
There are no repository secrets, cloud credentials, registry pushes or deployment
environments in this job. Normal repository approval rules for fork runs apply.

The job runs `scripts/ci-container-smoke.ps1`, which reuses the local Compose
wrapper and HTTP smoke script. It generates fresh SQL, RabbitMQ and JWT secrets,
masks them before executing containers, and writes a private `.env` beneath
`RUNNER_TEMP`. That file is outside the Docker build context. It does not print
resolved configuration, environment files, complete container inspection or
application logs, and uploads no artifacts containing runtime configuration.
Bootstrap admin remains disabled. SQL `sa` is limited to the isolated disposable
development database created by this run; production identities are separate.

## What the check verifies

- All five current Docker targets build: web gateway, API, Notifier, migration bundle and console.
  Locked restores and NuGet audit failures retain the Dockerfile's normal gates.
- Every built image declares UID 1654 and executes a command as that user.
  The running API and Notifier also execute as UID 1654 and can write their mounted
  Data Protection key directory.
- SQL and RabbitMQ reach Compose health, the migration bundle exits successfully,
  and SQL migration history contains the latest migration present in the checkout.
- The API returns 200 for liveness/readiness; Notifier returns 200 for its root.
  Anonymous task requests and SignalR negotiation return 401 without redirects.
- Mailpit returns HTTP 200 and accepts SMTP connections from the API network.
- The console image starts its normal menu and exits with option 13.
- HTTPS gateway serves the built SPA and its JavaScript asset, proxies API and hub
  errors, returns a Secure antiforgery cookie and excludes private queries from
  gateway logs. A generated self-signed development certificate is accepted only
  for localhost requests inside this disposable CI adapter.

The script removes its Compose project, volumes and temporary environment file in
`finally`. Cancellation can interrupt cleanup; the GitHub-hosted VM is discarded.
Images are tagged with the checked-out event commit SHA and are never published.
No shared host, user database or existing developer `.env` is used by this adapter.

## Recording evidence

Record the successful Actions run URL and event SHA in the delivery trace before
claiming container runtime verification. The earlier four-image gate passed before
gateway addition; the five-image HTTPS extension requires its own successful run.
A committed workflow proves that the
gate exists; it does not prove a passing run. Docker was unavailable in the local
implementation workspace on 2026-10-11, so the new gate was checked statically
there. The first runner result remains required.

This smoke covers startup and deployment prerequisites. Browser HTTPS cookies,
account confirmation, private notification delivery, durable messaging and Azure
deployment require their own integration evidence. Unit/integration tests and
governance validation remain separate required checks. Branch protection must
require the container check if it is to block merging.

For local use, follow [local development](local-development.md) and
[HTTPS web gateway](web-gateway.md). The CI adapter
refuses non-GitHub or non-Linux execution because it deletes its disposable data.

## Official references

- [GitHub Actions secure use](https://docs.github.com/en/actions/reference/security/secure-use)
  describes immutable action pins, runner isolation and privileged trigger risks.
- [Checkout v7.0.1 revision](https://github.com/actions/checkout/commit/3d3c42e5aac5ba805825da76410c181273ba90b1)
  is the pinned checkout action.
- [Ubuntu 24.04 runner inventory](https://github.com/actions/runner-images/blob/main/images/ubuntu/Ubuntu2404-Readme.md)
  lists Docker, Compose and PowerShell; the script checks Docker availability at runtime.
- [Compose startup and wait](https://docs.docker.com/reference/cli/docker/compose/up/)
  defines waiting for running/healthy services.
- [GitHub runner variables](https://docs.github.com/en/actions/reference/workflows-and-actions/variables)
  defines the hosted-runner guard and temporary directory.
- [GitHub value masking](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-commands#masking-a-value-in-a-log)
  documents registering sensitive values before they can appear in logs.
