param location string
param name string
param tags object
param targetResourceId string
param groupId string
param subnetId string
param vnetId string
param zoneName string
param existingZoneResourceId string = ''
var ownZone = empty(existingZoneResourceId)
resource zone 'Microsoft.Network/privateDnsZones@2024-06-01' = if (ownZone) {
  name: zoneName
  location: 'global'
  tags: tags
}
resource existingZone 'Microsoft.Network/privateDnsZones@2024-06-01' existing = if (!ownZone) {
  name: last(split(existingZoneResourceId, '/'))
  scope: resourceGroup(split(existingZoneResourceId, '/')[2], split(existingZoneResourceId, '/')[4])
}
// Existing zones must already be linked to the environment VNet (validated by preflight).
resource link 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = if (ownZone) {
  parent: zone
  name: '${name}-vnet'
  location: 'global'
  properties: { registrationEnabled: false, virtualNetwork: { id: vnetId } }
}
resource endpoint 'Microsoft.Network/privateEndpoints@2024-05-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    subnet: { id: subnetId }
    privateLinkServiceConnections: [{ name: groupId, properties: { privateLinkServiceId: targetResourceId, groupIds: [groupId] } }]
  }
}
resource zoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' = {
  parent: endpoint
  name: 'default'
  properties: { privateDnsZoneConfigs: [{ name: groupId, properties: { privateDnsZoneId: ownZone ? zone.id : existingZone.id } }] }
}
output endpointId string = endpoint.id
output dnsZoneId string = ownZone ? zone.id : existingZone.id
