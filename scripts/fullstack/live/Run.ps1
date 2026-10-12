#requires -Version 7.4
[CmdletBinding()]
param([string] $ConfigurationPath, [string] $RunDirectory)
. (Join-Path $PSScriptRoot 'Common.ps1')
$directory = $null
try {
    if (-not $ConfigurationPath -or -not (Test-Path -LiteralPath $ConfigurationPath -PathType Leaf)) { Write-Host 'SKIP LiveAzure: reviewed authorization/configuration is absent. No network call or evidence.'; exit 2 }
    $configuration = Read-LiveJson $ConfigurationPath
    $missing = @(Get-LiveVerificationMissingInputs $configuration)
    if ($missing.Count) { Write-Host ('SKIP LiveAzure: missing actual inputs/probes: ' + ($missing -join ', ') + '. No network call or evidence.'); exit 2 }
    Assert-LiveConfiguration $configuration
    $deploymentPath = Resolve-LiveFile $configuration.deploymentConfigurationPath
    $releasePath = Resolve-LiveFile $configuration.releaseStatePath
    $deployment = Read-TaskManagerConfiguration $deploymentPath
    $release = Read-LiveJson $releasePath
    Assert-LiveReleaseBinding $deployment $release $configuration.commitSha
    if ($RunDirectory) {
        $candidateDirectory = [IO.Path]::GetFullPath($RunDirectory)
        $null = Assert-FullStackPrivatePath (Join-Path $candidateDirectory 'live-context.json')
        if ([IO.DirectoryInfo]::new($candidateDirectory).Name -cnotmatch '^[a-f0-9]{32}$' -or -not (Test-Path -LiteralPath $candidateDirectory -PathType Container) -or
            @(Get-ChildItem -LiteralPath $candidateDirectory -Force).Count) { throw 'Live run directory must be fresh and privately owned.' }
        $directory = $candidateDirectory
        Set-FullStackPrivateDirectory $directory
    } else { $directory = New-FullStackRunDirectory }
    $context = @{runId=[IO.DirectoryInfo]::new($directory).Name;commitSha=$configuration.commitSha;origin=$release.apps.candidateUrls.web;
        candidateRevisions=$release.apps.candidateRevisions;deploymentConfigurationPath=$deploymentPath;releaseStatePath=$releasePath;
        azureCliPath=(Resolve-LiveFile $configuration.azureCliPath)}
    Save-FullStackPrivateJson (Join-Path $directory 'live-context.json') @{schemaVersion=1;kind='live-azure-scope';runId=$context.runId;commitSha=$context.commitSha;origin=$context.origin;candidateRevisions=$context.candidateRevisions}
    $started = [DateTimeOffset]::UtcNow
    $invoker = New-TaskManagerInvoker $context.azureCliPath
    Assert-TaskManagerContext $invoker $deployment
    Assert-TaskManagerCandidate $invoker $deployment $release.foundation $release.apps $release.manifest $release.candidateSuffix
    Assert-LiveCandidateProviders $invoker $deployment $release
    Test-TaskManagerLiveDependencies $invoker $deployment $release.foundation
    $guards = Invoke-TaskManagerAnonymousSmoke $context.origin
    Save-FullStackPrivateJson (Join-Path $directory 'candidate-scope-checks.json') @{schemaVersion=1;kind='live-azure-candidate-scope-only';runId=$context.runId;
        commitSha=$context.commitSha;origin=$context.origin;candidateRevisions=$context.candidateRevisions;anonymousGuards=$guards;
        nativeAdapterConfigurationVerified=$true;authenticatedNineChecksCompleted=$false}
    $results = @{}
    foreach ($check in Get-LiveRequiredChecks) {
        if ([DateTimeOffset]::UtcNow - $started -gt [TimeSpan]::FromMinutes(25)) { throw 'Live evidence execution exceeded its freshness budget.' }
        Assert-LiveConfiguration $configuration
        $results[$check] = Invoke-LiveNativeProbe $configuration.nativeProbes[$check] $context $check $directory -TimeoutSeconds 180
    }
    # A probe may restart a candidate. Bind again to active actual revisions,
    # immutable images and selected configuration before composing evidence.
    Assert-TaskManagerCandidate $invoker $deployment $release.foundation $release.apps $release.manifest $release.candidateSuffix
    Assert-LiveCandidateProviders $invoker $deployment $release
    if ([DateTimeOffset]::UtcNow - $started -gt [TimeSpan]::FromMinutes(30)) { throw 'Native execution exceeded its complete fresh-evidence interval.' }
    $evidence = New-LiveAuthenticatedEvidence $context $results
    Assert-TaskManagerAuthenticatedEvidence $evidence $release
    Save-FullStackPrivateJson (Join-Path $directory 'authenticated-candidate.json') $evidence
    Save-FullStackPrivateJson (Join-Path $directory 'live-runner-result.json') @{schemaVersion=1;kind='live-azure-runner';runId=$context.runId;result='PASS';executedNativeChecks=9;promotionChanged=$false}
    Write-Host "PASS LiveAzure: nine executed native probes verified; private run $($context.runId). Record/review/promotion remain separate commands."
    exit 0
} catch {
    if ($directory) {
        Save-FullStackPrivateJson (Join-Path $directory 'live-runner-result.json') @{schemaVersion=1;kind='live-azure-runner';runId=[IO.DirectoryInfo]::new($directory).Name;result='FAIL';promotionChanged=$false;failureType=$_.Exception.GetType().Name}
    }
    Write-Host 'FAIL LiveAzure: scope/native execution/evidence verification failed. No promotion; raw private diagnostics are suppressed.'
    exit 1
}
