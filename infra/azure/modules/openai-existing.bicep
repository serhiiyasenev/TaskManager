param accountName string
param apiPrincipalId string
// Deployment/model/quota compatibility is verified by preflight; no shared deployment is changed.
resource account 'Microsoft.CognitiveServices/accounts@2024-10-01' existing = { name: accountName }
resource inference 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: account
  name: guid(account.id, apiPrincipalId, 'TaskManagerOpenAIUser')
  properties: { principalId: apiPrincipalId, principalType: 'ServicePrincipal', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd') }
}
output resourceId string = account.id
output endpoint string = account.properties.endpoint
