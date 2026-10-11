Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-TaskManagerLocalContext {
    $repositoryPath = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
    $environmentPath = Join-Path $repositoryPath '.env'
    if (-not (Test-Path -LiteralPath $environmentPath)) {
        throw 'Missing .env. Run scripts/local-init.ps1 first.'
    }
    [pscustomobject]@{
        Repository = $repositoryPath
        Environment = $environmentPath
        Compose = Join-Path $repositoryPath 'docker-compose.yml'
    }
}

function Assert-TaskManagerDocker {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw 'Docker is unavailable. Install/start Docker with Linux containers before running this script.'
    }
    & docker compose version --short | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Docker Compose v2 is required.' }
    & docker info --format '{{.ServerVersion}}' 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Docker daemon is unavailable. Start Docker with Linux containers.' }
}

function Invoke-TaskManagerCompose {
    param(
        [Parameter(Mandatory)] $Context,
        [Parameter(Mandatory)] [string[]] $Arguments
    )
    & docker compose --project-directory $Context.Repository --env-file $Context.Environment --file $Context.Compose @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Docker Compose failed (exit $LASTEXITCODE)." }
}
