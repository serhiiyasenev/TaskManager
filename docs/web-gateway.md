# Web gateway and HTTPS development

`WebHost` is a database-free ASP.NET Core 10 host. Its entrypoint is
`TaskManager.WebHost.Program`; it references the framework-only `Hosting` library
and YARP 2.3.0. It serves the built Ukrainian React application and proxies
`/api` to API and `/chathub` to Notifier. Browser authentication remains
`/api/auth`; there is no additional `/auth` API alias.

## Local browser stack

With the pinned .NET SDK and Docker/Compose available:

```powershell
./scripts/local-init.ps1
./scripts/local-https-init.ps1 -Trust
./scripts/local-web-up.ps1
./scripts/local-web-smoke.ps1
```

Skip `local-init.ps1` when an existing private `.env` is present. Certificate init
keeps existing SQL/broker/JWT values and refuses to replace an existing certificate
whose password is missing. It exports a password-protected localhost development
PFX into ignored `.local/certificates/localhost.pfx`; only `-Trust` requests
installation of development trust. The private certificate directory is restricted
to the local user/system administrators; on Linux it is mode 0700. The bound PFX
is readable by UID 1654 inside the container. Neither the password nor private key
is printed. Do not use this certificate or development credentials in production.

Open `https://localhost:7151`. This is the shared browser origin for HTML, API,
cookies and SignalR. Mailpit at `http://localhost:8025` receives confirmation and
recovery links. Gateway certificate, SQL, broker and application keys remain in
private local files/environment; none enters the Docker build context or React
bundle. Override `WEB_PORT` and `PUBLIC_ORIGIN` together when changing the public
port. A custom certificate path requires explicit export/configuration.

The Compose `web` profile adds the gateway. Existing `local-up.ps1` and optional
`console` profile still start the API/Notifier stack. `local-down.ps1` stops the
project and retains data; use `--profile web` when invoking Compose manually to
include the gateway. API/Notifier direct ports remain loopback development tools.
Browser sessions require the HTTPS gateway.

## Configuration and trust

| Setting | Meaning |
| --- | --- |
| `Gateway:ApiDestination` | Explicit root URL of API |
| `Gateway:NotifierDestination` | Explicit root URL of Notifier |
| `BrowserAuth:PublicOrigin` | Explicit public HTTPS origin shared with API/Notifier |
| `ProxyTrust:KnownProxies` | Additional exact proxy IP addresses |
| `ProxyTrust:KnownNetworks` | Additional explicitly trusted CIDR ranges |
| `ProxyTrust:AllowedHosts` | Optional explicit forwarded host allowlist; defaults to public-origin hostname |

Destination URLs cannot contain credentials, a path prefix, query or fragment.
Production destinations must use HTTPS with normal certificate validation.
Development/testing may use HTTP for the isolated internal service network.
YARP resets forwarded headers from the accepted request, preserving Cookie,
Authorization, Origin, request body, query and API response cookies. It supports
WebSocket upgrades; the notifier cluster permits a bounded three-minute inactivity
window, with SignalR keepalives maintaining active connections.

API/Notifier process forwarded headers before HTTPS, authentication and CSRF.
Framework loopback trust is retained; exact IPs/ranges are added without clearing
trust into an unrestricted configuration. A single immediate hop is consumed.
Malformed trust, unspecified proxy IPs, universal CIDRs and wildcard hosts fail
configuration. An unknown peer cannot replace scheme, host or client IP.

Local Compose assigns the gateway exact IP `172.29.24.10` in its project network
`172.29.24.0/24`; only that additional IP is trusted by API/Notifier. Override
`COMPOSE_NETWORK_SUBNET` and `GATEWAY_IP` together if the subnet conflicts with
another local network. Production ingress must supply its actual known proxy
addresses or controlled network ranges; no trust-all deployment default is provided.
The gateway receives no database, JWT signing or Data Protection credentials.

## Routing, probes and privacy

`/health/live` reports gateway HTTP availability and makes no upstream request.
`/health/ready` additionally requires the built SPA entrypoint and API
`/health/ready` HTTP 200, using a bounded two-second request with no cookies,
authorization or automatic redirects and normal TLS validation. Failed, redirected
or timed-out API probes return gateway 503. The API's own readiness covers critical
SQL dependencies; this does not prove notification delivery. Private gateway probes
bypass browser HTTPS redirection. Unknown health
or auth paths and missing asset files return 404. API/hub requests use their
upstream response and cannot become SPA HTML. Ordinary browser routes return the
SPA entrypoint; static files come only from `wwwroot`.

Gateway responses carry `Cache-Control: no-store`, `Referrer-Policy: no-referrer`,
`X-Content-Type-Options: nosniff` and frame denial. Framework hosting, YARP and
HttpClient request URL logs are limited to Warning because recovery/invitation
links can carry private query tokens. No raw query/URI request logging is added.
Frontend bundles contain no source maps. Authentication, CSRF and membership
validation continue in API/Notifier; the gateway has no business authorization logic.

## Verification evidence

Gateway integration tests use real loopback Kestrel upstreams for HTTP and
WebSocket transport, including headers/cookies/body, errors, assets/SPA routing
and startup failures. Forwarding tests exercise exact IP/CIDR/default loopback
trust, unknown peers, host restrictions and hop limits. The container gate now
builds five images and checks HTTPS SPA/assets/API/hub routing and the Secure
antiforgery cookie through the real gateway. Its self-signed certificate exception
is confined to localhost in a disposable CI runner; local smoke uses normal trust.

Runtime results belong in the delivery trace. The earlier four-image container
gate passed before this gateway was added; that result does not prove the new
web image or HTTPS path. Docker remains absent from the local implementation host.
See [container verification](container-verification.md) and
[authentication and access](authentication-and-access.md).

Official references checked on 2026-10-11:

- [YARP 2.3.0 package](https://www.nuget.org/packages/Yarp.ReverseProxy/2.3.0)
- [YARP configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/config-providers?view=aspnetcore-10.0)
- [YARP forwarded transforms](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/transforms-request?view=aspnetcore-10.0)
- [YARP WebSockets](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/websockets?view=aspnetcore-10.0)
- [Trusted forwarded headers](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0), [KnownIPNetworks](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.builder.forwardedheadersoptions.knownipnetworks?view=aspnetcore-10.0)
- [Development certificate export](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-dev-certs)
