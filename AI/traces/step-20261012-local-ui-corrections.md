# Step 28 follow-up — local invitation contract and Kyiv display

## Request and scope

The owner reported the invitation-list error shown after a successful private
invitation creation, and requested Kyiv instead of Kiev. This is a local repair
of the accepted frontend work. The local application remains running against
TaskManager_Codex_20261012; Azure remains deferred.

## Findings and changes

- WebAPI uses JsonIgnoreCondition.WhenWritingNull. Fresh invitations therefore
  omit acceptedAtUtc and revokedAtUtc; accepted/revoked invitations omit the
  other lifecycle timestamp. The frontend required both properties, rejected
  the HTTP 200 list response, and generated its own ApiError(502).
- TeamsPages now uses shared TeamInvitationSchema. Only the two nullable
  lifecycle properties accept omission and normalize it to null. Required IDs,
  email, creation/expiry strings, and malformed-type rejection are preserved.
- Browser Intl can return Europe/Kiev. An exact alias helper displays and
  defaults Europe/Kyiv in task and AI forms and restored AI draft labels.
  Other time zones, editable user input, original draft evidence and UTC date
  calculations are preserved.
- ECC agents handled invitation regression/fix, timezone regression/fix and
  independent security review. Root ran the combined checks and updated dist.

## Verification

- Invitation request-boundary regression: 3 expected failures before the fix;
  all 14 cases pass afterwards. These tests call the real request function with
  serialized HTTP 200 JSON rather than bypassing schema validation.
- Timezone regression: 3 expected failures before the fix; all 12 cases pass
  afterwards, including rendered forms and winter/summer/DST alias parity.
- `npm test -- --maxWorkers=1`: 113/113 pass, 11 files.
- `npm run lint`: pass, no warnings.
- `npm run build`: TypeScript and Vite pass. Existing bundle-size advisory
  remains; no dependencies were added.
- Real SQL/API/gateway/browser regression using
  `pwsh -NoProfile -File scripts/fullstack/Run.ps1 -Mode Controlled -NoBuild`:
  old dist gives 4 pass/2 fail, exactly the new invitation-list assertion on
  desktop and mobile (run 56750df0c2f845fcb76c59ad640aaa77).
- Rebuilt dist gives 6/6 pass, zero skips/retries, including owner reload of
  fresh and accepted invitations (run 4a7f9a1bb2ad401ebdf7fe770e924c8a).
  Each run owns a separate SQL database. Runner verifies exact database/server
  cleanup. Controlled fake AI/email/transport fixtures are explicit and are
  not evidence of real Azure provider behavior.
- Running localhost gateway serves the exact new dist index and JS asset
  index-CX9s7m3H.js; actual Chrome login/registration, asset loading, anonymous
  API 401 and Secure/HttpOnly antiforgery-cookie checks pass. API, Notifier and
  gateway readiness return 200.
- Independent review found no blocking auth/privacy/regression issue.

## Delivery state

Changed and verified locally; updated frontend is served by the existing
gateway. Backend binaries, migrations and database authorization are unchanged.
No commit, push, Azure deployment or OS timezone/trust change was performed.
Owner should copy any currently displayed one-time invitation link before
refreshing the page, because refreshing intentionally hides that link.
