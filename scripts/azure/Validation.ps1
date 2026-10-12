#requires -Version 7.4
. (Join-Path $PSScriptRoot 'Common.ps1')

function Read-TaskManagerConfiguration {
    param([Parameter(Mandatory)] [string] $Path)
    $config = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -AsHashtable -Depth 100
    $contract = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../../infra/azure/deployment-contract.json') -Raw | ConvertFrom-Json -AsHashtable
    Assert-TaskManagerGuid $config.subscriptionId; Assert-TaskManagerGuid $config.tenantId
    Assert-TaskManagerAzureName $config.resourceGroupName; Assert-TaskManagerAzureName $config.githubEnvironment
    if ($config.expectedFederatedSubject -match '[\r\n*]' -or [string]::IsNullOrWhiteSpace($config.expectedFederatedSubject)) { throw 'An exact inspected federated subject is required.' }
    $parameters = $config.foundation
    foreach ($name in $contract.foundationParameters.required) { if (-not $parameters.Contains($name) -or $null -eq $parameters[$name] -or "$($parameters[$name])" -eq '') { throw "Missing foundation parameter: $name." } }
    $names = @($contract.foundationParameters.required) + @($contract.foundationParameters.optional)
    foreach ($name in $parameters.Keys) { if ($name -notin $names) { throw "Unknown foundation parameter: $name." } }
    foreach ($name in @('resourceNamePrefix', 'environmentName')) {
        if ($parameters[$name] -cnotmatch '^[a-z][a-z0-9-]{1,11}$' -or $parameters[$name].Contains('--') -or $parameters[$name].EndsWith('-')) { throw 'Invalid product environment name or prefix.' }
    }
    if ($parameters.resourceNamePrefix.Length -lt 3 -or $parameters.location -cnotmatch '^[a-z0-9]{2,30}$' -or
        ($parameters.monthlyBudget -isnot [long] -and $parameters.monthlyBudget -isnot [int]) -or $parameters.monthlyBudget -lt 1) { throw 'A concrete region and positive integer monthly budget are required.' }
    if (@($parameters.budgetContactEmails).Count -lt 1 -or @($parameters.budgetContactEmails).Count -gt 5) { throw 'One to five budget contacts are required.' }
    foreach ($email in $parameters.budgetContactEmails) {
        $mail = [Net.Mail.MailAddress]::new($email)
        if ($mail.Address -cne $email) { throw 'Budget contacts must be plain email addresses.' }
    }
    if ((Get-TaskManagerField $parameters 'publicOrigin' '')) { Assert-TaskManagerOrigin $parameters.publicOrigin | Out-Null }
    Assert-TaskManagerSmokeCidrs $config.smokeAllowedCidrs
    foreach ($name in @($parameters.Keys | Where-Object { $_ -like 'existing*ResourceId' }) + @('emailDomainResourceId')) {
        if ($parameters[$name]) {
            Assert-TaskManagerResourceId $parameters[$name]
            if (-not $parameters[$name].StartsWith("/subscriptions/$($config.subscriptionId)/", [StringComparison]::OrdinalIgnoreCase)) { throw 'Cross-subscription shared resources require a separately reviewed delivery contract.' }
        }
    }
    if (-not (Get-TaskManagerField $parameters 'existingOpenAiResourceId' '')) {
        foreach ($name in @('openAiModelName', 'openAiModelVersion')) { if (-not (Get-TaskManagerField $parameters $name '')) { throw 'A new Azure OpenAI deployment requires an explicitly available model and version.' } }
    }
    if ((Get-TaskManagerField $parameters 'existingManagedEnvironmentResourceId' '')) {
        foreach ($name in $contract.foundationParameters.conditionalRequired.existingManagedEnvironment) { if (-not (Get-TaskManagerField $parameters $name '')) { throw 'The existing environment requires concrete VNet/subnet/private DNS inputs.' } }
    }
    if ((Get-TaskManagerField $parameters 'existingSqlServerResourceId' '') -and
        (Get-TaskManagerField $config 'operatorAuthorizedSqlBootstrapRoute' '') -cne 'DedicatedBootstrapIdentityGrantedInTargetDatabase') {
        throw 'Existing SQL requires the owner to grant the dedicated bootstrap identity an administrator-equivalent route in the product database before jobs can run.'
    }
    if ($parameters.emailFromAddress -match '[\r\n]' -or ([Net.Mail.MailAddress]::new($parameters.emailFromAddress)).Address -cne $parameters.emailFromAddress) { throw 'A plain verified sender address is required.' }
    return $config
}
function Get-TaskManagerFoundationOutputs {
    param([Parameter(Mandatory)] $Response)
    $raw = Get-TaskManagerField (Get-TaskManagerField $Response 'properties') 'outputs'
    if ($null -eq $raw) { throw 'Deployment did not return outputs.' }
    $result = @{}
    foreach ($key in $raw.Keys) { $result[$key] = Get-TaskManagerField $raw[$key] 'value' }
    return $result
}
function Assert-TaskManagerFoundationOutputs {
    param([Parameter(Mandatory)] $Outputs)
    $contract = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../../infra/azure/deployment-contract.json') -Raw | ConvertFrom-Json -AsHashtable
    foreach ($key in $contract.foundationOutputs) { if (-not $Outputs.Contains($key)) { throw "Missing actual foundation output: $key." } }
    Assert-TaskManagerOrigin $Outputs.publicOrigin | Out-Null
    if ($Outputs.environmentDefaultDomain -cnotmatch '^[a-z0-9.-]+\.azurecontainerapps\.io$' -or $Outputs.registryLoginServer -cnotmatch '^[a-z0-9]+\.azurecr\.io$' -or
        $Outputs.sqlServerFqdn -cnotmatch '^[a-z0-9-]+\.database\.windows\.net$' -or $Outputs.jwtSecretName -cne 'taskmanager-jwt-signing') { throw 'Unexpected Azure resource endpoint.' }
    Assert-TaskManagerResourceId $Outputs.registryResourceId 'Microsoft.ContainerRegistry/registries'
    foreach ($key in $contract.identityNames) {
        $identity = $Outputs.identityIds[$key]
        Assert-TaskManagerGuid $identity.objectId; Assert-TaskManagerGuid $identity.clientId
        Assert-TaskManagerResourceId $identity.resourceId 'Microsoft.ManagedIdentity/userAssignedIdentities'
    }
}
function Test-TaskManagerOffline {
    param([Parameter(Mandatory)] [string] $BicepPath)
    $repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $repository '.local/azure/bicep-runtime'
    $versions = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'tool-versions.json') -Raw | ConvertFrom-Json -AsHashtable
    $version = & $BicepPath --version
    if ($LASTEXITCODE -ne 0 -or $version -notmatch ([Regex]::Escape($versions.Bicep.version))) { throw 'Bicep version does not match the lock.' }
    $contract = Get-Content -LiteralPath (Join-Path $repository 'infra/azure/deployment-contract.json') -Raw | ConvertFrom-Json -AsHashtable
    foreach ($entry in @('foundation', 'jobs', 'apps')) {
        $target = Join-Path $repository ".local/azure/validation/$entry.json"
        $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
        & $BicepPath build (Join-Path $repository $contract.templates[$entry]) --outfile $target
        if ($LASTEXITCODE -ne 0) { throw "Native Bicep compilation failed: $entry." }
        $template = Get-Content -LiteralPath $target -Raw | ConvertFrom-Json -AsHashtable -Depth 100
        if ($entry -eq 'foundation') { Assert-TaskManagerNotificationRoles $template }
        $expected = $contract["${entry}Parameters"]
        $actual = @($template.parameters.Keys)
        foreach ($name in @($expected.required) + @($expected.optional)) { if ($name -notin $actual) { throw "Compiled parameter missing from $entry contract: $name." } }
        if ($actual.Count -ne @(@($expected.required) + @($expected.optional)).Count) { throw "Compiled $entry parameters differ from the contract." }
        $expectedOutputs = $contract["${entry}Outputs"]
        if (@($template.outputs.Keys).Count -ne $expectedOutputs.Count -or @($expectedOutputs | Where-Object { $_ -notin $template.outputs.Keys }).Count) { throw "Compiled $entry outputs differ from the contract." }
        [pscustomobject]@{ template = $entry; compiler = $versions.Bicep.version; compiled = $true; contractMatches = $true }
    }
}
function Assert-TaskManagerNotificationRoles {
    param([Parameter(Mandatory)] $Template)
    $fixture = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../../infra/azure/approved-notification-roles.json') -Raw | ConvertFrom-Json -AsHashtable
    $serialized = $Template | ConvertTo-Json -Depth 100 -Compress
    foreach ($forbidden in @($fixture.prohibitedRuntimeRoles.Values) + @('4f6e0244-6a4f-4e5b-865a-c8eeb2b9af45')) {
        if ($serialized.Contains($forbidden, [StringComparison]::OrdinalIgnoreCase)) { throw 'Compiled template contains an incorrect or prohibited runtime notification role.' }
    }
    $modules = @($Template.resources | Where-Object { $_.type -ceq 'Microsoft.Resources/deployments' -and $_.properties.Contains('template') })
    foreach ($key in $fixture.roles.Keys) {
        $role = $fixture.roles[$key]
        $scopeType = if ($key -like 'serviceBus*') { 'Microsoft.ServiceBus/namespaces/queues' } else { 'Microsoft.SignalRService/signalR' }
        $matches = @()
        foreach ($module in $modules) {
            foreach ($assignment in @($module.properties.template.resources | Where-Object { $_.type -ceq 'Microsoft.Authorization/roleAssignments' })) {
                $definition = Get-TaskManagerField $assignment.properties 'roleDefinitionId' ''
                if (-not $definition.Contains($role.roleId, [StringComparison]::OrdinalIgnoreCase)) { continue }
                if ($assignment.properties.principalType -cne 'ServicePrincipal' -or -not $assignment.scope.Contains("'$scopeType'", [StringComparison]::Ordinal)) { throw 'Compiled notification role has an unapproved principal type or scope.' }
                $principal = $assignment.properties.principalId
                $bound = $null
                foreach ($actor in $role.requiredPrincipals) {
                    $parameter = "${actor}PrincipalId"
                    if ($principal -ceq "[parameters('$parameter')]" -and $module.properties.parameters[$parameter].value.EndsWith(".identityIds.value.$actor.objectId]", [StringComparison]::Ordinal)) { $bound = $actor }
                }
                if (-not $bound) { throw 'Compiled notification role is not bound to the required dedicated runtime identity.' }
                if ($key -like 'serviceBus*') {
                    $queues = @($module.properties.parameters.queueNames.value)
                    if ($queues.Count -ne 2 -or @($queues | Where-Object { $_ -notin @('TaskNotifications', 'TaskReminders') }).Count -or
                        $assignment.copy.count -cne "[length(parameters('queueNames'))]") { throw 'Native broker permissions must cover each of the two dedicated queues.' }
                }
                $matches += $bound
            }
        }
        if ($matches.Count -ne $role.requiredPrincipals.Count -or @($role.requiredPrincipals | Where-Object { $_ -notin $matches }).Count) { throw 'Required native sender/receiver/hub permission is absent or duplicated in the compiled template.' }
    }
}
function Assert-TaskManagerCostReport {
    param([Parameter(Mandatory)] $Report, [Parameter(Mandatory)] $Configuration)
    $time = [DateTimeOffset]::MinValue
    if ((Get-TaskManagerField $Report 'source') -cne 'Azure Retail Prices API' -or (Get-TaskManagerField $Report 'currency') -cne 'USD' -or
        (Get-TaskManagerField $Configuration 'monthlyBudgetCurrency') -cne 'USD' -or (Get-TaskManagerField $Report 'completeServiceCoverage') -ne $true -or
        (Get-TaskManagerField $Report 'configuredRegion') -cne $Configuration.foundation.location -or (Get-TaskManagerField $Report 'configuredMonthlyBudget') -ne $Configuration.foundation.monthlyBudget -or
        -not [DateTimeOffset]::TryParse((Get-TaskManagerField $Report 'fetchedAtUtc'), [ref] $time) -or [DateTimeOffset]::UtcNow - $time -gt [TimeSpan]::FromHours(24) -or $time -gt [DateTimeOffset]::UtcNow.AddMinutes(1)) { throw 'A complete current-region USD cost report fetched within 24 hours and bound to the reviewed budget is required.' }
    $subtotal = Get-TaskManagerField $Report 'knownSubtotalUsd'
    if ($null -eq $subtotal -or -not [double]::IsFinite([double] $subtotal) -or $subtotal -lt 0 -or $subtotal -gt $Configuration.foundation.monthlyBudget) { throw 'Known cost estimate exceeds the reviewed budget or is invalid.' }
}
