#requires -Version 7.4
. (Join-Path $PSScriptRoot 'Validation.ps1')

function Test-TaskManagerSharedInfrastructure {
    param([Parameter(Mandatory)] [scriptblock] $Invoker, [Parameter(Mandatory)] $Configuration)
    $parameters = $Configuration.foundation
    $registryId = Get-TaskManagerField $parameters 'existingRegistryResourceId' ''
    if ($registryId) {
        $registryParts = $registryId.Split('/')
        $registry = & $Invoker @('acr', 'show', '--name', $registryParts[-1], '--resource-group', $registryParts[4], '--query', '{id:id,roleAssignmentMode:roleAssignmentMode,publicNetworkAccess:publicNetworkAccess}')
        $mode = switch ($registry.roleAssignmentMode) { 'LegacyRegistryPermissions' { 'RbacRegistryPermissions' }; 'AbacRepositoryPermissions' { 'AbacRepositoryPermissions' }; default { throw 'Existing registry permission mode is unavailable or unsupported.' } }
        if ($registry.id -ine $registryId -or $mode -cne (Get-TaskManagerField $parameters 'existingRegistryRoleAssignmentMode' 'RbacRegistryPermissions') -or $registry.publicNetworkAccess -cne 'Enabled') { throw 'Existing registry mode/network differs from the reviewed compatible public-build route.' }
        $arm = & $Invoker @('rest', '--method', 'get', '--url', "https://management.azure.com${registryId}?api-version=2025-04-01", '--query', 'properties.policies.azureADAuthenticationAsArmPolicy.status')
        if ($arm -ine 'enabled') { throw 'Actual ACR ARM-audience authentication is disabled; existing registry settings are never changed.' }
    }
    $environmentId = Get-TaskManagerField $parameters 'existingManagedEnvironmentResourceId' ''
    if ($environmentId) {
        $environment = & $Invoker @('rest', '--method', 'get', '--url', "https://management.azure.com${environmentId}?api-version=2025-07-01", '--query', '{id:id,properties:properties}')
        if ($environment.id -ine $environmentId -or $environment.properties.vnetConfiguration.internal -ne $false -or @($environment.properties.workloadProfiles | Where-Object { $_.name -ceq 'Consumption' -and $_.workloadProfileType -ceq 'Consumption' }).Count -ne 1) { throw 'Existing environment must be external and support the exact Consumption workload profile.' }
        $subnetId = $environment.properties.vnetConfiguration.infrastructureSubnetId
        if (-not $subnetId.StartsWith($parameters.existingVnetResourceId + '/subnets/', [StringComparison]::OrdinalIgnoreCase) -or -not $parameters.existingPrivateEndpointSubnetResourceId.StartsWith($parameters.existingVnetResourceId + '/subnets/', [StringComparison]::OrdinalIgnoreCase) -or $parameters.existingPrivateEndpointSubnetResourceId -ieq $subnetId) { throw 'Actual environment/private endpoint subnets must belong to the reviewed VNet and be distinct.' }
        $subnet = & $Invoker @('rest', '--method', 'get', '--url', "https://management.azure.com${subnetId}?api-version=2024-05-01", '--query', 'properties')
        if ((Get-TaskManagerField $subnet 'addressPrefix' '') -cne $parameters.ingressSubnetCidr -or @($subnet.delegations | Where-Object { $_.properties.serviceName -ceq 'Microsoft.App/environments' }).Count -ne 1) { throw 'Trusted proxy CIDR/delegation differs from the actual environment infrastructure subnet.' }
        $private = & $Invoker @('rest', '--method', 'get', '--url', "https://management.azure.com$($parameters.existingPrivateEndpointSubnetResourceId)?api-version=2024-05-01", '--query', 'properties')
        if ($private.privateEndpointNetworkPolicies -cne 'Disabled' -or -not (Get-TaskManagerField $private 'addressPrefix' '')) { throw 'Actual private endpoint subnet configuration is incompatible.' }
        foreach ($key in @('existingSqlPrivateDnsZoneResourceId', 'existingSignalRPrivateDnsZoneResourceId')) {
            $zone = $parameters[$key]; $parts = $zone.Split('/')
            $expectedZone = if ($key -eq 'existingSqlPrivateDnsZoneResourceId') { 'privatelink.database.windows.net' } else { 'privatelink.service.signalr.net' }
            if ($parts[-1] -cne $expectedZone) { throw 'Private DNS zone name does not match the target service.' }
            $links = & $Invoker @('network', 'private-dns', 'link', 'vnet', 'list', '--resource-group', $parts[4], '--zone-name', $parts[-1], '--query', '[].{virtualNetwork:virtualNetwork,provisioningState:provisioningState,virtualNetworkLinkState:virtualNetworkLinkState}')
            if (@($links | Where-Object { $_.virtualNetwork.id -ieq $parameters.existingVnetResourceId -and $_.provisioningState -ceq 'Succeeded' -and $_.virtualNetworkLinkState -ceq 'Completed' }).Count -ne 1) { throw 'Actual existing private DNS zone is not linked to the environment VNet.' }
        }
    }
}
function Set-TaskManagerPreservedFoundationBoundaries {
    param([Parameter(Mandatory)] [scriptblock] $Invoker, [Parameter(Mandatory)] $Configuration)
    $prefix = "$($Configuration.foundation.resourceNamePrefix)-$($Configuration.foundation.environmentName)-signalr-"
    $resources = @(& $Invoker @('resource', 'list', '--resource-group', $Configuration.resourceGroupName, '--resource-type', 'Microsoft.SignalRService/signalR', '--query', '[].{id:id,name:name,tags:tags}'))
    $owned = @($resources | Where-Object { $_.name.StartsWith($prefix, [StringComparison]::Ordinal) })
    if (-not $owned.Count) { return }
    if ($owned.Count -ne 1 -or $owned[0].tags.product -cne 'TaskManager') { throw 'Existing SignalR name ownership is ambiguous.' }
    $current = & $Invoker @('rest', '--method', 'get', '--url', "https://management.azure.com$($owned[0].id)?api-version=2024-03-01", '--query', '{connections:properties.privateEndpointConnections,acls:properties.networkACLs,origins:properties.cors.allowedOrigins}')
    if ($current.acls.defaultAction -cne 'Deny' -or @($current.acls.publicNetwork.allow).Count -ne 1 -or $current.acls.publicNetwork.allow[0] -cne 'ClientConnection' -or @(Get-TaskManagerField $current.acls 'ipRules' @()).Count) { throw 'Existing owned SignalR boundary drift must be reviewed before foundation.' }
    $expectedEndpoint = "/subscriptions/$($Configuration.subscriptionId)/resourceGroups/$($Configuration.resourceGroupName)/providers/Microsoft.Network/privateEndpoints/$($Configuration.foundation.resourceNamePrefix)-$($Configuration.foundation.environmentName)-signalr-private"
    $names = @()
    foreach ($acl in $current.acls.privateEndpoints) {
        if (@($acl.allow).Count -ne 1 -or $acl.allow[0] -cne 'ServerConnection' -or @($current.connections | Where-Object { $_.name -ceq $acl.name -and $_.properties.privateEndpoint.id -ieq $expectedEndpoint -and $_.properties.privateLinkServiceConnectionState.status -ceq 'Approved' }).Count -ne 1) { throw 'Existing SignalR ACL refers to an unverified private endpoint.' }
        $names += $acl.name
    }
    $origins = @($current.origins)
    if ($origins.Count -lt 1 -or $origins.Count -gt 3) { throw 'Existing SignalR origins are missing or exceed the bounded policy.' }
    foreach ($origin in $origins) { Assert-TaskManagerOrigin $origin | Out-Null }
    $configuredOrigin = Get-TaskManagerField $Configuration.foundation 'publicOrigin' ''
    if ($configuredOrigin -and $origins[0] -cne $configuredOrigin) { throw 'Changing an established public origin requires a separate reviewed transition.' }
    $Configuration.foundation.signalRPrivateEndpointConnectionNames = $names
    $Configuration.foundation.signalRAdditionalAllowedOrigins = @($origins | Select-Object -Skip 1)
}

