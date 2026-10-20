param location string
param name string
param tags object
param apiPrincipalId string
param notifierPrincipalId string
param queueNames array
resource bus 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: name
  location: location
  tags: tags
  sku: { name: 'Standard', tier: 'Standard' }
  properties: { disableLocalAuth: true, publicNetworkAccess: 'Enabled', minimumTlsVersion: '1.2' }
}
resource queues 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' = [for queueName in queueNames: {
  parent: bus
  name: queueName
  properties: {
    lockDuration: 'PT1M'
    maxDeliveryCount: 3
    defaultMessageTimeToLive: 'P14D'
    deadLetteringOnMessageExpiration: true
    requiresDuplicateDetection: true
    duplicateDetectionHistoryTimeWindow: 'PT10M'
    requiresSession: false
    enableExpress: false
    enablePartitioning: false
    maxSizeInMegabytes: 1024
  }
}]
// Queue-scoped native sender/receiver rights. SQL dedup remains authoritative.
resource apiSenders 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (queueName, index) in queueNames: {
  scope: queues[index]
  name: guid(queues[index].id, apiPrincipalId, 'Sender')
  properties: { principalId: apiPrincipalId, principalType: 'ServicePrincipal', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39') }
}]
resource notifierSenders 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (queueName, index) in queueNames: {
  scope: queues[index]
  name: guid(queues[index].id, notifierPrincipalId, 'Sender')
  properties: { principalId: notifierPrincipalId, principalType: 'ServicePrincipal', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39') }
}]
resource notifierReceivers 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (queueName, index) in queueNames: {
  scope: queues[index]
  name: guid(queues[index].id, notifierPrincipalId, 'Receiver')
  properties: { principalId: notifierPrincipalId, principalType: 'ServicePrincipal', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0') }
}]
output resourceId string = bus.id
output fullyQualifiedNamespace string = '${bus.name}.servicebus.windows.net'
