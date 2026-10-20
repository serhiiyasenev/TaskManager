# ADR 003 — TaskManager product modernization

Status: Proposed for review. Implementation scope explicitly authorized by the owner in the Codex chat on 2026-10-11; this file does not rewrite accepted ADR 001/002.

## Context

The checkout is already .NET 10 and contains Identity/JWT, SQL Server, RabbitMQ, transactional reminder outbox and a console client. It lacks a browser UI, durable private notification inbox, product AI and Azure delivery. Anonymous business endpoints and public broadcasts require repair before browser workflows.

## Proposed decisions

- Keep DAL/BLL and integer User/Team/Project/Task/ExecutedTask keys. Develop existing Identity; explicit JWT console scheme plus a secure browser cookie, antiforgery and server-side membership checks. Retain existing routes while using narrow mutation DTOs.
- Separate React/TypeScript/Vite frontend, database-free ASP.NET Core YARP web gateway, API and notifier/worker. Browser API/auth/hub requests share an origin. Data Protection is shared only among TaskManager components that validate its cookie; never across products.
- Preserve stable occurrence/message IDs, transactional outbox, expiring leases and publisher-confirm semantics. SQL inbox is authoritative; authenticated SignalR is a change hint. RabbitMQ remains local; Azure Service Bus is an adapter with bounded retry/DLQ, satisfying rather than replacing ADR 001's resilience intent.
- Add source-backed, expiring AI drafts using Azure OpenAI official SDK. Extraction never creates tasks; commit revalidates rights and atomically stores tasks, materialization/idempotency and outbox. Configuration/availability is explicit; deterministic fake only in controlled tests/development.
- Add incremental migrations without editing old migrations or deleting user/business data. Existing unconditional demo credentials/privileges need a targeted safe migration and explicit development seed. Runtime never concurrently migrates every replica.
- Deploy independent Container Apps and an independent SQL database via Bicep and OIDC, with product identities, external key ring, migration job, probes and telemetry. Shared resources are inputs; production spend/deployment needs actual tenant parameters and budget.

## Consequences and compatibility

Additive browser/AI/inbox contracts and configuration expand the former reminder-only plan; the owner's accepted brief explicitly requests this scope. API authorization tightens previously unsafe anonymous behavior. Existing users/hashes/roles are reused; no automatic promotion or new global Manager entitlement. Network delivery remains at least once; database dedup prevents duplicate inbox/task materialization. A revision rollback cannot reverse an incompatible database migration.

## Evidence

Baseline: `docs/modernization/baseline.md`. Execution: `AI/plan.md` steps 21–28 and `AI/traces/`. Stable versions and SDK APIs must be verified against official documentation/package registries before use. No Azure values, secrets, model availability, costs or live results are assumed.
