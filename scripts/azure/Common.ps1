#requires -Version 7.4
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# This module is declarative: invoking a command requires an explicit invoker.
# Tests pass a synthetic invoker; production callers use New-TaskManagerInvoker.
function Get-TaskManagerField {
    param([Parameter(Mandatory)] $Object, [Parameter(Mandatory)] [string] $Name, $Default = $null)
    if ($Object -is [Collections.IDictionary]) { if ($Object.Contains($Name)) { return $Object[$Name] }; return $Default }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -ne $property) { return $property.Value }
    return $Default
}
function Assert-TaskManagerOrigin {
    param([Parameter(Mandatory)] [string] $Origin)
    $parsed = $null
    if (-not [Uri]::TryCreate($Origin, [UriKind]::Absolute, [ref] $parsed) -or $parsed.Scheme -ne 'https' -or
        $parsed.UserInfo -or $parsed.Query -or $parsed.Fragment -or $parsed.AbsolutePath -ne '/' -or
        $parsed.HostNameType -ne [UriHostNameType]::Dns -or $parsed.Host -match '(^localhost$|\*|\s)') { throw 'Invalid public HTTPS origin.' }
    $parsed.GetLeftPart([UriPartial]::Authority).TrimEnd('/')
}
function Assert-TaskManagerGuid {
    param([Parameter(Mandatory)] [string] $Value)
    $parsed = [Guid]::Empty
    if (-not [Guid]::TryParseExact($Value, 'D', [ref] $parsed) -or $parsed -eq [Guid]::Empty) { throw 'Invalid required Azure identifier.' }
}
function Assert-TaskManagerReleaseManifest {
    param([Parameter(Mandatory)] $Manifest, [Parameter(Mandatory)] [string] $RegistryLoginServer, [Parameter(Mandatory)] [string] $CommitSha)
    if ((Get-TaskManagerField $Manifest 'schemaVersion') -ne 1 -or (Get-TaskManagerField $Manifest 'product') -ne 'TaskManager') { throw 'Unsupported release schema.' }
    if ($CommitSha -cnotmatch '^[a-f0-9]{40}$' -or (Get-TaskManagerField $Manifest 'commitSha') -cne $CommitSha) { throw 'Release commit mismatch.' }
    if ($RegistryLoginServer -cnotmatch '^[a-z0-9]+\.azurecr\.io$' -or (Get-TaskManagerField $Manifest 'registryLoginServer') -cne $RegistryLoginServer) { throw 'Unapproved registry.' }
    Assert-TaskManagerGuid (Get-TaskManagerField $Manifest 'subscriptionId')
    Assert-TaskManagerGuid (Get-TaskManagerField $Manifest 'tenantId')
    Assert-TaskManagerOrigin (Get-TaskManagerField $Manifest 'publicOrigin') | Out-Null
    $images = Get-TaskManagerField $Manifest 'images'
    if ($null -eq $images) { throw 'Missing release images.' }
    $units = @('web', 'api', 'notifier', 'migrations', 'sqlBootstrap')
    $keys = if ($images -is [Collections.IDictionary]) { @($images.Keys) } else { @($images.PSObject.Properties.Name) }
    if ($keys.Count -ne $units.Count -or @($keys | Where-Object { $_ -notin $units }).Count) { throw 'Incomplete release image set.' }
    foreach ($unit in $units) {
        $image = Get-TaskManagerField $images $unit
        $repository = if ($unit -eq 'sqlBootstrap') { 'taskmanager/sql-bootstrap' } else { "taskmanager/$unit" }
        $digest = Get-TaskManagerField $image 'digest'
        if ($digest -cnotmatch '^sha256:[a-f0-9]{64}$' -or (Get-TaskManagerField $image 'repository') -cne $repository -or
            (Get-TaskManagerField $image 'reference') -cne "$RegistryLoginServer/$repository@$digest") { throw 'Release image is not a verified immutable product reference.' }
    }
}
function Assert-TaskManagerOidcClaims {
    param([Parameter(Mandatory)] $Claims, [Parameter(Mandatory)] [string] $ExpectedSubject, [Parameter(Mandatory)] [string] $EnvironmentName)
    if ([string]::IsNullOrWhiteSpace($ExpectedSubject) -or $ExpectedSubject.Length -gt 512 -or $ExpectedSubject -match '[\r\n*]' -or
        (Get-TaskManagerField $Claims 'sub') -cne $ExpectedSubject -or
        (Get-TaskManagerField $Claims 'iss') -cne 'https://token.actions.githubusercontent.com' -or
        (Get-TaskManagerField $Claims 'aud') -cne 'api://AzureADTokenExchange' -or
        (Get-TaskManagerField $Claims 'repository') -cne 'serhiiyasenev/TaskManager' -or
        (Get-TaskManagerField $Claims 'ref') -cne 'refs/heads/main' -or
        (Get-TaskManagerField $Claims 'event_name') -cne 'workflow_dispatch' -or
        (Get-TaskManagerField $Claims 'environment') -cne $EnvironmentName -or
        (Get-TaskManagerField $Claims 'workflow_ref') -cne 'serhiiyasenev/TaskManager/.github/workflows/azure-deploy.yml@refs/heads/main') { throw 'Untrusted OIDC execution context.' }
    # This inspection is a configuration check. Azure Login validates the signed
    # token and its federation; decoding claims alone never grants cloud access.
}
function Get-TaskManagerWhatIfSummary {
    param([Parameter(Mandatory)] $Result, [Parameter(Mandatory)] [string] $ResourceGroupId, [string[]] $AllowedExistingResourceIds = @(), [string[]] $ApprovedOwnedLeafIds = @())
    if ((Get-TaskManagerField $Result 'status') -cne 'Succeeded') { throw 'What-if did not complete successfully.' }
    $changes = Get-TaskManagerField $Result 'changes'
    if ($null -eq $changes) { throw 'What-if is missing its change list.' }
    foreach ($change in @($changes)) {
        $id = Get-TaskManagerField $change 'resourceId'; $type = Get-TaskManagerField $change 'changeType'
        if (-not ($id -is [string]) -or $id -match '[\r\n?]' -or $type -notin @('Create', 'Modify', 'NoChange', 'NoEffect', 'Ignore')) { throw 'Unsafe or incomplete what-if result.' }
        $owned = $id.StartsWith($ResourceGroupId.TrimEnd('/') + '/providers/', [StringComparison]::OrdinalIgnoreCase)
        if ($id -in $AllowedExistingResourceIds -and $type -notin @('NoChange', 'NoEffect', 'Ignore')) { throw 'What-if would modify a shared parent resource.' }
        if (-not $owned -and $id -notin $ApprovedOwnedLeafIds -and (-not ($id -in $AllowedExistingResourceIds) -or $type -notin @('NoChange', 'NoEffect', 'Ignore'))) { throw 'What-if would modify an unowned resource.' }
        if ($type -eq 'Ignore' -and $id -notin $AllowedExistingResourceIds) { throw 'What-if could not evaluate an owned resource.' }
        # Full ARM payloads can include private configuration. Keep only these
        # selected fields; never serialize before/after/delta values to logs.
        [pscustomobject]@{ resourceId = $id; changeType = $type }
    }
}

