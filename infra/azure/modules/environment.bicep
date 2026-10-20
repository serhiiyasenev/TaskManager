param location string
param name string
param tags object
param infrastructureSubnetId string
param logAnalyticsWorkspaceId string

resource workspace 'Microsoft.OperationalInsights/workspaces@2022-10-01' existing = {
  name: last(split(logAnalyticsWorkspaceId, '/'))
}
resource environment 'Microsoft.App/managedEnvironments@2025-07-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: workspace.properties.customerId
        // Control-plane reference only, never an output or application environment variable.
        sharedKey: workspace.listKeys().primarySharedKey
      }
    }
    vnetConfiguration: { infrastructureSubnetId: infrastructureSubnetId, internal: false }
    workloadProfiles: [{ name: 'Consumption', workloadProfileType: 'Consumption' }]
  }
}
output resourceId string = environment.id
output defaultDomain string = environment.properties.defaultDomain
