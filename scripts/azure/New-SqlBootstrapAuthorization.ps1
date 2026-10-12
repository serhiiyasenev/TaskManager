#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)] [string] $FoundationPath, [Parameter(Mandatory)] [string] $OutputPath)
. (Join-Path $PSScriptRoot 'Validation.ps1')
$foundation = Get-Content -LiteralPath $FoundationPath -Raw | ConvertFrom-Json -AsHashtable -Depth 100
Assert-TaskManagerFoundationOutputs $foundation
$identity = $foundation.identityIds.sqlBootstrap
if ($identity.name -cnotmatch '^[a-z][a-z0-9-]{1,100}$' -or $foundation.sqlDatabaseName -cnotmatch '^[a-z][a-z0-9-]{1,100}$') { throw 'Invalid SQL bootstrap identity/database name.' }
$bytes = ([Guid] $identity.clientId).ToByteArray()
$sid = '0x' + [Convert]::ToHexString($bytes)
$sql = @"
-- OWNER-RUN ONLY. Connect using the existing administrator to exactly:
-- Server: $($foundation.sqlServerFqdn)
-- Database: $($foundation.sqlDatabaseName)
-- This does not change a server administrator or grant access to another database.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF DB_NAME() <> N'$($foundation.sqlDatabaseName)' THROW 51000, 'Wrong product database.', 1;
IF DATABASE_PRINCIPAL_ID(N'$($identity.name)') IS NULL
    CREATE USER [$($identity.name)] WITH SID = $sid, TYPE = E;
ELSE IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$($identity.name)' AND sid = $sid AND type = 'E')
    THROW 51001, 'Existing principal does not match the dedicated bootstrap client ID.', 1;
GRANT CONTROL ON DATABASE::[$($foundation.sqlDatabaseName)] TO [$($identity.name)];
COMMIT;
"@
# Establish private output-directory permissions without allowing arbitrary writes.
Save-TaskManagerPrivateJson -Path "$OutputPath.metadata.json" -Value @{ database = $foundation.sqlDatabaseName; clientId = $identity.clientId; ownerRunRequired = $true }
[IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), $sql, [Text.UTF8Encoding]::new($false))
Write-Host 'Exact database-bound authorization SQL saved. It has not been executed.'
