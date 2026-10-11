# Durable private notifications

The accepted roadmap is [AI/plan.md](../AI/plan.md), step 26. Notification IDs are
stable GUIDs; task, project and user IDs retain the existing integer contract.
Event types retain their wire values: completed `0`, reminder `1`, escalation `2`,
created `3`. These values differ from the persisted outbox kind values.

## Delivery boundary

Task mutations persist their notification intent in the same SQL transaction.
The shared worker claims pending outbox rows with an expiring five-minute lease;
each broker publication has a two-minute outer timeout. Publication reuses the
exact stored body and ID after uncertain broker confirmation or a failed SQL
save. The dispatcher never rewrites historical task/project names.

The API does not host the scheduler. Notifier runs one shared scheduler per
process: outbox polling every five seconds, reminder scans at the configured
interval, batches of at most 100. Disabling reminders stops reminder generation;
created/completed intents continue to dispatch. Replica coordination uses SQL
claims and concurrency guards rather than an in-process lock.

The consumer validates strict UTF8, a maximum 64 KiB body, matching broker/body
IDs, the structured schema and canonical stored intent. Current task assignment,
project team and reminder occurrence are checked again. A relational inbox and
processing marker commit before the broker ACK/complete operation. A matching
duplicate is acknowledged without another inbox row or hint; an altered payload
under the same ID is permanently invalid. Suppressed events retain their marker.
A delivery may arrive while its producer's outbox is still pending.

SignalR carries an optional ID-only inbox change hint after durable commit and
settlement. A bounded hint failure does not undo SQL or republish the message.
The authenticated inbox API is the recovery path after reconnect. Broker ACK
loss can cause redelivery; it does not imply a second durable notification.

## RabbitMQ

`Notifications:Provider=RabbitMQ` uses the existing configured durable main and
reminder queues. Their declaration arguments remain `null`, preserving existing
classic queues. Publisher and consumer share the same topology declaration:

| Resource | Configuration |
| --- | --- |
| Existing main queue | Original durable declaration and arguments |
| `<main>.retry` | New durable quorum queue; configured TTL; default exchange routes back to main |
| Retry dead lettering | `at-least-once`, `reject-publish`, 16 MiB length limit |
| `TaskManager.Notifications.Dead` | Durable direct exchange |
| `<main>.dead` | New durable quorum queue, bounded with `reject-publish`, bound to the product dead exchange |

Transient processing failure publishes the same body/ID to the retry queue with
only the sanitized application retry count, waits for a tracked publisher
confirmation, then acknowledges the old delivery. The initial delivery counts
as attempt one. Attempt three fails into the dead queue; permanently invalid
messages go there immediately. Dead publication also requires confirmation
before ACK. Confirmation failure leaves the old delivery unacknowledged and
triggers connection recovery. Shutdown leaves incomplete deliveries unsettled.

The fixed maximum is three business attempts **per retry lineage**. ACK loss can
fork copies with older attempt headers, so this is not a promise of exactly
three global callback invocations under every network fault. Stable IDs and SQL
deduplication handle these copies. Publisher connection/confirmation retries
are distinct from business attempts.

TTL alone does not guarantee the transfer back to the main queue. RabbitMQ's
[at-least-once quorum dead lettering](https://www.rabbitmq.com/docs/quorum-queues#activating-at-least-once-dead-lettering)
retains the expired message until its target confirms it. Unavailable/full
targets apply backpressure; bounded queues use rejection, not dropping the
oldest private message. RabbitMQ permits a small in-flight overshoot of a length
limit. Oversized poison deliveries store a compact size/digest quarantine record
instead of a partial private body. Dead queues otherwise contain private message
bodies and must keep the product's broker access controls.

`RabbitMQ:RetryAttempts` must be `3`; `RetryDelayMilliseconds` is bounded to
`100..60000` (default `1000`). Existing classic queues are not upgraded in place.
Publisher and consumer channels are separate; the publisher channel enables
tracked confirmations and serializes publication. No `Nack(requeue: true)` loop
is used. Consumer payload memory is used only inside the awaited callback,
following the [RabbitMQ .NET client contract](https://www.rabbitmq.com/client-libraries/dotnet-api-guide).

## Azure Service Bus

`Notifications:Provider=AzureServiceBus` selects the pinned official
`Azure.Messaging.ServiceBus` **7.21.0** SDK. Configuration uses
`ServiceBus:FullyQualifiedNamespace`, `QueueName`, optional `ReminderQueueName`
and optional user-assigned `ManagedIdentityClientId`. Deployed authentication is
explicit managed identity; no SAS connection string is required. Logical
outbox queue names are mapped from the Service Bus configuration.

The sender awaits `SendMessageAsync`; a successful return means broker
confirmation. IDs are never generated as a fallback for malformed notification
input. Senders are cached for the shared client lifetime and only configured
product queues are permitted.

Processors use PeekLock, `AutoCompleteMessages=false`, no prefetch, bounded
concurrency (`1..8`, default `1`) and finite lock renewal (`60..300` seconds,
default `120`). Transient processing failures abandon below attempt three;
broker `DeliveryCount` is authoritative. Permanent failure or exhausted attempts
uses a fixed safe dead-letter reason. SQL commit precedes manual complete.
Resources must also configure their deployment delivery limit and retain their
DLQ; the application does not need queue administration privileges.

SDK retry count is bounded to two, network try timeout to the configured
`1..60` seconds (default `30`); the outer publication timeout remains two minutes.
Connection failures and lock/complete loss remain uncertain outcomes, as
documented in [Service Bus settlement](https://learn.microsoft.com/en-us/azure/service-bus-messaging/message-transfers-locks-settlement).
Broker duplicate detection, when configured, supplements SQL deduplication.

## Retention and privacy

Separate scoped workers run bounded AI draft cleanup and notification inbox
cleanup. Inbox content older than 90 days is removed in batches of at most 100;
the content-free processing marker remains to prevent delayed replay from
recreating it. This describes inbox content retention; it does not claim that
task history or every outbox body is purged by the inbox worker.

Application logs contain safe status codes/IDs and queue metadata, not message
bodies, raw provider/broker exceptions or connection strings. Realtime routing
uses current authenticated scope and generic legacy hints. The browser fetches
private details through the authorized inbox API rather than receiving them in
an unverified stale connection.

## Verification limits

Local relational tests use SQLite and native SDK broker mocks. They prove the
application's commit/settlement ordering, deduplication path and SDK method
contracts, not an Azure deployment. Separate opt-in CI checks use actual
RabbitMQ and migrated disposable SQL Server. Refer to the step trace for the
exact executed RED/GREEN/CI counts; source availability alone is not a PASS.

Service Bus/managed identity and Azure SignalR require configured Azure
resources and live credentials to verify end-to-end. No such live result or
cloud delivery guarantee is inferred from mocks. Docker is unavailable on the
current local host; actual container execution is performed by the trusted CI
runner.
