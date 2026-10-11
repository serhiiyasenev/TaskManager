param(
    [string] $ApiOrigin = 'http://localhost:8080',
    [string] $NotifierOrigin = 'http://localhost:8081'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
foreach ($path in @('/health/live', '/health/ready')) {
    $response = Invoke-WebRequest -Uri ($ApiOrigin.TrimEnd('/') + $path) -UseBasicParsing -TimeoutSec 10
    if ($response.StatusCode -ne 200) { throw "API $path did not return HTTP 200." }
    Write-Host "PASS API $path HTTP 200"
}
$response = Invoke-WebRequest -Uri ($NotifierOrigin.TrimEnd('/') + '/') -UseBasicParsing -TimeoutSec 10
if ($response.StatusCode -ne 200) { throw 'Notifier root did not return HTTP 200.' }
Write-Host 'PASS Notifier root HTTP 200. Hub is at /chathub; this does not verify durable delivery or authorization.'
