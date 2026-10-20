param location string
param namePrefix string
param tags object

// Web has only registry/telemetry rights; business identities are never attached to it.
var names = ['web', 'api', 'notifier', 'migration', 'sqlBootstrap']
resource identities 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = [for name in names: {
  name: '${namePrefix}-${toLower(name)}-mi'
  location: location
  tags: tags
}]
output identityIds object = {
  web: { name: identities[0].name, resourceId: identities[0].id, objectId: identities[0].properties.principalId, clientId: identities[0].properties.clientId }
  api: { name: identities[1].name, resourceId: identities[1].id, objectId: identities[1].properties.principalId, clientId: identities[1].properties.clientId }
  notifier: { name: identities[2].name, resourceId: identities[2].id, objectId: identities[2].properties.principalId, clientId: identities[2].properties.clientId }
  migration: { name: identities[3].name, resourceId: identities[3].id, objectId: identities[3].properties.principalId, clientId: identities[3].properties.clientId }
  sqlBootstrap: { name: identities[4].name, resourceId: identities[4].id, objectId: identities[4].properties.principalId, clientId: identities[4].properties.clientId }
}
