# Full-stack verification

## What each gate proves

| Gate | Actual boundary | Remaining boundary |
| --- | --- | --- |
| `scripts/fullstack/tests/Run.ps1` | Native PowerShell child process startup/EOF/timeout, private state validation, database namespace guards, aggregate result validation and explicit live SKIP | Does not start SQL, product hosts or browsers |
| `Run.ps1 -Mode Controlled` | Actual HTTPS Web/API/Notifier programs, Identity cookie/CSRF/mail tokens, disposable SQL migrations/business writes, local private SignalR and product browser requests | AI/email fixtures and explicitly controlled transport; no Rabbit/Azure claim |
| `Run.ps1 -Mode RabbitMQ` | The same product stack plus native disposable Rabbit producer/listener/settlement | AI/email fixtures; no Azure identity/adapters/live model claim |
| Real Azure candidate smoke | Exact immutable deployed revisions, controlled real accounts/mail and actual adapters | Requires the real environment and retained actual evidence; local gates cannot substitute |

The host is a **test-only** project in `tools/TaskManager.FullStackHost`. Production container targets do not include it. It loads the actual .NET10 WebAPI, Notifier and WebHost programs through native Kestrel. Browser tests use `tests/frontend/playwright.fullstack.config.ts` without HTTP interception or a fake frontend server. The UI/API assertions exercise actual Identity and SQL flows. Fixture AI/email providers are explicit and are available only in the test host.

The host creates only `taskmanager_e2e_test_<32 lowercase GUID hex>` databases using the explicit SQL master server connection and applies the existing migrations. It never migrates or deletes `TaskManager_Codex_20261012` or another application database. The runner validates this namespace and binds it to the private run directory. Windows Schannel uses a uniquely named current-user nonexportable CNG key for the ephemeral certificate. The key is deleted and its absence verified after actual hosts stop. Linux uses an in-memory RSA key. Neither path adds a certificate store or trust entry.

## Prepare and run

Use x64 PowerShell7 on Windows or Linux, the SDK locked by `global.json`, Node locked by `.node-version`, locked npm dependencies and a prepared production frontend `dist`. The new workflow uses immutable action SHAs, locked PowerShell packages and digest-pinned SQL2022/RabbitMQ4.3.6 images recorded in `scripts/fullstack/versions.json`.

Prepare dependencies/builds explicitly before verification; no package lock is changed by the runner:

```powershell
dotnet restore tools/TaskManager.FullStackHost/TaskManager.FullStackHost.csproj --locked-mode -warnaserror:NU1901,NU1902,NU1903,NU1904
dotnet build tools/TaskManager.FullStackHost/TaskManager.FullStackHost.csproj --configuration Release --no-restore -warnaserror:NU1901,NU1902,NU1903,NU1904
npm ci --ignore-scripts
npm run build:frontend
pwsh -NoProfile -File scripts/fullstack/tests/Run.ps1
pwsh -NoProfile -File scripts/fullstack/tests/Live.ps1
```

Supply `TASKMANAGER_FULLSTACK_SQL` as an environment variable from private configuration. It must name an explicit SQL server and use `Database=master`/`Initial Catalog=master` if a database keyword is present. The owner must allow creating/dropping the uniquely named disposable test database. Do not place credentials in the CLI or paste them into logs. No server/credential is inferred from production appsettings. Without the connection, the runner reports **SKIP** and returns2 before starting a host/database/browser.

Windows without RabbitMQ selects Controlled explicitly:

```powershell
$env:TASKMANAGER_FULLSTACK_SQL = Get-Content -LiteralPath '.local/fullstack-inputs/sql-master.txt' -Raw
$env:TASKMANAGER_UI_BROWSER = 'chrome' # Existing installed Chrome; no user profile.
pwsh -NoProfile -File scripts/fullstack/Run.ps1 -Mode Controlled -NoBuild
```

Run these commands from the repository root. The production UI is
`src/frontend/dist`; browser suites and configuration are in `tests/frontend`.
The root npm lockfile covers both workspaces, with tooling resolved from root
`node_modules`.

The runner's default without `-NoBuild` performs an explicit sequential locked restore/build of the test host only. Frontend dependency/build preparation remains explicit. `-NoBuild` requires existing verified host/UI artifacts and never silently builds or restores them. Build/host/browser stdout and stderr are asynchronously drained into `Stream.Null`; raw SQL/Identity/mail/control-token exception output is not persisted or forwarded. Setup failure diagnostics therefore use safe failure types and sanitized browser counts rather than raw private logs.

Linux CI uses RabbitMQ mode with these explicit process environment variables:

