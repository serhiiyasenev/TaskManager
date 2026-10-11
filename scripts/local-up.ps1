. (Join-Path $PSScriptRoot 'local-common.ps1')
$context = Get-TaskManagerLocalContext
Assert-TaskManagerDocker
Invoke-TaskManagerCompose -Context $context -Arguments @('config', '--quiet')
Invoke-TaskManagerCompose -Context $context -Arguments @('up', '--build', '--detach', '--wait', '--wait-timeout', '240', 'webapi', 'notifier')
Write-Host 'Local services started. Run scripts/local-smoke.ps1 to verify HTTP health.'
