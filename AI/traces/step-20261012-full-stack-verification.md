# Step28 — full-stack and live verification

## GOAL / context reloaded

Roadmap28 is active after27 accepted at79b6e3d. Accepted research/ADRs remain
unchanged. Preserve existing data/contracts, Consensia deletion, broker ordering
and TaskManager_Codex_20261012. Three agents continue disjoint work; root owns
project references/locks/shared builds/Git. Freeze C# before any shared build.

## Concrete full-stack contract

Use a separate test-only Kestrel host loading the actual WebAPI, Notifier and
WebHost programs through native .NET10 WebApplicationFactory.UseKestrel. It
creates only taskmanager_e2e_test_<GUID> databases on the explicitly supplied
SQL server and applies the existing eight migrations. HTTPS is an ephemeral
loopback certificate with no OS trust/store changes. Production images exclude
this tool; no public fake production providers or test control routes are added.

Explicit Testing-only email and structured AI fixture transports exercise real
Identity confirmation/reset tokens, cookies/CSRF, source validation and SQL
draft/commit behavior. Native Linux CI uses actual RabbitMQ; local Windows may
select an explicitly named controlled transport because RabbitMQ is absent.
The latter verifies actual SQL/coordinator/private SignalR behavior, with no
claim that it proves broker delivery or Azure adapters.

Host writes ignored private state JSON: schemaVersion1,mode=controlled-full-stack,
baseUrl HTTPS gateway, controlUrl loopback HTTP, random controlToken, databaseName,
provider=fake, transport=controlled|rabbitmq. No SQL/JWT values in this file.
Control endpoints require X-FullStack-Control, never enter product routing:
GET/mail?email returns confirmationUrl/resetUrl; POST/clock/advance{minutes};
POST/ai/mode{mode=normal|unavailable|invalid-output}; POST/dispatch triggers the
actual outbox dispatcher; GET/summary returns safe aggregate SQL/settlement counts.
All endpoints and fixtures are test-only, loopback, bounded and explicitly enabled.

Browser tests use actual product requests without route interception: register/
confirm/login, team/project, notes source review/edit/partial commit, tasks/inbox,
refresh/relogin; foreign objects, revoked membership, real session expiry and
provider failure. Desktop/mobile share the contract. Never upload raw private
mail/cookie/control-token traces; retain sanitized results/synthetic screenshots.

## Remaining live boundary

Azure identity/auth, tenant/subscription/region, approved resources, model/quota,
verified sender, billing budget/contacts and controlled live accounts are absent.
Live model runner remains explicit SKIP. The exact-revision Azure smoke checklist
cannot be satisfied by this local fixture host; no synthetic evidence promotes
traffic. Complete local/CI code and demo first, then request missing live inputs
in one concise bundle. Never auto-merge PR51.
