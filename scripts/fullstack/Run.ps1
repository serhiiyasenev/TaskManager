#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)] [ValidateSet('Controlled','RabbitMQ','LiveAzure')] [string] $Mode,
      [switch] $NoBuild, [ValidateSet('Release','Debug')] [string] $Configuration = 'Release',
      [ValidateRange(1,300)] [int] $StartupTimeoutSeconds = 120,
      [ValidateRange(1,3600)] [int] $BrowserTimeoutSeconds = 900,
      [ValidateRange(1,120)] [int] $ShutdownGraceSeconds = 120,
      [string] $LiveConfigurationPath)
. (Join-Path $PSScriptRoot 'Common.ps1')
if ($Mode -eq 'LiveAzure') {
    & (Join-Path $PSScriptRoot 'Test-LivePrerequisites.ps1') -ConfigurationPath $LiveConfigurationPath
    exit $LASTEXITCODE
}
Reset-FullStackScreenshots
if (-not $env:TASKMANAGER_FULLSTACK_SQL) { Write-Host 'SKIP: explicit TASKMANAGER_FULLSTACK_SQL master connection is absent. No test host/database/browser started.'; exit 2 }
Assert-FullStackSqlMaster $env:TASKMANAGER_FULLSTACK_SQL
if ($Mode -eq 'RabbitMQ') {
    foreach ($key in @('TASKMANAGER_FULLSTACK_RABBIT_HOST','TASKMANAGER_FULLSTACK_RABBIT_PORT','TASKMANAGER_FULLSTACK_RABBIT_USER','TASKMANAGER_FULLSTACK_RABBIT_PASSWORD')) {
        if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($key))) { throw 'RabbitMQ mode requires explicit disposable broker host/port/user/password environment values.' }
    }
    if ($env:TASKMANAGER_FULLSTACK_RABBIT_HOST -notin @('localhost','127.0.0.1','::1') -or $env:TASKMANAGER_FULLSTACK_RABBIT_PORT -notmatch '^\d{1,5}$' -or
        [int] $env:TASKMANAGER_FULLSTACK_RABBIT_PORT -lt 1024 -or [int] $env:TASKMANAGER_FULLSTACK_RABBIT_PORT -gt 65535) { throw 'Full-stack broker must be the explicitly disposable loopback service.' }
}
$repository = Get-FullStackRepository
$frontend = Join-Path $repository 'frontend'
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$node = (Get-Command node -ErrorAction Stop).Source
$runDirectory = New-FullStackRunDirectory
$runId = [IO.DirectoryInfo]::new($runDirectory).Name
$statePath = Join-Path $runDirectory 'state.json'
$cleanupPath = Join-Path $runDirectory 'cleanup.json'
$fullStackHostHandle = $null; $browser = $null; $state = $null; $failure = $false; $cleanupVerified = $false
$hostEnvironment = @{ TASKMANAGER_FULLSTACK_ENABLE = '1'; TASKMANAGER_FULLSTACK_SQL = $env:TASKMANAGER_FULLSTACK_SQL;
    TASKMANAGER_FULLSTACK_STATE = $statePath; TASKMANAGER_FULLSTACK_TRANSPORT = $Mode; TASKMANAGER_FULLSTACK_RUN_ID = $runId }
