#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)] [string] $ReleasePath, [Parameter(Mandatory)] [string] $ConfigurationPath,
      [Parameter(Mandatory)] [string] $OutputPath)
. (Join-Path $PSScriptRoot 'Validation.ps1')
$config = Read-TaskManagerConfiguration $ConfigurationPath
$state = Get-Content -LiteralPath $ReleasePath -Raw | ConvertFrom-Json -AsHashtable -Depth 100
Assert-TaskManagerReleaseManifest $state.manifest $state.foundation.registryLoginServer $state.manifest.commitSha
$foundation = @{}
foreach ($name in @('registryLoginServer', 'registryResourceId', 'managedEnvironmentId', 'appNames', 'publicOrigin', 'environmentDefaultDomain')) { $foundation[$name] = $state.foundation[$name] }
$journal = @{ schemaVersion = 1; product = 'TaskManager'; githubEnvironment = $config.githubEnvironment; manifest = $state.manifest; foundation = $foundation;
    snapshots = $state.snapshots; candidateSuffix = $state.candidateSuffix; phase = $state.phase; jobs = $state.jobs; completed = $state.completed }
foreach ($key in @('apps', 'smoke', 'canonicalSmoke', 'authenticatedEvidence', 'failedPhase', 'startedAtUtc', 'promotedAtUtc', 'rolledBackAtUtc')) { if ($state.Contains($key)) { $journal[$key] = $state[$key] } }
# Selected release metadata only: no runtimeConfiguration, connection strings,
# parameters, email contacts, JWT value, token, telemetry key or raw cloud payload.
Save-TaskManagerPrivateJson $OutputPath $journal
Write-Host 'Allowlisted release journal exported; it contains revision/traffic/job IDs and immutable image references.'
