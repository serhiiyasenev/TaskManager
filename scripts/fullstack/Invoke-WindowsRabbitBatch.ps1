#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('Server','Diagnostics','Control')][string] $Script,
      [ValidatePattern('^taskmanager_e2e_[a-f0-9]{32}@localhost$')][string] $Node,
      [ValidateSet('ping','check_running','check_local_alarms','shutdown')][string] $Command)
. (Join-Path $PSScriptRoot 'Common.ps1')
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sbin = Join-Path $repository '.tools/native-rabbit/rabbit-4.3.6/rabbitmq_server-4.3.6/sbin'
# Elixir treats an empty cookie environment value as truthy and replaces the
# native private -setcookie value. Remove inherited overrides in this child.
[Environment]::SetEnvironmentVariable('RABBITMQ_ERLANG_COOKIE',$null,[EnvironmentVariableTarget]::Process)
[Environment]::SetEnvironmentVariable('ERLANG_COOKIE',$null,[EnvironmentVariableTarget]::Process)
# The native flag prevents profile-cookie I/O; the CLI's late cookie resolver
# also receives the same explicit value in its private process environment.
$argsPath = $env:TASKMANAGER_NATIVE_RABBIT_ARGS
if (-not $argsPath -or -not [IO.Path]::IsPathFullyQualified($argsPath) -or
    [IO.Path]::GetFileName($argsPath) -ne 'erl-private.args' -or (Get-Item -LiteralPath $argsPath).Length -gt 512) { throw 'Owned private native arguments are missing.' }
$argsPath = Assert-FullStackPrivatePath $argsPath
$parent = [IO.DirectoryInfo]::new([IO.Path]::GetDirectoryName($argsPath))
if ($parent.Name -cnotmatch '^[a-f0-9]{32}$' -or $env:RABBITMQ_NODENAME -cne "taskmanager_e2e_$($parent.Name)@localhost" -or
    ((Get-Item -LiteralPath $argsPath).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Native arguments do not belong to this owned node.' }
$content = [IO.File]::ReadAllText($argsPath)
if ($content -notmatch '\A-setcookie ([A-F0-9]{64})\r?\n-kernel inet_dist_use_interface \{127,0,0,1\}\r?\n\z') { throw 'Owned native arguments have an unexpected shape.' }
[Environment]::SetEnvironmentVariable('RABBITMQ_ERLANG_COOKIE',$Matches[1],[EnvironmentVariableTarget]::Process)
$content=$null;$Matches=$null
if ($Script -eq 'Server') {
    if ($Node -or $Command) { throw 'Server startup receives no caller commands.' }
    & (Join-Path $sbin 'rabbitmq-server.bat')
} else {
    if (-not $Node -or -not $Command -or (($Script -eq 'Control') -ne ($Command -eq 'shutdown'))) { throw 'Invalid owned broker operation.' }
    $batch = if ($Script -eq 'Control') {'rabbitmqctl.bat'} else {'rabbitmq-diagnostics.bat'}
    & (Join-Path $sbin $batch) -n $Node -q $Command
}
exit $LASTEXITCODE
