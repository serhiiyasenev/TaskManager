#requires -Version 7.4
. (Join-Path $PSScriptRoot '../Common.ps1')
. (Join-Path $PSScriptRoot '../../azure/Operations.ps1')
$script:liveVerifiedExecutions = @{}

function Get-LiveRequiredChecks {
    @('browserCookieCsrf','teamDataIsolation','emailConfirmationRecovery','sqlManagedIdentity','serviceBusDelivery',
      'signalRAuthenticatedInbox','azureOpenAiReviewCommit','persistentDataProtectionRestart','telemetryPrivacy')
}
function Assert-LiveCandidateOrigin([string] $Origin) {
    $value = Assert-TaskManagerOrigin $Origin
    $uri = [Uri] $value
    if (-not $uri.IsDefaultPort -or $uri.IsLoopback -or $uri.Host -cnotmatch '^[a-z0-9-]+--r[a-f0-9]{12}\.[a-z0-9.-]+\.azurecontainerapps\.io$' -or $value -cne $Origin) {
        throw 'Live smoke must use the exact HTTPS Azure Web candidate revision origin.'
    }
}
function Resolve-LiveFile([string] $Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw 'An explicit reviewed file is required.' }
    $target = [IO.Path]::GetFullPath($Path, (Get-FullStackRepository))
    $file = [IO.FileInfo]::new($target)
    if (-not $file.Exists -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Reviewed input must be a regular existing file.' }
    $directory = $file.Directory
    while ($null -ne $directory) {
        if ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reviewed files cannot traverse links or junctions.' }
        $directory = $directory.Parent
    }
    return $target
}
function Read-LiveJson([string] $Path, [long] $MaximumBytes = 1048576) {
    $target = Resolve-LiveFile $Path
    if ([IO.FileInfo]::new($target).Length -gt $MaximumBytes) { throw 'Live input exceeded its bounded size.' }
    try { Get-Content -LiteralPath $target -Raw | ConvertFrom-Json -AsHashtable -Depth 60 }
    catch { throw 'Invalid private live JSON; content is not logged.' }
}
function Get-LiveVerificationMissingInputs($Configuration) {
    foreach ($key in @('authorizeLiveExecution','commitSha','deploymentConfigurationPath','deploymentConfigurationSha256','releaseStatePath','releaseStateSha256','azureCliPath','nativeProbes')) {
        $value = Get-FullStackField $Configuration $key
        if ($null -eq $value -or ($value -is [string] -and [string]::IsNullOrWhiteSpace($value)) -or
            ($key -eq 'authorizeLiveExecution' -and $value -eq $false)) { $key }
    }
    foreach ($key in @('deploymentConfigurationPath','releaseStatePath','azureCliPath')) {
        $value = Get-FullStackField $Configuration $key
        if ($value -is [string] -and $value -and -not (Test-Path -LiteralPath ([IO.Path]::GetFullPath($value, (Get-FullStackRepository))) -PathType Leaf)) { "$key.file" }
    }
    $probes = Get-FullStackField $Configuration 'nativeProbes' @{}
    foreach ($check in Get-LiveRequiredChecks) {
        $probe = Get-FullStackField $probes $check
        if ($null -eq $probe) { "nativeProbes.$check"; continue }
        foreach ($key in @('path','sha256','accessConfigurationPath','accessConfigurationSha256','requiredEnvironment')) {
            $value = Get-FullStackField $probe $key
            if ($null -eq $value -or ($value -is [string] -and [string]::IsNullOrWhiteSpace($value))) { "nativeProbes.$check.$key" }
        }
        foreach ($key in @('path','accessConfigurationPath')) {
            $value = Get-FullStackField $probe $key
            if ($value -is [string] -and $value -and -not (Test-Path -LiteralPath ([IO.Path]::GetFullPath($value, (Get-FullStackRepository))) -PathType Leaf)) { "nativeProbes.$check.$key.file" }
        }
        foreach ($name in @(Get-FullStackField $probe 'requiredEnvironment' @())) {
            if ($name -is [string] -and $name -cmatch '^TASKMANAGER_LIVE_[A-Z0-9_]{1,100}$' -and
                [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) { "nativeProbes.$check.environment.$name" }
        }
    }
}
function Resolve-LiveNativeProbe($Probe, [string] $Check) {
    if ($Check -cnotin (Get-LiveRequiredChecks)) { throw 'Unknown live check.' }
    $root = [IO.Path]::GetFullPath((Join-Path (Get-FullStackRepository) 'scripts/fullstack/live/probes'))
    $path = Resolve-LiveFile (Get-FullStackField $Probe 'path')
    if ($path -ine (Join-Path $root "$Check.ps1")) { throw 'Only the reviewed named repository native probe may receive live access.' }
    $hash = Get-FullStackField $Probe 'sha256' ''
    if ($hash -cnotmatch '^[a-f0-9]{64}$' -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $hash) {
        throw 'Native probe source does not match its reviewed digest.'
    }
    $path
}
function Assert-LiveConfiguration($Configuration) {
    Assert-LiveExactKeys $Configuration @('schemaVersion','authorizeLiveExecution','commitSha','deploymentConfigurationPath','deploymentConfigurationSha256',
        'releaseStatePath','releaseStateSha256','azureCliPath','nativeProbes')
    if ($Configuration.schemaVersion -ne 1 -or $Configuration.authorizeLiveExecution -isnot [bool] -or -not $Configuration.authorizeLiveExecution -or
        $Configuration.commitSha -cnotmatch '^[a-f0-9]{40}$') { throw 'Reviewed live execution requires explicit boolean authorization and a fixed commit.' }
    foreach ($kind in @('deploymentConfiguration','releaseState')) {
        $path = Resolve-LiveFile $Configuration["${kind}Path"]
        $hash = $Configuration["${kind}Sha256"]
        if ($hash -cnotmatch '^[a-f0-9]{64}$' -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $hash) { throw 'Reviewed Azure release/configuration bytes changed.' }
    }
    $null = Resolve-LiveFile $Configuration.azureCliPath
    Assert-LiveExactKeys $Configuration.nativeProbes (Get-LiveRequiredChecks)
    foreach ($check in Get-LiveRequiredChecks) {
        $probe = $Configuration.nativeProbes[$check]
        Assert-LiveExactKeys $probe @('path','sha256','accessConfigurationPath','accessConfigurationSha256','requiredEnvironment')
        $null = Resolve-LiveNativeProbe $probe $check
        $access = Resolve-LiveFile $probe.accessConfigurationPath
        if ($probe.accessConfigurationSha256 -cnotmatch '^[a-f0-9]{64}$' -or (Get-FileHash -LiteralPath $access -Algorithm SHA256).Hash.ToLowerInvariant() -cne $probe.accessConfigurationSha256) { throw 'Reviewed native access configuration changed.' }
        if ($probe.requiredEnvironment -isnot [array] -or $probe.requiredEnvironment.Count -gt 20 -or
            @($probe.requiredEnvironment | Select-Object -Unique).Count -ne $probe.requiredEnvironment.Count) { throw 'Native environment allowlist is invalid.' }
        foreach ($name in $probe.requiredEnvironment) {
            if ($name -isnot [string] -or $name -cnotmatch '^TASKMANAGER_LIVE_[A-Z0-9_]{1,100}$') { throw 'Only explicit native credential environment names are allowed.' }
        }
    }
}
function Assert-LiveReleaseBinding($Configuration, $State, [string] $CommitSha) {
    Assert-TaskManagerFoundationOutputs $State.foundation
    Assert-TaskManagerReleaseManifest $State.manifest $State.foundation.registryLoginServer $CommitSha
    if ($State.phase -cne 'CandidateAwaitingAuthenticatedSmoke' -or $State.githubEnvironment -cne $Configuration.githubEnvironment -or
        $State.manifest.subscriptionId -cne $Configuration.subscriptionId -or $State.manifest.tenantId -cne $Configuration.tenantId -or
        $State.manifest.resourceGroupName -cne $Configuration.resourceGroupName -or $State.manifest.environmentName -cne $Configuration.foundation.environmentName -or
        $State.manifest.resourceNamePrefix -cne $Configuration.foundation.resourceNamePrefix -or $State.candidateSuffix -cne ('r' + $CommitSha.Substring(0,12))) { throw 'Candidate is not the reviewed awaiting-authentication release.' }
    foreach ($unit in @('web','api','notifier')) {
        if ($State.foundation.appNames[$unit] -cne "$($Configuration.foundation.resourceNamePrefix)-$($Configuration.foundation.environmentName)-$unit") { throw 'Candidate application scope differs from the reviewed deployment.' }
    }
    foreach ($phase in @('sql-bootstrap-before','migration-once','sql-bootstrap-after')) {
        if ((Get-FullStackField (Get-FullStackField $State.jobs $phase @{}) 'status') -cne 'Succeeded') { throw 'The exact database release jobs have not all succeeded.' }
    }
    Assert-LiveCandidateOrigin $State.apps.candidateUrls.web
    foreach ($id in $State.foundation.resourceIds.Values) {
        Assert-TaskManagerResourceId $id
        if (-not $id.StartsWith("/subscriptions/$($Configuration.subscriptionId)/", [StringComparison]::OrdinalIgnoreCase)) { throw 'Candidate dependencies belong to another subscription.' }
    }
}
function Assert-LiveCandidateProviders([scriptblock] $Invoker, $Configuration, $State) {
    $foundation = $State.foundation; $runtime = $foundation.runtimeConfiguration
    foreach ($pair in @(@('dataProtection','AzureBlobKeyVault'),@('serviceBus','AzureServiceBus'),@('realtime','AzureSignalR'),@('ai','Azure'),@('email','AzureCommunication'),@('telemetry','AzureMonitor'))) {
        if ((Get-FullStackField $runtime[$pair[0]] 'provider') -cne $pair[1]) { throw 'Live proof requires every native Azure adapter.' }
    }
    foreach ($unit in @('web','api','notifier')) {
        $expected = @{
            ASPNETCORE_ENVIRONMENT='Production'; DOTNET_ENVIRONMENT='Production'; BrowserAuth__PublicOrigin=$foundation.publicOrigin;
            BrowserAuth__AdditionalAllowedOrigins__0=$State.apps.candidateUrls.web; Telemetry__Provider='AzureMonitor';
            Telemetry__ManagedIdentityClientId=$foundation.identityIds[$unit].clientId
        }
        if ($unit -eq 'web') {
            $expected.Gateway__ApiDestination=$State.apps.candidateUrls.api
            $expected.Gateway__NotifierDestination=$State.apps.candidateUrls.notifier
        } else {
            $expected.Database__Authentication='AzureManagedIdentity'; $expected.DataProtection__Provider='AzureBlobKeyVault'
            $expected.DataProtection__BlobUri=$runtime.dataProtection.blobUri; $expected.DataProtection__KeyVaultKeyUri=$runtime.dataProtection.keyVaultKeyUri
            $expected.DataProtection__ManagedIdentityClientId=$foundation.identityIds[$unit].clientId
            $expected.Notifications__Provider='AzureServiceBus'; $expected.ServiceBus__FullyQualifiedNamespace=$runtime.serviceBus.fullyQualifiedNamespace
            $expected.ServiceBus__QueueName='TaskNotifications'; $expected.ServiceBus__ReminderQueueName='TaskReminders'
            $expected.ServiceBus__ManagedIdentityClientId=$foundation.identityIds[$unit].clientId
            $expected.ConnectionStrings__DbConnection="Server=tcp:$($foundation.sqlServerFqdn),1433;Database=$($foundation.sqlDatabaseName);Authentication=Active Directory Managed Identity;User Id=$($foundation.identityIds[$unit].clientId);Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
            if ($unit -eq 'api') {
                $expected.AI__Provider='Azure'; $expected.AI__Endpoint=$runtime.ai.endpoint; $expected.AI__Deployment=$runtime.ai.deployment
                $expected.AI__ManagedIdentityClientId=$foundation.identityIds.api.clientId; $expected.Email__Provider='AzureCommunication'
                $expected.Email__AzureCommunicationEndpoint=$runtime.email.azureCommunicationEndpoint; $expected.Email__FromAddress=$runtime.email.fromAddress
                $expected.Email__ManagedIdentityClientId=$foundation.identityIds.api.clientId
            } else {
                $expected.Realtime__Provider='AzureSignalR'; $expected.Realtime__Endpoint=$runtime.realtime.endpoint
                $expected.Realtime__ManagedIdentityClientId=$foundation.identityIds.notifier.clientId
            }
        }
        # Query selected configuration, never JWT values or an environment dump.
        $filter = @($expected.Keys | Sort-Object | ForEach-Object { "name=='$_'" }) -join ' || '
        $query = "properties.template.containers[0].env[?$filter].{name:name,value:value}"
        $values = @(& $Invoker @('containerapp','revision','show','--name',$foundation.appNames[$unit],'--resource-group',$Configuration.resourceGroupName,
            '--revision',$State.apps.candidateRevisions[$unit],'--query',$query))
        if ($values.Count -ne $expected.Count) { throw 'Actual native adapter configuration is incomplete.' }
        foreach ($name in $expected.Keys) {
            $matches = @($values | Where-Object { $_.name -ceq $name })
            if ($matches.Count -ne 1 -or $matches[0].value -cne $expected[$name]) { throw 'Actual candidate origin, identity, destination or provider differs from the reviewed configuration.' }
        }
    }
}
function Assert-LiveExactKeys($Object, [string[]] $Keys) {
    if ($Object -isnot [Collections.IDictionary] -or $Object.Count -ne $Keys.Count -or @($Object.Keys | Where-Object { $_ -cnotin $Keys }).Count) {
        throw 'Live report contains unknown or missing fields.'
    }
}
function Assert-LiveBinding($Value, $Context) {
    foreach ($key in @('runId','commitSha','origin')) {
        if ((Get-FullStackField $Value $key) -cne (Get-FullStackField $Context $key)) { throw 'Live report belongs to another run or candidate.' }
    }
    $revisions = Get-FullStackField $Value 'candidateRevisions' @{}
    Assert-LiveExactKeys $revisions @('web','api','notifier')
    foreach ($unit in @('web','api','notifier')) {
        if ($revisions[$unit] -cne $Context.candidateRevisions[$unit]) { throw 'Live report belongs to another deployed revision.' }
    }
}
function Assert-LiveNativeProbeResult($Result, $Context, [string] $Check, [string] $Directory,
    [DateTimeOffset] $ExecutionStarted, [DateTimeOffset] $ExecutionCompleted) {
    Assert-LiveExactKeys $Result @('schemaVersion','kind','check','runId','commitSha','origin','candidateRevisions','provider','syntheticFixtures',
        'status','nativeOperationCount','startedAtUtc','completedAtUtc','evidenceFile','evidenceSha256')
    Assert-LiveBinding $Result $Context
    if ($Check -cnotin (Get-LiveRequiredChecks) -or $Result.schemaVersion -ne 1 -or $Result.kind -cne 'live-azure-native-probe' -or
        $Result.check -cne $Check -or $Result.provider -cne 'Azure' -or $Result.syntheticFixtures -isnot [bool] -or $Result.syntheticFixtures -ne $false -or
        $Result.status -cne 'Passed' -or ($Result.nativeOperationCount -isnot [int] -and $Result.nativeOperationCount -isnot [long]) -or
        $Result.nativeOperationCount -lt 1 -or $Result.nativeOperationCount -gt 100000 -or $Result.evidenceFile -cne "$Check.evidence.json") { throw 'Actual native probe did not report a passing bounded Azure execution.' }
    $start = [DateTimeOffset]::MinValue; $end = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse($Result.startedAtUtc, [ref] $start) -or -not [DateTimeOffset]::TryParse($Result.completedAtUtc, [ref] $end) -or
        $start.Offset -ne [TimeSpan]::Zero -or $end.Offset -ne [TimeSpan]::Zero -or $start -lt $ExecutionStarted -or $end -gt $ExecutionCompleted -or
        $end -lt $start -or $ExecutionCompleted - $ExecutionStarted -gt [TimeSpan]::FromMinutes(30) -or [DateTimeOffset]::UtcNow - $end -gt [TimeSpan]::FromMinutes(30)) { throw 'Probe evidence is stale or outside its observed native execution.' }
    $path = Assert-FullStackPrivatePath (Join-Path $Directory $Result.evidenceFile)
    if ([IO.DirectoryInfo]::new([IO.Path]::GetFullPath($Directory)).Name -cne $Context.runId) { throw 'Evidence directory belongs to another private run.' }
    $path = Resolve-LiveFile $path
    if ([IO.FileInfo]::new($path).LastWriteTimeUtc -lt $ExecutionStarted.UtcDateTime.AddSeconds(-2)) { throw 'Probe reused a pre-existing artifact.' }
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($Result.evidenceSha256 -cnotmatch '^[a-f0-9]{64}$' -or $hash -cne $Result.evidenceSha256) { throw 'Native evidence bytes do not match the reported hash.' }
    $artifact = Read-LiveJson $path
    # Only aggregate observations and hashed correlation are retained. Notes,
    # response bodies, URLs, account/mail/token/exception text are prohibited.
    Assert-LiveExactKeys $artifact @('schemaVersion','kind','check','runId','commitSha','origin','candidateRevisions','provider','syntheticFixtures','observations')
    Assert-LiveBinding $artifact $Context
    if ($artifact.schemaVersion -ne 1 -or $artifact.kind -cne 'live-native-observations' -or $artifact.check -cne $Check -or
        $artifact.provider -cne 'Azure' -or $artifact.syntheticFixtures -isnot [bool] -or $artifact.syntheticFixtures -ne $false -or
        $artifact.observations -isnot [array] -or $artifact.observations.Count -lt 1 -or $artifact.observations.Count -gt 1000) { throw 'Native observations are missing or invalid.' }
    foreach ($observation in $artifact.observations) {
        Assert-LiveExactKeys $observation @('name','status','correlationSha256')
        if ($observation.name -cnotmatch '^[a-z][a-zA-Z0-9]{1,63}$' -or $observation.status -cne 'Passed' -or
            $observation.correlationSha256 -cnotmatch '^[a-f0-9]{64}$') { throw 'Native observations must contain bounded codes and hashed correlation only.' }
    }
    [pscustomobject]@{ check = $Check; status = 'Passed'; provider = 'Azure'; evidenceSha256 = $hash; evidencePath = $path }
}
function New-LiveAuthenticatedEvidence($Context, $Results) {
    if ($Context.runId -cnotmatch '^[a-f0-9]{32}$' -or $Context.commitSha -cnotmatch '^[a-f0-9]{40}$') { throw 'Invalid live execution binding.' }
    Assert-LiveCandidateOrigin $Context.origin
    Assert-LiveExactKeys $Results (Get-LiveRequiredChecks)
    $checks = @{}
    foreach ($check in Get-LiveRequiredChecks) {
        $value = $Results[$check]
        $registered = Get-FullStackField $script:liveVerifiedExecutions "$($Context.runId):$check"
        if ($null -eq $registered -or -not [object]::ReferenceEquals($registered, $value) -or $value.provider -cne 'Azure' -or
            $value.status -cne 'Passed' -or (Get-FileHash -LiteralPath (Resolve-LiveFile $value.evidencePath) -Algorithm SHA256).Hash.ToLowerInvariant() -cne $value.evidenceSha256 -or
            (Get-FileHash -LiteralPath (Resolve-LiveFile $value.resultPath) -Algorithm SHA256).Hash.ToLowerInvariant() -cne $value.resultSha256 -or
            (Get-FileHash -LiteralPath (Resolve-LiveFile $value.sourcePath) -Algorithm SHA256).Hash.ToLowerInvariant() -cne $value.sourceSha256) {
            throw 'Every check requires a successful native process and unchanged verified artifact from this run.'
        }
        $checks[$check] = @{ status = 'Passed'; evidenceSha256 = $value.evidenceSha256 }
    }
    @{ schemaVersion = 1; kind = 'live-authenticated-candidate'; syntheticFixtures = $false; commitSha = $Context.commitSha; origin = $Context.origin;
       candidateRevisions = $Context.candidateRevisions; completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); checks = $checks }
}
function Invoke-LiveNativeProbe($Probe, $Context, [string] $Check, [string] $Directory, [int] $TimeoutSeconds = 180) {
    $source = Resolve-LiveNativeProbe $Probe $Check
    $access = Resolve-LiveFile $Probe.accessConfigurationPath
    if ((Get-FileHash -LiteralPath $access -Algorithm SHA256).Hash.ToLowerInvariant() -cne $Probe.accessConfigurationSha256) { throw 'Reviewed native access configuration changed.' }
    $resultPath = Join-Path $Directory "$Check.result.json"
    $artifactPath = Join-Path $Directory "$Check.evidence.json"
    if ((Test-Path -LiteralPath $resultPath) -or (Test-Path -LiteralPath $artifactPath)) { throw 'Native result paths must be fresh.' }
    $contextPath = Join-Path $Directory "$Check.context.json"
    Save-FullStackPrivateJson $contextPath @{ schemaVersion = 1; kind = 'live-azure-probe-context'; check = $Check; runId = $Context.runId;
        commitSha = $Context.commitSha; origin = $Context.origin; candidateRevisions = $Context.candidateRevisions; provider = 'Azure'; syntheticFixtures = $false;
        deploymentConfigurationPath = $Context.deploymentConfigurationPath; releaseStatePath = $Context.releaseStatePath;
        accessConfigurationPath = $access; azureCliPath = $Context.azureCliPath; artifactPath = $artifactPath }
    $environment = @{}
    foreach ($entry in [Environment]::GetEnvironmentVariables().Keys) {
        if ($entry -match '^(TASKMANAGER_LIVE_|GH_TOKEN$|GITHUB_TOKEN$|ACTIONS_ID_TOKEN_REQUEST_TOKEN$)') { $environment[$entry] = '' }
    }
    foreach ($name in $Probe.requiredEnvironment) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if ([string]::IsNullOrWhiteSpace($value)) { throw 'An explicitly required native credential is unavailable.' }
        $environment[$name] = $value
    }
    if ($env:AZURE_CONFIG_DIR) { $environment.AZURE_CONFIG_DIR = $env:AZURE_CONFIG_DIR }
    $handle = $null; $started = [DateTimeOffset]::UtcNow
    try {
        $handle = Start-FullStackChild -Executable (Get-Process -Id $PID).Path -Arguments @('-NoProfile','-File',$source,'-ContextPath',$contextPath,'-ResultPath',$resultPath) -WorkingDirectory (Get-FullStackRepository) -Environment $environment
        if ((Wait-FullStackChild $handle -TimeoutSeconds $TimeoutSeconds) -ne 0) { throw 'Native live probe failed; raw output is suppressed.' }
        $ended = [DateTimeOffset]::UtcNow
        $resultPath = Resolve-LiveFile $resultPath
        if ([IO.FileInfo]::new($resultPath).LastWriteTimeUtc -lt $started.UtcDateTime.AddSeconds(-2)) { throw 'Native process did not write a fresh result.' }
        $verified = Assert-LiveNativeProbeResult (Read-LiveJson $resultPath 65536) $Context $Check $Directory $started $ended
        $null = Resolve-LiveNativeProbe $Probe $Check
        $verified | Add-Member -NotePropertyMembers @{resultPath=$resultPath;resultSha256=(Get-FileHash -LiteralPath $resultPath -Algorithm SHA256).Hash.ToLowerInvariant();
            sourcePath=$source;sourceSha256=$Probe.sha256}
        # Registration happens only after the actual child exit and validation;
        # a dictionary of Passed fields or local/scope fixtures cannot compose it.
        $script:liveVerifiedExecutions["$($Context.runId):$Check"] = $verified
        return $verified
    } finally {
        if ($null -ne $handle) { $null = Stop-FullStackChild $handle -GraceSeconds 1; $handle.Process.Dispose() }
    }
}
