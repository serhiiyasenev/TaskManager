# Step 26 rendered inbox evidence

**14/14 Playwright cases passed, with no failures, skips or flaky results.** The
final run used the built React App and real Microsoft SignalR browser SDK in
disposable installed Chrome contexts at 1440×900 and 390×844. See
[inbox-report.json](inbox-report.json) for exact run metadata and selected PNGs.

HTTP, negotiation and WebSocket responses are deliberately synthetic fixtures.
This verifies client rendering, actions and protocol handling. It does not prove
Identity cookies, server permissions, SQL persistence, broker delivery or Azure
services; those have separate integration/infrastructure gates.

## Observed behavior

- Four product kinds, canonical links, global counts, read confirmation and
  unread pagination. Double click issued one bodyless PUT with CSRF transport.
- Real client protocol handshake, strict ID-only hints, deduplication, canonical
  refetch after reconnect, bounded initial retries and explicit recovery.
- Loading, empty, cached partial outage, safe error, revoked access and logout.
- After 403, reconnect verifies fresh canonical access first. A denied or held
  response keeps private data hidden and the socket stopped; success allows the
  connection and current rows to return.
- Labelled controls, visible keyboard refresh focus, minimum 24px control
  targets, measured status/alert contrast and no document horizontal overflow.

Rendered review found and verified fixes for the connected-status contrast
boundary and the no-op reconnect action after 403. The new component regression
failed on the missing canonical request before the fix; all **18** focused inbox
component cases then passed. The browser suite retained seven cases per viewport
and extended the permission case with denied, pending and successful recovery.

The reduced-motion preference was active. This subset did not assert that every
animation stopped; the shared baseline suite owns that stronger check. English
copy is prepared, but this run exercised Ukrainian UI only.

## Reproduce

After the normal production build, from `frontend`:

```powershell
node node_modules/@playwright/test/cli.js test ui-inbox.spec.ts
```

Playwright manages the loopback preview on port 4173 and disposable browser
contexts. No personal browser profile is reused. Raw reports remain in ignored
`frontend/test-results`; committed evidence contains selected synthetic images.

From the repository root, run the installed ECC evidence checker:

```powershell
$inboxEccChecker = Join-Path $env:USERPROFILE '.codex/plugins/cache/ecc/ecc/2.2.3/skills/frontend-design-direction/scripts/check-evidence.js'
node $inboxEccChecker docs/modernization/ui-evidence/inbox-report.json
```

It completed with exit code **0** and output:

```text
Evidence record complete; visual correctness still requires rendered inspection.
```

The checker verifies record completeness and local artifacts. The passing
browser assertions and inspected rendered images provide the observations.

## References

- [Canonical notification contract](../../../contracts/notifications.openapi.json)
- [UI contract](../ui-contract.md)
- [Playwright WebSocket interception](https://playwright.dev/docs/api/class-websocketroute)
- [Playwright clock](https://playwright.dev/docs/clock)
- [Controlled API mocking](https://playwright.dev/docs/mock)
