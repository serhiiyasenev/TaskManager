# Step26 — durable private notification inbox and worker delivery

## GOAL / current step

Implement authorized roadmap26 after25 acceptedatd717ffc (629realSQL,69unit+
38browser,five-image CI PASS). Keep acceptedADR0013attempts+Rabbitretry/DLX,
stableIDs,leases/publisher-confirms and existing JWT/HTTP/console compatibility.
ECC contract-first complements the ongoing TDD/security/verification workflow.
This trace is execution context, not a second roadmap.

## Agreed boundary and ownership

Root owns canonical HTTP/realtime contract and generated C#/Zod payload types,
DAL model configuration/migration, host DI/health/Compose, private hub routing
and final verification/Git. Runtime agent owns SQL inbox/processed markers,
read/mark API service/controller/entities and meaningful rollback/concurrency/
current-rights tests. Delivery agent owns shared scheduler/dispatcher, retention
worker and Rabbit/ServiceBus adapters/settlement tests. Security agent owns
frontend inbox/provider/dashboard integration and UI/contract/reconnect tests.
All C# writes (including new tests) freeze before root shared builds.

HTTP: GET /api/notifications?page=1&pageSize=20&unreadOnly=false returns scoped
items/page/pageSize/totalCount/unreadCount; counts precede pagination. PUT
/{id}/read is idempotent, owner-only with current project/team rights and CSRF.
Foreign/inaccessible records return404 even for another administrator.
Kind uses NotificationEventType0Executed/1Reminder/2Escalation/3Created, not the
different internal OutboxKind numbering. SignalR InboxChanged carries only a
notificationId; reconnect refetches canonicalSQL, client dedups IDs and aborts/
clears/stops on logout/identity change. No browser storage of private history.

## Durable processing / security constraints

Notification plus unique message processing marker/hash commit atomically in
SQL BEFORE brokerACK. Duplicate matching IDs never create/hint again; altered
body with the sameID is a permanent failure. Hash immutable received payload,
not current DB names (rename between ack-loss deliveries must not mismatch).
Project/task/currentrecipient scope is fresh; compare stored projectId to current
task projectId and validate reminder occurrence IDs/flags/state. Do not require
producer Published/SentAt, since delivery may precede its confirm bookkeeping.
Stale/reassigned occurrences are durably Suppressed thenACK. Historical IDs are
scalar, ownerFKcascade; deleted/moved/inaccessible tasks are not exposed.
Minimal processing markers outlive90d notification content, so lateDLQ replay
does not resurrect private history. SignalR is a private hint, no exactly-once
network promise. Established connections must not keep revoked scope.

## Transport/worker decisions to implement

Official verified stable ServiceBus7.21.0 and AzureSignalR1.33.1, existing
Identity1.21.0. Deployed adapters use explicit MI, no in-memory/fake fallback.
ServiceBus PeekLock/AutoComplete=false: durable processing then Complete,
bounded DeliveryCount/Abandon/dead-letter. SDK retries/timeouts bounded.

Rabbit4.3.6 main classic queues stay unchanged. New retry queues must be quorum
with at-least-once dead-lettering/reject-publish/bounded capacity, TTL/DLX route
back to main. Confirm same-ID retry/dead publication beforeACK original. No
unbounded BasicNack requeue loop or silent immutable queue-type change. Shared
topology helper avoids producer/consumer mismatch.3total business attempts per
delivery lineage; ACK-loss copies may repeat invocations, with SQLdedup required.
Sources:
- https://www.rabbitmq.com/docs/quorum-queues#activating-at-least-once-dead-lettering
- https://www.rabbitmq.com/docs/quorum-queues#poison-message-handling
- https://learn.microsoft.com/en-us/azure/service-bus-messaging/message-transfers-locks-settlement
- https://learn.microsoft.com/en-us/azure/azure-signalr/signalr-howto-authorize-managed-identity

Move the scheduler implementation into BLL/shared Notifier runtime; retain an
unregistered WebAPI compatibility wrapper for old direct tests, no API hosted
scheduler. Preserve100batch/CAS5minlease/2minpublish/occurrence invalidation.
Dispatch Created/Executed separately without mutating reminder SentAt/IDs.
Use a retention-only scoped service, avoiding inference dependencies in worker.
Legacy AddExecutedTask must commit its event/outbox atomically; preserve response
boolean semantics through the shared claim/dispatch implementation if an
immediate broker-confirm attempt is needed. No broker insideSQLtransaction.

## Next action

Root writes the canonical additive contract and constrained generation/check
script first; agents then implement disjoint modules after shared types are
ready. Run meaningful runtimeRED before behavioral fixes; rootscaffolds one
incremental inbox migration after model freeze, no old migration edits.
Azure infrastructure27 and actualfull-stack/live28 still follow this step.

## TDD checkpoint — 2026-10-11 UTC

Pinned dependency restore completed. New backend cases compiled and executed:
120 total, 119 expected failures and one anonymous-route PASS. Missing inbox,
dedup/settlement/SDK adapters, worker retention, private groups and atomic legacy
execution are explicit temporary NotImplemented checkpoints. The existing
completion endpoint instead failed meaningful stable-intent/boolean/rollback
assertions. One deleted-task fixture initially read its cascaded-away outbox
before the processor; capture the immutable delivered body before mutation.
The corrected five obsolete-event cases all reach the missing processor and
fail there (including task-deleted). No test skips, gates or analyzers disabled.
Ignored diagnostics: TestResults/notifications-red/notifications-red.trx and
notifications-corrected-red.trx. New implementation follows this checkpoint;
this intermediate RED commit is local until a working integration is verified.