function Invoke-TaskManagerTemplate {
    param([Parameter(Mandatory)] [scriptblock] $Invoker, [Parameter(Mandatory)] $Configuration,
          [Parameter(Mandatory)] [string] $StateDirectory, [Parameter(Mandatory)] [ValidateSet('foundation', 'jobs', 'apps')] [string] $Entry,
          [Parameter(Mandatory)] $Parameters, [switch] $WhatIfOnly)
    $wrapped = @{}; foreach ($key in $Parameters.Keys) { $wrapped[$key] = @{ value = $Parameters[$key] } }
    $path = Join-Path $StateDirectory "$Entry.parameters.json"
    Save-TaskManagerPrivateJson -Path $path -Value @{ '$schema' = 'https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#'; contentVersion = '1.0.0.0'; parameters = $wrapped }
    $repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    $template = Join-Path $repository ".local/azure/validation/$Entry.json"
    if (-not (Test-Path -LiteralPath $template)) { throw 'Run native offline template compilation before cloud validation.' }
    $name = "$($Configuration.foundation.resourceNamePrefix)-$($Configuration.foundation.environmentName)-$Entry"
    $base = @('deployment', 'group'); $arguments = @('--name', $name, '--resource-group', $Configuration.resourceGroupName, '--template-file', $template, '--parameters', "@$path")
    $null = & $Invoker ($base + @('validate') + $arguments + @('--query', '{properties:{provisioningState:properties.provisioningState}}'))
    $preview = & $Invoker ($base + @('what-if') + $arguments + @('--no-pretty-print', '--result-format', 'FullResourcePayloads'))
    $shared = @($Configuration.foundation.Keys | Where-Object { $_ -like 'existing*ResourceId' -and $Configuration.foundation[$_] } | ForEach-Object { $Configuration.foundation[$_] })
    $groupId = "/subscriptions/$($Configuration.subscriptionId)/resourceGroups/$($Configuration.resourceGroupName)"
    $approved = @(Get-TaskManagerField $Configuration 'approvedOwnedLeafResourceIds' @())
    Assert-TaskManagerOwnedLeafApprovals $Configuration $preview $approved
    Save-TaskManagerPrivateJson -Path (Join-Path $StateDirectory "$Entry.what-if-inventory.json") -Value @($preview.changes | ForEach-Object { @{ resourceId = $_.resourceId; changeType = $_.changeType } })
    $summary = @(Get-TaskManagerWhatIfSummary -Result $preview -ResourceGroupId $groupId -AllowedExistingResourceIds $shared -ApprovedOwnedLeafIds $approved)
    Save-TaskManagerPrivateJson -Path (Join-Path $StateDirectory "$Entry.what-if-summary.json") -Value @{ evaluatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); changes = $summary }
    if ($WhatIfOnly) { return }
    $result = & $Invoker ($base + @('create') + $arguments + @('--query', '{properties:{provisioningState:properties.provisioningState,outputs:properties.outputs}}'))
    if ($result.properties.provisioningState -cne 'Succeeded') { throw 'ARM deployment did not succeed.' }
    Get-TaskManagerFoundationOutputs $result
}
function Assert-TaskManagerOwnedLeafApprovals {
    param([Parameter(Mandatory)] $Configuration, [Parameter(Mandatory)] $Preview, [string[]] $ApprovedIds = @())
    $shared = @($Configuration.foundation.Keys | Where-Object { $_ -like 'existing*ResourceId' -and $Configuration.foundation[$_] } | ForEach-Object { $Configuration.foundation[$_] })
    $prefix = "$($Configuration.foundation.resourceNamePrefix)-$($Configuration.foundation.environmentName)"
    $sharedGroups = @($shared | ForEach-Object { $_.Split('/providers/')[0] } | Select-Object -Unique)
    # email-role is a module in the ACS RG even when the ACS resource is newly created.
    $acs = Get-TaskManagerField $Configuration.foundation 'existingCommunicationServiceResourceId' ''
    foreach ($id in $ApprovedIds) {
        Assert-TaskManagerResourceId $id
        $parent = @($shared | Where-Object { $id.StartsWith($_ + '/', [StringComparison]::OrdinalIgnoreCase) })
        $group = $id.Split('/providers/')[0]
        $wrapper = $group -in $sharedGroups -and $id -match ('/providers/Microsoft.Resources/deployments/' + [Regex]::Escape($prefix) + '-(email-role|registry-existing|openai-existing|sql-existing)$')
        $acsRole = $acs -and $group -ieq $acs.Split('/providers/')[0] -and $id -match '/providers/Microsoft.Authorization/roleDefinitions/[a-fA-F0-9-]{36}$'
        $leaf = $parent.Count -eq 1 -and ($id -match '/providers/Microsoft.Authorization/roleAssignments/[a-fA-F0-9-]{36}$' -or
            $id -match ('/databases/' + [Regex]::Escape("$prefix-db") + '(/backupShortTermRetentionPolicies/default)?$'))
        if (-not $wrapper -and -not $acsRole -and -not $leaf) { throw 'External approvals must be exact product module/database/scoped-role children; shared parent changes are prohibited.' }
        $change = @($Preview.changes | Where-Object { $_.resourceId -ieq $id })
        if ($acsRole -and $change.Count -eq 1 -and $change[0].changeType -in @('Create', 'Modify')) {
            $after = Get-TaskManagerField $change[0] 'after' @{}; $properties = Get-TaskManagerField $after 'properties' @{}
            $permissions = @(Get-TaskManagerField $properties 'permissions' @())
            $scopes = @(Get-TaskManagerField $properties 'assignableScopes' @())
            if ((Get-TaskManagerField $properties 'type' '') -cne 'CustomRole' -or (Get-TaskManagerField $properties 'roleName' '') -cnotmatch '^TaskManager Native Email Sender [a-z0-9]{13}$' -or
                $scopes.Count -ne 1 -or $scopes[0] -ine $group -or $permissions.Count -ne 1) { throw 'ACS custom role what-if metadata is incomplete or has unapproved scope/name.' }
            $permission = $permissions[0]
            $actions = @(Get-TaskManagerField $permission 'actions' @())
            $allowed = @('Microsoft.Communication/CommunicationServices/Read', 'Microsoft.Communication/CommunicationServices/Write')
            if ($actions.Count -ne 2 -or @($actions | Where-Object { $_ -notin $allowed }).Count -or @(Get-TaskManagerField $permission 'dataActions' @()).Count -or
                @(Get-TaskManagerField $permission 'notActions' @()).Count -or @(Get-TaskManagerField $permission 'notDataActions' @()).Count) { throw 'ACS custom role expands the approved native email permissions.' }
        }
    }
}
function Get-TaskManagerAppSnapshots {
    param([Parameter(Mandatory)] [scriptblock] $Invoker, [Parameter(Mandatory)] $Configuration, [Parameter(Mandatory)] $Foundation)
    $existing = @(& $Invoker @('containerapp', 'list', '--resource-group', $Configuration.resourceGroupName, '--query', '[].{name:name,id:id}'))
    $snapshots = @{}
    foreach ($unit in @('web', 'api', 'notifier')) {
        $name = $Foundation.appNames[$unit]
        if ($name -notin @($existing | ForEach-Object { $_.name })) { $snapshots[$unit] = @{ exists = $false; traffic = @(); ipSecurityRestrictions = @(); customDomains = @() }; continue }
        $app = & $Invoker @('containerapp', 'show', '--name', $name, '--resource-group', $Configuration.resourceGroupName,
            '--query', '{id:id,tags:tags,environment:properties.managedEnvironmentId,ingress:properties.configuration.ingress}')
        if ($app.tags.product -cne 'TaskManager' -or $app.environment -cne $Foundation.managedEnvironmentId) { throw 'An existing application name belongs to another deployment.' }
        $traffic = @($app.ingress.traffic)
        if (-not $traffic.Count -or ($traffic | Measure-Object -Property weight -Sum).Sum -ne 100) { throw 'Existing traffic weights must be explicit and total 100.' }
        foreach ($item in $traffic) {
            if ((Get-TaskManagerField $item 'latestRevision' $false) -or -not (Get-TaskManagerField $item 'revisionName')) { throw 'Resolve latest-revision traffic to explicit revision names before deployment.' }
        }
        $snapshots[$unit] = @{ exists = $true; traffic = $traffic; ipSecurityRestrictions = @(Get-TaskManagerField $app.ingress 'ipSecurityRestrictions' @()); customDomains = @(Get-TaskManagerField $app.ingress 'customDomains' @()) }
    }
    $snapshots
}
function Assert-TaskManagerJobTemplate {
    param([Parameter(Mandatory)] [scriptblock] $Invoker, [Parameter(Mandatory)] $Configuration, [Parameter(Mandatory)] [string] $JobName, [Parameter(Mandatory)] [string] $ImageReference)
    $job = & $Invoker @('containerapp', 'job', 'show', '--name', $JobName, '--resource-group', $Configuration.resourceGroupName,
        '--query', '{configuration:properties.configuration,containers:properties.template.containers}')
    if ($job.configuration.triggerType -cne 'Manual' -or $job.configuration.replicaRetryLimit -ne 0 -or $job.configuration.manualTriggerConfig.parallelism -ne 1 -or
        $job.configuration.manualTriggerConfig.replicaCompletionCount -ne 1 -or @($job.containers).Count -ne 1 -or $job.containers[0].image -cne $ImageReference) { throw 'Job execution configuration or immutable image differs from the reviewed release.' }
}
function Invoke-TaskManagerDatabaseJobs {
    param([Parameter(Mandatory)] [scriptblock] $Invoker, [Parameter(Mandatory)] $Configuration, [Parameter(Mandatory)] $Manifest,
          [Parameter(Mandatory)] $State, [Parameter(Mandatory)] [string] $StatePath, [Parameter(Mandatory)] [scriptblock] $FinalizeJobs,
          [ValidateRange(0, 30)] [int] $PollSeconds = 10)
    foreach ($phase in @('sql-bootstrap-before', 'migration-once', 'sql-bootstrap-after')) {
        $State.phase = $phase; Save-TaskManagerPrivateJson $StatePath $State
        if ($phase -eq 'sql-bootstrap-after') { & $FinalizeJobs | Out-Null }
        $unit = if ($phase -eq 'migration-once') { 'migrations' } else { 'sqlBootstrap' }
        $job = if ($unit -eq 'migrations') { $State.jobNames.migrationJobName } else { $State.jobNames.sqlBootstrapJobName }
        Assert-TaskManagerJobTemplate $Invoker $Configuration $job $Manifest.images[$unit].reference
        if ($State.jobs.Contains($phase)) { throw 'A database execution was already requested; automatic replay is prohibited.' }
        $State.jobs[$phase] = @{ status = 'StartRequested'; jobName = $job }; Save-TaskManagerPrivateJson $StatePath $State
        $execution = Start-TaskManagerSingleJob $Invoker $Configuration.resourceGroupName $job
        $State.jobs[$phase].executionName = $execution; Save-TaskManagerPrivateJson $StatePath $State
        $result = Wait-TaskManagerJobExecution -Invoker $Invoker -ResourceGroupName $Configuration.resourceGroupName -JobName $job -ExecutionName $execution -PollSeconds $PollSeconds
        $State.jobs[$phase].status = $result.status; $State.completed += $phase; Save-TaskManagerPrivateJson $StatePath $State
    }
}
function Set-TaskManagerSignalRBoundary {
    param([Parameter(Mandatory)] [scriptblock] $Invoker, [Parameter(Mandatory)] $Foundation, [Parameter(Mandatory)] [string] $ExpectedCandidateOrigin, [Parameter(Mandatory)] [string] $StateDirectory)
    Assert-TaskManagerOrigin $ExpectedCandidateOrigin | Out-Null
    $id = $Foundation.resourceIds.signalR; Assert-TaskManagerResourceId $id 'Microsoft.SignalRService/signalR'
    $uri = "https://management.azure.com${id}?api-version=2024-03-01"
    $actual = & $Invoker @('rest', '--method', 'get', '--url', $uri, '--query', '{location:location,tags:tags,connections:properties.privateEndpointConnections}')
    if ($actual.tags.product -cne 'TaskManager') { throw 'SignalR ACL updates are limited to the owned product resource.' }
    $connections = @($actual.connections | Where-Object { $_.properties.privateEndpoint.id -ieq $Foundation.resourceIds.signalRPrivateEndpoint -and
        $_.properties.privateLinkServiceConnectionState.status -ceq 'Approved' -and $_.properties.provisioningState -ceq 'Succeeded' })
    if ($connections.Count -ne 1 -or $connections[0].name -notmatch '^[a-zA-Z0-9.-]+$') { throw 'Exactly one actual approved owned SignalR private endpoint connection is required.' }
    $names = @($connections[0].name)
    $body = @{ location = $actual.location; properties = @{ networkACLs = @{ defaultAction = 'Deny'; publicNetwork = @{ allow = @('ClientConnection') };
        privateEndpoints = @(@{ name = $names[0]; allow = @('ServerConnection') }); ipRules = @() }; cors = @{ allowedOrigins = @($Foundation.publicOrigin, $ExpectedCandidateOrigin) } } }
    $path = Join-Path $StateDirectory 'signalr-boundary.json'; Save-TaskManagerPrivateJson $path $body
    $null = & $Invoker @('rest', '--method', 'patch', '--url', $uri, '--body', "@$path", '--query', '{state:properties.provisioningState}')
    for ($poll = 0; $poll -lt 30; $poll++) {
        $current = & $Invoker @('rest', '--method', 'get', '--url', $uri, '--query', '{state:properties.provisioningState,networkACLs:properties.networkACLs,cors:properties.cors}')
        if ($current.state -ceq 'Succeeded') {
            if (($current.networkACLs | ConvertTo-Json -Depth 20 -Compress) -cne ($body.properties.networkACLs | ConvertTo-Json -Depth 20 -Compress)) {
                # Compare selected sets rather than ARM object property ordering.
                if ($current.networkACLs.defaultAction -cne 'Deny' -or @($current.networkACLs.publicNetwork.allow).Count -ne 1 -or $current.networkACLs.publicNetwork.allow[0] -cne 'ClientConnection' -or
                    @($current.networkACLs.privateEndpoints).Count -ne 1 -or $current.networkACLs.privateEndpoints[0].name -cne $names[0] -or
                    @($current.networkACLs.privateEndpoints[0].allow).Count -ne 1 -or $current.networkACLs.privateEndpoints[0].allow[0] -cne 'ServerConnection') { throw 'SignalR ACL verification failed.' }
            }
            if (@(Get-TaskManagerField $current.networkACLs 'ipRules' @()).Count) { throw 'Unreviewed SignalR IP rules remain enabled.' }
            if (@($current.cors.allowedOrigins).Count -ne 2 -or @($current.cors.allowedOrigins | Where-Object { $_ -notin $body.properties.cors.allowedOrigins }).Count) { throw 'SignalR CORS verification failed.' }
            return $names
        }
        if ($current.state -notin @('Updating', 'Creating', 'Accepted')) { throw 'SignalR boundary update failed.' }
        Start-Sleep -Seconds 5
    }
    throw 'SignalR boundary update remains uncertain; application deployment is blocked.'
}

