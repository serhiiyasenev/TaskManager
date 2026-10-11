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

## Integration checkpoints and audit

Additive migration `20261011232451_AddDurableNotificationInbox` inspected:
two inbox/marker tables, nullable internal execution intent reference, unique
filtered execution ID, bounded names/hash, owner-only cascade and marker Restrict.
Old migration files were not edited. New provider/host/private routing and legacy
commit batch:21PASS;3expected missing behaviors (independent escalation flag,
notification retention worker), plus2 test-fixture errors corrected (standalone
SDK options require IConfiguration; legacy request requires TaskName). The
corrected rollback request was executed against66d8dc2's old service and failed
expected500/actual200; current GREEN source was restored automatically in finally.

Read-only agent audit reproduced4additional RED cases: normal Development
Notifier DI failed resolving API-only IUserAccess, and3post-commit reassignment/
deleted-task/moved-project cases still emitted hints. Worker storage registration
now omits the HTTP inbox service; API keeps mandatory caller authority. Realtime
publisher queries current own task/project/membership and verifies its scope
group remains current before sending minimal hints. No SQL bypass or client
group selection was introduced. Existing Status concurrency guard was verified;
the distinct failed-confirm/deadline stale-claim case is being tested separately.

The deterministic stale-before-claim / winner-failed-confirm case reachedRED:
expected1broker invocation, actual2. Its original Pending/null snapshot bypassed
the winner's new future backoff. LockedUntilUtc now participates in EF compare-
and-swap alongside Status/LockId. This uses the existing SQL column; only the
new step26 designer/current model snapshot gained concurrency metadata. The
same batch verified17other audit/host/legacy/escalation/cleanup casesPASS. A
broader notification/reminder/auth/AI regression batch is running next; real
SQL/broker cases explicitly skip locally until disposable CI services exist.

## Local backend verification — current working sources

Full Release suite with unchanged `.runsettings`: **760PASS /0FAIL /9explicit
SQL/broker SKIP /769total**,42s. Coverage **88.99% lines(4882/5486),73.64%
branches(2378/3229)**. Locked solution restore with NU1901–NU1904 as errors PASS.
Canonical notification generator `--check` PASS. Full TRX/coverage are ignored
under TestResults/notification-full; this is local evidence, not actual SQL or
Azure verification. Both migrated legacy execution fixture failures were repaired
by seeding the matching execution ID/time/Done state; production guards and test
assertions stayed intact. The stale failed-confirm claim case nowPASS.

CI backend now provisions disposable SQL Server and RabbitMQ4.3.6. New actual
broker→migrated SQL test covers tracked application publisher confirms, a lost
ACK/channel close and broker redelivery, single durable inbox/marker/hint, an
explicit processing fault transferred through real quorum TTL, stable retry ID/
body, and three failed invocations quarantined in the durable dead queue. The
fault injection is controlled test behavior, not a claimed real SQL outage.
Its queue cleanup is limited to freshly generated loopback test queue names.
Two independent-connection SQL duplicate/read concurrency cases use the shared
strictly named migrated disposable DB helper. Their runtime results await CI.