```text
TASKMANAGER_FULLSTACK_RABBIT_HOST=127.0.0.1
TASKMANAGER_FULLSTACK_RABBIT_PORT=<disposable loopback port>
TASKMANAGER_FULLSTACK_RABBIT_USER=<disposable CI user>
TASKMANAGER_FULLSTACK_RABBIT_PASSWORD=<private disposable password>
```

Then run `pwsh -NoProfile -File scripts/fullstack/Run.ps1 -Mode RabbitMQ -NoBuild`. The workflow uses disposable services with scoped per-run queue names derived from the owned database GUID, including retry/dead-letter queues. Those published CI-only test credentials are never reused by deployment or the application. The test host's queue cleanup must complete before PASS. Existing unit/frontend/container gate thresholds remain unchanged.

## Private state, control and cleanup

For the actual broker on this Windows machine, use
[native Windows verification](native-windows-verification.md). It prepares a
workspace-only broker process and runs both the full backend suite and this
RabbitMQ browser gate without a system service installation.

Each run creates `.local/fullstack/<32 hex run ID>/state.json` with owner-only directory/file permissions. The host requires the absolute repository-local path, exact filename and fresh non-existing state file. State is written atomically after startup and contains schema1, mode `controlled-full-stack`, actual loopback HTTPS `baseUrl`, loopback HTTP `controlUrl`, random `controlToken`, exact owned `databaseName`, provider `fake`, and transport `controlled|rabbitmq`. It contains no SQL connection or JWT value. The browser receives only the state path/run ID, not inherited server credentials.

Test-only loopback control endpoints require `X-FullStack-Control` and never enter production routing. They capture synthetic confirmation/reset mail, control a bounded clock, select fixture AI failure modes, trigger the actual outbox dispatcher and return aggregate counts. Never upload their token, mailbox response, confirmation/reset URLs or private state.

The runner keeps host stdin open throughout browser execution, then closes it to request graceful shutdown on both OSes. The host stops actual servers, removes its exact owned database/queues/private key files and writes sibling `cleanup.json`:

```json
{"schemaVersion":1,"mode":"controlled-full-stack-cleanup","databaseName":"<exact owned test name>","databaseDropped":true,"serversStopped":true,"brokerQueuesDeleted":true}
```

PASS requires graceful exit0 and this actual cleanup proof bound to the same database. Startup/browser/shutdown timeouts are bounded. A forced process-tree kill cannot run a guaranteed `finally`; it produces **FAIL / cleanup unverified**, never a false cleanup success. Private `runner-result.json` records only run ID, explicit provider/transport, result and cleanup status. The credential-bearing state file is removed after shutdown. The operator can inspect the run-folder GUID/cleanup metadata and clean only `taskmanager_e2e_test_<that GUID>` using the same authorized master connection if forced termination left it behind; never run wildcard database deletion.

## Results and privacy

The Playwright reporter writes `tests/frontend/test-results/fullstack-summary.json` with schema1, kind `controlled-full-stack-browser`, current run ID, provider `fake`, explicit transport and total/passed/failed/skipped/flaky counts. It updates these safe aggregates after each case. A bounded optional `failedCases` array records only numeric case index, fixed project/status and a location in the owned journey source; it excludes titles, URLs, account details, error messages and stacks. The runner rejects missing/stale/empty/skipped/flaky/failing summaries. Screenshots use fixed synthetic product-page names under `tests/frontend/test-results/fullstack-screenshots/`. Only that resolved owned directory is cleared before a new run, preventing stale screenshot uploads; accepted screenshot sets elsewhere remain untouched.

The full-stack browser config has one worker, retries0, desktop1440×900 and mobile390×844. Traces/video/automatic failure screenshots are disabled. `PLAYWRIGHT_NO_COPY_PROMPT=1` prevents the native reporter from capturing DOM snapshots. Native `error-context.md` attachments can still include raw error text; the reporter removes only those exact owned files immediately and performs a bounded contained scan at begin/end. `.github/workflows/fullstack.yml` uploads only the sanitized aggregate JSON and explicitly captured synthetic product PNGs. It never uploads `.local`, raw browser output, mail/cookies/control tokens, environment files or traces. The job has no cloud secrets, environment deployment or OIDC `id-token` permission.

Runner fixtures use actual native synthetic child processes. That fixture PASS is not a product/SQL/browser PASS. The separate [Windows Controlled evidence](modernization/fullstack-evidence/README.md) records six actual product browser cases and verified native database/listener/key cleanup. Its explicit fixture AI/email/transport do not prove Azure adapters.

## Real Azure prerequisites and acceptance

Azure deployment and verification are deferred by the user. The future opt-in remains off. No actual nine-check Azure evidence or cloud PASS has been produced.

```powershell
pwsh -NoProfile -File scripts/fullstack/Run.ps1 -Mode LiveAzure
```

