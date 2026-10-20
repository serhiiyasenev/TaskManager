targetScope = 'resourceGroup'
param location string = resourceGroup().location
param foundationOutputs object
@minLength(71)
@maxLength(71)
param webImageDigest string
@minLength(71)
@maxLength(71)
param apiImageDigest string
@minLength(71)
@maxLength(71)
param notifierImageDigest string
@minLength(1)
@maxLength(20)
param candidateSuffix string
@description('Versioned URI of the existing private JWT secret; no key value is passed to Bicep.')
param jwtSecretUri string
@description('Snapshot of explicit existing revision traffic, with zero-weight old candidate label removed; never latestRevision.')
param webExistingTraffic array = []
param apiExistingTraffic array = []
param notifierExistingTraffic array = []
param webExistingIpSecurityRestrictions array = []
@description('Approved existing/custom certificate bindings; existing deployments must preserve their current bindings.')
param webCustomDomains array = []
@minLength(1)
@description('Strict bounded operator CIDRs. Used only for first deployment; existing global restrictions are preserved.')
param smokeAllowedCidrs array
@minValue(1)
@maxValue(4)
param notifierMinReplicas int = 1
@minValue(1)
@maxValue(4)
param notifierMaxReplicas int = 2

var outputs = foundationOutputs
var config = outputs.runtimeConfiguration
// Configure before deployment, then require equality with ARM's actual Web latestRevisionFqdn.
var expectedWebCandidateOrigin = 'https://${outputs.appNames.web}--${candidateSuffix}.${outputs.environmentDefaultDomain}'
var origins = [
  { name: 'BrowserAuth__PublicOrigin', value: outputs.publicOrigin }
  { name: 'BrowserAuth__AdditionalAllowedOrigins__0', value: expectedWebCandidateOrigin }
]
var sharedEnvironment = concat(origins, [
  { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
  { name: 'DOTNET_ENVIRONMENT', value: 'Production' }
  { name: 'ASPNETCORE_HTTP_PORTS', value: '8080' }
  { name: 'ProxyTrust__KnownNetworks__0', value: outputs.ingressSubnetCidr }
  { name: 'ProxyTrust__AllowedHosts__0', value: split(replace(outputs.publicOrigin, 'https://', ''), ':')[0] }
  { name: 'ProxyTrust__AllowedHosts__1', value: replace(expectedWebCandidateOrigin, 'https://', '') }
  { name: 'Telemetry__Provider', value: config.telemetry.provider }
  { name: 'Telemetry__ConnectionString', value: config.telemetry.connectionString }
  { name: 'Telemetry__TracesPerSecond', value: '1' }
])
var businessEnvironment = [
  { name: 'Database__Authentication', value: 'AzureManagedIdentity' }
  { name: 'DataProtection__Provider', value: config.dataProtection.provider }
  { name: 'DataProtection__BlobUri', value: config.dataProtection.blobUri }
  { name: 'DataProtection__KeyVaultKeyUri', value: config.dataProtection.keyVaultKeyUri }
  { name: 'Jwt__Issuer', value: 'TaskManagerAPI' }
  { name: 'Jwt__Audience', value: 'TaskManagerClients' }
  { name: 'Notifications__Provider', value: 'AzureServiceBus' }
  { name: 'ServiceBus__FullyQualifiedNamespace', value: config.serviceBus.fullyQualifiedNamespace }
  { name: 'ServiceBus__QueueName', value: config.serviceBus.queueName }
  { name: 'ServiceBus__ReminderQueueName', value: config.serviceBus.reminderQueueName }
  { name: 'ServiceBus__MaxConcurrentCalls', value: '1' }
  { name: 'ServiceBus__MaxAutoLockRenewalSeconds', value: '120' }
  { name: 'ProxyTrust__ForwardLimit', value: '2' }
  { name: 'BootstrapAdmin__Enabled', value: 'false' }
]
var apiIdentity = outputs.identityIds.api
var notifierIdentity = outputs.identityIds.notifier
var webIdentity = outputs.identityIds.web
var firstDeploymentRestrictions = [for (cidr, index) in smokeAllowedCidrs: { name: 'smoke-${index}', ipAddressRange: cidr, action: 'Allow', description: 'First deployment operator smoke gate' }]
module api 'modules/container-app.bicep' = {
  name: '${outputs.appNames.api}-candidate'
  params: {
    location: location
    name: outputs.appNames.api
    environmentId: outputs.managedEnvironmentId
    registryLoginServer: outputs.registryLoginServer
    identityResourceId: apiIdentity.resourceId
    image: '${outputs.registryLoginServer}/taskmanager/api@${apiImageDigest}'
    revisionSuffix: candidateSuffix
    external: false
    minReplicas: 0
    maxReplicas: 3
    existingTraffic: apiExistingTraffic
    jwtSecretUri: jwtSecretUri
    environmentVariables: concat(sharedEnvironment, businessEnvironment, [
      { name: 'ConnectionStrings__DbConnection', value: 'Server=tcp:${outputs.sqlServerFqdn},1433;Database=${outputs.sqlDatabaseName};Authentication=Active Directory Managed Identity;User Id=${apiIdentity.clientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;' }
      { name: 'DataProtection__ManagedIdentityClientId', value: apiIdentity.clientId }
      { name: 'ServiceBus__ManagedIdentityClientId', value: apiIdentity.clientId }
      { name: 'Telemetry__ManagedIdentityClientId', value: apiIdentity.clientId }
      { name: 'Telemetry__ServiceName', value: 'taskmanager-api' }
      { name: 'AI__Provider', value: 'Azure' }
      { name: 'AI__Endpoint', value: config.ai.endpoint }
      { name: 'AI__Deployment', value: config.ai.deployment }
      { name: 'AI__ManagedIdentityClientId', value: apiIdentity.clientId }
      { name: 'Email__Provider', value: 'AzureCommunication' }
      { name: 'Email__AzureCommunicationEndpoint', value: config.email.azureCommunicationEndpoint }
      { name: 'Email__FromAddress', value: config.email.fromAddress }
      { name: 'Email__ManagedIdentityClientId', value: apiIdentity.clientId }
      { name: 'Email__SendTimeoutSeconds', value: '30' }
      { name: 'Reminders__Enabled', value: 'false' }
    ])
  }
}
module notifier 'modules/container-app.bicep' = {
  name: '${outputs.appNames.notifier}-candidate'
  params: {
    location: location
    name: outputs.appNames.notifier
    environmentId: outputs.managedEnvironmentId
    registryLoginServer: outputs.registryLoginServer
    identityResourceId: notifierIdentity.resourceId
    image: '${outputs.registryLoginServer}/taskmanager/notifier@${notifierImageDigest}'
    revisionSuffix: candidateSuffix
    external: false
    minReplicas: notifierMinReplicas
    maxReplicas: notifierMaxReplicas
    existingTraffic: notifierExistingTraffic
    jwtSecretUri: jwtSecretUri
    environmentVariables: concat(sharedEnvironment, businessEnvironment, [
      { name: 'ConnectionStrings__DbConnection', value: 'Server=tcp:${outputs.sqlServerFqdn},1433;Database=${outputs.sqlDatabaseName};Authentication=Active Directory Managed Identity;User Id=${notifierIdentity.clientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;' }
      { name: 'DataProtection__ManagedIdentityClientId', value: notifierIdentity.clientId }
      { name: 'ServiceBus__ManagedIdentityClientId', value: notifierIdentity.clientId }
      { name: 'Telemetry__ManagedIdentityClientId', value: notifierIdentity.clientId }
      { name: 'Telemetry__ServiceName', value: 'taskmanager-notifier' }
      { name: 'Realtime__Provider', value: 'AzureSignalR' }
      { name: 'Realtime__Endpoint', value: config.realtime.endpoint }
      { name: 'Realtime__ManagedIdentityClientId', value: notifierIdentity.clientId }
      { name: 'Reminders__Enabled', value: 'true' }
    ])
  }
}
module web 'modules/container-app.bicep' = {
  name: '${outputs.appNames.web}-candidate'
  params: {
    location: location
    name: outputs.appNames.web
    environmentId: outputs.managedEnvironmentId
    registryLoginServer: outputs.registryLoginServer
    identityResourceId: webIdentity.resourceId
    image: '${outputs.registryLoginServer}/taskmanager/web@${webImageDigest}'
    revisionSuffix: candidateSuffix
    external: true
    minReplicas: 0
    maxReplicas: 3
    existingTraffic: webExistingTraffic
    customDomains: webCustomDomains
    ipSecurityRestrictions: empty(webExistingTraffic)
      ? firstDeploymentRestrictions
      : webExistingIpSecurityRestrictions
    environmentVariables: concat(sharedEnvironment, [
      { name: 'ProxyTrust__ForwardLimit', value: '1' }
      { name: 'Telemetry__ManagedIdentityClientId', value: webIdentity.clientId }
      { name: 'Telemetry__ServiceName', value: 'taskmanager-web' }
      { name: 'Gateway__ApiDestination', value: 'https://${api.outputs.candidateFqdn}' }
      { name: 'Gateway__NotifierDestination', value: 'https://${notifier.outputs.candidateFqdn}' }
    ])
  }
}
output candidateRevisions object = { web: web.outputs.candidateRevisionName, api: api.outputs.candidateRevisionName, notifier: notifier.outputs.candidateRevisionName }
output candidateUrls object = { web: 'https://${web.outputs.candidateFqdn}', api: 'https://${api.outputs.candidateFqdn}', notifier: 'https://${notifier.outputs.candidateFqdn}' }
output expectedWebCandidateOrigin string = expectedWebCandidateOrigin
output canonicalWebFqdn string = web.outputs.canonicalFqdn
output appResourceIds object = { web: web.outputs.resourceId, api: api.outputs.resourceId, notifier: notifier.outputs.resourceId }