function New-TaskManagerInvoker {
    param([Parameter(Mandatory)] [string] $AzureCliPath)
    $executable = [IO.Path]::GetFullPath($AzureCliPath)
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'Pinned Azure CLI is unavailable.' }
    $env:AZURE_EXTENSION_USE_DYNAMIC_INSTALL = 'no'
    try { $versionText = & $executable --version --output json 2>$null; $versionInfo = $versionText | ConvertFrom-Json -AsHashtable }
    catch { throw 'Pinned Azure CLI version could not be verified.' }
    if ($LASTEXITCODE -ne 0 -or $versionInfo['azure-cli'] -cne '2.91.0' -or $versionInfo.extensions.Count) { throw 'Azure CLI2.91.0 without overriding extensions is required.' }
    { param([string[]] $Arguments)
        if ($Arguments -contains '--debug' -or $Arguments -contains '--verbose') { throw 'Verbose credential-bearing CLI output is prohibited.' }
        try { $output = @(& $executable @Arguments --only-show-errors --output json 2>&1); $exit = $LASTEXITCODE }
        catch { throw 'Azure operation could not execute safely.' }
        if ($exit -ne 0) { throw "Azure operation failed (exit $exit)." }
        if ($Arguments.Count -ge 2 -and $Arguments[0] -eq 'acr' -and $Arguments[1] -eq 'login') { return $null }
        $text = ($output | ForEach-Object { $_.ToString() }) -join "`n"
        if ([string]::IsNullOrWhiteSpace($text)) { return $null }
        try { $text | ConvertFrom-Json -AsHashtable -Depth 100 } catch { throw 'Azure operation returned an invalid response.' }
    }.GetNewClosure()
}

