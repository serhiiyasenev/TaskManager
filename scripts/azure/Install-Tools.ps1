#requires -Version 7.4
[CmdletBinding()]
param([ValidateSet('Bicep', 'AzureCli', 'PowerShell', 'Actionlint')] [string[]] $Tools = @('Bicep'))
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$toolRoot = [IO.Path]::GetFullPath((Join-Path $repository '.tools/azure'))
if (-not $toolRoot.StartsWith($repository + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid tool directory.' }
$versions = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'tool-versions.json') -Raw | ConvertFrom-Json -AsHashtable
if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne [Runtime.InteropServices.Architecture]::X64) { throw 'Pinned delivery tools currently require x64 Windows or Linux.' }
$platform = if ($IsWindows) { 'windows-x64' } elseif ($IsLinux) { 'linux-x64' } else { throw 'Supported delivery platforms are Windows and Linux.' }
$result = @{}
foreach ($name in $Tools) {
    $definition = $versions[$name]; $asset = $definition[$platform]
    $allowedPrefix = switch ($name) {
        'Bicep' { 'https://github.com/Azure/bicep/releases/download/' }
        'AzureCli' { 'https://github.com/Azure/azure-cli/releases/download/' }
        'PowerShell' { 'https://github.com/PowerShell/PowerShell/releases/download/' }
        'Actionlint' { 'https://github.com/rhysd/actionlint/releases/download/' }
    }
    if (-not $asset.url.StartsWith($allowedPrefix, [StringComparison]::Ordinal) -or $asset.sha256 -cnotmatch '^[a-f0-9]{64}$' -or $definition.version -cnotmatch '^\d+\.\d+\.\d+$') { throw 'Invalid pinned Microsoft tool asset.' }
    $destination = [IO.Path]::GetFullPath((Join-Path $toolRoot "$name/$($definition.version)/$platform"))
    if (-not $destination.StartsWith($toolRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Tool destination escapes its workspace.' }
    $null = [IO.Directory]::CreateDirectory($destination)
    $download = Join-Path $destination ([IO.Path]::GetFileName(([Uri] $asset.url).AbsolutePath))
    if (-not (Test-Path -LiteralPath $download)) {
        $pending = "$download.partial"
        Invoke-WebRequest -Uri $asset.url -OutFile $pending
        if ((Get-FileHash -LiteralPath $pending -Algorithm SHA256).Hash.ToLowerInvariant() -cne $asset.sha256) { Remove-Item -LiteralPath $pending; throw 'Downloaded Microsoft tool hash mismatch.' }
        # Both absolute targets were bounded above; one native shell owns the move.
        Move-Item -LiteralPath $pending -Destination $download
    }
    if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash.ToLowerInvariant() -cne $asset.sha256) { throw 'Cached Microsoft tool hash mismatch.' }
    if ($asset.format -eq 'binary') {
        $executable = Join-Path $destination $asset.executable
        if ($executable -ne $download) { Copy-Item -LiteralPath $download -Destination $executable -Force }
    }
    else {
        $expanded = Join-Path $destination 'package'
        $null = [IO.Directory]::CreateDirectory($expanded)
        if ($asset.format -eq 'zip') {
            $archive = [IO.Compression.ZipFile]::OpenRead($download)
            try {
                foreach ($entry in $archive.Entries) {
                    $entryPath = [IO.Path]::GetFullPath((Join-Path $expanded $entry.FullName))
                    if (-not $entryPath.StartsWith($expanded + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Archive entry escapes its tool directory.' }
                }
            } finally { $archive.Dispose() }
            Expand-Archive -LiteralPath $download -DestinationPath $expanded -Force
        }
        else {
            & tar --extract --gzip --file $download --directory $expanded --no-same-owner
            if ($LASTEXITCODE -ne 0) { throw 'Pinned tool extraction failed.' }
        }
        $matches = @(Get-ChildItem -LiteralPath $expanded -Recurse -File -Filter $asset.executable)
        if ($matches.Count -ne 1) { throw 'Pinned tool executable was not uniquely located.' }
        $executable = $matches[0].FullName
    }
    if ($IsLinux) { [IO.File]::SetUnixFileMode($executable, [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::UserExecute) }
    $result[$name] = $executable
}
[pscustomobject] $result
