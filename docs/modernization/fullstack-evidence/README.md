# Native Windows full-stack evidence

## Actual local RabbitMQ gate

Final browser run `618291d04d7e41f59226a27fa8de2e4b`: six passed, zero failures/
skips/retries with actual RabbitMQ4.3.6 and OTP28.5. The same session ran all
860 backend tests with actual SQL/RabbitMQ: zero failures or skips. Coverage:
89.27% lines (5317/5956),75.11% branches (2831/3769).

Retained [browser aggregate](windows-rabbit-summary.json),
[host/database/queue cleanup](windows-rabbit-cleanup.json) and
[owned portable broker cleanup](windows-native-broker-result.json), completed
2026-10-12T16:10:19Z. Broker/EPMD listeners were removed; no Windows service,
registry installation or Azure call was performed. The existing user profile
cookie is preserved; native explicit cookie flags bypass its use.

## Earlier Controlled transport gate and screenshots

Actual run `b4e3a2e86d9a494eaebcaf5360424ddc`, completed 2026-10-12T15:27:10Z:
six desktop/mobile cases passed, zero failures/skips/retries. The real HTTPS
gateway, API, Notifier, SQL Server17, Identity tokens/cookies and private local
SignalR were exercised without browser API interception. AI/email and the
selected Controlled transport are explicit test fixtures, not Azure evidence.

The [aggregate report](windows-controlled-summary.json) contains no account,
cookie, mail link or control token. [Cleanup proof](windows-controlled-cleanup.json)
confirms that the exact disposable database and actual listeners were removed;
the Windows private key was explicitly deleted before the proof was written.
The independent product database `TaskManager_Codex_20261012` was preserved.

These are actual product pages with synthetic data. They demonstrate the
rendered workflow; the assertions and aggregate report establish the test result.

| Page | Desktop | Mobile |
| --- | --- | --- |
| Source review | [PNG](desktop-1440x900-ai-review.png) | [PNG](mobile-390x844-ai-review.png) |
| Durable private inbox | [PNG](desktop-1440x900-durable-inbox.png) | [PNG](mobile-390x844-durable-inbox.png) |
| Manual task during AI outage | [PNG](desktop-1440x900-manual-task.png) | [PNG](mobile-390x844-manual-task.png) |

See [run instructions and boundaries](../../fullstack-verification.md).
An injected clock proves real HTTP cookie expiry, not wall-clock expiration of
an already-open native SignalR connection. This local run cannot satisfy the
authenticated Azure promotion gate or measure real model quality.
