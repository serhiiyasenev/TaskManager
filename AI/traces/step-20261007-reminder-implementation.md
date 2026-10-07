# Reminder reliability repair — implementation

Implemented plan steps 16–18: transactional outbox with additive migration, occurrence invalidation and concurrency tokens, leased dispatch with stable IDs and publisher confirms, post-processing consumer acknowledgments.

Added relational recovery/concurrency tests, publisher and consumer acknowledgment tests, reminder-setting invalidation coverage, and a SQL Server snapshot consistency check. Delivery/deployment/rollback guarantees are documented in docs/reminder-delivery.md.

Validation: source review and git diff --check completed. No .NET SDK is available in this workspace; build/test execution is pending PR CI. Step 19 stays active until results are reviewed. No database was migrated and no branch was merged.
