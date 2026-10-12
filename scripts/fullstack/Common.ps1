#requires -Version 7.4
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-FullStackField($Object, [string] $Name, $Default = $null) {
    if ($Object -is [Collections.IDictionary]) { if ($Object.Contains($Name)) { return $Object[$Name] }; return $Default }
    if ($null -eq $Object) { return $Default }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -ne $property) { return $property.Value }
    return $Default
}
function Assert-FullStackLoopbackUri([string] $Value, [string] $Scheme) {
    $uri = $null
    if (-not [Uri]::TryCreate($Value, [UriKind]::Absolute, [ref] $uri) -or $uri.Scheme -cne $Scheme -or -not $uri.IsLoopback -or
        $uri.Host -notin @('localhost','127.0.0.1','[::1]','::1') -or $uri.Port -lt 1024 -or $uri.UserInfo -or $uri.Query -or $uri.Fragment -or $uri.AbsolutePath -ne '/') { throw 'Full-stack endpoints must be bounded loopback authorities.' }
}
function Assert-FullStackState($State, [ValidateSet('Controlled','RabbitMQ')] [string] $Transport) {
    if ((Get-FullStackField $State 'schemaVersion') -ne 1 -or (Get-FullStackField $State 'mode') -cne 'controlled-full-stack' -or
        (Get-FullStackField $State 'provider') -cne 'fake' -or (Get-FullStackField $State 'transport') -cne $Transport.ToLowerInvariant() -or
        (Get-FullStackField $State 'databaseName' '') -cnotmatch '^taskmanager_e2e_test_[a-f0-9]{32}$' -or
        (Get-FullStackField $State 'controlToken' '') -cnotmatch '^[a-fA-F0-9]{64}$') { throw 'Private full-stack state is missing its explicit test-only ownership/provider contract.' }
    Assert-FullStackLoopbackUri (Get-FullStackField $State 'baseUrl') https
    Assert-FullStackLoopbackUri (Get-FullStackField $State 'controlUrl') http
}
function Assert-FullStackSqlMaster([string] $ConnectionString) {
    if ([string]::IsNullOrWhiteSpace($ConnectionString)) { throw 'Explicit test SQL master connection is required.' }
    # Invoke the actual setter: PowerShell's IDictionary adapter otherwise adds
    # a literal "ConnectionString" dictionary key instead of parsing keywords.
    try { $builder = [Data.Common.DbConnectionStringBuilder]::new(); $builder.set_ConnectionString($ConnectionString) }
    catch { throw 'Invalid test SQL connection syntax.' }
    foreach ($key in @('Database','Initial Catalog')) {
        if ($builder.ContainsKey($key) -and "$($builder[$key])" -ine 'master') { throw 'Full-stack SQL must target master; existing application databases are prohibited.' }
    }
    if (-not @($builder.Keys | Where-Object { $_ -in @('Server','Data Source','Address','Addr','Network Address') }).Count) { throw 'Explicit SQL server is required.' }
}
function Assert-FullStackBrowserSummary($Summary, [string] $RunId, [ValidateSet('Controlled','RabbitMQ')] [string] $Transport) {
    if ((Get-FullStackField $Summary 'schemaVersion') -ne 1 -or (Get-FullStackField $Summary 'kind') -cne 'controlled-full-stack-browser' -or
        (Get-FullStackField $Summary 'runId') -cne $RunId -or (Get-FullStackField $Summary 'provider') -cne 'fake' -or
        (Get-FullStackField $Summary 'transport') -cne $Transport.ToLowerInvariant()) { throw 'Browser result does not belong to this controlled run.' }
    foreach ($key in @('total','passed','failed','skipped','flaky')) {
        $value = Get-FullStackField $Summary $key
        if (($value -isnot [int] -and $value -isnot [long]) -or $value -lt 0) { throw 'Browser result contains invalid aggregate counts.' }
    }
    if ($Summary.total -lt 1 -or $Summary.passed -ne $Summary.total -or $Summary.failed -ne 0 -or $Summary.skipped -ne 0 -or $Summary.flaky -ne 0) { throw 'Controlled browser verification requires executed passing cases without skips/retries.' }
}
function Get-FullStackRepository { [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')) }
function Assert-FullStackPrivatePath([string] $Path) {
    $root = [IO.Path]::GetFullPath((Join-Path (Get-FullStackRepository) '.local/fullstack'))
    $target = [IO.Path]::GetFullPath($Path)
    if (-not $target.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Full-stack private files must remain under the repository-owned .local/fullstack directory.' }
    $directory = [IO.DirectoryInfo]::new([IO.Path]::GetDirectoryName($target))
    while ($null -ne $directory -and $directory.FullName.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
        if ($directory.Exists -and ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Private full-stack paths cannot traverse a link or junction.' }
        $directory = $directory.Parent
    }
    return $target
}
function Set-FullStackPrivateDirectory([string] $Path) {
    $directory = [IO.Directory]::CreateDirectory($Path).FullName
    if ($IsLinux) { [IO.File]::SetUnixFileMode($directory, [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::UserExecute) }
    else {
        $acl = [Security.AccessControl.DirectorySecurity]::new(); $acl.SetAccessRuleProtection($true, $false)
        $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
        [IO.FileSystemAclExtensions]::SetAccessControl([IO.DirectoryInfo]::new($directory), $acl)
    }
}
function New-FullStackRunDirectory {
    $runId = [Guid]::NewGuid().ToString('N')
    $path = Join-Path (Get-FullStackRepository) ".local/fullstack/$runId"
    $null = Assert-FullStackPrivatePath (Join-Path $path 'state.json')
    Set-FullStackPrivateDirectory $path
    return [IO.Path]::GetFullPath($path)
}
function Save-FullStackPrivateJson([string] $Path, $Value) {
    $target = Assert-FullStackPrivatePath $Path
    Set-FullStackPrivateDirectory ([IO.Path]::GetDirectoryName($target))
    $pending = "$target.pending"
    [IO.File]::WriteAllText($pending, ($Value | ConvertTo-Json -Depth 50), [Text.UTF8Encoding]::new($false))
    if ($IsLinux) { [IO.File]::SetUnixFileMode($pending, [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite) }
    Move-Item -LiteralPath $pending -Destination $target -Force
}
function Start-FullStackChild {
    param([Parameter(Mandatory)] [string] $Executable, [string[]] $Arguments = @(), [Parameter(Mandatory)] [string] $WorkingDirectory,
          [Parameter(Mandatory)] [Collections.IDictionary] $Environment,
          [switch] $IsolateRabbitEnvironment)
    $file = [IO.Path]::GetFullPath($Executable)
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw 'Explicit child executable is unavailable.' }
    $info = [Diagnostics.ProcessStartInfo]::new($file)
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true; $info.WorkingDirectory = [IO.Path]::GetFullPath($WorkingDirectory)
    $info.RedirectStandardInput = $true; $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    # Browser/build children receive no inherited server credentials. Only the
    # host call explicitly adds the selected per-run SQL/broker environment.
    foreach ($key in @($info.Environment.Keys)) {
        if ($key -match '^(TASKMANAGER_(FULLSTACK|TEST)_|AZURE_|AI__|Email__|Jwt__|ConnectionStrings__)' -or
            ($IsolateRabbitEnvironment -and ($key -match '^(RABBITMQ_|ERL_|ERLANG_)' -or $key -in @('CONFIG_FILE','CONFIG_FILES','ADVANCED_CONFIG_FILE','CONF_ENV_FILE','NODENAME','NODE_PORT','DIST_PORT','USE_LONGNAME','MNESIA_BASE','MNESIA_DIR','LOG_BASE','PID_FILE','SERVER_ERL_ARGS','SERVER_ADDITIONAL_ERL_ARGS','SERVER_START_ARGS','CTL_ERL_ARGS','CTL_DIST_PORT_MIN','CTL_DIST_PORT_MAX','BOOT_MODULE','CLEAN_BOOT_FILE','SASL_BOOT_FILE')))) { $null = $info.Environment.Remove($key) }
    }
    foreach ($key in $Environment.Keys) { $info.Environment[$key] = [string] $Environment[$key] }
    $process = [Diagnostics.Process]::new(); $process.StartInfo = $info
    try { if (-not $process.Start()) { throw 'Child start failed.' } }
    catch { $process.Dispose(); throw 'Full-stack child could not start.' }
    # Drain both pipes asynchronously into Stream.Null: no buffering/persistence,
    # console forwarding or deadlock from raw product/mail/cookie exception logs.
    [pscustomobject]@{ Process = $process; StdoutDrain = $process.StandardOutput.BaseStream.CopyToAsync([IO.Stream]::Null);
        StderrDrain = $process.StandardError.BaseStream.CopyToAsync([IO.Stream]::Null) }
}
function Stop-FullStackChild {
    param([Parameter(Mandatory)] $Handle, [ValidateRange(1,120)] [int] $GraceSeconds = 30)
    $process = $Handle.Process
    $graceful = $true
    if (-not $process.HasExited) {
        $process.StandardInput.Close()
        $shutdownClock = [Diagnostics.Stopwatch]::StartNew()
        while (-not $process.HasExited -and $shutdownClock.Elapsed.TotalSeconds -lt $GraceSeconds) { $null = $process.WaitForExit(200) }
        if (-not $process.HasExited) {
            $graceful = $false
            $process.Kill($true)
            if (-not $process.WaitForExit(10000)) { throw 'Owned child process termination remains unverified.' }
        }
    }
    $null = $Handle.StdoutDrain.Wait(2000); $null = $Handle.StderrDrain.Wait(2000)
    [pscustomobject]@{ graceful = $graceful; exitCode = $process.ExitCode }
}
function Wait-FullStackChild {
    param([Parameter(Mandatory)] $Handle, [ValidateRange(1,3600)] [int] $TimeoutSeconds)
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while (-not $Handle.Process.HasExited) {
        if ($clock.Elapsed.TotalSeconds -ge $TimeoutSeconds) { throw 'Full-stack child exceeded its bounded execution timeout.' }
        Start-Sleep -Milliseconds 200
    }
    return $Handle.Process.ExitCode
}
function Wait-FullStackState {
    param([Parameter(Mandatory)] $Handle, [Parameter(Mandatory)] [string] $Path, [ValidateSet('Controlled','RabbitMQ')] [string] $Transport,
          [ValidateRange(1,300)] [int] $TimeoutSeconds = 120)
    $target = Assert-FullStackPrivatePath $Path
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        if ($Handle.Process.HasExited) { throw 'Test host exited before publishing ready private state.' }
        if (Test-Path -LiteralPath $target -PathType Leaf) {
            if ((Get-Item -LiteralPath $target).Length -gt 65536) { throw 'Private state exceeded its bounded size.' }
            try { $state = Get-Content -LiteralPath $target -Raw | ConvertFrom-Json -AsHashtable -Depth 20 } catch { throw 'Private state is invalid; raw state content is not logged.' }
            Assert-FullStackState $state $Transport
            if ($state.databaseName -cne ('taskmanager_e2e_test_' + [IO.DirectoryInfo]::new([IO.Path]::GetDirectoryName($target)).Name)) { throw 'Ready state does not belong to the exact private run directory.' }
            return $state
        }
        Start-Sleep -Milliseconds 200
    }
    throw 'Test host did not publish ready state within the startup timeout.'
}
function Assert-FullStackCleanup($Cleanup, [string] $DatabaseName) {
    if ((Get-FullStackField $Cleanup 'schemaVersion') -ne 1 -or (Get-FullStackField $Cleanup 'mode') -cne 'controlled-full-stack-cleanup' -or
        (Get-FullStackField $Cleanup 'databaseName') -cne $DatabaseName -or (Get-FullStackField $Cleanup 'databaseDropped') -ne $true -or
        (Get-FullStackField $Cleanup 'serversStopped') -ne $true -or (Get-FullStackField $Cleanup 'brokerQueuesDeleted') -ne $true) { throw 'Actual owned database/server/broker cleanup is not verified.' }
}
function Reset-FullStackScreenshots {
    $repository = Get-FullStackRepository
    $root = [IO.Path]::GetFullPath((Join-Path $repository 'frontend/test-results'))
    $target = [IO.Path]::GetFullPath((Join-Path $root 'fullstack-screenshots'))
    if (-not $target.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($target) -cne 'fullstack-screenshots') { throw 'Invalid owned screenshot directory.' }
    $directory = [IO.DirectoryInfo]::new($target)
    while ($directory.FullName.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
        if ($directory.Exists -and ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Screenshot cleanup cannot traverse a link/junction.' }
        if ($null -eq $directory.Parent) { break }; $directory = $directory.Parent
    }
    if (Test-Path -LiteralPath $target) {
        $resolved = (Resolve-Path -LiteralPath $target).ProviderPath
        if ($resolved -ine $target) { throw 'Resolved screenshot cleanup target differs from the owned path.' }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    $null = [IO.Directory]::CreateDirectory($target)
}
