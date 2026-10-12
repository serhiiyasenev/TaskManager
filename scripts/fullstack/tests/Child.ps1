#requires -Version 7.4
param([ValidateSet('Graceful','Unresponsive','RabbitEnvironment')] [string] $Mode = 'Graceful')
if ($Mode -eq 'RabbitEnvironment') {
    $foreignAbsent=$true
    foreach ($name in @('RABBITMQ_SERVER_START_ARGS','ERL_LIBS','ERLANG_FIXTURE_FOREIGN','CONFIG_FILES','SERVER_START_ARGS','CTL_ERL_ARGS')) {
        if ($null -ne [Environment]::GetEnvironmentVariable($name)) { $foreignAbsent=$false }
    }
    $result=@{foreignAbsent=$foreignAbsent;
        ownedPreserved=([Environment]::GetEnvironmentVariable('RABBITMQ_NODENAME') -ceq 'taskmanager_e2e_fixture@localhost');
        unrelatedPreserved=([Environment]::GetEnvironmentVariable('TASKMANAGER_FIXTURE_UNRELATED') -ceq 'synthetic-unrelated')}
    [IO.File]::WriteAllText($env:TASKMANAGER_FIXTURE_MARKER,($result | ConvertTo-Json -Compress))
    exit 0
}
[Console]::Out.WriteLine('SYNTHETIC_PRIVATE_CHILD_OUTPUT_NEVER_FORWARD')
[Console]::Error.WriteLine('SYNTHETIC_PRIVATE_CHILD_ERROR_NEVER_FORWARD')
if ($Mode -eq 'Unresponsive') { Start-Sleep -Seconds 15; exit 0 }
$null = [Console]::In.ReadToEnd()
[IO.File]::WriteAllText($env:TASKMANAGER_FIXTURE_MARKER, 'graceful-eof-cleanup')
exit 0
