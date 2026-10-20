#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)] [string] $ConfigurationPath, [Parameter(Mandatory)] [string] $CommitSha,
      [Parameter(Mandatory)] [string] $AzureCliPath, [Parameter(Mandatory)] [string] $BicepPath,
      [ValidateSet('Validate', 'Foundation', 'Release')] [string] $Mode = 'Validate')
. (Join-Path $PSScriptRoot 'Operations.ps1')
$config = Read-TaskManagerConfiguration $ConfigurationPath
if ($CommitSha -cnotmatch '^[a-f0-9]{40}$') { throw 'Complete immutable release commit required.' }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$stateDirectory = Join-Path $repository ".local/azure/$($config.foundation.environmentName)/$CommitSha"
$statePath = Join-Path $stateDirectory 'release.json'
$invoker = New-TaskManagerInvoker $AzureCliPath
Assert-TaskManagerContext $invoker $config
Test-TaskManagerSharedInfrastructure $invoker $config
Set-TaskManagerPreservedFoundationBoundaries $invoker $config
Test-TaskManagerOffline $BicepPath | Out-Null
if ($Mode -eq 'Validate') {
    Invoke-TaskManagerTemplate $invoker $config $stateDirectory foundation $config.foundation -WhatIfOnly
    Write-Host 'Actual foundation validation and safe what-if gates passed. No deployment was performed.'
    exit 0
}
if ($Mode -eq 'Foundation') {
    $cost = Get-Content -LiteralPath (Join-Path $repository '.local/azure/cost-estimate.json') -Raw | ConvertFrom-Json -AsHashtable -Depth 100
    Assert-TaskManagerCostReport $cost $config
    $foundation = Invoke-TaskManagerTemplate $invoker $config $stateDirectory foundation $config.foundation
    Assert-TaskManagerFoundationOutputs $foundation
    Save-TaskManagerPrivateJson (Join-Path $stateDirectory 'foundation.json') $foundation
    if ((Get-TaskManagerField $config.foundation 'existingSqlServerResourceId' '')) {
        & (Join-Path $PSScriptRoot 'New-SqlBootstrapAuthorization.ps1') -FoundationPath (Join-Path $stateDirectory 'foundation.json') -OutputPath (Join-Path $stateDirectory 'owner-authorize-bootstrap.sql')
    }
    Write-Host 'Foundation complete. No application, consumer or migration execution started.'
    exit 0
}
if (Test-Path -LiteralPath $statePath) { throw 'Release state already exists. Inspect exact saved job execution before any recovery; this command never restarts an uncertain migration.' }
$cost = Get-Content -LiteralPath (Join-Path $repository '.local/azure/cost-estimate.json') -Raw | ConvertFrom-Json -AsHashtable -Depth 100
Assert-TaskManagerCostReport $cost $config
$foundation = Get-Content -LiteralPath (Join-Path $stateDirectory 'foundation.json') -Raw | ConvertFrom-Json -AsHashtable -Depth 100
Assert-TaskManagerFoundationOutputs $foundation
$manifest = Get-Content -LiteralPath (Join-Path $stateDirectory 'images.json') -Raw | ConvertFrom-Json -AsHashtable -Depth 100
Assert-TaskManagerReleaseManifest $manifest $foundation.registryLoginServer $CommitSha
if ($manifest.subscriptionId -cne $config.subscriptionId -or $manifest.resourceGroupName -cne $config.resourceGroupName -or $manifest.tenantId -cne $config.tenantId -or
    $manifest.registryResourceId -ine $foundation.registryResourceId -or $manifest.publicOrigin -cne $foundation.publicOrigin) { throw 'Release scope differs from the reviewed configuration/foundation.' }