function Get-OrCreateTaskManagerJwtSecret {
    param([Parameter(Mandatory)] [scriptblock] $Invoker, [Parameter(Mandatory)] $Foundation, [Parameter(Mandatory)] [string] $StateDirectory)
    $vaultUri = Assert-TaskManagerOrigin $Foundation.keyVaultUri
    if ($vaultUri -cnotmatch '^https://[a-z0-9-]+\.vault\.azure\.net$') { throw 'Unexpected owned vault endpoint.' }
    $vault = ([Uri] $vaultUri).Host.Split('.')[0]
    $existing = @(& $Invoker @('keyvault', 'secret', 'list', '--vault-name', $vault,
        '--query', "[?ends_with(id, '/$($Foundation.jwtSecretName)')].{id:id,attributes:attributes}"))
    if ($existing.Count) {
        if ($existing.Count -ne 1 -or $existing[0].id -cne "$vaultUri/secrets/$($Foundation.jwtSecretName)") { throw 'Existing JWT metadata does not match the dedicated owned secret.' }
        $versions = @(& $Invoker @('keyvault', 'secret', 'list-versions', '--vault-name', $vault, '--name', $Foundation.jwtSecretName,
            '--query', '[].{id:id,attributes:attributes}'))
        $enabled = @($versions | Where-Object { $_.attributes.enabled -eq $true -and (-not (Get-TaskManagerField $_.attributes 'expires') -or $_.attributes.expires -gt [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()) } |
            Sort-Object { $_.attributes.created } -Descending)
        if (-not $enabled.Count) { throw 'Existing JWT versions are disabled or expired; ordinary deployment cannot overwrite them.' }
        $id = $enabled[0].id
    }
    else {
        $path = Join-Path $StateDirectory 'jwt-signing.private'
        # Establish restricted directory permissions before putting key material there.
        Save-TaskManagerPrivateJson -Path (Join-Path $StateDirectory 'secret-operation.json') -Value @{ operation = 'create-if-absent'; secretName = $Foundation.jwtSecretName }
        $key = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
        try {
            [IO.File]::WriteAllText($path, $key, [Text.UTF8Encoding]::new($false))
            if ($IsLinux) { [IO.File]::SetUnixFileMode($path, [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite) }
            # Environment deployment concurrency serializes this create-only policy.
            # Key Vault SET creates a version, not an atomic compare-and-swap.
            $result = & $Invoker @('keyvault', 'secret', 'set', '--vault-name', $vault, '--name', $Foundation.jwtSecretName, '--file', $path, '--encoding', 'utf-8', '--query', '{id:id}')
            $id = $result.id
        } finally {
            $key = $null
            if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
        }
    }
    if ($id -cnotmatch ('^' + [Regex]::Escape($vaultUri) + '/secrets/' + [Regex]::Escape($Foundation.jwtSecretName) + '/[a-f0-9]{32}$')) { throw 'JWT secret metadata must identify an actual versioned owned secret URI.' }
    $id
}
function Test-TaskManagerLiveDependencies {
    param([Parameter(Mandatory)] [scriptblock] $Invoker, [Parameter(Mandatory)] $Configuration, [Parameter(Mandatory)] $Foundation)
    $domainId = $Configuration.foundation.emailDomainResourceId
    $domain = & $Invoker @('rest', '--method', 'get', '--url', "https://management.azure.com${domainId}?api-version=2025-09-01", '--query', 'properties')
    if ($domain.provisioningState -cne 'Succeeded' -or $domain.fromSenderDomain -ine ([Net.Mail.MailAddress]::new($Configuration.foundation.emailFromAddress)).Host) { throw 'Verified sender domain is not ready or does not match FromAddress.' }
    if ($domain.domainManagement -cne 'AzureManaged') {
        foreach ($kind in @('Domain', 'SPF', 'DKIM', 'DKIM2')) { if ((Get-TaskManagerField (Get-TaskManagerField $domain.verificationStates $kind @{}) 'status') -cne 'Verified') { throw 'Sender DNS verification is incomplete.' } }
    }
    $senders = & $Invoker @('rest', '--method', 'get', '--url', "https://management.azure.com$domainId/senderUsernames?api-version=2025-09-01", '--query', '{value:value,nextLink:nextLink}')
    if ((Get-TaskManagerField $senders 'nextLink') -or @($senders.value | Where-Object { $_.properties.username -ceq ([Net.Mail.MailAddress]::new($Configuration.foundation.emailFromAddress)).User }).Count -ne 1) { throw 'Exact sender username must exist; incomplete paginated metadata cannot pass.' }
    $communication = & $Invoker @('rest', '--method', 'get', '--url', "https://management.azure.com$($Foundation.resourceIds.communicationService)?api-version=2025-09-01", '--query', 'properties.linkedDomains')
    if ($domainId -notin @($communication)) { throw 'The verified domain is not linked to the configured communication resource.' }
    $deployment = $Configuration.foundation.openAiDeploymentName
    if ($deployment -cnotmatch '^[a-zA-Z0-9_-]{1,128}$') { throw 'Invalid AI deployment identifier.' }
    $ai = & $Invoker @('rest', '--method', 'get', '--url', "https://management.azure.com$($Foundation.resourceIds.openAi)/deployments/${deployment}?api-version=2024-10-01", '--query', '{properties:properties,sku:sku}')
    if ($ai.properties.provisioningState -cne 'Succeeded' -or $ai.sku.capacity -lt 1) { throw 'Actual Azure OpenAI deployment/quota is unavailable.' }
    foreach ($pair in @(@('openAiModelName', 'name'), @('openAiModelVersion', 'version'))) {
        $expected = Get-TaskManagerField $Configuration.foundation $pair[0] ''
        if ($expected -and $ai.properties.model[$pair[1]] -cne $expected) { throw 'Actual OpenAI model/version differs from the reviewed configuration.' }
    }
    foreach ($type in @('sql', 'signalR')) {
        $endpointId = $Foundation.resourceIds["${type}PrivateEndpoint"]
        $endpoint = & $Invoker @('rest', '--method', 'get', '--url', "https://management.azure.com${endpointId}?api-version=2024-05-01", '--query', 'properties.privateLinkServiceConnections')
        if (@($endpoint).Count -ne 1 -or $endpoint[0].properties.privateLinkServiceConnectionState.status -cne 'Approved') { throw 'An actual private endpoint connection is not approved.' }
    }
    # Runtime SQL MI/DNS connectivity is proven by the exact bootstrap/migration job,
    # not by ARM endpoint metadata. Real email delivery/inference require controlled smoke.
}
function Assert-TaskManagerCandidate {
    param([Parameter(Mandatory)] [scriptblock] $Invoker, [Parameter(Mandatory)] $Configuration, [Parameter(Mandatory)] $Foundation,
          [Parameter(Mandatory)] $Apps, [Parameter(Mandatory)] $Manifest, [Parameter(Mandatory)] [string] $CandidateSuffix)
    $expected = "https://$($Foundation.appNames.web)--$CandidateSuffix.$($Foundation.environmentDefaultDomain)"
    if ($Apps.expectedWebCandidateOrigin -cne $expected -or $Apps.candidateUrls.web -cne $expected) { throw 'Actual Web revision FQDN differs from its configured origin.' }
    foreach ($unit in @('web', 'api', 'notifier')) {
        $revision = "$($Foundation.appNames[$unit])--$CandidateSuffix"
        if ($Apps.candidateRevisions[$unit] -cne $revision) { throw 'ARM returned another candidate revision.' }
        $actual = & $Invoker @('containerapp', 'revision', 'show', '--name', $Foundation.appNames[$unit], '--resource-group', $Configuration.resourceGroupName,
            '--revision', $revision, '--query', '{name:name,properties:{active:properties.active,fqdn:properties.fqdn,containers:properties.template.containers,provisioningState:properties.provisioningState}}')
        if ($actual.name -cne $revision -or $actual.properties.active -ne $true -or $actual.properties.provisioningState -cne 'Provisioned' -and $actual.properties.provisioningState -cne 'Succeeded' -or
            @($actual.properties.containers).Count -ne 1 -or $actual.properties.containers[0].image -cne $Manifest.images[$unit].reference -or
            "https://$($actual.properties.fqdn)" -cne $Apps.candidateUrls[$unit]) { throw 'Actual candidate revision or digest is not the reviewed release.' }
        $mapping = & $Invoker @('containerapp', 'show', '--name', $Foundation.appNames[$unit], '--resource-group', $Configuration.resourceGroupName, '--query', 'properties.configuration.ingress.traffic')
        if (@($mapping | Where-Object { (Get-TaskManagerField $_ 'label') -ceq 'candidate' -and $_.revisionName -ceq $revision }).Count -ne 1) { throw 'Candidate label does not target the actual reviewed revision.' }
    }
}
function Invoke-TaskManagerAnonymousSmoke {
    param([Parameter(Mandatory)] [string] $Origin)
    $origin = Assert-TaskManagerOrigin $Origin
    $handler = [Net.Http.HttpClientHandler]::new(); $handler.AllowAutoRedirect = $false; $handler.UseCookies = $true
    $client = [Net.Http.HttpClient]::new($handler); $client.Timeout = [TimeSpan]::FromSeconds(20)
    $checks = @()
    try {
        foreach ($case in @(@('GET', '/', 200), @('GET', '/health/ready', 200), @('GET', '/api/auth/me', 401), @('GET', '/api/auth/csrf', 200), @('POST', '/chathub/negotiate?negotiateVersion=1', 401))) {
            $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($case[0]), "$origin$($case[1])")
            $request.Headers.Add('Origin', $origin)
            try {
                $response = $client.Send($request)
                try {
                    if ([int] $response.StatusCode -ne $case[2]) { throw 'Live anonymous smoke did not return the expected status.' }
                    if (-not $response.Headers.CacheControl -or -not $response.Headers.CacheControl.NoStore -or
                        -not $response.Headers.Contains('Referrer-Policy') -or 'no-referrer' -notin @($response.Headers.GetValues('Referrer-Policy'))) { throw 'Live privacy response headers are missing.' }
                    $checks += @{ method = $case[0]; path = $case[1]; status = [int] $response.StatusCode }
                } finally { $response.Dispose() }
            } finally { $request.Dispose() }
        }
        @{ kind = 'live-anonymous-guards'; origin = $origin; completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); checks = $checks; authenticatedIdentitySqlAiEmailEndToEnd = $false }
    } finally { $client.Dispose(); $handler.Dispose() }
}
function Set-TaskManagerTraffic {
    param([Parameter(Mandatory)] [scriptblock] $Invoker, [Parameter(Mandatory)] $Configuration, [Parameter(Mandatory)] $Foundation,
          [Parameter(Mandatory)] [string] $Unit, [Parameter(Mandatory)] [array] $Traffic)
    if (-not $Traffic.Count -or ($Traffic | Measure-Object -Property weight -Sum).Sum -ne 100) { throw 'Traffic must total 100 with explicit revision names.' }
    $weights = @(); foreach ($item in $Traffic) { if ((Get-TaskManagerField $item 'latestRevision' $false) -or -not $item.revisionName) { throw 'Latest traffic is prohibited.' }; $weights += "$($item.revisionName)=$($item.weight)" }
    $null = & $Invoker (@('containerapp', 'ingress', 'traffic', 'set', '--name', $Foundation.appNames[$Unit], '--resource-group', $Configuration.resourceGroupName, '--revision-weight') + $weights + @('--query', 'length(@)'))
}
function Set-TaskManagerIngressPolicy {
    param([Parameter(Mandatory)] [scriptblock] $Invoker, [Parameter(Mandatory)] $Configuration, [Parameter(Mandatory)] $Foundation,
          [Parameter(Mandatory)] [array] $Rules, [Parameter(Mandatory)] [string] $StateDirectory)
    $id = "/subscriptions/$($Configuration.subscriptionId)/resourceGroups/$($Configuration.resourceGroupName)/providers/Microsoft.App/containerApps/$($Foundation.appNames.web)"
    $path = Join-Path $StateDirectory 'ingress-policy.json'
    Save-TaskManagerPrivateJson $path @{ properties = @{ configuration = @{ ingress = @{ ipSecurityRestrictions = $Rules } } } }
    $null = & $Invoker @('rest', '--method', 'patch', '--url', "https://management.azure.com${id}?api-version=2025-07-01", '--body', "@$path", '--query', '{state:properties.provisioningState}')
}
