Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryPath = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$environmentPath = Join-Path $repositoryPath '.env'
if (Test-Path -LiteralPath $environmentPath) {
    throw '.env already exists. Keep the existing credentials; edit that file explicitly if configuration needs to change.'
}

function New-LocalSecret {
    $bytes = New-Object byte[] 32
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($bytes) }
    finally { $generator.Dispose() }
    [BitConverter]::ToString($bytes).Replace('-', '').ToLowerInvariant()
}

$configuration = Get-Content -LiteralPath (Join-Path $repositoryPath '.env.example') -Raw
$configuration = $configuration.Replace('MSSQL_SA_PASSWORD=', ('MSSQL_SA_PASSWORD=Aa9!' + (New-LocalSecret)))
$configuration = $configuration.Replace('RABBITMQ_PASSWORD=', ('RABBITMQ_PASSWORD=' + (New-LocalSecret)))
$configuration = $configuration.Replace('TASKMANAGER_JWT_KEY=', ('TASKMANAGER_JWT_KEY=' + (New-LocalSecret)))
[IO.File]::WriteAllText($environmentPath, $configuration, (New-Object Text.UTF8Encoding $false))
Write-Host 'Created private .env with unique local credentials. No credential values were printed.'
