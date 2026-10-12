# Native Windows SQL and Rabbit verification

Azure deployment is deferred by the owner. This gate runs on the existing
local SQL Server with a temporary, workspace-only RabbitMQ process. It does
not install a service, edit the registry, change profiles or register certificates.
The product database `TaskManager_Codex_20261012` is retained; test databases
have strictly owned GUID names and are removed by their test owners.

## Prepare verified portable tools

Use PowerShell7 and the repository's pinned .NET/Node/npm dependencies. Prepare
the test-host Release build and frontend production build as described in
[full-stack verification](fullstack-verification.md). Existing Chrome runs in
disposable contexts. The machine needs the Microsoft VC++ runtime; this machine
already has it. The Erlang ZIP needs no installer or registry initialization.

Download only these official versioned assets into `.tools/native-rabbit/downloads`:

| Asset | SHA256 |
| --- | --- |
| [otp_win64_28.5.zip](https://github.com/erlang/otp/releases/download/OTP-28.5/otp_win64_28.5.zip) | `d3227232221e68aa8790666e915604269d5471b61792a5d78a043ac653351140` |
| [rabbitmq-server-windows-4.3.6.zip](https://github.com/rabbitmq/rabbitmq-server/releases/download/v4.3.6/rabbitmq-server-windows-4.3.6.zip) | `48161b084f1257edc112b24fccda378cc3e83c8e4b94984801cd6651817981f1` |

Compare `Get-FileHash -Algorithm SHA256` to the published
[OTP assets](https://github.com/erlang/otp/releases/expanded_assets/OTP-28.5) and
[Rabbit assets](https://github.com/rabbitmq/rabbitmq-server/releases/expanded_assets/v4.3.6)
before extracting. Extract into `.tools/native-rabbit/otp-28.5` and
`.tools/native-rabbit/rabbit-4.3.6`; the latter contains `rabbitmq_server-4.3.6`.
The launcher rechecks the archive digests and required native executable paths.
It performs no implicit download or system installation.

## Run

The ignored `.local/fullstack-inputs/sql-master.txt` is prepared on this machine
with Windows integrated authentication and an explicit loopback `Database=master`
connection. It stores no password. Never substitute an application catalog.

```powershell
pwsh -NoProfile -File scripts/fullstack/Invoke-WindowsRabbitVerification.ps1 `
  -SqlMasterPath .local/fullstack-inputs/sql-master.txt
```

The launcher refuses occupied ports5672/43691/25691. It creates a fresh private
run directory, unique node, data/log/plugin paths and a random Erlang cookie.
AMQP, EPMD and server distribution bind to loopback. Management plugins remain
disabled. The known CI-only Rabbit account is used only by this disposable
loopback broker because the existing actual-broker regression uses that fixture.

Native `-args_file` with one explicit `-setcookie` bypasses profile-cookie I/O.
The child CLI also receives the same value only in its private environment,
preventing its late cookie resolver from replacing the native value. Inherited
Rabbit/Erlang overrides are cleared before owned values are supplied. Neither
the cookie nor SQL credentials are placed in visible tool arguments or reports.

The actual sequence is broker readiness/alarms/loopback check → all backend
SQL/Rabbit tests → desktop/mobile full-stack browser tests → actual host/database/
queue cleanup → broker VM shutdown → owned EPMD shutdown → port absence.
Backend and browser children have bounded timeouts and discard raw streams.
Only aggregate reports and explicitly synthetic screenshots may be shared.

The safe `native-broker-result.json` in the private GUID run directory records
the outcome and port cleanup. A forced termination or incomplete gate does not
produce PASS. Existing application data is never part of broker cleanup.

AI/email are explicit controlled adapters in the test host. Native broker,
SQL, Identity, gateway and private SignalR are actual components. This gate
does not establish real model quality, Azure identity or container execution.
Linux container checks remain a separate gate.
