# TaskManager Azure templates

`deployment-contract.json` is the shared infrastructure/delivery contract. The application roadmap remains `AI/plan.md`; deployment operations belong in `docs/azure-deployment.md`.

## Deployment boundaries

| Entry point | Resources and boundary |
| --- | --- |
| `foundation.bicep` | Dedicated identities, database/private connectivity, registry/environment or approved existing references, key ring, broker, realtime, AI/email references, monitoring and budget. It contains **no container apps or running jobs**. |
| `jobs.bicep` | Digest-pinned **manual** bootstrap and migration jobs. Defining a job does not execute it. Bootstrap `Prepare` runs before exactly one migration; `Finalize` reapplies/verifies the application grants afterward. Both must succeed before candidate apps. |
| `apps.bicep` | Candidate API and Notifier revisions, then Web using their actual returned revision FQDNs. Existing ingress weights remain explicit and unchanged. Promotion is a separate delivery action after smoke. |

The Notifier runs background consumers and scheduler even at zero ingress weight. Never apply `apps.bicep` before the database gates complete. Concurrent release/migration execution is forbidden by the delivery lock; zero automatic job retries cannot itself prevent an operator from starting a second execution.

Foundation owns five distinct user-assigned identities. API, Notifier, Migrator and SQLBootstrap have distinct SQL authority. The fifth Web identity has only registry pull and monitoring ingestion roles; Web receives no database, JWT, Data Protection, broker, AI or email configuration/roles.

## Parameters and existing resources

The two `foundation.parameters.*.example.json` files contain explicit `REQUIRED_*` markers. Replace every marker in an ignored local parameter file. The illustrative `monthlyBudget=100` and deployment capacity are **inputs, not a price estimate or a promise of sufficient quota**. Budget currency comes from the subscription's billing context. A blank `publicOrigin` intentionally selects the actual environment's canonical Web host; an operator-supplied custom origin requires valid DNS/certificate bindings in `webCustomDomains`.

Approved shared ACR, ACA environment, OpenAI account, ACS service or SQL server are declared `existing`. Modules never redeclare their configuration or delete their resources. They add only the agreed TaskManager permissions and dedicated database. Existing ACR's RBAC/ABAC mode is discovered rather than changed; repository ABAC conditions restrict pull access to `taskmanager/` using the documented **Request** attribute. ARM-audience image pull support must be verified for the actual ACR.

An existing ACA environment must already be external, use a compatible Consumption workload profile and be connected to the supplied VNet. Verify the actual infrastructure subnet matches the bounded `ingressSubnetCidr`; do not accept the sample CIDR for an existing environment. The approved private endpoint subnet must be in that network. Existing SQL/SignalR private DNS zones must already be linked correctly; existing zone resources/links are not overwritten. In a new environment, dedicated network/DNS resources are created in this deployment's resource group.

For a new dedicated SQL server, the separate SQLBootstrap identity is the Microsoft Entra administrator. Migrator is never an administrator. For an existing server, its administrator and network policy remain unchanged: the owner must authorize a concrete route granting the dedicated SQLBootstrap identity the required target-database bootstrap authority. Delivery fails closed without that prerequisite. ARM RBAC uses object IDs; native SQL mappings contain both object/client IDs, with explicit client-ID SID creation as required by the current specific `CREATE USER` documentation. No principal is silently replaced when its SID differs. Actual managed-identity connections remain a live gate.

The ordinary SQL FQDN remains `<server>.database.windows.net`; private DNS changes its routing. Connections use encrypted native managed identity authentication with certificate verification. Neither development SA credentials nor `TrustServerCertificate=True` are deployed.

Existing AI deployment/model/version, regional quota and native SDK structured-output/output-limit compatibility require verification. New model/deployment parameters have no invented defaults. The ACS sender must actually belong to a linked verified domain, with the supplied data geography; creating an ACS service does not verify a custom domain or deliver an email.

## Secrets, key ring and SignalR gates

Foundation does not generate, accept or output a JWT signing-key value. Trusted delivery creates a cryptographically random key only if absent, stores it privately in the dedicated Key Vault, and supplies a **versioned** `jwtSecretUri`. Only API and Notifier receive that reference. Routine deployment must not overwrite or rotate it. Rollback retains the prior secret version.

Data Protection uses a fixed full Blob URI, the shared `TaskManager` application name, explicit host managed identities and a **versionless** wrapping-key URI. Blob versioning and deletion retention are enabled. Key rotation does not expire/disable old wrapping versions; retain them while any persisted Data Protection key needs them. Cookie persistence across hosts/restarts and cloud authorization are separately verified by application/live tests.