function Assert-TaskManagerAzureName {
    param([Parameter(Mandatory)] [string] $Value)
    if ($Value -cnotmatch '^[a-z][a-z0-9-]{1,100}$' -or $Value.Contains('--') -or $Value.EndsWith('-')) { throw 'Invalid deployment resource name.' }
}

function Wait-TaskManagerJobExecution {
    param([Parameter(Mandatory)] [scriptblock] $Invoker,
          [Parameter(Mandatory)] [string] $ResourceGroupName,
          [Parameter(Mandatory)] [string] $JobName,
          [Parameter(Mandatory)] [string] $ExecutionName,
          [ValidateRange(1, 180)] [int] $MaxPolls = 120,
          [ValidateRange(0, 30)] [int] $PollSeconds = 10)
    Assert-TaskManagerAzureName $ResourceGroupName; Assert-TaskManagerAzureName $JobName; Assert-TaskManagerAzureName $ExecutionName
    if (-not $ExecutionName.StartsWith($JobName + '-', [StringComparison]::Ordinal)) { throw 'Job execution does not match the requested job.' }
    for ($attempt = 0; $attempt -lt $MaxPolls; $attempt++) {
        $value = & $Invoker @('containerapp', 'job', 'execution', 'show', '--name', $JobName, '--resource-group', $ResourceGroupName, '--job-execution-name', $ExecutionName,
            '--query', '{name:name,properties:{status:properties.status}}')
        if ((Get-TaskManagerField $value 'name') -cne $ExecutionName) { throw 'Unexpected job execution response.' }
        $status = Get-TaskManagerField (Get-TaskManagerField $value 'properties') 'status'
        if ($status -ceq 'Succeeded') { return [pscustomobject]@{ executionName = $ExecutionName; status = 'Succeeded' } }
        if ($status -cnotin @('Running', 'Pending', 'Processing')) { throw 'The requested job execution failed or stopped.' }
        if ($attempt + 1 -lt $MaxPolls -and $PollSeconds -gt 0) { Start-Sleep -Seconds $PollSeconds }
    }
    # A timeout is an uncertain outcome. Do not start another migration, claim
    # success, change traffic or assume the still-running execution stopped.
    throw 'The requested job execution did not finish before the polling limit.'
}

function Start-TaskManagerSingleJob {
    param([Parameter(Mandatory)] [scriptblock] $Invoker,
          [Parameter(Mandatory)] [string] $ResourceGroupName,
          [Parameter(Mandatory)] [string] $JobName)
    Assert-TaskManagerAzureName $ResourceGroupName; Assert-TaskManagerAzureName $JobName
    $executions = @(& $Invoker @('containerapp', 'job', 'execution', 'list', '--name', $JobName, '--resource-group', $ResourceGroupName,
        '--query', '[].{name:name,properties:{status:properties.status}}'))
    foreach ($execution in $executions) {
        if ((Get-TaskManagerField (Get-TaskManagerField $execution 'properties') 'status') -in @('Running', 'Pending', 'Processing')) { throw 'A job execution is already active; concurrent migration is prohibited.' }
    }
    $started = & $Invoker @('containerapp', 'job', 'start', '--name', $JobName, '--resource-group', $ResourceGroupName, '--query', '{name:name}')
    $name = Get-TaskManagerField $started 'name'
    Assert-TaskManagerAzureName $name
    if (-not $name.StartsWith($JobName + '-', [StringComparison]::Ordinal)) { throw 'Job start returned an unexpected execution identifier.' }
    $name
}