if ($Mode -eq 'RabbitMQ') {
    foreach ($key in @('TASKMANAGER_FULLSTACK_RABBIT_HOST','TASKMANAGER_FULLSTACK_RABBIT_PORT','TASKMANAGER_FULLSTACK_RABBIT_USER','TASKMANAGER_FULLSTACK_RABBIT_PASSWORD')) { $hostEnvironment[$key] = [Environment]::GetEnvironmentVariable($key) }
}
try {
    if (-not (Test-Path -LiteralPath (Join-Path $frontend 'dist/index.html')) -or -not (Test-Path -LiteralPath (Join-Path $frontend 'node_modules/@playwright/test/cli.js'))) { throw 'Prepare locked frontend dependencies and production dist explicitly before full-stack execution.' }
    if (-not $NoBuild) {
        Write-Host 'Building only the test host sequentially using locked dependencies; frontend preparation remains explicit.'
        foreach ($arguments in @(@('restore','tools/TaskManager.FullStackHost/TaskManager.FullStackHost.csproj','--locked-mode','-warnaserror:NU1901,NU1902,NU1903,NU1904'),
            @('build','tools/TaskManager.FullStackHost/TaskManager.FullStackHost.csproj','--configuration',$Configuration,'--no-restore','-warnaserror:NU1901,NU1902,NU1903,NU1904'))) {
            $build = Start-FullStackChild -Executable $dotnet -Arguments $arguments -WorkingDirectory $repository -Environment @{}
            try { if ((Wait-FullStackChild $build -TimeoutSeconds 600) -ne 0) { throw 'Explicit locked test-host build failed; raw build output is not forwarded.' } }
            finally { $null = Stop-FullStackChild $build -GraceSeconds 1; $build.Process.Dispose() }
        }
    }
    $assembly = Join-Path $repository "tools/TaskManager.FullStackHost/bin/$Configuration/net10.0/TaskManager.FullStackHost.dll"
    if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) { throw 'Verified test-host artifact is missing; -NoBuild never performs an implicit restore/build.' }
    Write-Host "Starting explicitly enabled local full-stack host ($Mode)."
    $fullStackHostHandle = Start-FullStackChild -Executable $dotnet -Arguments @($assembly) -WorkingDirectory $repository -Environment $hostEnvironment
    $state = Wait-FullStackState -Handle $fullStackHostHandle -Path $statePath -Transport $Mode -TimeoutSeconds $StartupTimeoutSeconds
    Write-Host 'Test host ready. Running actual desktop/mobile product requests without interception.'
    $browser = Start-FullStackChild -Executable $node -Arguments @('node_modules/@playwright/test/cli.js','test','--config','playwright.fullstack.config.ts') -WorkingDirectory $frontend -Environment @{
        TASKMANAGER_FULLSTACK_STATE = $statePath; TASKMANAGER_FULLSTACK_RUN_ID = $runId; TASKMANAGER_UI_BROWSER = $(if ($env:TASKMANAGER_UI_BROWSER) { $env:TASKMANAGER_UI_BROWSER } else { 'chrome' }) }
    if ((Wait-FullStackChild $browser -TimeoutSeconds $BrowserTimeoutSeconds) -ne 0) { throw 'Actual browser verification failed; raw auth/mail/control output is not forwarded.' }
    $summaryPath = Join-Path $frontend 'test-results/fullstack-summary.json'
    $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json -AsHashtable -Depth 10
    Assert-FullStackBrowserSummary $summary $runId $Mode
} catch {
    $failure = $true
    # Keep only the exception type: runtime messages could contain credentials,
    # request URLs, mail or browser assertions. Native child streams were discarded.
    Write-Host "FAIL controlled full-stack run ($($_.Exception.GetType().Name)); inspect the bounded setup and sanitized browser summary."
} finally {
    if ($null -ne $browser) { try { $null = Stop-FullStackChild $browser -GraceSeconds 1 } catch { $failure = $true }; $browser.Process.Dispose() }
    if ($null -ne $fullStackHostHandle) {
        try {
            $stopped = Stop-FullStackChild $fullStackHostHandle -GraceSeconds $ShutdownGraceSeconds
            if (-not $stopped.graceful -or $stopped.exitCode -ne 0 -or $null -eq $state -or -not (Test-Path -LiteralPath $cleanupPath -PathType Leaf)) { throw 'Host cleanup is unverified.' }
            $cleanup = Get-Content -LiteralPath $cleanupPath -Raw | ConvertFrom-Json -AsHashtable -Depth 10
            Assert-FullStackCleanup $cleanup $state.databaseName
            $cleanupVerified = $true
        } catch { $failure = $true; Write-Host 'FAIL: exact owned host/database/broker cleanup remains unverified; no cleanup success is claimed.' }
        finally { $fullStackHostHandle.Process.Dispose() }
    }
    $hostEnvironment.Clear()
    if (Test-Path -LiteralPath $statePath -PathType Leaf) { $target = Assert-FullStackPrivatePath $statePath; Remove-Item -LiteralPath $target }
    Save-FullStackPrivateJson (Join-Path $runDirectory 'runner-result.json') @{ schemaVersion = 1; mode = 'controlled-full-stack-runner'; runId = $runId; provider = 'fake'; transport = $Mode.ToLowerInvariant();
        passed = (-not $failure -and $cleanupVerified); cleanupVerified = $cleanupVerified; liveAzureEvidence = $false; completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O') }
}
if ($failure -or -not $cleanupVerified) { exit 1 }
Write-Host 'PASS actual local full-stack browser requests and verified owned cleanup. Fake AI/email and local transport are explicit; this result cannot authorize live Azure promotion.'
