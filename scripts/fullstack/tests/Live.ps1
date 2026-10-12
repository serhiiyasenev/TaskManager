#requires -Version 7.4
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../live/Common.ps1')
$script:passed=0;$script:failed=0
function Check([string]$Name,[scriptblock]$Body){try{&$Body;$script:passed++;Write-Host "PASS $Name"}catch{$script:failed++;Write-Host "FAIL $Name ($($_.Exception.GetType().Name))"}}
function Expect([bool]$Condition){if(-not $Condition){throw 'Live boundary assertion failed.'}}
function Reject([scriptblock]$Body){$rejected=$false;try{&$Body|Out-Null}catch{$rejected=$true};Expect $rejected}
function Context {
    @{runId='b'*32;commitSha='c'*40;origin='https://fixture-web--rcccccccccccc.fixture.azurecontainerapps.io';candidateRevisions=@{web='fixture-web--rcccccccccccc';api='fixture-api--rcccccccccccc';notifier='fixture-notifier--rcccccccccccc'}}
}
Check 'missing real inputs are listed before any network/probe invocation' {
    $missing=@(Get-LiveVerificationMissingInputs @{})
    Expect ($missing -contains 'authorizeLiveExecution' -and $missing -contains 'deploymentConfigurationPath' -and $missing -contains 'nativeProbes')
}
Check 'local fixture origin cannot be accepted as an actual Azure candidate' {
    Assert-LiveCandidateOrigin (Context).origin
    Reject {Assert-LiveCandidateOrigin 'https://127.0.0.1:41001'}
    Reject {Assert-LiveCandidateOrigin 'https://user:password@fixture.azurecontainerapps.io'}
}
Check 'controlled/test probe script cannot receive live credentials despite a supplied digest' {
    $file=Join-Path $PSScriptRoot 'Child.ps1'
    $hash=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    Reject {Resolve-LiveNativeProbe @{path=$file;sha256=$hash} 'browserCookieCsrf'}
    Expect ((Get-LiveRequiredChecks).Count -eq 9)
}
Check 'probe result requires fresh matching native execution and actual artifact hash' {
    $directory=New-FullStackRunDirectory;$context=Context;$context.runId=[IO.DirectoryInfo]::new($directory).Name
    $start=[DateTimeOffset]::UtcNow.AddSeconds(-2);$end=[DateTimeOffset]::UtcNow
    $artifact=Join-Path $directory 'browserCookieCsrf.evidence.json'
    # Synthetic protocol bytes only. Validation below never registers an
    # executed native probe and cannot compose promotion evidence.
    Save-FullStackPrivateJson $artifact @{schemaVersion=1;kind='live-native-observations';check='browserCookieCsrf';runId=$context.runId;
        commitSha=$context.commitSha;origin=$context.origin;candidateRevisions=$context.candidateRevisions;provider='Azure';syntheticFixtures=$false;
        observations=@(@{name='syntheticProtocolFixture';status='Passed';correlationSha256='d'*64})}
    $result=@{schemaVersion=1;kind='live-azure-native-probe';check='browserCookieCsrf';runId=$context.runId;commitSha=$context.commitSha;origin=$context.origin;candidateRevisions=$context.candidateRevisions;
        provider='Azure';syntheticFixtures=$false;status='Passed';nativeOperationCount=1;startedAtUtc=$start.ToString('O');completedAtUtc=$end.ToString('O');evidenceFile='browserCookieCsrf.evidence.json';evidenceSha256=(Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash.ToLowerInvariant()}
    $verified=Assert-LiveNativeProbeResult $result $context browserCookieCsrf $directory $start.AddSeconds(-1) $end.AddSeconds(1)
    Expect ($verified.evidenceSha256 -ceq $result.evidenceSha256)
    $result.evidenceSha256='a'*64;Reject {Assert-LiveNativeProbeResult $result $context browserCookieCsrf $directory $start.AddSeconds(-1) $end.AddSeconds(1)}
}
Check 'a matching hash cannot launder private notes or mailbox/token fields' {
    $directory=New-FullStackRunDirectory;$context=Context;$context.runId=[IO.DirectoryInfo]::new($directory).Name
    $start=[DateTimeOffset]::UtcNow.AddSeconds(-2);$end=[DateTimeOffset]::UtcNow.AddSeconds(1)
    $artifact=Join-Path $directory 'telemetryPrivacy.evidence.json'
    Save-FullStackPrivateJson $artifact @{schemaVersion=1;kind='live-native-observations';check='telemetryPrivacy';runId=$context.runId;
        commitSha=$context.commitSha;origin=$context.origin;candidateRevisions=$context.candidateRevisions;provider='Azure';syntheticFixtures=$false;
        observations=@(@{name='syntheticProtocolFixture';status='Passed';correlationSha256='d'*64});notesText='SYNTHETIC_PRIVATE_SENTINEL'}
    $result=@{schemaVersion=1;kind='live-azure-native-probe';check='telemetryPrivacy';runId=$context.runId;commitSha=$context.commitSha;origin=$context.origin;candidateRevisions=$context.candidateRevisions;
        provider='Azure';syntheticFixtures=$false;status='Passed';nativeOperationCount=1;startedAtUtc=$start.ToString('O');completedAtUtc=$end.ToString('O');evidenceFile='telemetryPrivacy.evidence.json';evidenceSha256=(Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash.ToLowerInvariant()}
    Reject {Assert-LiveNativeProbeResult $result $context telemetryPrivacy $directory $start.AddSeconds(-1) $end.AddSeconds(1)}
    $result.Remove('evidenceFile');Reject {Assert-LiveNativeProbeResult $result $context telemetryPrivacy $directory $start.AddSeconds(-1) $end.AddSeconds(1)}
}
Check 'stale execution and changed revisions are rejected before accepting artifact bytes' {
    $directory=New-FullStackRunDirectory;$context=Context;$context.runId=[IO.DirectoryInfo]::new($directory).Name
    $start=[DateTimeOffset]::UtcNow.AddHours(-1);$end=$start.AddSeconds(1)
    $result=@{schemaVersion=1;kind='live-azure-native-probe';check='browserCookieCsrf';runId=$context.runId;commitSha=$context.commitSha;origin=$context.origin;candidateRevisions=$context.candidateRevisions;
        provider='Azure';syntheticFixtures=$false;status='Passed';nativeOperationCount=1;startedAtUtc=$start.ToString('O');completedAtUtc=$end.ToString('O');evidenceFile='browserCookieCsrf.evidence.json';evidenceSha256='a'*64}
    Reject {Assert-LiveNativeProbeResult $result $context browserCookieCsrf $directory $start $end}
    $result.candidateRevisions=@{web='another-web--rcccccccccccc';api=$context.candidateRevisions.api;notifier=$context.candidateRevisions.notifier}
    Reject {Assert-LiveNativeProbeResult $result $context browserCookieCsrf $directory $start $end}
}
Check 'missing proof never turns a scope check or a local PASS into nine-check evidence' {
    $context=Context
    Reject {New-LiveAuthenticatedEvidence $context @{} }
    $results=@{};foreach($key in Get-LiveRequiredChecks){$results[$key]=@{status='Passed';evidenceSha256='a'*64;provider='fake'}}
    Reject {New-LiveAuthenticatedEvidence $context $results}
}
Check 'presence-only legacy inputs still exit SKIP2 through an actual native checker' {
    $directory=New-FullStackRunDirectory;$path=Join-Path $directory 'legacy-live-inputs.json'
    Save-FullStackPrivateJson $path @{tenantId='synthetic';nativeAzureAdapterAccess='present';candidateOrigin='https://127.0.0.1:41001'}
    $handle=Start-FullStackChild -Executable (Get-Process -Id $PID).Path -Arguments @('-NoProfile','-File',(Join-Path $PSScriptRoot '../Test-LivePrerequisites.ps1'),'-ConfigurationPath',$path) -WorkingDirectory (Get-FullStackRepository) -Environment @{}
    try {Expect ((Wait-FullStackChild $handle -TimeoutSeconds 10) -eq 2);Expect (-not (Test-Path -LiteralPath (Join-Path $directory 'authenticated-candidate.json')))}
    finally {$null=Stop-FullStackChild $handle -GraceSeconds 1;$handle.Process.Dispose()}
}
Write-Host "Live runner boundary fixtures: $script:passed passed, $script:failed failed. Synthetic protocol data only; no Azure/network/model/mail calls."
if($script:failed){exit 1}
