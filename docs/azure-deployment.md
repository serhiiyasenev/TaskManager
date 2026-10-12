# Azure delivery runbook

## Evidence and prerequisites

The delivery contract is `infra/azure/deployment-contract.json`. The scripts support PowerShell 7 on x64 Windows and Linux. Native Bicep compilation and synthetic CLI fixture checks are offline evidence. They do not prove an Azure deployment, DNS, permissions, quota, actual email delivery or authenticated cloud E2E.

Supply an authorized tenant/subscription, dedicated product resource group, actual Azure region, positive monthly budget with real contacts, verified ACS email domain/sender/data geography, actual OpenAI deployment/model/version/capacity, and the smoke operator's public CIDR. `scripts/azure/config.example.json` deliberately contains invalid `REQUIRED_*` markers and budget0; it is not a runnable environment or cost estimate. Keep the completed configuration under ignored `.local/azure/`. A blank `publicOrigin` selects the actual canonical ACA Web host. A custom domain requires real DNS/TLS and preserved `webCustomDomains`; configure that approved binding before relying on custom-origin smoke.

Existing resources are explicitly referenced. Shared ACR settings, environment networking, SQL administrator, AI deployment and ACS domain are not silently reconfigured. Existing ACR permission mode/ARM-audience authentication and external Consumption environment/VNet/subnet/CIDR/private DNS links are inspected before deployment. A private-only ACR without a separately approved private build route is rejected. Cross-subscription shared references require a separately reviewed contract. The existing SQL owner must authorize the dedicated bootstrap identity in the **product database**, as described below.

Provisioning needs reviewed ARM deployment/resource permissions and narrowly authorized role assignments; runtime identities receive their template-defined roles. The delivery principal additionally needs ACR repository push rights appropriate to the registry's actual RBAC/ABAC mode and create/read-version metadata rights for the owned JWT secret. The script does not grant itself subscription-wide administrator authority. Provider registration, quota and RBAC propagation must be verified in the actual subscription. Failures stop delivery before apps/traffic.

## Pinned tools and offline gates

`scripts/azure/tool-versions.json` locks Microsoft package versions, official asset SHA256 hashes and action commit SHAs. Tools install inside ignored `.tools/azure/`, without a system MSI/profile change. Bicep single-file extraction stays in `.local/azure/` rather than a sandbox-protected AppData directory. Docker must already provide a working Linux-container engine. Buildx0.38.0 and BuildKit0.34.0 are pinned; the builder image is locked to its verified registry digest.

Run from the repository on either supported OS:

```powershell
$tools = ./scripts/azure/Install-Tools.ps1 -Tools Bicep,AzureCli,PowerShell
& $tools.PowerShell -NoProfile -File scripts/azure/tests/Run.ps1
& $tools.PowerShell -NoProfile -File scripts/azure/Test-Deployment.ps1 -OfflineOnly -BicepPath $tools.Bicep
```

The test invoker supplies explicit synthetic replies and exercises immutable manifests, OIDC context, what-if ownership, exact execution polling, failed migration gates, key metadata reuse, shared registry mismatch and repeat-foundation SignalR preservation. It performs no Azure authentication or deployment. `.github/workflows/azure-validate.yml` runs these checks on Windows and Linux without `id-token` permission or cloud credentials. Existing backend/frontend/container gates remain separate.

## Trusted OIDC workflow

Use `.github/workflows/azure-deploy.yml` only through `workflow_dispatch` on `main`. Protect `taskmanager-staging` and `taskmanager-production` GitHub environments with required reviewers and a main-only deployment branch policy. The workflow has environment-scoped concurrency with `cancel-in-progress:false`; do not run independent local deployments concurrently with it. PR workflows never receive cloud credentials. No `pull_request_target` or arbitrary source checkout is used.

Set protected environment variables `AZURE_CLIENT_ID`, `TASKMANAGER_AZURE_CONFIG_JSON` and `TASKMANAGER_AZURE_PRICING_INPUTS_JSON`. Configure the federated identity with GitHub issuer, audience `api://AzureADTokenExchange` and the **actual inspected exact subject** copied into `expectedFederatedSubject`. Do not assume the classic `repo:owner/repo:environment:name` format: GitHub's current defaults may include immutable owner/repository IDs after repository creation, rename or transfer. `Inspect-Oidc.ps1` compares the actual token subject, repository, main ref, event, environment and workflow ref; Azure Login then validates the signed federation. Decoding a payload is a configuration check, not token verification. [GitHub OIDC claims](https://docs.github.com/en/actions/reference/security/oidc), [Azure federation](https://docs.github.com/en/actions/how-tos/secure-your-work/security-harden-deployments/oidc-in-azure).

Cloud login remains interactive for authorized local operations, or OIDC in the protected workflow. Use isolated `AZURE_CONFIG_DIR`/`DOCKER_CONFIG` inside `.local/azure/`; never print access tokens, enable CLI debug, pass JWT values in command arguments, or upload raw parameter files/logs.

## Current-region cost review

