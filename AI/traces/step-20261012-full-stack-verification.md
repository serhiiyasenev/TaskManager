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

## Current implementation checkpoint

Root added/restored the test-only host project and solution entry; production
Docker context excludes it. Browser config/tests are separate from the existing
52 intercepted UI scenarios. Strict extended TypeScript and zero-warning ESLint
passed; actual frontend build passed. Native host first compile had two fixture
overload ambiguities, corrected explicitly; subsequent build passed with zero
warnings/errors. No production code changed in this stage so far.

Runner fixtures:8 passed, including native stdin EOF/forced timeout, private
state/summary/SQL guards and explicit LiveAzure SKIP2. Native actionlint/AST
passed. Live AI CLI without opt-in independently reports SKIP, exit2, no calls.

First actual Windows Controlled execution ebede38cecc7427e9011ead5f6122d13
stopped before browser: native-health / InvalidOperationException. State was not
published; cleanup.json was absent and runner correctly reports cleanup unverified.
This is a harness/startup failure, not a claimed product E2E RED or successful
cleanup. Runtime agent is adding bounded health/cleanup diagnostics; security
agent reviews startup/shutdown. Preserve the product DB; verify only this run's
strictly named owned test resources. No shared build is running during agent edits.

Second diagnostic run bda9388c15664aec923c0740c344646f verified API200 and
Notifier200 with matching actual ports; gateway failed SecureConnectionError.
Native cleanup removed its database/queues but listener proof remained false;
runner correctly stayed FAIL. Independent SQL catalog check preserved the
product database and all eight migrations. This was a test harness TLS failure.

Microsoft documents Schannel's ephemeral-key handshake limitation. Test-only
Windows HTTPS now uses a uniquely named, nonexportable current-user CNG key,
without certificate store/trust registration. Cleanup deletes that exact key
and verifies absence after actual servers stop; Linux remains memory-only RSA.
Native server StopAsync precedes factory disposal. Kernel listener absence plus
a nonaccepted TCP connection are both required, with per-endpoint safe diagnostics.
All C# sources frozen; native host build passed zero warnings/errors. Next gate
is a fresh real SQL/HTTPS/Chrome Controlled run, then actual Linux Rabbit CI.

Fresh Windows run23bedb2aee2b48ff9a0c51b7ab070a23 reached actual browser execution:
TLS/readiness passed and all six browser cases failed at the shared createWorkspace
helper (journeys22/130/181). No API interception was used. Native cleanup.json and
runner-result.json verified owned database deletion, listener absence, queue/key
cleanup; product database remains outside that namespace. Browser agent now
diagnoses the exact shared UI step rather than weakening acceptance/timeouts.

NuGet and npm advisories: none. Canonical notification generated outputs current.
Frontend unit gate:87 passed; lines92.57%,branches75.5%,statements83.56%,functions80.88%.
Full solution Release build passed with two existing xUnit2002 warnings in
TaskAnalyticsServiceIntegrationTests (no new host warning/error). Backend SQL
regression/coverage is running separately with native master connection.

Boundary: injected clock proves real cookie/API expiry. Native ASP.NET SignalR
existing-connection expiration heartbeat uses system UTC; manual clock advance
does not prove an already-open socket's timed expiry. No artificial socket close
is added and no broader realtime-expiry claim follows from that fixture.

Concrete browser failure diagnosis: wrapping select label made exact Команда
selector fail on desktop; shared constant team names caused genuine409 on
mobile because product team names are unique. Fix is a test-only role selector
and per-workspace unique synthetic names. No product uniqueness/access gate is
relaxed. Reporter will persist safe progress and remove automatic private error
context attachments; trace/video off alone did not disable their native capture.

Native full backend:859 passed/0 failed/1 explicit local Rabbit skip (860 total),
37s. Coverage5317/5956 lines=89.27%,2826/3769 branches=74.98%.
Existing UI regression:52 passed (1.0m), including both user-reported long-content
focus/responsive/reduced-motion cases (desktop/mobile). Further gate repetition
is limited to the concrete new full-stack harness defects and required CI.

Second run566a1735cd124172ae4fb407aafc4e65:4 passed/2 failed, both case3 expected
502 copy while the accepted native unsafe-AI contract is503 ServiceUnavailable.
Corrected the test to observe the real POST503/ProblemDetails code and actual
UX; existing no-write/manual/reset assertions remain. Cleanup again verified.

Final Windows Controlled runb4e3a2e86d9a494eaebcaf5360424ddc completed
2026-10-12T15:27:10Z:6 passed/0 failed/0 skipped/0 retries. Actual source-backed
two partial SQL commits, private live hints/inbox/read, foreign/revoked access,
native cookie expiry, persistence/relogin, unavailable/invalid AI no-writes,
manual task and actual Identity password recovery passed on both viewports.
Cleanup verified database/listener/queue absence and exact Windows CNG key
deletion. Sanitized aggregate/cleanup and six synthetic product screenshots are
retained at docs/modernization/fullstack-evidence. Accepted old screenshots were
restored after the separate52 regression run; fresh incidental captures removed.
Host directory/ancestor guards reject existing work directories and reparse links
before ACL/create. Host rebuild passed0 warnings/errors; TS/lint passed unchanged
strict gates. Next required boundary is real Linux Rabbit full-stack CI.

