#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)] [string] $ConfigurationPath, [Parameter(Mandatory)] [string] $ReleasePath, [Parameter(Mandatory)] [string] $AzureCliPath)
. (Join-Path $PSScriptRoot 'Operations.ps1')
$config = Read-TaskManagerConfiguration $ConfigurationPath
$state = Get-Content -LiteralPath $ReleasePath -Raw | ConvertFrom-Json -AsHashtable -Depth 100
if ($state.phase -cne 'ReadyForPromotion' -or $state.smoke.kind -cne 'live-anonymous-guards') { throw 'A reviewed completed candidate smoke is required before promotion.' }
Assert-TaskManagerAuthenticatedEvidence (Get-TaskManagerField $state 'authenticatedEvidence' @{}) $state
$manifest = $state.manifest; $foundation = $state.foundation
Assert-TaskManagerReleaseManifest $manifest $foundation.registryLoginServer $manifest.commitSha
if ($manifest.subscriptionId -cne $config.subscriptionId -or $manifest.resourceGroupName -cne $config.resourceGroupName) { throw 'Promotion configuration belongs to another deployment.' }
foreach ($phase in @('sql-bootstrap-before', 'migration-once', 'sql-bootstrap-after')) { if ($state.jobs[$phase].status -cne 'Succeeded') { throw 'All exact database job executions must have succeeded.' } }
$invoker = New-TaskManagerInvoker $AzureCliPath; Assert-TaskManagerContext $invoker $config
Assert-TaskManagerCandidate $invoker $config $foundation $state.apps $manifest $state.candidateSuffix
if ([DateTimeOffset]::UtcNow - [DateTimeOffset]::Parse($state.smoke.completedAtUtc) -gt [TimeSpan]::FromMinutes(30)) { throw 'Candidate smoke is older than 30 minutes; repeat it before promotion.' }
$current = Get-TaskManagerAppSnapshots $invoker $config $foundation
foreach ($unit in @('web', 'api', 'notifier')) {
    $expectedWeights = @($state.snapshots[$unit].traffic | ForEach-Object { "$($_.revisionName)=$($_.weight)" } | Sort-Object)
    $actualWeights = @($current[$unit].traffic | Where-Object { $_.revisionName -cne $state.apps.candidateRevisions[$unit] } | ForEach-Object { "$($_.revisionName)=$($_.weight)" } | Sort-Object)
    if (($expectedWeights -join ',') -cne ($actualWeights -join ',')) { throw 'Traffic changed after candidate deployment; recapture and review before promotion.' }
}
$state.phase = 'Promoting'; Save-TaskManagerPrivateJson $ReleasePath $state
try {
    foreach ($unit in @('api', 'notifier', 'web')) { Set-TaskManagerTraffic $invoker $config $foundation $unit @(@{ revisionName = $state.apps.candidateRevisions[$unit]; weight = 100 }) }
    if (-not $state.snapshots.web.exists) {
        # Only remove the exact first-deploy smoke policy after all gates. An
        # existing production ingress policy is preserved byte-for-byte.
        $rules = @($current.web.ipSecurityRestrictions)
        if ($rules.Count -ne $config.smokeAllowedCidrs.Count -or @($rules | Where-Object { $_.action -cne 'Allow' -or $_.ipAddressRange -notin $config.smokeAllowedCidrs -or $_.name -notmatch '^smoke-\d+$' }).Count) { throw 'First-deploy ingress policy changed; public opening is blocked.' }
        Set-TaskManagerIngressPolicy $invoker $config $foundation @() ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($ReleasePath)))
    }
    $state.canonicalSmoke = Invoke-TaskManagerAnonymousSmoke $foundation.publicOrigin
    # Prior notifier revisions are consumers even with zero HTTP weight.
    # Candidate worker remains active; deactivate only captured old workers.
    foreach ($old in $state.snapshots.notifier.traffic) {
        if ($old.revisionName -cne $state.apps.candidateRevisions.notifier) { $null = & $invoker @('containerapp', 'revision', 'deactivate', '--name', $foundation.appNames.notifier, '--resource-group', $config.resourceGroupName, '--revision', $old.revisionName, '--query', '{name:name}') }
    }
    $state.phase = 'Promoted'; $state.promotedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); Save-TaskManagerPrivateJson $ReleasePath $state
    Write-Host 'Explicit candidate revision weights promoted; previous notifier workers deactivated. Database migrations remain applied.'
} catch {
    $state.phase = 'PromotionFailedOrUncertain'; Save-TaskManagerPrivateJson $ReleasePath $state
    throw 'Promotion requires rollback using the recorded release state; no database rollback is performed.'
}
