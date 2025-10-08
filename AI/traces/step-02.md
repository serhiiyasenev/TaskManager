# Trace Step 02 — Draft Notifier Configuration for Retry/DLX

**Date:** 2025-10-09  
**What Was Done:**  
Prototyped new RabbitMQ configuration sections:

```json
"RabbitMq": {
  "RetryCount": 3,
  "RetryDelaySeconds": 5,
  "DeadLetterExchange": "notifier.dlx"
}
```