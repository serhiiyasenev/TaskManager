. (Join-Path $PSScriptRoot 'local-common.ps1')
$context = Get-TaskManagerLocalContext
Assert-TaskManagerDocker
Invoke-TaskManagerCompose -Context $context -Arguments @('--profile', 'web', '--profile', 'console', 'down', '--remove-orphans')
Write-Host 'Stopped local services. Database, broker and Data Protection volumes are retained.'
