param location string
param name string
param environmentId string
param registryLoginServer string
param identityResourceId string
param image string
param environmentVariables array
resource job 'Microsoft.App/jobs@2025-07-01' = {
  name: name
  location: location
  tags: { product: 'TaskManager', managedBy: 'Bicep', executionPolicy: 'trusted-release-manual-only' }
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${identityResourceId}': {} } }
  properties: {
    environmentId: environmentId
    workloadProfileName: 'Consumption'
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 600
      replicaRetryLimit: 0
      manualTriggerConfig: { parallelism: 1, replicaCompletionCount: 1 }
      registries: [{ server: registryLoginServer, identity: identityResourceId }]
    }
    template: {
      containers: [{ name: 'job', image: image, env: environmentVariables, resources: { cpu: json('0.25'), memory: '0.5Gi' } }]
    }
  }
}
output jobName string = job.name
