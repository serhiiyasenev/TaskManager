param location string
param serverName string
param databaseName string
param tags object
param bootstrapIdentity object
@allowed(['Basic', 'S0', 'S1', 'S2'])
param databaseSkuName string = 'S0'

resource server 'Microsoft.Sql/servers@2023-08-01' = {
  name: serverName
  location: location
  tags: tags
  properties: {
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Disabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      principalType: 'Application'
      login: bootstrapIdentity.name
      sid: bootstrapIdentity.objectId
      tenantId: tenant().tenantId
    }
  }
}
resource database 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: server
  name: databaseName
  location: location
  tags: tags
  sku: { name: databaseSkuName, tier: databaseSkuName == 'Basic' ? 'Basic' : 'Standard' }
  properties: { requestedBackupStorageRedundancy: 'Local', zoneRedundant: false }
}
resource shortTermRetention 'Microsoft.Sql/servers/databases/backupShortTermRetentionPolicies@2023-08-01' = {
  parent: database
  name: 'default'
  properties: { retentionDays: databaseSkuName == 'Basic' ? 7 : 14 }
}
output serverId string = server.id
output databaseId string = database.id
output serverFqdn string = server.properties.fullyQualifiedDomainName
