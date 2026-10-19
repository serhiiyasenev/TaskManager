# Local account recovery — owner request, 2026-10-13

The owner explicitly requested access to the existing admin@example.com account
in their own local repository/database after the manual email workflow failed
for them. Only that account's login credentials were recovered; no role grant
was needed for login.

## Evidence and action

- Initial native SQL read: expected user ID 11, password present, email
  unconfirmed, lockout active, no admin role.
- Initial Mailpit read: HTTP 200, two existing messages, none addressed to the
  requested account. Actual stored and normalized email match the request.
- Fresh HTTPS API resend-confirmation returned 202 and a newly delivered local
  message addressed to exactly the requested account. Confirmation link origin,
  path and user ID were validated in memory before consumption.
- Confirm-email returned 200. Forgot-password returned 202 and a new local
  reset message. Its origin, path and email were validated in memory.
- Reset-password returned 200 with a newly generated random password. The
  password is stored only in the owner-only ignored
  `.local/native-app/admin-credentials.json`; no password/hash/token was printed
  to command output or committed to source.
- Real login and me endpoints returned 200 with expected user ID 11 and
  confirmed email. The normal Chrome login form was then independently tested
  with those credentials and established the correct authenticated session.
- Final native SQL read: email confirmed, failure count 0, lockout inactive.
  Role membership remains unchanged. SMTP and application settings remain as
  configured; bootstrap stays disabled.

The recovery used supported Identity endpoints with a fresh CSRF token, exact
Origin and an HTTPS cookie jar. Test cookies were discarded. No migration,
task/project/team mutation, backend source change or Azure action was needed.
Independent ECC reviews checked the recovery scope and SMTP wiring.
