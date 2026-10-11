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
foreach ($path in @('/health/live', '/health/ready')) {
    $response = Invoke-WebRequest -Uri ($NotifierOrigin.TrimEnd('/') + $path) -UseBasicParsing -TimeoutSec 10
    if ($response.StatusCode -ne 200) { throw "Notifier $path did not return HTTP 200." }
    Write-Host "PASS Notifier $path HTTP 200"
}
Write-Host 'SQL and broker readiness passed. Durable delivery and authenticated hub workflows have separate tests.'
