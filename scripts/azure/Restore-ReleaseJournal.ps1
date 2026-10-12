#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)] [string] $JournalPath, [Parameter(Mandatory)] [string] $ConfigurationPath,
      [Parameter(Mandatory)] [string] $CommitSha, [Parameter(Mandatory)] [string] $OutputPath)
. (Join-Path $PSScriptRoot 'Validation.ps1')
$config = Read-TaskManagerConfiguration $ConfigurationPath
$journal = Get-Content -LiteralPath $JournalPath -Raw | ConvertFrom-Json -AsHashtable -Depth 100
Assert-TaskManagerReleaseManifest $journal.manifest $journal.foundation.registryLoginServer $CommitSha
if ($journal.githubEnvironment -cne $config.githubEnvironment -or $journal.manifest.subscriptionId -cne $config.subscriptionId -or $journal.manifest.tenantId -cne $config.tenantId -or
    $journal.manifest.resourceGroupName -cne $config.resourceGroupName -or $journal.manifest.environmentName -cne $config.foundation.environmentName -or
    $journal.manifest.resourceNamePrefix -cne $config.foundation.resourceNamePrefix) { throw 'Downloaded journal belongs to another reviewed release scope.' }
foreach ($unit in @('web', 'api', 'notifier')) { if ($journal.foundation.appNames[$unit] -cne "$($config.foundation.resourceNamePrefix)-$($config.foundation.environmentName)-$unit") { throw 'Journal application names differ from the reviewed deployment.' } }
if ($journal.candidateSuffix -cne ('r' + $CommitSha.Substring(0, 12))) { throw 'Journal candidate revision suffix does not match the immutable release.' }
Save-TaskManagerPrivateJson $OutputPath $journal
Write-Host 'Trusted-source release journal scope verified and restored privately.'
