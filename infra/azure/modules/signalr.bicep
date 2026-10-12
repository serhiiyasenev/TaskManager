param location string
param name string
param tags object
param notifierPrincipalId string
param allowedOrigin string
param additionalOrigins array = []
// Actual service-side names are discovered from Azure after PE creation, never guessed.
param privateEndpointConnectionNames array = []
resource signalr 'Microsoft.SignalRService/signalR@2024-03-01' = {
  name: name
  location: location
  tags: tags
  sku: { name: 'Standard_S1', tier: 'Standard', capacity: 1 }
  properties: {
    features: [{ flag: 'ServiceMode', value: 'Default' }, { flag: 'EnableLiveTrace', value: 'false' }]
    disableLocalAuth: true
    publicNetworkAccess: 'Enabled'
    cors: { allowedOrigins: concat([allowedOrigin], additionalOrigins) }
    networkACLs: {
      defaultAction: 'Deny'
      publicNetwork: { allow: ['ClientConnection'] }
      privateEndpoints: [for connectionName in privateEndpointConnectionNames: { name: connectionName, allow: ['ServerConnection'] }]
    }
  }
}
resource appServer 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: signalr
  name: guid(signalr.id, notifierPrincipalId, 'AppServer')
  properties: {
    principalId: notifierPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '420fcaa2-552c-430f-98ca-3264be4806c7')
  }
}
output resourceId string = signalr.id
output endpoint string = 'https://${signalr.properties.hostName}'
