param location string
param name string
param tags object
param apiPrincipalId string
param deploymentName string
param modelName string
param modelVersion string
@allowed(['Standard', 'GlobalStandard', 'DataZoneStandard'])
param deploymentSkuName string
@minValue(1)
param deploymentCapacity int
resource account 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: name
  location: location
  tags: tags
  kind: 'OpenAI'
  sku: { name: 'S0' }
  properties: { customSubDomainName: name, disableLocalAuth: true, publicNetworkAccess: 'Enabled' }
}
resource deployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: account
  name: deploymentName
  sku: { name: deploymentSkuName, capacity: deploymentCapacity }
  properties: { model: { format: 'OpenAI', name: modelName, version: modelVersion }, versionUpgradeOption: 'NoAutoUpgrade' }
}
resource inference 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: account
  name: guid(account.id, apiPrincipalId, 'OpenAIUser')
  properties: { principalId: apiPrincipalId, principalType: 'ServicePrincipal', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd') }
}
output resourceId string = account.id
output endpoint string = account.properties.endpoint
