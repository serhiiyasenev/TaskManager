param location string
param name string
param environmentId string
param registryLoginServer string
param identityResourceId string
param image string
param revisionSuffix string
param external bool
param minReplicas int
param maxReplicas int
param environmentVariables array
param existingTraffic array
param ipSecurityRestrictions array = []
param customDomains array = []
param jwtSecretUri string = ''

var candidateRevision = '${name}--${revisionSuffix}'
// Moving the fixed candidate label must retain every existing revision's exact weight.
var preservedTraffic = [for entry in existingTraffic: union({ revisionName: entry.revisionName, weight: entry.weight },
  !empty(entry.?label) && entry.?label != 'candidate' ? { label: entry.label } : {})]
var traffic = concat(preservedTraffic, [{ revisionName: candidateRevision, label: 'candidate', weight: empty(existingTraffic) ? 100 : 0 }])
resource app 'Microsoft.App/containerApps@2025-07-01' = {
  name: name
  location: location
  tags: { product: 'TaskManager', managedBy: 'Bicep' }
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${identityResourceId}': {} } }
  properties: {
    managedEnvironmentId: environmentId
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Multiple'
      maxInactiveRevisions: 5
      ingress: {
        external: external
        allowInsecure: false
        targetPort: 8080
        transport: 'auto'
        traffic: traffic
        ipSecurityRestrictions: ipSecurityRestrictions
        customDomains: customDomains
      }
      registries: [{ server: registryLoginServer, identity: identityResourceId }]
      secrets: empty(jwtSecretUri) ? [] : [{ name: 'jwt-signing', keyVaultUrl: jwtSecretUri, identity: identityResourceId }]
    }
    template: {
      revisionSuffix: revisionSuffix
      containers: [{
        name: 'app'
        image: image
        resources: { cpu: json('0.25'), memory: '0.5Gi' }
        env: concat(environmentVariables, empty(jwtSecretUri) ? [] : [{ name: 'Jwt__Key', secretRef: 'jwt-signing' }])
        probes: [
          { type: 'Startup', httpGet: { path: '/health/live', port: 8080, scheme: 'HTTP' }, periodSeconds: 5, timeoutSeconds: 3, failureThreshold: 30 }
          { type: 'Liveness', httpGet: { path: '/health/live', port: 8080, scheme: 'HTTP' }, periodSeconds: 20, timeoutSeconds: 3, failureThreshold: 3 }
          { type: 'Readiness', httpGet: { path: '/health/ready', port: 8080, scheme: 'HTTP' }, periodSeconds: 10, timeoutSeconds: 5, failureThreshold: 3 }
        ]
      }]
      scale: { minReplicas: minReplicas, maxReplicas: maxReplicas }
    }
  }
}
// These are actual control-plane response properties, not guessed DNS destinations.
output candidateRevisionName string = app.properties.latestRevisionName
output candidateFqdn string = app.properties.latestRevisionFqdn
output canonicalFqdn string = app.properties.configuration.ingress.fqdn
output resourceId string = app.id