$state = @{ schemaVersion = 1; product = 'TaskManager'; manifest = $manifest; startedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); phase = 'preflight'; jobs = @{}; completed = @() }
Save-TaskManagerPrivateJson $statePath $state
$candidateSuffix = 'r' + $CommitSha.Substring(0, 12)
$expectedCandidateOrigin = "https://$($foundation.appNames.web)--$candidateSuffix.$($foundation.environmentDefaultDomain)"
$snapshots = Get-TaskManagerAppSnapshots $invoker $config $foundation
$state.snapshots = $snapshots; $state.foundation = $foundation; $state.candidateSuffix = $candidateSuffix
Save-TaskManagerPrivateJson $statePath $state
try {
    Test-TaskManagerLiveDependencies $invoker $config $foundation
    $state.jwtSecretUri = Get-OrCreateTaskManagerJwtSecret $invoker $foundation $stateDirectory
    $state.signalRPrivateEndpointConnectionNames = @(Set-TaskManagerSignalRBoundary $invoker $foundation $expectedCandidateOrigin $stateDirectory)
    Save-TaskManagerPrivateJson (Join-Path $stateDirectory 'next-foundation-boundaries.json') @{ signalRPrivateEndpointConnectionNames = $state.signalRPrivateEndpointConnectionNames; signalRAdditionalAllowedOrigins = @($expectedCandidateOrigin) }
    $jobParameters = @{ location = $config.foundation.location; foundationOutputs = $foundation; migrationsImageDigest = $manifest.images.migrations.digest; sqlBootstrapImageDigest = $manifest.images.sqlBootstrap.digest; sqlBootstrapPhase = 'Prepare' }
    $state.phase = 'jobs'; Save-TaskManagerPrivateJson $statePath $state
    $state.jobNames = Invoke-TaskManagerTemplate $invoker $config $stateDirectory jobs $jobParameters
    $finalize = { $jobParameters.sqlBootstrapPhase = 'Finalize'; Invoke-TaskManagerTemplate $invoker $config $stateDirectory jobs $jobParameters }.GetNewClosure()
    Invoke-TaskManagerDatabaseJobs $invoker $config $manifest $state $statePath $finalize
    $appsParameters = @{ location = $config.foundation.location; foundationOutputs = $foundation; candidateSuffix = $candidateSuffix; jwtSecretUri = $state.jwtSecretUri;
        webImageDigest = $manifest.images.web.digest; apiImageDigest = $manifest.images.api.digest; notifierImageDigest = $manifest.images.notifier.digest;
        smokeAllowedCidrs = $config.smokeAllowedCidrs; webExistingTraffic = $snapshots.web.traffic; apiExistingTraffic = $snapshots.api.traffic; notifierExistingTraffic = $snapshots.notifier.traffic;
        webExistingIpSecurityRestrictions = $snapshots.web.ipSecurityRestrictions; webCustomDomains = $snapshots.web.customDomains }
    $state.phase = 'candidate-apps'; Save-TaskManagerPrivateJson $statePath $state
    $state.apps = Invoke-TaskManagerTemplate $invoker $config $stateDirectory apps $appsParameters
    Save-TaskManagerPrivateJson $statePath $state
    Assert-TaskManagerCandidate $invoker $config $foundation $state.apps $manifest $candidateSuffix
    $state.phase = 'smoke'; Save-TaskManagerPrivateJson $statePath $state
    $state.smoke = Invoke-TaskManagerAnonymousSmoke $state.apps.candidateUrls.web
    if (-not $snapshots.web.exists) { $state.canonicalSmoke = Invoke-TaskManagerAnonymousSmoke $foundation.publicOrigin }
    Save-TaskManagerPrivateJson $statePath $state
    # Promotion is a separate explicit command so a reviewed concrete candidate
    # and truthful smoke record exist before public traffic changes.
    $state.phase = 'CandidateAwaitingAuthenticatedSmoke'; Save-TaskManagerPrivateJson $statePath $state
    Write-Host 'Exact migration and two bootstrap executions succeeded; candidate anonymous guards passed. Promotion is blocked until independent authenticated/Azure adapter smoke evidence is recorded.'
} catch {
    $state.failedPhase = $state.phase; $state.phase = 'FailedOrUncertain'; Save-TaskManagerPrivateJson $statePath $state
    if ($state.failedPhase -in @('candidate-apps', 'smoke')) {
        try { & (Join-Path $PSScriptRoot 'Rollback.ps1') -ConfigurationPath $ConfigurationPath -ReleasePath $statePath -AzureCliPath $AzureCliPath }
        catch { Write-Warning 'Candidate cleanup remains unverified. Run the recorded rollback command before another deployment.' }
    }
    throw 'Release stopped. Private state records the exact phase/execution; traffic promotion is blocked.'
}
