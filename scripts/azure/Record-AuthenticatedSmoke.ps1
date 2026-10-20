#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)] [string] $ReleasePath, [Parameter(Mandatory)] [string] $EvidencePath)
. (Join-Path $PSScriptRoot 'Common.ps1')
$state = Get-Content -LiteralPath $ReleasePath -Raw | ConvertFrom-Json -AsHashtable -Depth 100
$evidence = Get-Content -LiteralPath $EvidencePath -Raw | ConvertFrom-Json -AsHashtable -Depth 50
if ($state.phase -cne 'CandidateAwaitingAuthenticatedSmoke') { throw 'Only a verified prepared candidate may record its independent authenticated gate.' }
Assert-TaskManagerAuthenticatedEvidence $evidence $state
$record = @{}
foreach ($key in @('schemaVersion', 'kind', 'syntheticFixtures', 'commitSha', 'origin', 'candidateRevisions', 'completedAtUtc')) { $record[$key] = $evidence[$key] }
$record.checks = @{}
foreach ($key in @('browserCookieCsrf', 'teamDataIsolation', 'emailConfirmationRecovery', 'sqlManagedIdentity', 'serviceBusDelivery', 'signalRAuthenticatedInbox', 'azureOpenAiReviewCommit', 'persistentDataProtectionRestart', 'telemetryPrivacy')) {
    $record.checks[$key] = @{ status = $evidence.checks[$key].status; evidenceSha256 = $evidence.checks[$key].evidenceSha256 }
}
$state.authenticatedEvidence = $record
$state.phase = 'ReadyForPromotion'
Save-TaskManagerPrivateJson $ReleasePath $state
Write-Host 'Independent authenticated candidate evidence bound to the exact release. This command records evidence; it does not run the checks.'
