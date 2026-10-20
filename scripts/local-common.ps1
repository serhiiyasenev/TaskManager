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

function Set-TaskManagerPrivateDirectory {
    param([Parameter(Mandatory)] [string] $Path)
    if ($env:OS -eq 'Windows_NT') {
        $security = New-Object Security.AccessControl.DirectorySecurity
        $security.SetAccessRuleProtection($true, $false)
        $inherit = [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit
        $identities = @(
            [Security.Principal.WindowsIdentity]::GetCurrent().User
            (New-Object Security.Principal.SecurityIdentifier ([Security.Principal.WellKnownSidType]::LocalSystemSid, $null))
            (New-Object Security.Principal.SecurityIdentifier ([Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid, $null))
        )
        foreach ($identity in $identities) {
            $rule = New-Object Security.AccessControl.FileSystemAccessRule ($identity, 'FullControl', $inherit, 'None', 'Allow')
            $security.AddAccessRule($rule)
        }
        Set-Acl -LiteralPath $Path -AclObject $security
    }
    else {
        [IO.File]::SetUnixFileMode($Path,
            [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::UserExecute)
    }
}

function New-TaskManagerDevelopmentCertificate {
    param([Parameter(Mandatory)] [string] $CertificatePath, [Parameter(Mandatory)] [string] $Password, [switch] $Trust)
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET SDK is required to export a development HTTPS certificate.' }
    $certificateDirectory = Split-Path -Parent ([IO.Path]::GetFullPath($CertificatePath))
    $null = [IO.Directory]::CreateDirectory($certificateDirectory)
    Set-TaskManagerPrivateDirectory -Path $certificateDirectory
    if (-not (Test-Path -LiteralPath $CertificatePath)) {
        & dotnet dev-certs https --export-path $CertificatePath --password $Password | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Development certificate export failed.' }
    }
    if ($env:OS -ne 'Windows_NT') {
        # The host directory is private; the bound file must be readable by container UID 1654.
        [IO.File]::SetUnixFileMode($CertificatePath,
            [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::GroupRead -bor [IO.UnixFileMode]::OtherRead)
    }
    if ($Trust) {
        & dotnet dev-certs https --trust | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Development certificate trust failed.' }
    }
}