Owner steering2026-10-12: Azure deployment is explicitly deferred; verify
everything locally first. Do not repeat Azure input requests or perform cloud
calls. Keep future opt-in/evidence gates truthful and off. Investigate actual
local Rabbit/Erlang availability and workspace-only execution before claiming
local broker proof. No OS service/registry installation or user data deletion.

## Native local Rabbit verification — actual PASS

No installed Rabbit/Erlang/Docker/WSL runtime was available. Official OTP28.5
ZIP and Rabbit4.3.6 Windows ZIP downloaded to ignored .tools and verified against
published SHA256 (recorded in docs/native-windows-verification.md). No installer
was executed; existing VC runtime and portable Erl probe passed. A harmless
native -home probe exposed Windows duplicate home arguments. Native private
-args_file/-setcookie bypasses profile-cookie I/O; a same-value private child
environment bridge supplies the late CLI resolver without cookie argv/log output.

Startup attempts caught real CLI cookie mismatches; all owned ports removed
after each failure. Inherited Rabbit/Erlang/alias overrides are cleared; native
backend/browser children are bounded and raw streams discarded. Native runner
fixtures now9 passed (actual negative-control/isolated environment children);
future live-protocol fixtures8 passed, no cloud calls. The nine concrete Azure
probes remain explicitly pending; owner has deferred that phase.

First native session passed860 backend and6 browser but launcher rejected its
uninitialized LASTEXITCODE after a nested successful PowerShell script. Replaced
that check with a bounded native browser child and explicit process exit proof.
Final broker session621691200db443b5a4fff9dbea17482b completed2026-10-12T16:10:19Z:
actual OTP28.5/Rabbit4.3.6 readiness/alarms and all three ports loopback;
860/860 backend passed with0 skips; browser618291d04d7e41f59226a27fa8de2e4b6/6
passed,0 skipped/retried; host/database/queue and broker/EPMD port cleanup verified.
Native coverage89.27% lines5317/5956,75.11% branches2831/3769.
Sanitized broker/browser/cleanup reports retained under fullstack-evidence.

Independent final SQL catalog check: product DB retains8 migrations and19
original tasks; exact final test DB absent. The pre-existing profile cookie is
preserved (metadata only inspected; no content read/deletion). An earlier
sandbox metadata read was not authoritative evidence of absence. Source and
explicit native flags establish the isolated cookie behavior.
No actual cloud/model/mail-provider evidence or local Linux-container PASS is
claimed. Native Windows setup/run/limits are documented; remaining gate is PR CI.

## ECC self-evaluation before delivery

| Axis | Score | Evidence / improvement |
| --- | --- | --- |
| Accuracy |4| Actual860 backend,87unit,52UI,6 native Rabbit browser and cleanup proofs; distinguish fixtures from model/provider evidence and metadata from absence proof. |
| Completeness |4| Requested local gates pass; local Linux-container engine absent and current PR CI remains to finish. Azure explicitly deferred by owner. |
| Clarity |4| Native runbook and safe reports/synthetic screenshots give reproducible steps; detailed failure history stays in this trace. |
| Actionability |4| One Windows command replays owned SQL/Rabbit/browser gates; pinned archives require explicit preparation, no hidden system installation. |
| Conciseness |4| Separate test host/protocol adds substantial code and lock metadata; production paths remain unchanged and final report will focus on results/limits. |

Overall4.0. Remaining improvement: finish actual current PR CI and record it;
retain deferred Azure/live-model boundaries. The owner should agree that local
verification is established without presenting fixtures as deployed evidence.

## Current PR CI / image-pin repair

Commit770c0e219804da055e394c166ad4f2fb317e59c0 pushed to existing draft PR51.
Azure offline and AI process gates passed; full-stack Windows/Linux runner
fixtures passed. New actual-local-stack run38276334526/job114902826185 failed
before checkout/product tests: supplied SQL190696... digest is not in MCR.
Corrected the pin to actual official tag/digest GET/header/raw-byte SHA256
4402d880dd4c34bfa7d8705e56a86cd6c88da80a1f6bbbe741f999e76264a090.
Independent config hash verifies linux/amd64. Rabbit8dd6... OCI index and
linux/amd64 child/config independently verified, retained. Exact digest-addressed
GET200 verified2026-10-12T16:22:47–48Z. Registry metadata/anonymous token only;
no Azure deployment/account credential. No image pin, test or health gate removed.
Native actionlint passed. Repeat actual CI after this concrete repair.
