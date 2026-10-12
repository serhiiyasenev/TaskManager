. (Join-Path $PSScriptRoot 'local-common.ps1')
$context = Get-TaskManagerLocalContext
if (-not (Test-Path -LiteralPath (Join-Path $context.Repository '.local/certificates/localhost.pfx'))) {
    throw 'Run scripts/local-https-init.ps1 -Trust before starting the browser stack.'
}
Assert-TaskManagerDocker
Invoke-TaskManagerCompose -Context $context -Arguments @('--profile', 'web', 'config', '--quiet')
Invoke-TaskManagerCompose -Context $context -Arguments @('--profile', 'web', 'up', '--build', '--detach', '--wait', '--wait-timeout', '300', 'web')
Write-Host 'Browser stack started. Open https://localhost:7151 (or PUBLIC_ORIGIN). Run scripts/local-web-smoke.ps1.'