This mode reports **SKIP2 before any network call** unless all reviewed real files, explicit boolean authorization, nine native probe implementations and their private access inputs/credential environment values are supplied. Fields being present are insufficient. Invalid digests, unsafe source paths, malformed reports or uncertain/failed execution produce FAIL1. With complete reviewed inputs, `Test-LivePrerequisites.ps1` executes `live/Run.ps1` in an actual bounded native process; it is no longer an unconditional prerequisite-only SKIP.

The executable runner implements actual CLI tenant/subscription binding, release phase/job/manifest binding, active candidate revision/image/label/FQDN verification, exact gateway candidate destinations, production Azure provider/MI/origin configuration, actual sender/model/private-endpoint metadata, and HTTPS anonymous readiness/auth/CSRF/hub guards with no-store/no-referrer. These independent checks write `candidate-scope-checks.json` with `authenticatedNineChecksCompleted:false`. They do not count as any of the nine authenticated/native checks. Successful CLI login, ARM configuration or anonymous health never become native probe PASS.

The existing `tools/TaskExtraction.Evaluation` live30-case evaluation remains disabled unless `--run-live`, real Azure provider/access/deployment, complete token reservations and explicit input/output pricing/currency/cost caps are supplied. No fixture model result is described as live model quality.

Provide one reviewed bundle:

- Tenant/subscription/actual region, protected GitHub environment and exact inspected federated subject.
- Exact immutable candidate release/revision URLs and approved product/shared resource IDs/network routes.
- Verified billing budget/currency/contacts and current region-based retail worksheet with explicit usage assumptions.
- Available Azure OpenAI deployment/model/version/quota and native managed identity permission.
- Linked verified ACS sender/domain/data geography and controlled real accounts with an authorized test mailbox.
- SQL MI/private DNS, Service Bus, SignalR, Blob/Key Vault Data Protection and Azure Monitor permissions/routes.
- Explicit authorized live browser/model runner and retained sanitized evidence with trace correlation, without private notes/mail tokens.

### Native probe dependencies still outstanding

No concrete native probe implementation is supplied under `scripts/fullstack/live/probes/`. The executor/protocol cannot supply the deployment, accounts or collectors. Each implementation must be reviewed against this acceptance before an authorized future run:

| Exact check / script name | Required actual operations | Missing concrete implementation/access |
| --- | --- | --- |
| `browserCookieCsrf` | Real browser cookie/CSRF, registration-confirmation-login/logout, reset/lockout and anonymous denial | Deployed exact candidate, controlled accounts/passwords, real authorized mailbox recovery and browser workflow |
| `teamDataIsolation` | Two-team reads/writes, wrong-email invitation, membership/role revocation and denied stale rights | Real independent team accounts and owned test-data setup/cleanup browser/API workflow |
| `emailConfirmationRecovery` | ACS delivery to actual mailbox, confirmation/reset links and password recovery | Verified sender plus authorized real mailbox retrieval mechanism; no collector endpoint is invented |
| `sqlManagedIdentity` | Actual API/Notifier MI operations, restricted grants and committed owned SQL state | Approved private DNS/network route, MI execution and read-only SQL verification/access mechanism |
| `serviceBusDelivery` | Actual SDK publication/settlement/retry/idempotency and committed durable inbox | Approved namespace/queues, native message/SQL observations, owned test-data cleanup and a concrete collector |
| `signalRAuthenticatedInbox` | Real Azure SignalR authenticated hint/refetch/reconnect/read and established-connection revocation | Actual controlled browser connection, membership/password revocation and native delivery observations |
| `azureOpenAiReviewCommit` | Actual model extraction, immutable evidence, edited/partial/idempotent commit and unavailable fallback | Reviewed live deployment/model/quota/access, cost reservations and synthetic authorized notes/corpus; real inference workflow |
| `persistentDataProtectionRestart` | Session/token survival across actual host restart plus stamp revocation | Explicit candidate restart authority, Blob/Key Vault MI route, bounded restart/recovery procedure and owned accounts |
| `telemetryPrivacy` | Actual exported trace/metric/console sentinel absence, safe correlation and diagnostics | Real Azure Monitor/Log Analytics read route, approved queries, ingestion wait bound and concrete privacy collector |

Model30-case scores additionally require the accepted corpus and measured raw/validated results with uncertainty retained. Local fixture quality scores or ARM model availability are insufficient.

### Future live configuration

Keep configuration/access inputs private; never put credentials in arguments. The reviewed top-level JSON has exactly `schemaVersion:1`, `authorizeLiveExecution:true` (a boolean), `commitSha` (40 lowercase hex), `deploymentConfigurationPath`, `deploymentConfigurationSha256`, `releaseStatePath`, `releaseStateSha256`, `azureCliPath`, and `nativeProbes`. Review the exact deployment/journal file SHA256 values. The journal must be the immutable release still in `CandidateAwaitingAuthenticatedSmoke`, with both bootstrap executions and the one migration succeeded.

