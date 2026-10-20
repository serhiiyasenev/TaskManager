. (Join-Path $PSScriptRoot 'local-common.ps1')
$context = Get-TaskManagerLocalContext
Assert-TaskManagerDocker
Invoke-TaskManagerCompose -Context $context -Arguments @('--profile', 'console', 'run', '--build', '--rm', 'client')
