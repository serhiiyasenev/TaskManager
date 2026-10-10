# Reminder delivery and recovery

## Guarantees

A reminder occurrence and its serialized envelope are committed to SQL Server in one transaction. The task stores a private occurrence ID; its public `ReminderSentAt`/`EscalationSentAt` remains empty until RabbitMQ confirms publication. Existing endpoints, envelope fields and the SignalR `ReceiveMessage` event keep their contracts.

The dispatcher claims an outbox row with optimistic concurrency and a five-minute lease. The complete publish operation has a two-minute deadline; individual confirmation waits are limited to 30 seconds. A negative or ambiguous result leaves the row retryable. Repeated polling does not create a new occurrence, and competing workers cannot normally publish the same active claim. Each poll stages up to 100 tasks and dispatches up to 100 messages.

Publication is persistent, mandatory, and uses tracked publisher confirms. The outbox ID is reused as both the envelope correlation ID and RabbitMQ message ID across retries. Notifier acknowledges only after SignalR processing succeeds; failures are delayed briefly and requeued. Editing reminder settings or the performer invalidates pending occurrences; completed/canceled tasks do not dispatch their pending messages.

## At-least-once boundary

A crash after broker confirmation but before the publication marker commits can cause the **same message ID** to be sent again. A consumer crash after broadcasting but before acknowledgment can similarly repeat a notification. This is at-least-once delivery, not exactly-once display. Consumers that perform business side effects must persistently deduplicate by message ID. SignalR remains a live broadcast: successful sending does not mean an offline client received it, and email is still simulated. Task edits racing with an already in-flight send cannot retract that send.

## Deployment and recovery

1. Back up the database and stop old scheduler instances before upgrading. Old versions do not use the outbox and must not run alongside the new scheduler.
2. Apply migration `20261007093000_AddNotificationOutbox` before starting the updated WebAPI.
3. Keep WebAPI, SQL Server, RabbitMQ and Notifier running. `Reminders:Enabled=false` stops both enqueueing and dispatching.
4. Inspect `NotificationOutbox` for `Status=0` (pending), `1` (published) or `2` (canceled). A pending row with a future `LockedUntilUtc` is leased/backing off; after a crash it becomes available within five minutes. No manual reset is needed.
5. Investigate persistent broker/consumer failures using logs and the stable correlation ID. Do not mark pending entries published manually.

Drain pending messages before rolling back: the down migration drops the outbox and its occurrence IDs. A retention/cleanup policy for published and canceled rows, durable consumer deduplication, bounded poison-message retries/DLX, and production monitoring remain follow-up work. Keep clocks synchronized when running multiple scheduler instances. Expired leases permit recovery; a stalled process resuming after lease expiry is another possible duplicate-delivery boundary.

## Regression checks

`dotnet test Tests/Tests.csproj --filter "FullyQualifiedName~NotificationOutboxTests|FullyQualifiedName~RabbitMqPublisherConfirmTests|FullyQualifiedName~RabbitMqListenerExecuteTests"`

SQLite relational tests exercise rollback and concurrency, repeated scans, publish failure/restart, active claims, cancellation of stale work, and a database save failure after publication. Publisher/consumer tests verify confirmation options, stable IDs, cancellation and acknowledgment behavior. The SQL Server model is compared with the migration snapshot. These checks do not substitute for a real SQL Server/RabbitMQ failure-injection environment.