`nativeProbes` has exactly the nine keys above. Each value has exactly:

```json
{"path":"scripts/fullstack/live/probes/<check>.ps1","sha256":"<reviewed 64 lowercase hex>","accessConfigurationPath":"<private reviewed native access JSON path>","accessConfigurationSha256":"<reviewed 64 lowercase hex>","requiredEnvironment":["TASKMANAGER_LIVE_<EXPLICIT_CREDENTIAL_NAME>"]}
```

The access-file schema depends on the reviewed native implementation; the executor does not infer routes or turn presence into proof. `requiredEnvironment` can be empty if that implementation uses an authorized native MI/login route. Otherwise each listed private environment variable is mandatory. Only that probe's allowlisted `TASKMANAGER_LIVE_*` values and the authorized Azure login configuration directory are passed; raw child stdout/stderr are drained to `Stream.Null`.

The only executable is the current PowerShell process and the only allowed source is the exact named regular repository `.ps1` file under `live/probes/`, matching its reviewed digest. Links/junctions, controlled/test scripts, arbitrary shell command strings and custom argument arrays are rejected. Source/access/release/config hashes are checked again before execution; source/result/evidence hashes are checked again before final evidence composition. Each probe has a 180-second bound; the checker bounds the entire native run to 1800 seconds. A timeout is FAIL, without promotion.

### Native probe protocol

Each reviewed script accepts the fixed arguments `-ContextPath <private JSON> -ResultPath <fresh private JSON>`. The context provides schema/kind/check, fresh run ID, exact commit/origin/revisions, explicit Azure/nonfixture identity, deployment/journal/access/CLI paths and exact private artifact path. It must execute actual operations, clean up its explicitly owned test data, exit0 only after all required observations pass, and write fresh files inside `.local/fullstack/<runId>/`.

The result has exactly these fields:

```text
schemaVersion: 1; kind: live-azure-native-probe; check: <exact nine-check key>
runId: <current 32 lowercase hex>; commitSha; origin; candidateRevisions: {web,api,notifier}
provider: Azure; syntheticFixtures: false (boolean); status: Passed
nativeOperationCount: <positive integer>; startedAtUtc; completedAtUtc
evidenceFile: <check>.evidence.json; evidenceSha256: <SHA256 of actual artifact bytes>
```

Timestamps must be UTC and inside the observed native process execution interval, with fresh file times. The evidence artifact has exactly `schemaVersion:1`, `kind:live-native-observations`, `check`, the same run/commit/origin/revisions, `provider:Azure`, `syntheticFixtures:false`, and `observations` (1–1000 entries). Each observation has exactly `name` (bounded code), `status:Passed`, and `correlationSha256` (hashed native correlation). Unknown fields, raw notes/mail URLs/cookies/tokens/error text, stale/wrong-scope records and hash mismatches are rejected. Semantic proof depends on the reviewed actual probe implementation, not its claimed operation count.

Only nine successfully executed/registered native processes with unchanged validated artifacts can compose `authenticated-candidate.json`; a dictionary of statuses, a direct protocol-validator call, local fake provider or scope-only checks cannot. The actual candidate is rechecked after probes/restarts. Files remain private and are not uploaded by local CI. These protocol fixtures use synthetic bytes and never attest a cloud run.

After a future authorized run with real implementations/inputs:

```powershell
pwsh -NoProfile -File scripts/fullstack/Run.ps1 -Mode LiveAzure -LiveConfigurationPath '<private reviewed live JSON>'
# Review retained private evidence, then explicitly record using the existing gate:
pwsh -NoProfile -File scripts/azure/Record-AuthenticatedSmoke.ps1 -ReleasePath '<exact private release journal>' -EvidencePath '<private run>/authenticated-candidate.json'
```

Use [the Azure delivery runbook](azure-deployment.md) for exact-revision proof, cost/what-if/release ordering and separate promotion. Its unchanged authenticated gate requires all nine fresh actual checks/evidence hashes. The executor never records evidence in the release journal, changes traffic, deploys resources or calls Promote. Controlled local state has provider `fake`/loopback origin and cannot satisfy the gate. No real native probes/inputs/cloud run currently exist; future opt-in capability is not live Azure acceptance.

## Backend solution and host content roots

The canonical solution is `src/backend/TaskManager.sln`. Run the documented
commands from the repository root. The runner starts the .NET test-host child in
its build-output directory, where ASP.NET testing reads the generated
`MvcTestingAppManifest.json`. The manifest supplies actual application source
roots before the factory configures each host. This avoids dependency on an
ancestor solution file; browser working directories and private paths keep their
existing explicit locations. A missing manifest requires an explicit host build.
