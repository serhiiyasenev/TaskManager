param location string
param name string
param tags object
param verifiedDomainResourceId string
param dataLocation string
resource service 'Microsoft.Communication/communicationServices@2023-03-31' = {
  name: name
  location: 'global'
  tags: union(tags, { deploymentRegion: location })
  properties: { dataLocation: dataLocation, linkedDomains: [verifiedDomainResourceId] }
}
output resourceId string = service.id
output endpoint string = 'https://${service.properties.hostName}'
