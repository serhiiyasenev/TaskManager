#requires -Version 7.4
[CmdletBinding()]
param([string] $TemplatePath = '.local/azure/validation/foundation.json')
. (Join-Path $PSScriptRoot '../Validation.ps1')
$text = Get-Content -LiteralPath $TemplatePath -Raw
$template = $text | ConvertFrom-Json -AsHashtable -Depth 100
Assert-TaskManagerNotificationRoles $template
function ExpectRoleRejection($Value) {
    $rejected = $false
    try { Assert-TaskManagerNotificationRoles $Value } catch { $rejected = $true }
    if (-not $rejected) { throw 'Notification permission regression unexpectedly passed.' }
}
# Explicit corrupted compiled fixtures; no infrastructure source or cloud change.
ExpectRoleRejection (($text.Replace('4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0', '4f6e0244-6a4f-4e5b-865a-c8eeb2b9af45')) | ConvertFrom-Json -AsHashtable -Depth 100)
ExpectRoleRejection (($text.Replace('4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0', '090c5cfd-751d-490a-894a-3ce6f1109419')) | ConvertFrom-Json -AsHashtable -Depth 100)
ExpectRoleRejection (($text.Replace('420fcaa2-552c-430f-98ca-3264be4806c7', 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa')) | ConvertFrom-Json -AsHashtable -Depth 100)
$wrongScope = $text | ConvertFrom-Json -AsHashtable -Depth 100
foreach ($module in $wrongScope.resources) {
    if ($module.type -cne 'Microsoft.Resources/deployments' -or -not $module.properties.Contains('template')) { continue }
    foreach ($resource in $module.properties.template.resources) {
        if ($resource.type -ceq 'Microsoft.Authorization/roleAssignments' -and (Get-TaskManagerField $resource.properties 'roleDefinitionId' '').Contains('4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0')) { $resource.scope = "[resourceId('Microsoft.ServiceBus/namespaces', parameters('name'))]" }
    }
}
ExpectRoleRejection $wrongScope
Write-Host 'Compiled notification roles: actual positive contract + 4 corrupted permission fixtures passed. No cloud calls.'
