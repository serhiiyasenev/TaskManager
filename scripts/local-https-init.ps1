param([switch] $Trust)
. (Join-Path $PSScriptRoot 'local-common.ps1')
$context = Get-TaskManagerLocalContext
$configuration = Get-Content -LiteralPath $context.Environment -Raw
$passwordMatch = [regex]::Match($configuration, '(?m)^GATEWAY_CERT_PASSWORD=([^\r\n]*)$')
$certificatePassword = $passwordMatch.Groups[1].Value
$certificatePath = Join-Path $context.Repository '.local/certificates/localhost.pfx'
if (Test-Path -LiteralPath $certificatePath) {
    if ([string]::IsNullOrWhiteSpace($certificatePassword)) { throw 'Existing HTTPS certificate has no configured password. Preserve it and restore its private configuration.' }
}
elseif ([string]::IsNullOrWhiteSpace($certificatePassword)) {
    $bytes = New-Object byte[] 32
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($bytes) }
    finally { $generator.Dispose() }
    $certificatePassword = [BitConverter]::ToString($bytes).Replace('-', '').ToLowerInvariant()
}
$pathMatch = [regex]::Match($configuration, '(?m)^GATEWAY_CERT_PATH=([^\r\n]*)$')
if ($pathMatch.Success -and $pathMatch.Groups[1].Value -notin @('', './.local/certificates/localhost.pfx')) {
    throw 'A custom GATEWAY_CERT_PATH is configured. Export and configure that certificate explicitly.'
}
New-TaskManagerDevelopmentCertificate -CertificatePath $certificatePath -Password $certificatePassword -Trust:$Trust
foreach ($entry in @(
    [pscustomobject]@{ Key = 'GATEWAY_CERT_PASSWORD'; Value = $certificatePassword },
    [pscustomobject]@{ Key = 'GATEWAY_CERT_PATH'; Value = './.local/certificates/localhost.pfx' }
)) {
    $pattern = '(?m)^' + $entry.Key + '=[^\r\n]*$'
    if ([regex]::IsMatch($configuration, $pattern)) {
        $configuration = [regex]::Replace($configuration, $pattern, [Text.RegularExpressions.MatchEvaluator]{ param($match) $entry.Key + '=' + $entry.Value })
    }
    else { $configuration = $configuration.TrimEnd() + [Environment]::NewLine + $entry.Key + '=' + $entry.Value + [Environment]::NewLine }
}
[IO.File]::WriteAllText($context.Environment, $configuration, (New-Object Text.UTF8Encoding $false))
Write-Host 'Local HTTPS certificate and private configuration are ready. No password or private key was printed.'