Public Azure SignalR traffic permits only `ClientConnection`; Notifier server connections use its private endpoint and scoped App Server role. The service-side private endpoint connection name is generated by Azure. Initially the template permits no server connection names. Before apps start, delivery must read the actual connection, verify it points to `resourceIds.signalRPrivateEndpoint`, and configure that owned SignalR resource's private ACL. Preserve approved actual names in subsequent foundation parameters. The same gate configures exact public and expected candidate revision origins in SignalR CORS. Internet browsers require public SignalR client access after authenticated negotiation.

## Candidate and promotion

Candidate Web config includes the expected `https://<webAppName>--<candidateSuffix>.<actualEnvironmentDefaultDomain>` origin. **Smoke uses Web `latestRevisionFqdn` returned by ARM**, and requires exact equality to that configured origin. Gateway upstreams likewise use the actual returned candidate API/Notifier revision FQDNs. It must never smoke an old revision reached through canonical weighted backend endpoints.

Label `candidate` identifies the new revision but is not a smoke URL. Current Microsoft `connect-apps` documentation describes `APP---LABEL`, whereas earlier assumptions used `LABEL---APP`. Pinned Azure CLI 2.91.0 label commands return traffic mappings, not URLs. No label-derived hostname is claimed as ARM metadata or proof of routing. Actual revision FQDN, DNS/TLS, authentication and deployment identity/image matching are verified before acceptance.

On a first deployment, nonempty bounded operator CIDRs restrict external Web ingress until promotion. Existing deployments preserve their current ingress restrictions and certificate bindings. Prior revision weights are copied exactly, and the old `candidate` label is removed while its weight is preserved; the new candidate gets weight zero. No traffic record uses `latestRevision:true`. First deployment has no old revision and uses candidate weight100 behind the operator network gate. Removing that gate or changing public traffic is a separate promotion step after smoke.

Notifier has at least one replica. After promotion or rollback, explicitly deactivate the undesired Notifier revision: changing its ingress weight does not stop its workers. Runtime SQL leases/dedup protect overlapping compatible revisions; they do not make incompatible deployment contracts safe. Database changes must follow expand/contract; application rollback is not a database rollback.

## Offline evidence

These templates use stable API schemas and compile with the pinned standalone Bicep0.48.1. Example PowerShell invocation (no Azure call):

```powershell
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path (Get-Location).Path '.local/bicep-runtime'
foreach ($templateName in 'foundation', 'jobs', 'apps') {
    & ./.tools/bicep/bicep.exe build "infra/azure/$templateName.bicep" --outfile ".local/azure/$templateName.compile.json"
    if ($LASTEXITCODE -ne 0) { throw "Bicep compilation failed: $templateName" }
}
```

Native compilation/linting succeeded without diagnostics during implementation. Compiled files and release state stay under ignored `.local/`. This proves schema/compiler compatibility, not Azure deployment success. Provider registration, resource permissions, ACR pull propagation, actual private DNS/TLS/SQL MI authorization, AI quota/model, email delivery, telemetry privacy and live smoke require the authorized Azure environment. Missing tenant/subscription/region/budget/provider inputs are reported together by preflight before cloud mutations.

## Primary references

- [Stable ACA apps](https://learn.microsoft.com/en-us/azure/templates/microsoft.app/2025-07-01/containerapps), [manual jobs](https://learn.microsoft.com/en-us/azure/container-apps/jobs), [ingress forwarding](https://learn.microsoft.com/en-us/azure/container-apps/ingress-overview).
- [Native SQL user creation](https://learn.microsoft.com/en-us/sql/t-sql/statements/create-user-transact-sql?view=azuresqldb-current#k-create-a-contained-database-user-from-a-microsoft-entra-principal-without-validation), [SQL private endpoint routing](https://learn.microsoft.com/en-us/azure/azure-sql/database/private-endpoint-overview?view=azuresql).
- [ASP.NET Data Protection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0), [SignalR network ACL](https://learn.microsoft.com/en-us/azure/azure-signalr/howto-network-access-control), [native ACS managed-identity email](https://learn.microsoft.com/en-us/samples/azure-samples/communication-services-dotnet-quickstarts/email-sample-send-email-with-managed-identity/).
- [ACR ABAC Request conditions](https://learn.microsoft.com/en-us/azure/container-registry/container-registry-rbac-abac-repository-permissions), [ACA inter-app/revision DNS](https://learn.microsoft.com/en-us/azure/container-apps/connect-apps), [pinned CLI label implementation](https://github.com/Azure/azure-cli/blob/azure-cli-2.91.0/src/azure-cli/azure/cli/command_modules/containerapp/custom.py).
