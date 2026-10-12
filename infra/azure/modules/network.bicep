param location string
param namePrefix string
param tags object
param addressPrefix string
param ingressSubnetCidr string
param privateEndpointSubnetCidr string

resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: '${namePrefix}-vnet'
  location: location
  tags: tags
  properties: {
    addressSpace: { addressPrefixes: [addressPrefix] }
    subnets: [
      {
        name: 'container-apps'
        properties: {
          addressPrefix: ingressSubnetCidr
          delegations: [{ name: 'container-apps', properties: { serviceName: 'Microsoft.App/environments' } }]
        }
      }
      {
        name: 'private-endpoints'
        properties: { addressPrefix: privateEndpointSubnetCidr, privateEndpointNetworkPolicies: 'Disabled' }
      }
    ]
  }
}
output vnetId string = vnet.id
output infrastructureSubnetId string = '${vnet.id}/subnets/container-apps'
output privateEndpointSubnetId string = '${vnet.id}/subnets/private-endpoints'
