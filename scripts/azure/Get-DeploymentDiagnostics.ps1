#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)] [string] $ConfigurationPath, [Parameter(Mandatory)] [string] $ReleasePath,
      [Parameter(Mandatory)] [string] $AzureCliPath, [Parameter(Mandatory)] [string] $OutputPath)
. (Join-Path $PSScriptRoot 'Operations.ps1')
$config = Read-TaskManagerConfiguration $ConfigurationPath
$state = Get-Content -LiteralPath $ReleasePath -Raw | ConvertFrom-Json -AsHashtable -Depth 100
$invoker = New-TaskManagerInvoker $AzureCliPath; Assert-TaskManagerContext $invoker $config
$revisions = @{}
foreach ($unit in @('web', 'api', 'notifier')) {
    $revisions[$unit] = & $invoker @('containerapp', 'revision', 'list', '--name', $state.foundation.appNames[$unit], '--resource-group', $config.resourceGroupName,
        '--query', '[].{name:name,active:properties.active,healthState:properties.healthState,provisioningState:properties.provisioningState,replicas:properties.replicas}')
}
$jobs = @{}
foreach ($phase in $state.jobs.Keys) {
    $job = $state.jobs[$phase]
    if (Get-TaskManagerField $job 'executionName') { $jobs[$phase] = & $invoker @('containerapp', 'job', 'execution', 'show', '--name', $job.jobName, '--resource-group', $config.resourceGroupName,
        '--job-execution-name', $job.executionName, '--query', '{name:name,status:properties.status,startTime:properties.startTime,endTime:properties.endTime}') }
}
Save-TaskManagerPrivateJson $OutputPath @{ capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); phase = $state.phase; revisions = $revisions; jobs = $jobs;
    rawApplicationLogsCollected = $false; secretsCollected = $false }
Write-Host 'Selected revision and exact execution diagnostics saved; raw application logs and secrets were not collected.'
