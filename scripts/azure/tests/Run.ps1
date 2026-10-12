# These are local delivery-contract checks with explicit synthetic CLI replies.
# They do not authenticate, deploy resources, run SQL or claim a cloud smoke PASS.
#requires -Version 7.4
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../Common.ps1')
. (Join-Path $PSScriptRoot '../Operations.ps1')
$script:passed = 0
$script:failed = 0
function Check([string] $Name, [scriptblock] $Body) {
    try { & $Body; $script:passed++; Write-Host "PASS $Name" }
    catch { $script:failed++; Write-Host "FAIL $Name ($($_.Exception.GetType().Name))" }
}
function Expect([bool] $Condition) { if (-not $Condition) { throw 'Delivery assertion failed.' } }
function Reject([scriptblock] $Body) {
    $rejected = $false
    try { & $Body | Out-Null } catch { $rejected = $true; $script:lastRejection = $_.Exception.Message }
    Expect $rejected
}
function New-FixtureManifest {
    $digest = 'sha256:' + ('a' * 64)
    $images = @{}
    foreach ($unit in @('web', 'api', 'notifier', 'migrations', 'sqlBootstrap')) {
        $repository = if ($unit -eq 'sqlBootstrap') { 'taskmanager/sql-bootstrap' } else { "taskmanager/$unit" }
        $images[$unit] = @{ repository = $repository; digest = $digest; reference = "fixture.azurecr.io/$repository@$digest" }
    }
    @{ schemaVersion = 1; product = 'TaskManager'; commitSha = 'b' * 40;
       subscriptionId = '11111111-1111-4111-8111-111111111111'; tenantId = '22222222-2222-4222-8222-222222222222';
       environmentName = 'staging'; resourceGroupName = 'taskmanager-staging'; resourceNamePrefix = 'tm-stage';
       publicOrigin = 'https://taskmanager.example.test'; registryLoginServer = 'fixture.azurecr.io';
       registryResourceId = '/subscriptions/11111111-1111-4111-8111-111111111111/resourceGroups/taskmanager-staging/providers/Microsoft.ContainerRegistry/registries/fixture'; images = $images }
}
function New-FixtureClaims([string] $Subject = 'repo:serhiiyasenev@100/TaskManager@200:environment:taskmanager-staging') {
    @{ sub = $Subject; iss = 'https://token.actions.githubusercontent.com'; aud = 'api://AzureADTokenExchange';
       repository = 'serhiiyasenev/TaskManager'; ref = 'refs/heads/main'; event_name = 'workflow_dispatch';
       environment = 'taskmanager-staging'; workflow_ref = 'serhiiyasenev/TaskManager/.github/workflows/azure-deploy.yml@refs/heads/main' }
}
Check 'accepts the complete immutable five-image release' {
    Assert-TaskManagerReleaseManifest -Manifest (New-FixtureManifest) -RegistryLoginServer 'fixture.azurecr.io' -CommitSha ('b' * 40)
}
Check 'rejects a mutable image tag before any command' {
    $manifest = New-FixtureManifest; $manifest.images.api.reference = 'fixture.azurecr.io/taskmanager/api:latest'
    Reject { Assert-TaskManagerReleaseManifest -Manifest $manifest -RegistryLoginServer 'fixture.azurecr.io' -CommitSha ('b' * 40) }
}
Check 'rejects a mismatched digest and reference' {
    $manifest = New-FixtureManifest; $manifest.images.notifier.digest = 'sha256:' + ('c' * 64)
    Reject { Assert-TaskManagerReleaseManifest -Manifest $manifest -RegistryLoginServer 'fixture.azurecr.io' -CommitSha ('b' * 40) }
}
Check 'rejects another registry and absent bootstrap image' {
    $manifest = New-FixtureManifest; $manifest.images.Remove('sqlBootstrap')
    Reject { Assert-TaskManagerReleaseManifest -Manifest $manifest -RegistryLoginServer 'other.azurecr.io' -CommitSha ('b' * 40) }
}
Check 'accepts the exact configured immutable OIDC subject' {
    $claims = New-FixtureClaims
    Assert-TaskManagerOidcClaims -Claims $claims -ExpectedSubject $claims.sub -EnvironmentName 'taskmanager-staging'
}
Check 'accepts an exact configured classic subject without guessing it' {
    $subject = 'repo:serhiiyasenev/TaskManager:environment:taskmanager-staging'
    Assert-TaskManagerOidcClaims -Claims (New-FixtureClaims $subject) -ExpectedSubject $subject -EnvironmentName 'taskmanager-staging'
}
Check 'rejects a different configured OIDC subject' {
    Reject { Assert-TaskManagerOidcClaims -Claims (New-FixtureClaims) -ExpectedSubject 'another-subject' -EnvironmentName 'taskmanager-staging' }
}
Check 'rejects PR and feature-branch credentials' {
    $claims = New-FixtureClaims; $claims.event_name = 'pull_request'
    Reject { Assert-TaskManagerOidcClaims -Claims $claims -ExpectedSubject $claims.sub -EnvironmentName 'taskmanager-staging' }
    $claims = New-FixtureClaims; $claims.ref = 'refs/heads/feature'
    Reject { Assert-TaskManagerOidcClaims -Claims $claims -ExpectedSubject $claims.sub -EnvironmentName 'taskmanager-staging' }
}
$script:fixtureGroupId = '/subscriptions/11111111-1111-4111-8111-111111111111/resourceGroups/taskmanager-staging'
Check 'rejects destructive what-if and modification of a shared resource' {
    $result = @{ status = 'Succeeded'; changes = @(@{ resourceId = "$script:fixtureGroupId/providers/Microsoft.Sql/servers/tm-stage"; changeType = 'Delete' }) }
    Reject { Get-TaskManagerWhatIfSummary -Result $result -ResourceGroupId $script:fixtureGroupId }
    $result.changes[0].changeType = 'Modify'; $result.changes[0].resourceId = '/subscriptions/11111111-1111-4111-8111-111111111111/resourceGroups/shared/providers/Microsoft.ContainerRegistry/registries/shared'
    Reject { Get-TaskManagerWhatIfSummary -Result $result -ResourceGroupId $script:fixtureGroupId }
}
Check 'returns only safe what-if summary fields' {
    $result = @{ status = 'Succeeded'; changes = @(@{ resourceId = "$script:fixtureGroupId/providers/Microsoft.Sql/servers/tm-stage"; changeType = 'Create'; before = @{ password = 'synthetic-private-value' }; after = @{ password = 'synthetic-private-value' } }) }
    $summary = Get-TaskManagerWhatIfSummary -Result $result -ResourceGroupId $script:fixtureGroupId
    Expect (($summary | ConvertTo-Json -Depth 10) -notmatch 'synthetic-private-value|password')
}
Check 'polls the exact returned job execution instead of a latest success' {
    $calls = [Collections.Generic.List[object]]::new()
    $invoker = { param([string[]] $Arguments)
        $calls.Add($Arguments)
        @{ name = 'tm-stage-migrations-run1'; properties = @{ status = if ($calls.Count -eq 1) { 'Running' } else { 'Succeeded' } } }
    }.GetNewClosure()
    $result = Wait-TaskManagerJobExecution -Invoker $invoker -ResourceGroupName 'taskmanager-staging' -JobName 'tm-stage-migrations' -ExecutionName 'tm-stage-migrations-run1' -PollSeconds 0 -MaxPolls 3
    Expect ($result.status -eq 'Succeeded' -and $calls.Count -eq 2)
    foreach ($call in $calls) { Expect ($call -contains '--job-execution-name' -and $call -contains 'tm-stage-migrations-run1' -and $call -notcontains 'list') }
}
Check 'rejects a wrong execution and stops on migration failure' {
    $wrong = { param($Arguments) @{ name = 'another-execution'; properties = @{ status = 'Succeeded' } } }
    Reject { Wait-TaskManagerJobExecution -Invoker $wrong -ResourceGroupName 'taskmanager-staging' -JobName 'tm-stage-migrations' -ExecutionName 'tm-stage-migrations-run1' -PollSeconds 0 -MaxPolls 2 }
    $failed = { param($Arguments) @{ name = 'tm-stage-migrations-run1'; properties = @{ status = 'Failed'; error = 'synthetic-private-value' } } }
    try { Wait-TaskManagerJobExecution -Invoker $failed -ResourceGroupName 'taskmanager-staging' -JobName 'tm-stage-migrations' -ExecutionName 'tm-stage-migrations-run1' -PollSeconds 0 -MaxPolls 2; throw 'Expected migration failure.' }
    catch { Expect ($_.Exception.Message -notmatch 'synthetic-private-value' -and $_.Exception.Message -ne 'Expected migration failure.') }
}
Check 'rejects a running migration before starting another execution' {
    $calls = [Collections.Generic.List[object]]::new()
    $invoker = { param([string[]] $Arguments) $calls.Add($Arguments); @(@{ name = 'existing-run'; properties = @{ status = 'Running' } }) }.GetNewClosure()
    Reject { Start-TaskManagerSingleJob -Invoker $invoker -ResourceGroupName 'taskmanager-staging' -JobName 'tm-stage-migrations' }
    Expect ($calls.Count -eq 1 -and $calls[0] -contains 'list' -and $calls[0] -notcontains 'start')
}
Check 'times out finitely without reporting cloud success' {
    $calls = [Collections.Generic.List[object]]::new()
    $invoker = { param([string[]] $Arguments) $calls.Add($Arguments); @{ name = 'tm-stage-migrations-run1'; properties = @{ status = 'Running' } } }.GetNewClosure()
    Reject { Wait-TaskManagerJobExecution -Invoker $invoker -ResourceGroupName 'taskmanager-staging' -JobName 'tm-stage-migrations' -ExecutionName 'tm-stage-migrations-run1' -PollSeconds 0 -MaxPolls 3 }
    Expect ($calls.Count -eq 3)
}
Check 'rejects wildcard smoke networks and host-bearing origins' {
    Assert-TaskManagerSmokeCidrs @('203.0.113.7/32', '2001:db8::1/128')
    Reject { Assert-TaskManagerSmokeCidrs @('0.0.0.0/0') }
    Reject { Assert-TaskManagerSmokeCidrs @('203.0.113.7/8') }
    Reject { Assert-TaskManagerOrigin 'https://user:password@example.test' }
}
Check 'migration failure prevents apps, smoke and promotion' {
    $steps = [Collections.Generic.List[string]]::new()
    $execute = { param($Phase) $steps.Add($Phase); if ($Phase -eq 'migration-once') { throw 'Fixture migration failed.' } }.GetNewClosure()
    Reject { Invoke-TaskManagerReleasePhases -Execute $execute }
    Expect ($steps -contains 'sql-bootstrap-before' -and $steps -contains 'migration-once')
    Expect ($steps -notcontains 'sql-bootstrap-after' -and $steps -notcontains 'candidate-apps' -and $steps -notcontains 'promote')
}
Check 'release phases use exactly one migration between two bootstrap executions' {
    $steps = [Collections.Generic.List[string]]::new()
    $execute = { param($Phase) $steps.Add($Phase) }.GetNewClosure()
    Invoke-TaskManagerReleasePhases -Execute $execute
    Expect (@($steps | Where-Object { $_ -eq 'migration-once' }).Count -eq 1)
    Expect ($steps.IndexOf('sql-bootstrap-before') -lt $steps.IndexOf('migration-once'))
    Expect ($steps.IndexOf('sql-bootstrap-after') -gt $steps.IndexOf('migration-once'))
    Expect ($steps.IndexOf('candidate-apps') -gt $steps.IndexOf('sql-bootstrap-after'))
}
Check 'rollback deactivates candidate worker before restoring explicit traffic' {
    $steps = [Collections.Generic.List[string]]::new()
    $execute = { param($Phase) $steps.Add($Phase) }.GetNewClosure()
    Invoke-TaskManagerRollbackPhases -Execute $execute
    Expect ($steps[0] -eq 'deactivate-candidate-notifier')
    Expect ($steps -contains 'restore-explicit-traffic' -and $steps -notcontains 'migration-once')
}
Check 'production database sequence persists uncertain start and stops before final grants on failed migration' {
    $calls = [Collections.Generic.List[object]]::new(); $finalized = [Collections.Generic.List[string]]::new()
    $manifest = New-FixtureManifest
    $state = @{ phase = 'jobs'; jobs = @{}; completed = @(); jobNames = @{ sqlBootstrapJobName = 'tm-stage-bootstrap'; migrationJobName = 'tm-stage-migrations' } }
    $invoker = { param([string[]] $Arguments)
        $calls.Add($Arguments)
        $job = $Arguments[[Array]::IndexOf($Arguments, '--name') + 1]
        if ($Arguments -contains 'execution') {
            if ($Arguments -contains 'list') { return @() }
            return @{ name = "$job-run1"; properties = @{ status = if ($job -eq 'tm-stage-migrations') { 'Failed' } else { 'Succeeded' } } }
        }
        if ($Arguments -contains 'start') { return @{ name = "$job-run1" } }
        @{ configuration = @{ triggerType = 'Manual'; replicaRetryLimit = 0; manualTriggerConfig = @{ parallelism = 1; replicaCompletionCount = 1 } };
            containers = @(@{ image = if ($job -eq 'tm-stage-migrations') { $manifest.images.migrations.reference } else { $manifest.images.sqlBootstrap.reference } }) }
    }.GetNewClosure()
    $finalize = { $finalized.Add('Finalize') }.GetNewClosure()
    $path = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../.local/azure/test-fixtures/job-failure.json'))
    Reject { Invoke-TaskManagerDatabaseJobs $invoker @{resourceGroupName='taskmanager-staging'} $manifest $state $path $finalize -PollSeconds 0 }
    Expect ($finalized.Count -eq 0 -and $state.jobs['migration-once'].executionName -ceq 'tm-stage-migrations-run1')
    Expect ($state.jobs['migration-once'].status -ceq 'StartRequested' -and -not $state.jobs.Contains('sql-bootstrap-after'))
    Expect (@($calls | Where-Object { $_ -contains 'start' }).Count -eq 2)
    $saved = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
    Expect ($saved.jobs['migration-once'].executionName -ceq 'tm-stage-migrations-run1')
}
Check 'production JWT metadata reuse never reads or overwrites an existing key value' {
    $calls = [Collections.Generic.List[object]]::new()
    $invoker = { param([string[]] $Arguments) $calls.Add($Arguments); if ($Arguments -contains 'list') { return @(@{id='https://fixture.vault.azure.net/secrets/taskmanager-jwt-signing'}) }; @(@{id='https://fixture.vault.azure.net/secrets/taskmanager-jwt-signing/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa';attributes=@{enabled=$true;created=100}}) }.GetNewClosure()
    $uri = Get-OrCreateTaskManagerJwtSecret $invoker @{keyVaultUri='https://fixture.vault.azure.net';jwtSecretName='taskmanager-jwt-signing'} 'unused-fixture-directory'
    Expect ($uri.EndsWith('/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa') -and $calls.Count -eq 2 -and $calls[1] -contains 'list-versions' -and $calls[1] -notcontains 'set' -and $calls[1] -notcontains 'show')
}
Check 'first JWT creation checks absent-secret metadata, uses a private key file and removes it' {
    $calls = [Collections.Generic.List[object]]::new()
    $temporaryPaths = [Collections.Generic.List[string]]::new()
    $directory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../.local/azure/test-fixtures/new-key'))
    $invoker = { param([string[]] $Arguments)
        $calls.Add($Arguments)
        if ($Arguments -contains 'list') { return @() }
        if ($Arguments -contains 'list-versions') { throw 'Synthetic absent secret versions endpoint returns404.' }
        if ($Arguments -contains 'set') {
            $path = $Arguments[[Array]::IndexOf($Arguments,'--file')+1];$temporaryPaths.Add($path)
            $bytes = [Convert]::FromBase64String([IO.File]::ReadAllText($path))
            if ($bytes.Length -lt 32 -or $Arguments -contains '--value') { throw 'Key creation must use sufficient random bytes via a private file.' }
            return @{id='https://fixture.vault.azure.net/secrets/taskmanager-jwt-signing/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'}
        }
        throw 'Unexpected synthetic command.'
    }.GetNewClosure()
    $uri = Get-OrCreateTaskManagerJwtSecret $invoker @{keyVaultUri='https://fixture.vault.azure.net';jwtSecretName='taskmanager-jwt-signing'} $directory
    Expect ($uri.EndsWith('/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa') -and $calls.Count -eq 2 -and $temporaryPaths.Count -eq 1 -and -not (Test-Path -LiteralPath $temporaryPaths[0]))
}
Check 'shared registry mismatch blocks preflight without mutating the existing registry' {
    $calls = [Collections.Generic.List[object]]::new()
    $id = "$script:fixtureGroupId/providers/Microsoft.ContainerRegistry/registries/shared"
    $config = @{foundation=@{existingRegistryResourceId=$id;existingRegistryRoleAssignmentMode='AbacRepositoryPermissions'}}
    $invoker = {param([string[]]$Arguments) $calls.Add($Arguments); @{id=$id;roleAssignmentMode='LegacyRegistryPermissions';publicNetworkAccess='Enabled'}}.GetNewClosure()
    Reject { Test-TaskManagerSharedInfrastructure $invoker $config }
    Expect ($calls.Count -eq 1 -and $calls[0] -contains 'show' -and $calls[0] -notcontains 'update')
}
Check 'repeat foundation preserves actual approved SignalR ACL and current candidate CORS' {
    $config = @{subscriptionId='11111111-1111-4111-8111-111111111111';resourceGroupName='taskmanager-staging';foundation=@{resourceNamePrefix='tm';environmentName='staging';publicOrigin='https://web.example.test'}}
    $endpoint = "$script:fixtureGroupId/providers/Microsoft.Network/privateEndpoints/tm-staging-signalr-private"
    $calls = [Collections.Generic.List[object]]::new()
    $invoker = {param([string[]]$Arguments)
        $calls.Add($Arguments)
        if ($Arguments -contains 'list') { return @(@{id="$script:fixtureGroupId/providers/Microsoft.SignalRService/signalR/tm-staging-signalr-abc";name='tm-staging-signalr-abc';tags=@{product='TaskManager'}}) }
        @{acls=@{defaultAction='Deny';publicNetwork=@{allow=@('ClientConnection')};privateEndpoints=@(@{name='actual.azure-generated-name';allow=@('ServerConnection')})};
            origins=@('https://web.example.test','https://web--old.example.test');connections=@(@{name='actual.azure-generated-name';properties=@{privateEndpoint=@{id=$endpoint};privateLinkServiceConnectionState=@{status='Approved'}}})}
    }.GetNewClosure()
    Set-TaskManagerPreservedFoundationBoundaries $invoker $config
    Expect ($config.foundation.signalRPrivateEndpointConnectionNames[0] -ceq 'actual.azure-generated-name' -and $config.foundation.signalRAdditionalAllowedOrigins[0] -ceq 'https://web--old.example.test')
    Expect ($calls.Count -eq 2 -and $calls[1] -contains 'get')
}
Check 'promotion rejects anonymous evidence, wrong revision and expired authenticated record' {
    $state = @{manifest=(New-FixtureManifest);apps=@{candidateUrls=@{web='https://web--new.example.test'};candidateRevisions=@{web='web--new';api='api--new';notifier='notifier--new'}}}
    Reject { Assert-TaskManagerAuthenticatedEvidence @{kind='live-anonymous-guards'} $state }
    $checks = @{}; foreach ($key in @('browserCookieCsrf','teamDataIsolation','emailConfirmationRecovery','sqlManagedIdentity','serviceBusDelivery','signalRAuthenticatedInbox','azureOpenAiReviewCommit','persistentDataProtectionRestart','telemetryPrivacy')) { $checks[$key]=@{status='Passed';evidenceSha256='a'*64} }
    $evidence = @{schemaVersion=1;kind='live-authenticated-candidate';syntheticFixtures=$false;commitSha='b'*40;origin='https://web--new.example.test';candidateRevisions=@{web='web--old';api='api--new';notifier='notifier--new'};completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');checks=$checks}
    Reject { Assert-TaskManagerAuthenticatedEvidence $evidence $state }
    $evidence.candidateRevisions.web='web--new'; $evidence.completedAtUtc=[DateTimeOffset]::UtcNow.AddHours(-1).ToString('O')
    Reject { Assert-TaskManagerAuthenticatedEvidence $evidence $state }
    $evidence.completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');$evidence.checks.azureOpenAiReviewCommit.status='NotRun'
    Reject { Assert-TaskManagerAuthenticatedEvidence $evidence $state }
}
Check 'cross-RG exact module/custom role approvals reject ACS permission expansion' {
    $group='/subscriptions/11111111-1111-4111-8111-111111111111/resourceGroups/shared'
    $acs="$group/providers/Microsoft.Communication/communicationServices/shared-email"
    $role="$group/providers/Microsoft.Authorization/roleDefinitions/33333333-3333-4333-8333-333333333333"
    $wrapper="$group/providers/Microsoft.Resources/deployments/tm-staging-email-role"
    $config=@{foundation=@{resourceNamePrefix='tm';environmentName='staging';existingCommunicationServiceResourceId=$acs}}
    $preview=@{changes=@(@{resourceId=$role;changeType='Create';after=@{properties=@{type='CustomRole';roleName='TaskManager Native Email Sender abcdefghijklm';assignableScopes=@($group);permissions=@(@{actions=@('Microsoft.Communication/CommunicationServices/Read','Microsoft.Communication/CommunicationServices/Write');dataActions=@();notActions=@();notDataActions=@()})}}})}
    Assert-TaskManagerOwnedLeafApprovals $config $preview @($role,$wrapper)
    $preview.changes[0].after.properties.permissions[0].actions=@('*')
    Reject { Assert-TaskManagerOwnedLeafApprovals $config $preview @($role,$wrapper) }
    Reject { Assert-TaskManagerOwnedLeafApprovals $config $preview @($group+'/providers/Microsoft.Resources/deployments/unrelated-project') }
}
Check 'cost gate blocks missing service coverage, wrong region and stale price data' {
    $config=@{monthlyBudgetCurrency='USD';foundation=@{location='fixture-region';monthlyBudget=100}}
    $report=@{source='Azure Retail Prices API';currency='USD';completeServiceCoverage=$false;configuredRegion='fixture-region';configuredMonthlyBudget=100;fetchedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');knownSubtotalUsd=50}
    Reject { Assert-TaskManagerCostReport $report $config }
    $report.completeServiceCoverage=$true;$report.configuredRegion='wrong-region';Reject { Assert-TaskManagerCostReport $report $config }
    $report.configuredRegion='fixture-region';$report.fetchedAtUtc=[DateTimeOffset]::UtcNow.AddDays(-2).ToString('O');Reject { Assert-TaskManagerCostReport $report $config }
}
Write-Host "Delivery fixture checks: $script:passed passed, $script:failed failed. No cloud calls."
if ($script:failed -gt 0) { exit 1 }
