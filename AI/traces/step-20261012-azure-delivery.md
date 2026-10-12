# Step27 — reproducible Azure infrastructure and release gates

## GOAL / active step

Implement authorized roadmap27 after26 accepted at15aa4d3. Keep product data,
cookie/JWT contracts, SQL-before-ACK and outbox guarantees. Accepted ADR001/002
remain unchanged; ADR003 is proposed. This is execution context, not a roadmap.
ECC deployment-patterns joins TDD/security-review/verification-loop/self-eval.

## Current verified environment

Native Windows SQL Server at tcp:127.0.0.1,1433 accepts integrated authentication.
Owned TaskManager_Codex_20261012 has8migrations;10nativeSQLtestsPASS/0skip.
Docker/az/Az.Accounts/auth/tenant/subscription/region/budget/deployment/email
values absent. Do all unblocked code/IaC/tests before a bundled live-input request.
No Azure spending/deploy/model calls or live results are inferred.

Standalone Bicep0.48.1(cec7951cf6) downloaded only into ignored.tools/bicep and
verified against official release metadata: Windows SHA256
398af294cf16ac4becdbd008a77d148d0e8e91492309a6b60ec911b7e1cab082;
Linux SHA256b09ec25a9d376c1f8e33ede6ed22b587f915ad68488d5db77a6f9541748c7f6e.
Root owns dependency locks/shared builds/Git. Freeze allC#writes before gates.

## Ownership and concrete boundaries

- Delivery agent: infra/azure Bicep/resources/sample parameters/contracts; new
  product foundation then apps, separate identities/SQL/private DNS/RBAC;
  existing shared resources referenced only, never redefined.
- Runtime agent: shared API/Notifier Blob+versionlessKV key ring, explicit MI,
  real Azure email adapter, strict provider/secret/privacy guards and direct tests.
- Security agent: scripts/azure PowerShell and trusted OIDC/release/IaC workflows,
  costs/budget/runbook, digests/migration/revision/smoke/rollback gates.
- Root: bounded proxy-hop/ACA probe behavior and tests, SQL bootstrap helper/job
  image and least-privilege grants, shared dependencies/integration/verification.

## Verified technical constraints

DP SDK stable Blobs1.5.4/Keys1.6.4 with DataProtection10.0.12; explicit
ManagedIdentityCredential, no key/SAS or ambient credential fallback. Same blob,
versionless wrapping key, applicationNameTaskManager only API+Notifier; gateway
has no business DB/auth/key permissions. Preserve old wrapping-key versions.
Email SDK Azure.Communication.Email1.1.0 supports tokencredential native client;
verified sender/domain is a live prerequisite. SMTP remains explicit local path.

ACA stable resource schema2025-07-01. Public WebHost; internal API/Notifier8080;
all gateway destinations HTTPS. ACA overwrites XFP; appends only trusted rightmost
XFF. Web consumes1 hop; backend receives canonical YARP client plus ACA gateway
hop and consumes bounded2 with explicit environment subnet/public host trust.
Add real middleware topology tests and HTTP/live/readiness probe exceptions.
Private hub negotiation then native SDK directs internet browser to public
SignalR client endpoint; keep ClientConnection public while protecting servers.

Azure SQL contained identities/grants are separate from ARM RBAC; CREATE USER
WITH SID/TYPE=E avoids Graph lookup. SQL uses normal serverDNS/private endpoint,
separate privileged bootstrap and migrator/runtime permissions. One manual
migration execution before starting candidate workers/traffic. Manual job
parallelism1 is not global execution lock: release concurrency+running-job guard+
EF SQL migration lock remain necessary. A worker with0HTTPtraffic still runs.

OIDC only trusted main/manual-main and GitHub environment claims; no PR secrets,
untrusted code with Azure authority, long-lived AZURE_CREDENTIALS or auto-merge.
Validate/what-if, immutable digest images, infrastructure/permissions, migration,
revisions, actual smoke, traffic, explicit rollback/forward-repair/restore.
SQL/keyring/provider content, auth/query/cookies/notes never enter telemetry.
No fabricated free cost, model quota, AzureURL or deployed revision evidence.

## Implementation checkpoints (2026-10-12)

Three disjoint agents implemented infra, runtime SDK adapters, and release scripts.
Root integrated dependency locks, origins/proxies/probes, SQL bootstrap and console
privacy. Shared C# builds run only while all agent C# writes are frozen.

- Compiled runtime/proxy/SQL RED: 81 tests, 62 failed / 19 passed, no skips.
- First checkpoint: 116 tests, 109 passed / 7 failed. Six failures demonstrated
  missing ACS/provider/origin integration; one RSA SDK fixture lifetime defect
  was corrected without weakening production assertions.
- Telemetry and application-log RED: 131 tests, 117 passed / 14 failed, no skips.
  Twelve telemetry stubs and two private task-log regressions were demonstrated.
