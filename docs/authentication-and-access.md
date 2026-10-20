# Authentication and access

TaskManager reuses its existing integer-key ASP.NET Core Identity users, password
hashes and roles. Browser credentials use the API through the same-origin web
gateway; the console uses the legacy JWT endpoints. No bearer token belongs in
browser storage.

## Browser session

1. `GET /api/auth/csrf` returns `requestToken`; send it in `X-CSRF-TOKEN` on every
   unsafe browser request, including registration/login/confirmation/reset.
2. `POST /api/auth/register` accepts username, first/last name, email and password.
   It creates an ordinary unconfirmed account and sends a confirmation link.
3. `POST /api/auth/confirm-email` accepts user ID and the URL-safe encoded token.
   Registration never accepts role/team/email-confirmation assignments.
4. `POST /api/auth/login` issues only the `__Host-TaskManager.Session` cookie.
   HttpOnly, Secure, Path `/`, no Domain, SameSite Lax, bounded lifetime, no sliding
   extension. Authentication failures have one generic response; lockout and a
   configurable account rate limit apply.
5. `GET /api/auth/me` returns the signed-in profile; fetch a fresh CSRF token after
   login/logout or another identity change. `POST /api/auth/logout` ends the session.
6. Forgot/reset/resend-confirmation/change-password routes use existing Identity
   token providers. Recovery does not disclose whether an email is registered.
   Reset/change rotate the security stamp; other sessions are rejected immediately.

Cookie endpoints return401/403 without an HTML login redirect. An explicit Bearer
header selects JWT only, including an invalid token; it cannot fall back to a
valid cookie to bypass antiforgery. The console's `/api/Users/Register` and `/Login`
remain available with confirmation/rate limiting. JWT validates issuer, audience,
signature and lifetime. Set `Jwt:Key` privately; no tracked development signing
key is supplied.

## Current permissions

Business endpoints require authentication. Every operation reads the user's
current team membership from SQL; administrator access requires the actual
current SQL role as well as the authenticated claim. Stale cookies/JWTs cannot
preserve revoked team/admin privileges. Production service factories require the
access provider explicitly; optional constructor arguments serve legacy direct
tests only.

| Operation | Server rule |
| --- | --- |
| Read projects/tasks/teams | Current team; actual administrator may read across teams |
| Create project | Permitted current team; author set from authenticated caller |
| Edit/delete project | Permitted team and project author, or actual administrator |
| Task create/update/reminder/execute/delete | Permitted project; performer must be a current member of that project's team |
| Read user details | Self/current team; nested tasks are filtered to permitted projects |
| Change profile | Self or actual administrator; ordinary users cannot alter team or confirmed email through a profile DTO |
| Delete user | Self or actual administrator, subject to existing relationship constraints |
| Global legacy analytics | Actual current administrator |
| Detailed health diagnostics | Administrator; live/ready probes remain minimal and anonymous |

Scalar HTTP write DTOs and scalar responses prevent EF navigation graphs or
Identity credentials/stamps from becoming writable or exposed. Legacy direct
controller overloads are marked NonAction; HTTP cannot route to them.

Owned-team onboarding and consent invitations are the next sub-step. Existing
legacy teams acquire no new implicit owner/admin promotion. No global Manager
role is introduced.

## Private realtime

Notifier validates the same product cookie or JWT. It shares only TaskManager's
Data Protection application name (`TaskManager`) and session-cookie configuration.
Cross-origin `/chathub` requests are rejected; native console requests without an
Origin are allowed. Query-string access tokens do not authenticate. Security-stamp
revocation and session expiration close/reject connections as supported by SignalR.
Client broadcast is disabled; notifications route through `Clients.User` using the
verified identity ID. Executed-task recipients come from the task in SQL.

SignalR send/ack currently remains at least once and cannot restore offline
notifications. Durable inbox/dedup, recipient revalidation, reconnect replay and
bounded broker retry/DLQ are step26; do not call network delivery exactly once.

## Deployment configuration

Confirmed users without a team can create one and become its owner. Owners manage
their current team's name, invitations and membership. Invitation tokens expire
after seven days; only a SHA-256 token hash is stored. The authorized creation
response returns a copyable token once; it does not claim that mail was sent.
Acceptance requires the invited confirmed email, rechecks issuer rights and joins
only from no team or the same team. Replaying an accepted invitation is idempotent.
Revoked/expired invitations return 410. A current member can leave; an owner must
disband an empty team first. Project-bearing teams cannot be deleted.

`20261011184624_AddOwnedTeamInvitations` adds a nullable owner and a separate
invitation table. Legacy teams keep null owners and their members/data. An
administrator can manage them. Invitation responses are not cached or referred
to other sites; framework request-start logging suppresses token query strings.

- `BrowserAuth:PublicOrigin`: validated HTTPS origin without path/credentials/query;
  links use this value, never the request Host. Dev may use loopback HTTP, but the
  browser cookie/CSRF transport remains HTTPS.
- `BrowserAuth:SessionMinutes` (5–1440) and `AuthRequestsPerMinute` (5–1000).
- `DataProtection:Provider=FileSystem`, absolute `KeyRingPath` mounted outside the
  container. API and Notifier must share the product's ring and application name.
  At this stage unimplemented AzureBlob configuration fails closed. The later
  Azure adapter must add real Blob persistence and Key Vault key protection.
- `Email:SmtpHost`, `SmtpPort`, `FromAddress`, `UseTls`, optional private credentials.
  Production requires explicit host/sender/TLS. Local Compose supplies Mailpit;
  inspect synthetic development mail at `http://localhost:8025`.
- `Jwt:Key`, `Issuer`, `Audience`, `ExpirationMinutes`: unique product signing
  configuration, stored privately. API and Notifier validate the same JWT contract.

Apply migrations through the single-run job before starting application revisions.
`20261011175103_SecureLegacyAccounts` preserves users, hashes and business rows,
revokes known seed admin assignments and locks only accounts matching the historical
hash/stamp fingerprint. Recovery through a genuinely controlled confirmed email can
unlock an account. Human-modified credentials/stamps are left unchanged. Runtime
model seed is removed. The old undiscoverable migration is preserved; the new
forward repair is discoverable. Its downgrade never restores known credentials.

Bootstrap admin remains disabled by default. Use a new controlled email and explicit
private bootstrap configuration for a one-time administrator; existing ordinary
accounts are never automatically elevated. Disable bootstrap after use.

## Evidence and limits

Tests exercise actual cookie/JWT/Identity HTTP flows, relational SQLite membership,
real loopback SMTP, configured expiry/rate limits and real filesystem key-ring
restart. SQL upgrade tests require `TASKMANAGER_TEST_SQL_SERVER`; absence is an
explicit skip, never substituted by SQLite. CI provisions a disposable SQL Server.
Container and cloud execution require their real runners/credentials; local green
tests do not prove Azure deployment or live email/model configuration.
