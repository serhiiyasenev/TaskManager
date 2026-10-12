#requires -Version 7.4
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../Common.ps1')
$script:passed = 0; $script:failed = 0
function Check([string] $Name, [scriptblock] $Body) {
    try { & $Body; $script:passed++; Write-Host "PASS $Name" }
    catch { $script:failed++; Write-Host "FAIL $Name ($($_.Exception.GetType().Name))" }
}
function Expect([bool] $Condition) { if (-not $Condition) { throw 'Full-stack assertion failed.' } }
function Reject([scriptblock] $Body) { $rejected = $false; try { & $Body | Out-Null } catch { $rejected = $true }; Expect $rejected }
function State {
    @{schemaVersion=1;mode='controlled-full-stack';baseUrl='https://127.0.0.1:41001';controlUrl='http://127.0.0.1:41002';controlToken='a'*64;
        databaseName='taskmanager_e2e_test_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa';provider='fake';transport='controlled'}
}
Check 'accepts the exact private loopback controlled state' { Assert-FullStackState (State) Controlled }
Check 'accepts native Convert.ToHexString uppercase control token without changing it' {
    $state=State;$state.controlToken='A'*64;Assert-FullStackState $state Controlled;Expect ($state.controlToken -ceq ('A'*64))
}
Check 'rejects external control URL, production database and non-fake provider' {
    $state=State;$state.controlUrl='https://foreign.example.test';Reject { Assert-FullStackState $state Controlled }
    $state=State;$state.databaseName='TaskManager_Codex_20261012';Reject { Assert-FullStackState $state Controlled }
    $state=State;$state.provider='azure';Reject { Assert-FullStackState $state Controlled }
}
Check 'rejects SQL connections explicitly targeting an existing application database' {
    Assert-FullStackSqlMaster 'Server=localhost;Database=master;Integrated Security=True;Encrypt=True'
    Reject { Assert-FullStackSqlMaster 'Server=localhost;Database=TaskManager_Codex_20261012;Integrated Security=True' }
}
Check 'rejects stale, empty, skipped or failed browser summary' {
    $run='b'*32
    $summary=@{schemaVersion=1;kind='controlled-full-stack-browser';runId=$run;provider='fake';transport='controlled';total=6;passed=6;failed=0;skipped=0;flaky=0}
    Assert-FullStackBrowserSummary $summary $run Controlled
    $summary.runId='c'*32;Reject { Assert-FullStackBrowserSummary $summary $run Controlled };$summary.runId=$run
    $summary.skipped=1;Reject { Assert-FullStackBrowserSummary $summary $run Controlled };$summary.skipped=0
    $summary.total=0;$summary.passed=0;Reject { Assert-FullStackBrowserSummary $summary $run Controlled }
}
Check 'native child stdin EOF runs graceful cleanup without forwarding stdout or stderr' {
    $path=New-FullStackRunDirectory
    $marker=Join-Path $path 'fixture-marker.txt'
    $executable=(Get-Process -Id $PID).Path
    $handle=Start-FullStackChild -Executable $executable -Arguments @('-NoProfile','-File',(Join-Path $PSScriptRoot 'Child.ps1'),'-Mode','Graceful') -WorkingDirectory $path -Environment @{TASKMANAGER_FIXTURE_MARKER=$marker}
    try {
        Start-Sleep -Milliseconds 300
        $result=Stop-FullStackChild $handle -GraceSeconds 5
        Expect ($result.graceful -and $result.exitCode -eq 0 -and (Test-Path -LiteralPath $marker))
        Expect ((Get-Content -LiteralPath $marker -Raw) -ceq 'graceful-eof-cleanup')
    } finally { if (-not $handle.Process.HasExited) { $handle.Process.Kill($true) }; $handle.Process.Dispose() }
}
Check 'unresponsive native child is force-stopped and never counted as graceful cleanup' {
    $path=New-FullStackRunDirectory
    $executable=(Get-Process -Id $PID).Path
    $handle=Start-FullStackChild -Executable $executable -Arguments @('-NoProfile','-File',(Join-Path $PSScriptRoot 'Child.ps1'),'-Mode','Unresponsive') -WorkingDirectory $path -Environment @{}
    try {
        Start-Sleep -Milliseconds 300
        $result=Stop-FullStackChild $handle -GraceSeconds 1
        Expect (-not $result.graceful -and $handle.Process.HasExited)
    } finally { if (-not $handle.Process.HasExited) { $handle.Process.Kill($true) }; $handle.Process.Dispose() }
}
Check 'native Rabbit isolation clears foreign prefixes and aliases while preserving explicit owned values' {
    $path=New-FullStackRunDirectory
    $names=@('RABBITMQ_SERVER_START_ARGS','ERL_LIBS','ERLANG_FIXTURE_FOREIGN','CONFIG_FILES','SERVER_START_ARGS','CTL_ERL_ARGS','RABBITMQ_NODENAME','TASKMANAGER_FIXTURE_UNRELATED')
    $prior=@{}
    try {
        foreach ($name in $names) {
            $prior[$name]=[Environment]::GetEnvironmentVariable($name)
            [Environment]::SetEnvironmentVariable($name,'synthetic-foreign-override')
        }
        [Environment]::SetEnvironmentVariable('TASKMANAGER_FIXTURE_UNRELATED','synthetic-unrelated')
        # The negative control proves that actual native inheritance contains
        # the foreign overrides; the isolated child must remove them.
        foreach ($isolated in @($false,$true)) {
            $markerName=if ($isolated) {'isolated.json'} else {'inherited.json'}
            $marker=Join-Path $path $markerName
            $handle=Start-FullStackChild -Executable (Get-Process -Id $PID).Path -Arguments @('-NoProfile','-File',(Join-Path $PSScriptRoot 'Child.ps1'),'-Mode','RabbitEnvironment') -WorkingDirectory $path -Environment @{TASKMANAGER_FIXTURE_MARKER=$marker;RABBITMQ_NODENAME='taskmanager_e2e_fixture@localhost'} -IsolateRabbitEnvironment:$isolated
            try {
                Expect ((Wait-FullStackChild $handle -TimeoutSeconds 10) -eq 0)
                $result=Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json -AsHashtable
                Expect ($result.Count -eq 3 -and $result.foreignAbsent -is [bool] -and $result.foreignAbsent -eq $isolated -and $result.ownedPreserved -eq $true -and $result.unrelatedPreserved -eq $true)
            } finally {$null=Stop-FullStackChild $handle -GraceSeconds 1;$handle.Process.Dispose()}
        }
    } finally {foreach ($name in $prior.Keys){[Environment]::SetEnvironmentVariable($name,$prior[$name])}}
}
Check 'LiveAzure without real inputs exits SKIP2 and never creates promotion evidence' {
    $path=New-FullStackRunDirectory
    $executable=(Get-Process -Id $PID).Path
    $handle=Start-FullStackChild -Executable $executable -Arguments @('-NoProfile','-File',(Join-Path $PSScriptRoot '../Run.ps1'),'-Mode','LiveAzure') -WorkingDirectory (Get-FullStackRepository) -Environment @{}
    try { $code=Wait-FullStackChild $handle -TimeoutSeconds 10; Expect ($code -eq 2 -and -not (Test-Path -LiteralPath (Join-Path $path 'authenticated-candidate.json'))) }
    finally { $null=Stop-FullStackChild $handle -GraceSeconds 1; $handle.Process.Dispose() }
}
Write-Host "Full-stack runner fixtures: $script:passed passed, $script:failed failed. Native synthetic children only; no SQL/product/cloud calls."
if ($script:failed) { exit 1 }
