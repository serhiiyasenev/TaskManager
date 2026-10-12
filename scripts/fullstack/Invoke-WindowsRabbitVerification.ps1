#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string] $SqlMasterPath)
. (Join-Path $PSScriptRoot 'Common.ps1')
if (-not $IsWindows) { throw 'This portable verification launcher requires Windows.' }
$repository = Get-FullStackRepository
$toolRoot = Join-Path $repository '.tools/native-rabbit'
$otp = Join-Path $toolRoot 'otp-28.5'
$epmdExecutable = Join-Path $otp 'erts-16.4/bin/epmd.exe'
$rabbit = Join-Path $toolRoot 'rabbit-4.3.6/rabbitmq_server-4.3.6'
foreach ($archive in @(
    @{name='otp_win64_28.5.zip';sha='d3227232221e68aa8790666e915604269d5471b61792a5d78a043ac653351140'},
    @{name='rabbitmq-server-windows-4.3.6.zip';sha='48161b084f1257edc112b24fccda378cc3e83c8e4b94984801cd6651817981f1'})) {
    $path = Join-Path $toolRoot "downloads/$($archive.name)"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $archive.sha) { throw 'Prepared official archive digest is missing or invalid.' }
}
foreach ($path in @((Join-Path $otp 'bin/erl.exe'),$epmdExecutable,(Join-Path $rabbit 'sbin/rabbitmq-server.bat'))) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'Extract the verified portable tool archives first.' }
}
if ($repository -match '[%&!^<>"\r\n]') { throw 'Repository path cannot contain command-shell metacharacters.' }
$ports = @(5672,43691,25691)
function Get-OwnedListeners { ,@([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() | Where-Object Port -in $ports) }
if ((Get-OwnedListeners).Count) { throw 'A planned verification port is occupied; no existing broker/process is reused.' }
$sqlPath = [IO.Path]::GetFullPath($SqlMasterPath)
$sql = [IO.File]::ReadAllText($sqlPath).Trim()
Assert-FullStackSqlMaster $sql
$run = New-FullStackRunDirectory
$runId = [IO.DirectoryInfo]::new($run).Name
$node = "taskmanager_e2e_$runId@localhost"
foreach ($name in @('data','logs','plugins-expand')) { Set-FullStackPrivateDirectory (Join-Path $run $name) }
$argsPath = Join-Path $run 'erl-private.args'
$cookie = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
# One explicit cookie prevents native OTP from reading/writing the user's
# profile cookie. Windows' default/explicit -home arguments otherwise duplicate.
[IO.File]::WriteAllText($argsPath, "-setcookie $cookie`n-kernel inet_dist_use_interface {127,0,0,1}`n", [Text.UTF8Encoding]::new($false))
$cookie = $null
$configPath = Join-Path $run 'rabbitmq.conf'
[IO.File]::WriteAllText($configPath, @'
listeners.tcp.1 = 127.0.0.1:5672
distribution.listener.interface = 127.0.0.1
distribution.listener.port_range.min = 25691
distribution.listener.port_range.max = 25691
default_user = taskmanager-ci
default_pass = TaskManager_CI_Rabbit_Disposable
default_vhost = /
loopback_users.taskmanager-ci = true
log.console = false
'@, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $run 'enabled_plugins'), '[].', [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $run 'rabbitmq-env-conf.bat'), '', [Text.UTF8Encoding]::new($false))
$privateArgs = '-args_file "' + $argsPath.Replace('\','/') + '"'
$environment = @{
    TASKMANAGER_NATIVE_RABBIT_ARGS=$argsPath;
    ERLANG_HOME=$otp;ERL_EPMD_ADDRESS='127.0.0.1';ERL_EPMD_PORT='43691';ERL_FLAGS='';ERL_AFLAGS='';ERL_ZFLAGS='';
    ERL_CRASH_DUMP=(Join-Path $run 'erl_crash.dump');
    RABBITMQ_BASE=$run;RABBITMQ_CONFIG_FILE=$configPath;RABBITMQ_CONF_ENV_FILE=(Join-Path $run 'rabbitmq-env-conf.bat');
    RABBITMQ_MNESIA_BASE=(Join-Path $run 'data');RABBITMQ_MNESIA_DIR=(Join-Path $run 'data/node');
    RABBITMQ_LOG_BASE=(Join-Path $run 'logs');RABBITMQ_ENABLED_PLUGINS_FILE=(Join-Path $run 'enabled_plugins');
    RABBITMQ_PLUGINS_EXPAND_DIR=(Join-Path $run 'plugins-expand');RABBITMQ_PID_FILE=(Join-Path $run 'rabbit.pid');
    RABBITMQ_NODENAME=$node;RABBITMQ_USE_LONGNAME='false';RABBITMQ_DIST_PORT='25691';
    RABBITMQ_CTL_DIST_PORT_MIN='35691';RABBITMQ_CTL_DIST_PORT_MAX='35695';RABBITMQ_CTL_ERL_ARGS=$privateArgs;
    # Starting named native distribution on loopback avoids Rabbit's transient
    # wildcard availability probe before it configures the actual listener.
    RABBITMQ_SERVER_ADDITIONAL_ERL_ARGS=($privateArgs + " -sname $node -kernel inet_dist_listen_min 25691 -kernel inet_dist_listen_max 25691")
}
$epmd = $null;$server = $null;$passed = $false;$cleanupVerified = $false;$phase='startup';$failureType=$null
$pwshExecutable = (Get-Process -Id $PID).Path
$batchWrapper = Join-Path $PSScriptRoot 'Invoke-WindowsRabbitBatch.ps1'
function Invoke-OwnedRabbit([ValidateSet('ping','check_running','check_local_alarms','shutdown')][string] $Command) {
    $kind = if ($Command -eq 'shutdown') {'Control'} else {'Diagnostics'}
    $handle = Start-FullStackChild -Executable $pwshExecutable -Arguments @('-NoProfile','-File',$batchWrapper,'-Script',$kind,'-Node',$node,'-Command',$Command) -WorkingDirectory $repository -Environment $environment -IsolateRabbitEnvironment
    try { Wait-FullStackChild $handle -TimeoutSeconds 40 }
    finally { $null=Stop-FullStackChild $handle -GraceSeconds 1;$handle.Process.Dispose() }
}
try {
    $epmd = Start-FullStackChild -Executable $epmdExecutable -Arguments @('-address','127.0.0.1','-port','43691') -WorkingDirectory $run -Environment $environment -IsolateRabbitEnvironment
    $server = Start-FullStackChild -Executable $pwshExecutable -Arguments @('-NoProfile','-File',$batchWrapper,'-Script','Server') -WorkingDirectory $run -Environment $environment -IsolateRabbitEnvironment
    $ready = $false
    for ($attempt=0;$attempt -lt 12 -and -not $ready;$attempt++) {
        if ($server.Process.HasExited -or $epmd.Process.HasExited) { throw 'Owned broker startup failed; private diagnostics are not forwarded.' }
        try { $ready = (Invoke-OwnedRabbit 'check_running') -eq 0 } catch { }
        if (-not $ready) { Start-Sleep -Seconds 2 }
    }
    if (-not $ready -or (Invoke-OwnedRabbit 'check_local_alarms') -ne 0) { throw 'Actual local broker readiness/alarm check failed.' }
    $listeners = Get-OwnedListeners
    if (@($listeners | Where-Object { -not [Net.IPAddress]::IsLoopback($_.Address) }).Count -or
        @($ports | Where-Object { $_ -notin $listeners.Port }).Count) { throw 'Owned broker listeners are missing or extend beyond loopback.' }
    Write-Host 'PASS actual portable RabbitMQ4.3.6/OTP28.5 readiness; all three listeners are loopback.'
    $phase='backend';$env:TASKMANAGER_TEST_SQL_SERVER=$sql;$env:TASKMANAGER_TEST_RABBITMQ='true'
    $backend = Start-FullStackChild -Executable (Get-Command dotnet -ErrorAction Stop).Source -Arguments @('test','TaskManager.sln','--settings','.runsettings','--results-directory','TestResults/fullstack-native-rabbit-backend','--logger','trx;LogFileName=test_results.trx','--configuration','Release','--no-build') -WorkingDirectory $repository -Environment @{TASKMANAGER_TEST_SQL_SERVER=$sql;TASKMANAGER_TEST_RABBITMQ='true'}
    try { if ((Wait-FullStackChild $backend -TimeoutSeconds 600) -ne 0) { throw 'Actual native SQL/Rabbit backend regression failed; private raw output is discarded.' } }
    finally { $null=Stop-FullStackChild $backend -GraceSeconds 1;$backend.Process.Dispose() }
    Write-Host 'PASS actual native SQL/Rabbit backend regression; inspect TRX for exact counts.'
    $phase='browser'
    $browserRunner = Start-FullStackChild -Executable $pwshExecutable -Arguments @('-NoProfile','-File',(Join-Path $PSScriptRoot 'Run.ps1'),'-Mode','RabbitMQ','-NoBuild') -WorkingDirectory $repository -Environment @{
        TASKMANAGER_FULLSTACK_SQL=$sql;TASKMANAGER_UI_BROWSER='chrome';TASKMANAGER_FULLSTACK_RABBIT_HOST='127.0.0.1';
        TASKMANAGER_FULLSTACK_RABBIT_PORT='5672';TASKMANAGER_FULLSTACK_RABBIT_USER='taskmanager-ci';TASKMANAGER_FULLSTACK_RABBIT_PASSWORD='TaskManager_CI_Rabbit_Disposable'}
    try { if ((Wait-FullStackChild $browserRunner -TimeoutSeconds 1200) -ne 0) { throw 'Actual native SQL/Rabbit/browser verification failed.' } }
    finally { $null=Stop-FullStackChild $browserRunner -GraceSeconds 1;$browserRunner.Process.Dispose() }
    Write-Host 'PASS actual native SQL/Rabbit/browser verification with exact owned host/database/queue cleanup.'
    $passed=$true
} catch { $failureType=$_.Exception.GetType().Name;Write-Host ('FAIL native broker verification: ' + $failureType) }
finally {
    if ($server -and -not $server.Process.HasExited) {
        try { if ((Invoke-OwnedRabbit 'shutdown') -ne 0) { throw 'Broker shutdown failed.' }; $null=Wait-FullStackChild $server -TimeoutSeconds 40 }
        catch { $passed=$false }
    }
    if ($server) { try { $null=Stop-FullStackChild $server -GraceSeconds 1 } catch { $passed=$false };$server.Process.Dispose() }
    if ($epmd) {
        $kill = Start-FullStackChild -Executable $epmdExecutable -Arguments @('-port','43691','-kill') -WorkingDirectory $run -Environment $environment -IsolateRabbitEnvironment
        try { if ((Wait-FullStackChild $kill -TimeoutSeconds 10) -ne 0) { $passed=$false } }
        finally { $null=Stop-FullStackChild $kill -GraceSeconds 1;$kill.Process.Dispose() }
        try { $null=Wait-FullStackChild $epmd -TimeoutSeconds 10 } catch { $passed=$false }
        try { $null=Stop-FullStackChild $epmd -GraceSeconds 1 } catch { $passed=$false };$epmd.Process.Dispose()
    }
    $cleanupVerified=(Get-OwnedListeners).Count -eq 0
    if (Test-Path -LiteralPath $argsPath) { Remove-Item -LiteralPath $argsPath }
    $environment.Clear()
    Save-FullStackPrivateJson (Join-Path $run 'native-broker-result.json') @{schemaVersion=1;kind='native-windows-rabbit-verification';runId=$runId;
        rabbitVersion='4.3.6';otpVersion='28.5';passed=($passed -and $cleanupVerified);listenersRemoved=$cleanupVerified;phase=$phase;failureType=$failureType;
        serviceInstalled=$false;registryChanged=$false;profileCookieUsed=$false;cloudCalls=$false;completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')}
    foreach ($name in @('TASKMANAGER_TEST_SQL_SERVER','TASKMANAGER_TEST_RABBITMQ','TASKMANAGER_FULLSTACK_SQL','TASKMANAGER_UI_BROWSER',
        'TASKMANAGER_FULLSTACK_RABBIT_HOST','TASKMANAGER_FULLSTACK_RABBIT_PORT','TASKMANAGER_FULLSTACK_RABBIT_USER','TASKMANAGER_FULLSTACK_RABBIT_PASSWORD')) {
        [Environment]::SetEnvironmentVariable($name,$null)
    }
}
if (-not $passed -or -not $cleanupVerified) { exit 1 }
Write-Host 'PASS actual native Windows verification and owned broker/listener cleanup; no Azure deployment.'
