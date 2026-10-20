#requires -Version 7.4
[CmdletBinding()]
param([string] $ConfigurationPath, [string] $BicepPath, [switch] $OfflineOnly)
. (Join-Path $PSScriptRoot 'Validation.ps1')
if (-not $BicepPath) { $BicepPath = (& (Join-Path $PSScriptRoot 'Install-Tools.ps1') -Tools Bicep).Bicep }
Test-TaskManagerOffline -BicepPath $BicepPath
if ($ConfigurationPath) { $null = Read-TaskManagerConfiguration $ConfigurationPath; Write-Host 'Configuration boundary checks passed.' }
if (-not $OfflineOnly) { throw 'Cloud validation runs inside Deploy.ps1 using an explicitly pinned authenticated CLI and reviewed configuration. Use -OfflineOnly for offline gates.' }
