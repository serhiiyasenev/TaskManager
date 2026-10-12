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

## Next action

Root releases disjoint implementation after canonical infra/script inputs are
agreed. Run valid runtime/proxy/SQLpermissions RED before behavior fixes, compile
actual Bicep locally and in Linux CI, and keep cloud validation/what-if/live
gates explicitly pending actual parameters. Continue full-stack28 on native SQL.
