param serviceName string
param apiPrincipalId string
resource service 'Microsoft.Communication/communicationServices@2023-03-31' existing = { name: serviceName }
resource senderRole 'Microsoft.Authorization/roleDefinitions@2022-04-01' = {
  name: guid(resourceGroup().id, 'TaskManagerNativeEmailSender')
  properties: {
    roleName: 'TaskManager Native Email Sender ${uniqueString(resourceGroup().id)}'
    description: 'Official ACS native EmailClient managed identity permissions; no SMTP or broad Contributor.'
    type: 'CustomRole'
    assignableScopes: [resourceGroup().id]
    permissions: [{ actions: ['Microsoft.Communication/CommunicationServices/Read', 'Microsoft.Communication/CommunicationServices/Write'], notActions: [], dataActions: [], notDataActions: [] }]
  }
}
resource sender 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: service
  name: guid(service.id, apiPrincipalId, senderRole.id)
  properties: { principalId: apiPrincipalId, principalType: 'ServicePrincipal', roleDefinitionId: senderRole.id }
}
output resourceId string = service.id
output endpoint string = 'https://${service.properties.hostName}'
