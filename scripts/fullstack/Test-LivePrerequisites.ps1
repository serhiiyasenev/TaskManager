#requires -Version 7.4
[CmdletBinding()]
param([string] $ConfigurationPath)
. (Join-Path $PSScriptRoot 'live/Common.ps1')
if (-not $ConfigurationPath -or -not (Test-Path -LiteralPath $ConfigurationPath -PathType Leaf)) {
    Write-Host 'SKIP LiveAzure: reviewed real configuration/native probes are absent. No cloud/browser/model request or promotion evidence.'
    exit 2
}
$handle = $null
try {
    $configuration = Read-LiveJson $ConfigurationPath
    $missing = @(Get-LiveVerificationMissingInputs $configuration)
    if ($missing.Count) { Write-Host ('SKIP LiveAzure: missing actual inputs/probes: ' + ($missing -join ', ') + '. No network call or evidence.'); exit 2 }
    Assert-LiveConfiguration $configuration
    $directory = New-FullStackRunDirectory
    $environment = @{}
    foreach ($entry in [Environment]::GetEnvironmentVariables().Keys) {
        if ($entry -match '^(AZURE_|TASKMANAGER_LIVE_)') { $environment[$entry] = [Environment]::GetEnvironmentVariable($entry) }
    }
    $handle = Start-FullStackChild -Executable (Get-Process -Id $PID).Path -Arguments @('-NoProfile','-File',(Join-Path $PSScriptRoot 'live/Run.ps1'),
        '-ConfigurationPath',(Resolve-LiveFile $ConfigurationPath),'-RunDirectory',$directory) -WorkingDirectory (Get-FullStackRepository) -Environment $environment
    $code = Wait-FullStackChild $handle -TimeoutSeconds 1800
    if ($code -eq 0) {
        $result = Read-LiveJson (Join-Path $directory 'live-runner-result.json') 65536
        if ($result.result -cne 'PASS' -or $result.runId -cne [IO.DirectoryInfo]::new($directory).Name -or $result.executedNativeChecks -ne 9 -or $result.promotionChanged -ne $false) { throw 'Live process proof is incomplete.' }
        Write-Host "PASS LiveAzure: fresh native nine-check evidence retained privately in run $($result.runId). No promotion command was executed."
    } elseif ($code -eq 2) { Write-Host 'SKIP LiveAzure: reviewed actual inputs changed or are absent; no authenticated evidence.' }
    else { Write-Host 'FAIL LiveAzure: native scope/probe/evidence validation failed; inspect sanitized private runner result. No promotion.' }
    exit $code
} catch {
    Write-Host 'FAIL LiveAzure: invalid reviewed configuration or bounded native execution. Private content is not logged; no promotion.'
    exit 1
} finally {
    if ($null -ne $handle) { $null = Stop-FullStackChild $handle -GraceSeconds 1; $handle.Process.Dispose() }
}