- Latest checkpoint: 131 tests, 128 passed / 3 failed, no skips (17 seconds).
  Native DP Blob/Key Vault round trips, ACS completed-send behavior, cookie/CSRF
  candidate origins, strict Azure SQL/JWT configuration and real restricted SQL
  migration/permissions passed. Deferred telemetry instrumentation registration
  failed; two exporter fixtures inherited an unsampled ambient test parent.
  Registration is now before provider creation; fixtures explicitly establish
  sampling. The independent incoming-parent trace-budget assertion is unchanged.

The SQL bootstrap tool prepares three contained principals without Graph lookup,
then grants only migration DDL and per-table runtime permissions after all eight
migrations. Native SQL tests execute all forward migrations as the restricted
migrator and exercise real allowed/denied runtime writes. Disposable test DBs are
distinct from preserved TaskManager_Codex_20261012. Current SQL CREATE USER docs
specify the managed identity client ID for SID; older general guidance differs.
Actual Azure managed-identity connection remains a live gate, not an offline claim.

Root added a sixth non-root SQL-bootstrap image target and fail-closed smoke case
to the existing container CI. Docker is absent locally; actual image verification
is pending CI. Existing five deployment units remain web/API/notifier/migrations/
sql-bootstrap; the legacy console image is local/compatibility only.

All three Bicep entrypoints and modules compiled with zero diagnostics. Read-only
review caught an incorrect Service Bus receiver role GUID despite valid Bicep;
the delivery agent is correcting it against the official built-in role catalog.
PowerShell boundary fixtures reached 18 passed; final release integration review
is pending. Compilation/fixtures do not stand in for ARM validation or what-if.

Business logs now omit task/project/team names, email addresses and raw provider
exceptions. A remaining concrete EF Update / SqlClient automatic-console leak
is being fixed in both ILogger and the separate native Serilog pipeline, with
regressions asserting that safe application failure-type messages still survive.

Candidate smoke uses the actual ARM latestRevisionFqdn, checked against the
declared revision suffix/environment domain. Candidate labels map revisions;
no label URL is fabricated. Preserve existing production traffic until smoke,
deactivate candidate workers on rollback, preserve shared resources and old DP
wrapping versions. No Azure calls, spending, model quality or live URLs claimed.

Full native SQL checkpoint: 857 total, 854 passed / 2 failed / 1 explicit local
RabbitMQ skip, 39 seconds. Telemetry now passes: the native Azure exporter caches
transmitters by connection string, so independent SDK fixtures require distinct
synthetic instrumentation keys. Original wire/privacy assertions are unchanged.
The two remaining regressions are compatibility of the fixed bad-password log
wording and native OptionsValidationException for an unsupported DP provider.
Coverage at this checkpoint: 88.9% lines (5249/5904), 74.16% branches (2782/3751).
Locked restore and full Release build passed with two existing xUnit bool-assert
warnings; no new warnings, gates, coverage exclusions or analyzers were disabled.
Offline Bicep compile/contract gate passed for foundation/jobs/apps. Release
promotion now requires exact-revision authenticated evidence beyond anonymous
guards; actual live SDK/SQL/mail/model evidence remains pending real inputs.

## Next action

Final native SQL backend gate: 856 passed / 0 failed / 1 explicit local RabbitMQ
skip (857 total, 44 seconds). All eight forward migrations, restricted runtime
SQL grants, privacy, native Azure SDK fixtures and existing compatibility tests
passed. Full dependency audit found no vulnerable direct/transitive packages.
Final local coverage: 88.91% lines (5252/5907), 74.08% branches (2782/3755).
Final review then found two bootstrap gaps: certificate identity overrides and
pre-existing excessive role permissions. Three direct regressions failed before
the fix. Bootstrap now rejects certificate overrides and verifies effective
database/object/column authority against the workload allowlists, including
inherited db_datawriter rights; unexpected role ownership also fails closed.
It does not revoke or rewrite existing principals. All 13 bootstrap tests passed
on native SQL after repair, including narrow valid permissions/real migrations.
An additional concrete native logging override regression failed, then both
ILogger/Serilog privacy tests passed after final-options suppression was applied.

Final offline delivery checks: 26 PowerShell behavior fixtures passed; native
compiled role positive case plus four corrupted GUID/scope cases passed. The
approved notification role fixture is now enforced by the actual Bicep CI gate.
All PowerShell ASTs and both workflow YAML actionlint checks passed. Cost report
freshness/completeness and exact-revision authenticated evidence are mandatory
before foundation/traffic authority. No offline result grants live promotion.
The only local skip requires an actual RabbitMQ instance; real broker CI remains
mandatory. Unsupported providers now use native options validation; the old host
exception contract and all malformed URI/client-ID assertions are preserved.

Finish release-script review and actual CI, then continue full-stack28. Azure live
validation/what-if/deployment/evaluation require real identity, approved resource
parameters, sender, model/quota and budget after all unblocked work is ready.
