#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)] [string] $ConfigurationPath)
. (Join-Path $PSScriptRoot 'Validation.ps1')
$config = Read-TaskManagerConfiguration $ConfigurationPath
if ($env:GITHUB_ACTIONS -cne 'true' -or -not $env:ACTIONS_ID_TOKEN_REQUEST_URL -or -not $env:ACTIONS_ID_TOKEN_REQUEST_TOKEN) { throw 'OIDC inspection requires the protected trusted workflow context.' }
$uri = [Uri] $env:ACTIONS_ID_TOKEN_REQUEST_URL
if ($uri.Scheme -ne 'https' -or -not $uri.Host.EndsWith('.actions.githubusercontent.com', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected GitHub token request endpoint.' }
$separator = if ($uri.Query) { '&' } else { '?' }
$response = Invoke-RestMethod -Uri ($uri.AbsoluteUri + $separator + 'audience=api%3A%2F%2FAzureADTokenExchange') -Headers @{ Authorization = "Bearer $($env:ACTIONS_ID_TOKEN_REQUEST_TOKEN)" }
try {
    $parts = $response.value.Split('.')
    if ($parts.Count -ne 3) { throw 'Invalid token envelope.' }
    $encoded = $parts[1].Replace('-', '+').Replace('_', '/')
    $encoded = $encoded.PadRight($encoded.Length + ((4 - $encoded.Length % 4) % 4), '=')
    $claims = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($encoded)) | ConvertFrom-Json -AsHashtable
    Assert-TaskManagerOidcClaims $claims $config.expectedFederatedSubject $config.githubEnvironment
} finally { $response = $null; $claims = $null; $parts = $null; $encoded = $null }
Write-Host 'Actual OIDC subject and trusted main/environment context match reviewed federation. Azure Login must verify the signed token next.'
