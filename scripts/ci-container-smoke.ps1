# This adapter deliberately runs only on a disposable GitHub-hosted Linux runner.
# Runtime credentials must never enter the Docker build context or shared logs.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSEdition -ne 'Core' -or -not $IsLinux -or $env:GITHUB_ACTIONS -ne 'true' -or
    $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'Use local-up.ps1/local-smoke.ps1 locally. This script requires a disposable GitHub Actions Linux runner.'
}
if ($env:GITHUB_RUN_ID -notmatch '^\d+$' -or $env:GITHUB_RUN_ATTEMPT -notmatch '^\d+$' -or
    $env:GITHUB_SHA -notmatch '^[0-9a-fA-F]{40}$') {
    throw 'Missing or invalid GitHub run identity.'
}

. (Join-Path $PSScriptRoot 'local-common.ps1')
Assert-TaskManagerDocker

function New-ContainerSmokeSecret {
    $bytes = [byte[]]::new(32)
    [Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    [Convert]::ToHexString($bytes).ToLowerInvariant()
}

function Assert-ContainerSmokeHttp {
    param([string] $Uri, [int] $StatusCode, [string] $Method = 'GET', [switch] $DevelopmentCertificate)
    if ($DevelopmentCertificate -and ([Uri]$Uri).Host -ne 'localhost') { throw 'Development certificate smoke must stay on localhost.' }
    $response = Invoke-WebRequest -Uri $Uri -Method $Method -TimeoutSec 10 -MaximumRedirection 0 -SkipHttpErrorCheck -SkipCertificateCheck:$DevelopmentCertificate
    if ([int] $response.StatusCode -ne $StatusCode) {
        throw "HTTP smoke failed: $Method $Uri expected $StatusCode, received $($response.StatusCode)."
    }
    if ($StatusCode -eq 401 -and $response.Headers.ContainsKey('Location')) {
        throw 'Anonymous API/hub requests must return 401 without a redirect.'
    }
}

function Assert-ContainerSmokeImage {
    param([string] $Image)
    $configuredUser = & docker image inspect --format '{{.Config.User}}' $Image
    if ($LASTEXITCODE -ne 0 -or "$configuredUser".Trim() -ne '1654') {
        throw "Image $Image must declare non-root UID 1654."
    }
    $actualUser = & docker run --rm --read-only --network none --entrypoint id $Image -u
    if ($LASTEXITCODE -ne 0 -or "$actualUser".Trim() -ne '1654') {
        throw "Image $Image did not execute as non-root UID 1654."
    }
    Write-Host "PASS non-root image $Image"
}

$repositoryPath = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$runnerTempPath = (Resolve-Path -LiteralPath $env:RUNNER_TEMP).Path
$temporaryPath = Join-Path $runnerTempPath ('taskmanager-smoke-' + [Guid]::NewGuid().ToString('N'))
$null = [IO.Directory]::CreateDirectory($temporaryPath)
[IO.File]::SetUnixFileMode($temporaryPath,
    [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::UserExecute)
$environmentPath = Join-Path $temporaryPath '.env'
$projectName = 'taskmanager-ci-' + $env:GITHUB_RUN_ID + '-' + $env:GITHUB_RUN_ATTEMPT
$imageTag = $env:GITHUB_SHA.ToLowerInvariant()
$context = [pscustomobject]@{
    Repository = $repositoryPath
    Environment = $environmentPath
    Compose = Join-Path $repositoryPath 'docker-compose.yml'
}
$composeStarted = $false

try {
    $sqlPassword = 'Aa9!' + (New-ContainerSmokeSecret)
    $rabbitPassword = New-ContainerSmokeSecret
    $jwtKey = New-ContainerSmokeSecret
    $certificatePassword = New-ContainerSmokeSecret
    $certificatePath = Join-Path $temporaryPath 'localhost.pfx'
    foreach ($secret in @($sqlPassword, $rabbitPassword, $jwtKey, $certificatePassword)) {
        Write-Host ('::add-mask::' + $secret)
    }

    # Explicit values avoid using a developer's .env or reusable CI credentials.
    $environmentLines = @(
        "COMPOSE_PROJECT_NAME=$projectName"
        "TASKMANAGER_IMAGE_TAG=$imageTag"
        "MSSQL_SA_PASSWORD=$sqlPassword"
        'RABBITMQ_USER=taskmanager'
        "RABBITMQ_PASSWORD=$rabbitPassword"
        "TASKMANAGER_JWT_KEY=$jwtKey"
        'API_PORT=38080'
        'NOTIFIER_PORT=38081'
        'PUBLIC_ORIGIN=https://localhost:37551'
        'WEB_PORT=37551'
        "GATEWAY_CERT_PATH=$certificatePath"
        "GATEWAY_CERT_PASSWORD=$certificatePassword"
        'MAILPIT_PORT=38025'
        'SQL_PORT=31433'
        'RABBITMQ_PORT=35672'
        'RABBITMQ_MANAGEMENT_PORT=35673'
    )
    [IO.File]::WriteAllLines($environmentPath, $environmentLines, [Text.UTF8Encoding]::new($false))
    [IO.File]::SetUnixFileMode($environmentPath, [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite)
    New-TaskManagerDevelopmentCertificate -CertificatePath $certificatePath -Password $certificatePassword

    Invoke-TaskManagerCompose -Context $context -Arguments @('--profile', 'web', 'config', '--quiet')
    Invoke-TaskManagerCompose -Context $context -Arguments @('--profile', 'web', '--profile', 'console', 'build', 'webapi', 'notifier', 'migrations', 'client', 'web')
    foreach ($imageName in @('api', 'notifier', 'migrations', 'client', 'web')) {
        Assert-ContainerSmokeImage -Image "taskmanager-${imageName}:$imageTag"
    }

    $composeStarted = $true
    # Mailpit is explicit because it is not a Compose dependency of the API.
    Invoke-TaskManagerCompose -Context $context -Arguments @(
        '--profile', 'web', 'up', '--detach', '--no-build', '--wait', '--wait-timeout', '300', 'mailpit', 'webapi', 'notifier', 'web')

    $migrationContainers = @(Invoke-TaskManagerCompose -Context $context -Arguments @('ps', '--all', '--quiet', 'migrations'))
    if ($migrationContainers.Count -ne 1 -or $migrationContainers[0] -notmatch '^[0-9a-f]{12,64}$') {
        throw 'Expected exactly one migration job container.'
    }
    $migrationResult = & docker inspect --format '{{.State.Status}} {{.State.ExitCode}}' $migrationContainers[0]
    if ($LASTEXITCODE -ne 0 -or "$migrationResult".Trim() -ne 'exited 0') {
        throw 'Migration bundle did not complete successfully.'
    }

    $migrationFiles = @(Get-ChildItem -LiteralPath (Join-Path $repositoryPath 'DAL/Migrations') -File |
        Where-Object { $_.BaseName -match '^\d{14}_[A-Za-z0-9_]+$' } | Sort-Object BaseName -Descending)
    if ($migrationFiles.Count -eq 0) { throw 'No application migration found.' }
    $latestMigration = $migrationFiles[0].BaseName
    $migrationQuery = "SET NOCOUNT ON; SELECT COUNT(*) FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'$latestMigration';"
    # SQLCMDPASSWORD is expanded inside the existing SQL container, never on the host command line.
    $migrationCount = @(Invoke-TaskManagerCompose -Context $context -Arguments @(
        'exec', '-T', 'mssql', 'sh', '-c',
        'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" exec /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -d TaskManagerDB -b -h -1 -W -Q "$1"',
        '_', $migrationQuery))
    if (($migrationCount -join '').Trim() -ne '1') { throw 'The latest migration is absent from SQL migration history.' }
    Write-Host 'PASS migration bundle exit and latest SQL migration'

    foreach ($service in @('webapi', 'notifier')) {
        $actualUser = @(Invoke-TaskManagerCompose -Context $context -Arguments @('exec', '-T', $service, 'id', '-u'))
        if (($actualUser -join '').Trim() -ne '1654') { throw "$service must run as UID 1654." }
        Invoke-TaskManagerCompose -Context $context -Arguments @(
            'exec', '-T', $service, 'sh', '-c', 'test -w /home/app/.aspnet/DataProtection-Keys')
    }
    Write-Host 'PASS running application UIDs and writable Data Protection volume'

    & (Join-Path $PSScriptRoot 'local-smoke.ps1') -ApiOrigin 'http://127.0.0.1:38080' -NotifierOrigin 'http://127.0.0.1:38081'
    Assert-ContainerSmokeHttp -Uri 'http://127.0.0.1:38080/api/Tasks' -StatusCode 401
    Assert-ContainerSmokeHttp -Uri 'http://127.0.0.1:38081/chathub/negotiate?negotiateVersion=1' -Method POST -StatusCode 401
    Write-Host 'PASS anonymous API and SignalR negotiate return 401'

    $webOrigin = 'https://localhost:37551'
    foreach ($path in @('/', '/projects', '/confirm-email?token=synthetic-query-privacy-proof', '/health/live', '/health/ready', '/api/auth/csrf')) {
        Assert-ContainerSmokeHttp -Uri ($webOrigin + $path) -StatusCode 200 -DevelopmentCertificate
    }
    Assert-ContainerSmokeHttp -Uri ($webOrigin + '/api/Tasks') -StatusCode 401 -DevelopmentCertificate
    Assert-ContainerSmokeHttp -Uri ($webOrigin + '/chathub/negotiate?negotiateVersion=1') -Method POST -StatusCode 401 -DevelopmentCertificate
    Assert-ContainerSmokeHttp -Uri ($webOrigin + '/health/unknown') -StatusCode 404 -DevelopmentCertificate
    $shell = Invoke-WebRequest -Uri ($webOrigin + '/') -SkipCertificateCheck -TimeoutSec 10
    if ($shell.Headers['Referrer-Policy'] -ne 'no-referrer' -or $shell.Headers['Cache-Control'] -notcontains 'no-store') {
        throw 'Gateway privacy headers are missing.'
    }
    $scriptMatch = [regex]::Match($shell.Content, '<script[^>]+src="(/assets/[A-Za-z0-9._-]+\.js)"')
    if (-not $scriptMatch.Success) { throw 'Built React entrypoint is missing from the gateway shell.' }
    Assert-ContainerSmokeHttp -Uri ($webOrigin + $scriptMatch.Groups[1].Value) -StatusCode 200 -DevelopmentCertificate
    $csrfResponse = Invoke-WebRequest -Uri ($webOrigin + '/api/auth/csrf') -SkipCertificateCheck -TimeoutSec 10
    $csrfCookie = $csrfResponse.Headers['Set-Cookie'] -join ';'
    if ($csrfCookie -notmatch '__Host-TaskManager\.Antiforgery=' -or $csrfCookie -notmatch '(?i);\s*secure' -or $csrfCookie -match '(?i);\s*domain=') {
        throw 'The proxied HTTPS antiforgery cookie contract is invalid.'
    }
    $webContainers = @(Invoke-TaskManagerCompose -Context $context -Arguments @('--profile', 'web', 'ps', '--quiet', 'web'))
    if ($webContainers.Count -ne 1) { throw 'Expected exactly one web container.' }
    $webUser = & docker exec $webContainers[0] id -u
    if ($LASTEXITCODE -ne 0 -or "$webUser".Trim() -ne '1654') { throw 'Web gateway must run as UID 1654.' }
    $gatewayLogs = @(& docker logs $webContainers[0] 2>&1)
    if ($LASTEXITCODE -ne 0 -or ($gatewayLogs -join "`n") -match 'synthetic-query-privacy-proof') {
        throw 'Gateway query privacy verification failed.'
    }
    Write-Host 'PASS HTTPS SPA/assets/API/hub/CSRF-cookie routing, privacy and running web UID'

    Assert-ContainerSmokeHttp -Uri 'http://127.0.0.1:38025/' -StatusCode 200
    Invoke-TaskManagerCompose -Context $context -Arguments @(
        'exec', '-T', 'webapi', 'bash', '-c',
        'exec 3<>/dev/tcp/mailpit/1025; read -r -t 5 status <&3; [[ "$status" == "220 "* ]]')
    Write-Host 'PASS Mailpit HTTP and SMTP from application network'

    # Exercise the actual console entrypoint without creating accounts or emitting tokens.
    $clientOutput = @('13' | & docker compose --project-directory $context.Repository --env-file $context.Environment `
        --file $context.Compose --profile console run --rm --no-deps -T client)
    if ($LASTEXITCODE -ne 0 -or ($clientOutput -join "`n") -notmatch '13\. Exit the program') {
        throw 'Console image did not start and exit through its normal menu.'
    }
    Write-Host 'PASS console image startup and normal exit'
}
finally {
    if ($composeStarted) {
        try {
            Invoke-TaskManagerCompose -Context $context -Arguments @('--profile', 'web', '--profile', 'console', 'down', '--volumes', '--remove-orphans', '--timeout', '10')
        }
        catch {
            Write-Warning 'Disposable Compose cleanup failed; the GitHub-hosted runner will be discarded.'
        }
    }
    # Only remove the unique directory created by this invocation inside RUNNER_TEMP.
    $resolvedTemporaryPath = (Resolve-Path -LiteralPath $temporaryPath).Path
    if (-not $resolvedTemporaryPath.StartsWith($runnerTempPath.TrimEnd('/') + '/', [StringComparison]::Ordinal) -or
        (Split-Path -Leaf $resolvedTemporaryPath) -notmatch '^taskmanager-smoke-[0-9a-f]{32}$') {
        throw 'Refusing cleanup outside the generated runner temporary directory.'
    }
    Remove-Item -LiteralPath $resolvedTemporaryPath -Recurse -Force
}
