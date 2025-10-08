# Trace Step 01 — Research RabbitMQ Retry & DLX

**Date:** 2025-10-08  
**What Was Done:**  
Compared RabbitMQ retry approaches: delayed queues vs DLX.  
Evaluated trade-offs for simplicity and reliability.

**Key Findings:**  
- DLX with retry count ≤3 is reliable and widely used.  
- Back-off delay between retries prevents message storms.  
- Requires correlation-ID tracking to match messages.

**Next Action:**  
Define retry configuration (attempts = 3, delay = 5 s) and update `plan.md` accordingly.
