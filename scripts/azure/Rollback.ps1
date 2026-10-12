#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)] [string] $ConfigurationPath, [Parameter(Mandatory)] [string] $ReleasePath, [Parameter(Mandatory)] [string] $AzureCliPath)
. (Join-Path $PSScriptRoot 'Operations.ps1')
$config = Read-TaskManagerConfiguration $ConfigurationPath
$state = Get-Content -LiteralPath $ReleasePath -Raw | ConvertFrom-Json -AsHashtable -Depth 100
$foundation = $state.foundation; $manifest = $state.manifest
Assert-TaskManagerReleaseManifest $manifest $foundation.registryLoginServer $manifest.commitSha
if ($manifest.subscriptionId -cne $config.subscriptionId -or $manifest.resourceGroupName -cne $config.resourceGroupName) { throw 'Rollback configuration belongs to another release.' }
$invoker = New-TaskManagerInvoker $AzureCliPath; Assert-TaskManagerContext $invoker $config
$candidateNames = @{}; foreach ($unit in @('web', 'api', 'notifier')) { $candidateNames[$unit] = "$($foundation.appNames[$unit])--$($state.candidateSuffix)" }
$execute = {
    param($Phase)
    switch ($Phase) {
        'deactivate-candidate-notifier' {
            $revisions = @(& $invoker @('containerapp', 'revision', 'list', '--name', $foundation.appNames.notifier, '--resource-group', $config.resourceGroupName, '--query', '[].{name:name,active:properties.active}'))
            if ($candidateNames.notifier -in @($revisions | ForEach-Object { $_.name })) {
                $null = & $invoker @('containerapp', 'revision', 'deactivate', '--name', $foundation.appNames.notifier, '--resource-group', $config.resourceGroupName, '--revision', $candidateNames.notifier, '--query', '{name:name}')
                $worker = & $invoker @('containerapp', 'revision', 'show', '--name', $foundation.appNames.notifier, '--resource-group', $config.resourceGroupName, '--revision', $candidateNames.notifier, '--query', '{active:properties.active}')
                if ($worker.active -ne $false) { throw 'Candidate worker deactivation is unverified; rollback cannot report success.' }
            }
        }
        'restore-explicit-traffic' {
            foreach ($unit in @('api', 'notifier', 'web')) {
                if (-not $state.snapshots[$unit].exists) { continue }
                foreach ($old in $state.snapshots[$unit].traffic) { if ($old.weight -gt 0) { $null = & $invoker @('containerapp', 'revision', 'activate', '--name', $foundation.appNames[$unit], '--resource-group', $config.resourceGroupName, '--revision', $old.revisionName, '--query', '{name:name}') } }
                Set-TaskManagerTraffic $invoker $config $foundation $unit $state.snapshots[$unit].traffic
            }
        }
        'restore-ingress-policy' {
            $rules = if ($state.snapshots.web.exists) { $state.snapshots.web.ipSecurityRestrictions } else { @($config.smokeAllowedCidrs | ForEach-Object -Begin { $index = 0 } -Process { @{ name = "smoke-$index"; ipAddressRange = $_; action = 'Allow' }; $index++ }) }
            Set-TaskManagerIngressPolicy $invoker $config $foundation $rules ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($ReleasePath)))
        }
        'deactivate-candidate-web-api' {
            foreach ($unit in @('web', 'api')) {
                $revisions = @(& $invoker @('containerapp', 'revision', 'list', '--name', $foundation.appNames[$unit], '--resource-group', $config.resourceGroupName, '--query', '[].{name:name}'))
                if ($candidateNames[$unit] -in @($revisions | ForEach-Object { $_.name })) { $null = & $invoker @('containerapp', 'revision', 'deactivate', '--name', $foundation.appNames[$unit], '--resource-group', $config.resourceGroupName, '--revision', $candidateNames[$unit], '--query', '{name:name}') }
            }
        }
    }
}.GetNewClosure()
Invoke-TaskManagerRollbackPhases $execute
$state.phase = 'RolledBack'; $state.rolledBackAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); Save-TaskManagerPrivateJson $ReleasePath $state
Write-Host 'Candidate worker deactivated; captured explicit traffic/ingress restored. Database schema remains migrated.'
