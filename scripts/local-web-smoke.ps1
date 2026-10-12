param([string] $Origin = 'https://localhost:7151')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $Origin.StartsWith('https://', [StringComparison]::OrdinalIgnoreCase)) { throw 'Browser smoke requires an HTTPS origin.' }
foreach ($path in @('/', '/projects', '/health/live', '/health/ready', '/api/auth/csrf')) {
    $response = Invoke-WebRequest -Uri ($Origin.TrimEnd('/') + $path) -UseBasicParsing -TimeoutSec 15
    if ($response.StatusCode -ne 200) { throw "Gateway $path did not return HTTP 200." }
    if ($response.Headers['Referrer-Policy'] -ne 'no-referrer') { throw 'Gateway privacy header is missing.' }
    Write-Host "PASS HTTPS gateway $path"
}
Write-Host 'HTTPS routing, SPA and CSRF prerequisites passed. This smoke does not verify account or notification workflows.'