Complete `scripts/azure/pricing-inputs.example.json` with exact current Retail API meter IDs, actual meter regions/units and explicit monthly quantity formulas. Include **ContainerApps, SqlDatabase, Registry, ServiceBus, SignalR, OpenAI, CommunicationEmail, KeyVault, BlobStorage, Monitor, PrivateEndpoints, PrivateDns, Bandwidth**. Multiple lines are needed for CPU/memory/requests, tokens, logs, transactions and egress. Include overlapping candidate/prior revisions, always-running Notifier replicas and the three job executions. For example, a CPU-seconds meter quantity is `replicas × vCPU × measured active seconds`; hours must be converted when the meter uses seconds. No real amount is supplied without region/meter/usage inputs.

```powershell
& $tools.PowerShell -NoProfile -File scripts/azure/Get-CostEstimate.ps1 `
  -ConfigurationPath .local/azure/config.json -PricingInputsPath .local/azure/pricing-inputs.json `
  -OutputPath .local/azure/cost-estimate.json
```

The helper fetches live region-filtered [Retail API](https://learn.microsoft.com/en-us/rest/api/cost-management/retail-prices/azure-retail-prices) pages, requires an unambiguous current exact meter, validates units, records timestamp/effective date/formula and fails on missing prices/service categories or over-budget estimates. It compares USD only against an explicitly verified USD billing budget. Another currency needs a reviewed conversion contract. Retail rates are not contracted invoice prices. Shared charges/free grants cannot be attributed independently, so the helper subtracts no free grant. Workload quantities remain assumptions. Review the report before any `foundation`/`release` operation.

The Bicep budget sends alerts; **it does not cap spending**. Evaluation/notification is delayed. Use resource SKU/capacity/replica limits, AI input/output/rate quotas, retention limits and measured usage in addition to alerts. [Azure budgets](https://learn.microsoft.com/en-us/azure/cost-management-billing/costs/tutorial-acm-create-budgets).

## Foundation, images and database ordering

Replace `$commitSha` below with the full clean trusted-main commit. Create/approve the dedicated resource group beforehand. Every ARM template gets validation and what-if before deployment. Delete/unknown/unevaluated owned changes and shared parent mutations are blocked. Cross-RG product database/role/module children require **exact reviewed** `approvedOwnedLeafResourceIds`; there is no broad shared-RG exception. Review the private selected what-if inventory and the compiled source. ACS role definitions are checked for the precise native Read/Write permissions without DataActions. [What-if limits](https://learn.microsoft.com/en-us/azure/azure-resource-manager/bicep/deploy-what-if).

```powershell
$configPath = '.local/azure/config.json'
& $tools.PowerShell -NoProfile -File scripts/azure/Deploy.ps1 -ConfigurationPath $configPath `
  -CommitSha $commitSha -AzureCliPath $tools.AzureCli -BicepPath $tools.Bicep -Mode Validate
& $tools.PowerShell -NoProfile -File scripts/azure/Deploy.ps1 -ConfigurationPath $configPath `
  -CommitSha $commitSha -AzureCliPath $tools.AzureCli -BicepPath $tools.Bicep -Mode Foundation
# $stateDirectory is the actual .local/azure/<environmentName>/<commitSha> directory.
& $tools.PowerShell -NoProfile -File scripts/azure/Build-Images.ps1 -ConfigurationPath $configPath `
  -FoundationPath "$stateDirectory/foundation.json" -CommitSha $commitSha `
  -AzureCliPath $tools.AzureCli -ManifestPath "$stateDirectory/images.json"
& $tools.PowerShell -NoProfile -File scripts/azure/Deploy.ps1 -ConfigurationPath $configPath `
  -CommitSha $commitSha -AzureCliPath $tools.AzureCli -BicepPath $tools.Bicep -Mode Release
```

Foundation contains no apps/running jobs. A new registry must exist before images can be pushed. Five fixed product repositories are built for linux/amd64 and verified against actual ACR digest metadata; candidate jobs/apps consume only digest references. No mutable tag reaches a deployment parameter.

For existing SQL, foundation saves `owner-authorize-bootstrap.sql`. The existing administrator reviews and runs it against exactly the returned product database. It creates/verifies the dedicated identity's client-ID SID and grants CONTROL on that database; it neither changes the server administrator nor touches another database. Then set `operatorAuthorizedSqlBootstrapRoute` to `DedicatedBootstrapIdentityGrantedInTargetDatabase`. This acknowledgment alone is not proof: the actual bootstrap MI connection must succeed. Current specific [CREATE USER section K](https://learn.microsoft.com/en-us/sql/t-sql/statements/create-user-transact-sql?view=azuresqldb-current#k-create-a-contained-database-user-from-a-microsoft-entra-principal-without-validation) defines the explicit client-ID SID; ARM role assignments use object IDs.

Release queries actual approved SignalR private endpoint association, configures the owned Deny-default ACL with public ClientConnection/private ServerConnection, and exact public/candidate origins. Repeat foundation hydrates current verified ACL/CORS metadata before applying templates; default empty arrays must not remove a live server route. JWT creation uses CSPRNG48bytes via a private file only when no version exists; routine deploy reuses a valid **versioned** URI and never reads/prints or overwrites the value. Ordinary deployment is serialized; Key Vault SET is not an atomic create-if-absent operation. Data Protection's wrapping-key URI remains **versionless**; retain old key versions required to decrypt persisted keys.

Database sequence is mandatory: bootstrap **Prepare** → **one migration execution** → bootstrap **Finalize** → candidate apps. Manual jobs have one replica, one completion and zero automatic retries. Before each start the script rejects active executions, saves StartRequested, then persists/polls the exact returned execution name. A timeout/CLI interruption is an uncertain outcome; re-running `Release` refuses existing state. Inspect that exact execution with `Get-DeploymentDiagnostics.ps1`; never submit a second migration because another execution or the CLI timeout looked incomplete. CI concurrency plus native EF migration locking provide complementary protection. [ACA manual executions](https://learn.microsoft.com/en-us/azure/container-apps/jobs), [EF migration locking](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-9.0/whatsnew#protection-against-concurrent-migrations).

## Candidate, authenticated gate and promotion

API/Notifier candidate modules complete before Web uses their actual revision FQDNs. Web smoke uses its actual ARM revision FQDN and requires exact equality to the configured expected origin; `candidate` is checked as a revision mapping and is never treated as a returned URL. Existing traffic weights, IP restrictions and certificate bindings are captured/preserved; no `latestRevision` traffic shortcut exists. First deployment uses weight100 behind bounded operator CIDRs. The script verifies actual images/revisions/labels and live anonymous statuses/privacy headers. It then records `CandidateAwaitingAuthenticatedSmoke` and **cannot promote**.

Step28's actual controlled-account runner must provide schema1 evidence with `kind=live-authenticated-candidate`, `syntheticFixtures=false`, exact `commitSha`, actual candidate `origin`, all three exact `candidateRevisions`, `completedAtUtc`, and these checks containing `status=Passed` plus SHA256 of retained concrete evidence: `browserCookieCsrf`, `teamDataIsolation`, `emailConfirmationRecovery`, `sqlManagedIdentity`, `serviceBusDelivery`, `signalRAuthenticatedInbox`, `azureOpenAiReviewCommit`, `persistentDataProtectionRestart`, `telemetryPrivacy`. Do not place passwords, cookies, notes, source text or mail tokens in the summary. Each gate must be genuinely exercised; anonymous health, mocked UI or ARM metadata cannot substitute. The record command validates bindings and freshness; it **does not execute or prove those checks itself**.

```powershell
& $tools.PowerShell -NoProfile -File scripts/azure/Record-AuthenticatedSmoke.ps1 `
  -ReleasePath "$stateDirectory/release.json" -EvidencePath "$stateDirectory/authenticated-candidate.json"
& $tools.PowerShell -NoProfile -File scripts/azure/Promote.ps1 -ConfigurationPath $configPath `
  -ReleasePath "$stateDirectory/release.json" -AzureCliPath $tools.AzureCli
```

Both smoke records must be fresh within30minutes. Promotion rechecks exact revision/digest/label and captured traffic before changing explicit weights. First-deploy restrictions are removed only if they still match the exact recorded smoke policy. Existing production restrictions stay intact. Canonical smoke follows promotion; previous Notifier workers are explicitly deactivated. Zero HTTP weight is insufficient to stop a consumer/scheduler.

For workflow promotion/rollback, supply the exact full `release_sha` and completed trusted `release_run_id`. The workflow verifies source repository/event/main/commit/workflow path before downloading its immutable artifact. The allowlisted journal contains revision/traffic/job IDs and evidence hashes, not runtime connection strings, JWT values, telemetry keys or raw CLI payloads. A release without authenticated evidence stays blocked. Step28 can record its actual evidence before journal export; no synthetic result grants traffic authority.

## Rollback and diagnostics

```powershell
& $tools.PowerShell -NoProfile -File scripts/azure/Rollback.ps1 -ConfigurationPath $configPath `
  -ReleasePath "$stateDirectory/release.json" -AzureCliPath $tools.AzureCli
& $tools.PowerShell -NoProfile -File scripts/azure/Get-DeploymentDiagnostics.ps1 -ConfigurationPath $configPath `
  -ReleasePath "$stateDirectory/release.json" -AzureCliPath $tools.AzureCli `
  -OutputPath "$stateDirectory/diagnostics.json"
```

Rollback first deactivates/verifies the candidate Notifier, then reactivates captured old revisions, restores their explicit traffic and original ingress policy, and deactivates candidate Web/API. A first deploy has no old application revision and remains unavailable behind the smoke restriction. Schema migrations remain applied: rollback requires expand/contract-compatible applications, not an automatic database downgrade. If rollback/cleanup fails, state is uncertain and another deployment is blocked until the exact candidate worker/executions are inspected. Diagnostics select health/replica/execution metadata without fetching raw application logs or secrets.

No cloud deployment, authenticated Azure smoke or current project cost total has been claimed from offline gates. Missing real subscription/region/budget/federation/provider/account inputs are the remaining live-environment boundary.