function Assert-TaskManagerSmokeCidrs {
    param([Parameter(Mandatory)] [string[]] $Cidrs)
    if ($Cidrs.Count -lt 1 -or $Cidrs.Count -gt 8 -or @($Cidrs | Select-Object -Unique).Count -ne $Cidrs.Count) { throw 'One to eight distinct smoke operator CIDRs are required.' }
    foreach ($cidr in $Cidrs) {
        $parts = $cidr.Split('/'); $address = $null; $bits = 0
        if ($parts.Count -ne 2 -or -not [Net.IPAddress]::TryParse($parts[0], [ref] $address) -or -not [int]::TryParse($parts[1], [ref] $bits)) { throw 'Invalid smoke CIDR.' }
        $ipv4 = $address.AddressFamily -eq [Net.Sockets.AddressFamily]::InterNetwork
        if (($ipv4 -and ($bits -lt 24 -or $bits -gt 32)) -or (-not $ipv4 -and ($bits -lt 120 -or $bits -gt 128)) -or
            $address.Equals([Net.IPAddress]::Any) -or $address.Equals([Net.IPAddress]::IPv6Any) -or [Net.IPAddress]::IsLoopback($address)) { throw 'Smoke networks must be bounded public operator networks (IPv4 /24..32, IPv6 /120..128).' }
        $bytes = $address.GetAddressBytes()
        for ($index = 0; $index -lt $bytes.Length; $index++) {
            $remaining = $bits - $index * 8
            $mask = if ($remaining -ge 8) { 255 } elseif ($remaining -le 0) { 0 } else { 256 - [int][Math]::Pow(2, 8 - $remaining) }
            if (($bytes[$index] -band (255 - $mask)) -ne 0) { throw 'Smoke CIDR must use its canonical network address.' }
        }
    }
}
function Invoke-TaskManagerReleasePhases {
    param([Parameter(Mandatory)] [scriptblock] $Execute)
    foreach ($phase in @('preflight', 'validate-and-what-if', 'foundation', 'build-and-push-digests', 'configure-secret-and-signalr-acl', 'jobs',
        'sql-bootstrap-before', 'migration-once', 'sql-bootstrap-after', 'candidate-apps', 'smoke', 'promote')) { & $Execute $phase | Out-Null }
}
function Invoke-TaskManagerRollbackPhases {
    param([Parameter(Mandatory)] [scriptblock] $Execute)
    foreach ($phase in @('deactivate-candidate-notifier', 'restore-explicit-traffic', 'restore-ingress-policy', 'deactivate-candidate-web-api')) { & $Execute $phase | Out-Null }
}
function Assert-TaskManagerResourceId {
    param([Parameter(Mandatory)] [string] $Value, [string] $ProviderType)
    if ($Value -notmatch '^/subscriptions/[a-fA-F0-9-]{36}/resourceGroups/[^/?#\s]+/providers/[A-Za-z0-9.]+/[^?#\s]+$' -or $Value.Contains('..')) { throw 'Invalid Azure resource ID.' }
    if ($ProviderType -and $Value -notmatch ('/providers/' + [Regex]::Escape($ProviderType) + '/[^/]+$')) { throw 'Azure resource ID has an unexpected provider type.' }
}
function Assert-TaskManagerContext {
    param([Parameter(Mandatory)] [scriptblock] $Invoker, [Parameter(Mandatory)] $Configuration)
    $account = & $Invoker @('account', 'show', '--query', '{id:id,tenantId:tenantId,state:state}')
    if ((Get-TaskManagerField $account 'id') -cne $Configuration.subscriptionId -or (Get-TaskManagerField $account 'tenantId') -cne $Configuration.tenantId -or
        (Get-TaskManagerField $account 'state') -cne 'Enabled') { throw 'Authenticated Azure subscription or tenant does not match the reviewed configuration.' }
}
function Save-TaskManagerPrivateJson {
    param([Parameter(Mandatory)] [string] $Path, [Parameter(Mandatory)] $Value)
    $repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    $root = [IO.Path]::GetFullPath((Join-Path $repository '.local/azure'))
    $target = [IO.Path]::GetFullPath($Path)
    if (-not $target.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Private state must remain under .local/azure.' }
    $directory = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)).FullName
    if ($IsLinux) { [IO.File]::SetUnixFileMode($directory, [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::UserExecute) }
    else {
        $acl = [Security.AccessControl.DirectorySecurity]::new(); $acl.SetAccessRuleProtection($true, $false)
        $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
        # Persist only the modified discretionary ACL. Set-Acl may additionally
        # request SACL privileges unavailable to ordinary Windows delivery users.
        [IO.FileSystemAclExtensions]::SetAccessControl([IO.DirectoryInfo]::new($directory), $acl)
    }
    $temporary = "$target.pending"
    [IO.File]::WriteAllText($temporary, ($Value | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))
    if ($IsLinux) { [IO.File]::SetUnixFileMode($temporary, [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite) }
    Move-Item -LiteralPath $temporary -Destination $target -Force
}
function Assert-TaskManagerAuthenticatedEvidence {
    param([Parameter(Mandatory)] $Evidence, [Parameter(Mandatory)] $State)
    if ((Get-TaskManagerField $Evidence 'schemaVersion') -ne 1 -or (Get-TaskManagerField $Evidence 'kind') -cne 'live-authenticated-candidate' -or
        (Get-TaskManagerField $Evidence 'syntheticFixtures') -ne $false -or (Get-TaskManagerField $Evidence 'commitSha') -cne $State.manifest.commitSha -or
        (Get-TaskManagerField $Evidence 'origin') -cne $State.apps.candidateUrls.web) { throw 'Authenticated smoke evidence must identify this actual immutable candidate; anonymous or synthetic results cannot authorize promotion.' }
    $recorded = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse((Get-TaskManagerField $Evidence 'completedAtUtc'), [ref] $recorded) -or [DateTimeOffset]::UtcNow - $recorded -gt [TimeSpan]::FromMinutes(30) -or
        $recorded -gt [DateTimeOffset]::UtcNow.AddMinutes(1)) { throw 'Authenticated candidate evidence is missing, expired or in the future.' }
    foreach ($unit in @('web', 'api', 'notifier')) {
        if ((Get-TaskManagerField (Get-TaskManagerField $Evidence 'candidateRevisions' @{}) $unit) -cne $State.apps.candidateRevisions[$unit]) { throw 'Authenticated evidence refers to another candidate revision.' }
    }
    $checks = Get-TaskManagerField $Evidence 'checks' @{}
    foreach ($check in @('browserCookieCsrf', 'teamDataIsolation', 'emailConfirmationRecovery', 'sqlManagedIdentity', 'serviceBusDelivery', 'signalRAuthenticatedInbox',
        'azureOpenAiReviewCommit', 'persistentDataProtectionRestart', 'telemetryPrivacy')) {
        $result = Get-TaskManagerField $checks $check @{}
        if ((Get-TaskManagerField $result 'status') -cne 'Passed' -or (Get-TaskManagerField $result 'evidenceSha256' '') -cnotmatch '^[a-f0-9]{64}$') { throw 'A required actual authenticated/Azure adapter smoke check has not passed with recorded evidence.' }
    }
}
