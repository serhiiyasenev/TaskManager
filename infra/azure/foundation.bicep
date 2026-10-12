targetScope = 'resourceGroup'

@minLength(3)
@maxLength(12)
param resourceNamePrefix string
@minLength(2)
@maxLength(12)
param environmentName string
param location string = resourceGroup().location
@description('Explicit HTTPS product origin; empty selects the actual ACA canonical web FQDN after environment creation.')
param publicOrigin string = ''
@minValue(1)
param monthlyBudget int
@minLength(1)
param budgetContactEmails array
param budgetStartDate string = utcNow('yyyy-MM-01')
param existingRegistryResourceId string = ''
@allowed(['RbacRegistryPermissions', 'AbacRepositoryPermissions'])
param existingRegistryRoleAssignmentMode string = 'RbacRegistryPermissions'
param existingManagedEnvironmentResourceId string = ''
param existingVnetResourceId string = ''
param existingPrivateEndpointSubnetResourceId string = ''
param existingSqlPrivateDnsZoneResourceId string = ''
param existingSignalRPrivateDnsZoneResourceId string = ''
param vnetAddressPrefix string = '10.42.0.0/16'
param ingressSubnetCidr string = '10.42.0.0/23'
param privateEndpointSubnetCidr string = '10.42.2.0/27'
param existingSqlServerResourceId string = ''
@allowed(['Basic', 'S0', 'S1', 'S2'])
param sqlDatabaseSkuName string = 'S0'
param existingOpenAiResourceId string = ''
@minLength(1)
@maxLength(128)
param openAiDeploymentName string
param openAiModelName string = ''
param openAiModelVersion string = ''
@allowed(['Standard', 'GlobalStandard', 'DataZoneStandard'])
param openAiDeploymentSkuName string = 'GlobalStandard'
@minValue(1)
param openAiDeploymentCapacity int = 10
param existingCommunicationServiceResourceId string = ''
@description('A real, verified Email Communication Services domain linked to the ACS resource; validated live before app deployment.')
param emailDomainResourceId string
param emailDataLocation string
param emailFromAddress string
@description('Actual approved service-side names discovered after private endpoint creation; empty denies all server connections until post-foundation ACL gate.')
param signalRPrivateEndpointConnectionNames array = []
@maxLength(2)
@description('Approved actual revision origins preserved by preflight; post-foundation configuration adds the new expected candidate revision origin before apps deployment.')
param signalRAdditionalAllowedOrigins array = []

var prefix = '${resourceNamePrefix}-${environmentName}'
var suffix = uniqueString(resourceGroup().id, environmentName, resourceNamePrefix)
var tags = { product: 'TaskManager', environment: environmentName, managedBy: 'Bicep' }
var appNames = { web: '${prefix}-web', api: '${prefix}-api', notifier: '${prefix}-notifier' }
var ownEnvironment = empty(existingManagedEnvironmentResourceId)
var ownRegistry = empty(existingRegistryResourceId)
var ownSql = empty(existingSqlServerResourceId)
var ownOpenAi = empty(existingOpenAiResourceId)
var ownEmail = empty(existingCommunicationServiceResourceId)
var registryName = take('${replace(resourceNamePrefix, '-', '')}${replace(environmentName, '-', '')}${suffix}', 50)
var environmentNameAzure = '${prefix}-environment'
var sqlServerName = '${prefix}-sql-${take(suffix, 6)}'
var sqlDatabaseName = '${prefix}-db'
var openAiName = '${prefix}-ai-${take(suffix, 6)}'
var emailName = '${prefix}-email-${take(suffix, 6)}'
var registryParts = split(ownRegistry ? resourceId('Microsoft.ContainerRegistry/registries', registryName) : existingRegistryResourceId, '/')
var environmentParts = split(ownEnvironment ? resourceId('Microsoft.App/managedEnvironments', environmentNameAzure) : existingManagedEnvironmentResourceId, '/')
var sqlParts = split(ownSql ? resourceId('Microsoft.Sql/servers', sqlServerName) : existingSqlServerResourceId, '/')
var openAiParts = split(ownOpenAi ? resourceId('Microsoft.CognitiveServices/accounts', openAiName) : existingOpenAiResourceId, '/')

// Foundation has no container apps, revisions, timers, consumers, or running jobs.
module identities 'modules/identities.bicep' = { name: '${prefix}-identities', params: { location: location, namePrefix: prefix, tags: tags } }
var identityIds = identities.outputs.identityIds
var allPrincipals = [identityIds.web.objectId, identityIds.api.objectId, identityIds.notifier.objectId, identityIds.migration.objectId, identityIds.sqlBootstrap.objectId]
module network 'modules/network.bicep' = if (ownEnvironment) {
  name: '${prefix}-network'
  params: { location: location, namePrefix: prefix, tags: tags, addressPrefix: vnetAddressPrefix, ingressSubnetCidr: ingressSubnetCidr, privateEndpointSubnetCidr: privateEndpointSubnetCidr }
}
var vnetId = ownEnvironment ? network!.outputs.vnetId : existingVnetResourceId
var privateSubnetId = ownEnvironment ? network!.outputs.privateEndpointSubnetId : existingPrivateEndpointSubnetResourceId
module newSql 'modules/sql.bicep' = if (ownSql) {
  name: '${prefix}-sql'
  params: { location: location, serverName: sqlServerName, databaseName: sqlDatabaseName, tags: tags, bootstrapIdentity: identityIds.sqlBootstrap, databaseSkuName: sqlDatabaseSkuName }
}
module sharedSql 'modules/sql-existing.bicep' = if (!ownSql) {
  name: '${prefix}-sql-existing'
  scope: resourceGroup(sqlParts[2], sqlParts[4])
  params: { location: location, serverName: sqlParts[8], databaseName: sqlDatabaseName, tags: tags, databaseSkuName: sqlDatabaseSkuName }
}
var sqlServerId = ownSql ? newSql!.outputs.serverId : sharedSql!.outputs.serverId
var sqlDatabaseId = ownSql ? newSql!.outputs.databaseId : sharedSql!.outputs.databaseId
module sqlPrivate 'modules/private-endpoint.bicep' = {
  name: '${prefix}-sql-private'
  params: { location: location, name: '${prefix}-sql-private', tags: tags, targetResourceId: sqlServerId, groupId: 'sqlServer', subnetId: privateSubnetId, vnetId: vnetId, zoneName: 'privatelink.${az.environment().suffixes.sqlServerHostname}', existingZoneResourceId: existingSqlPrivateDnsZoneResourceId }
}
module registry 'modules/registry.bicep' = if (ownRegistry) {
  name: '${prefix}-registry'
  params: { location: location, name: registryName, tags: tags, principalIds: allPrincipals }
}
module registryExisting 'modules/registry-existing.bicep' = if (!ownRegistry) {
  name: '${prefix}-registry-existing'
  scope: resourceGroup(registryParts[2], registryParts[4])
  params: { registryName: registryParts[8], principalIds: allPrincipals, roleAssignmentMode: existingRegistryRoleAssignmentMode }
}
module keyRing 'modules/key-ring.bicep' = {
  name: '${prefix}-key-ring'
  params: { location: location, storageName: take('tm${suffix}', 24), vaultName: '${take(prefix, 14)}-kv-${take(suffix, 6)}', tags: tags, principalIds: [identityIds.api.objectId, identityIds.notifier.objectId] }
}
module serviceBus 'modules/service-bus.bicep' = {
  name: '${prefix}-service-bus'
  params: { location: location, name: '${prefix}-bus-${take(suffix, 6)}', tags: tags, apiPrincipalId: identityIds.api.objectId, notifierPrincipalId: identityIds.notifier.objectId, queueNames: ['TaskNotifications', 'TaskReminders'] }
}
module monitoring 'modules/monitoring.bicep' = {
  name: '${prefix}-monitoring'
  params: { location: location, namePrefix: prefix, tags: tags, principalIds: [identityIds.web.objectId, identityIds.api.objectId, identityIds.notifier.objectId], monthlyBudget: monthlyBudget, budgetContactEmails: budgetContactEmails, budgetStartDate: budgetStartDate, sqlDatabaseId: sqlDatabaseId, serviceBusId: serviceBus.outputs.resourceId }
}
module environment 'modules/environment.bicep' = if (ownEnvironment) {
  name: '${prefix}-environment'
  params: { location: location, name: environmentNameAzure, tags: tags, infrastructureSubnetId: network!.outputs.infrastructureSubnetId, logAnalyticsWorkspaceId: monitoring.outputs.workspaceId }
}
resource existingEnvironment 'Microsoft.App/managedEnvironments@2025-07-01' existing = if (!ownEnvironment) {
  name: environmentParts[8]
  scope: resourceGroup(environmentParts[2], environmentParts[4])
}
var environmentDefaultDomain = ownEnvironment ? environment!.outputs.defaultDomain : existingEnvironment!.properties.defaultDomain
var actualPublicOrigin = empty(publicOrigin) ? 'https://${appNames.web}.${environmentDefaultDomain}' : publicOrigin
module signalr 'modules/signalr.bicep' = {
  name: '${prefix}-signalr'
  params: { location: location, name: '${prefix}-signalr-${take(suffix, 6)}', tags: tags, notifierPrincipalId: identityIds.notifier.objectId, allowedOrigin: actualPublicOrigin, additionalOrigins: signalRAdditionalAllowedOrigins, privateEndpointConnectionNames: signalRPrivateEndpointConnectionNames }
}
module signalrPrivate 'modules/private-endpoint.bicep' = {
  name: '${prefix}-signalr-private'
  params: { location: location, name: '${prefix}-signalr-private', tags: tags, targetResourceId: signalr.outputs.resourceId, groupId: 'signalr', subnetId: privateSubnetId, vnetId: vnetId, zoneName: 'privatelink.service.signalr.net', existingZoneResourceId: existingSignalRPrivateDnsZoneResourceId }
}
module openAi 'modules/openai.bicep' = if (ownOpenAi) {
  name: '${prefix}-openai'
  params: { location: location, name: openAiName, tags: tags, apiPrincipalId: identityIds.api.objectId, deploymentName: openAiDeploymentName, modelName: openAiModelName, modelVersion: openAiModelVersion, deploymentSkuName: openAiDeploymentSkuName, deploymentCapacity: openAiDeploymentCapacity }
}
module openAiExisting 'modules/openai-existing.bicep' = if (!ownOpenAi) {
  name: '${prefix}-openai-existing'
  scope: resourceGroup(openAiParts[2], openAiParts[4])
  params: { accountName: openAiParts[8], apiPrincipalId: identityIds.api.objectId }
}
module email 'modules/email.bicep' = if (ownEmail) {
  name: '${prefix}-email'
  params: { location: location, name: emailName, tags: tags, verifiedDomainResourceId: emailDomainResourceId, dataLocation: emailDataLocation }
}
var emailParts = split(ownEmail ? resourceId('Microsoft.Communication/communicationServices', emailName) : existingCommunicationServiceResourceId, '/')
module emailRole 'modules/email-role.bicep' = {
  name: '${prefix}-email-role'
  scope: resourceGroup(emailParts[2], emailParts[4])
  params: { serviceName: emailParts[8], apiPrincipalId: identityIds.api.objectId }
  dependsOn: [email]
}

output registryResourceId string = ownRegistry ? registry!.outputs.resourceId : registryExisting!.outputs.resourceId
output registryLoginServer string = ownRegistry ? registry!.outputs.loginServer : registryExisting!.outputs.loginServer
output managedEnvironmentId string = ownEnvironment ? environment!.outputs.resourceId : existingEnvironment.id
output environmentDefaultDomain string = environmentDefaultDomain
output appNames object = appNames
output identityIds object = identityIds
output sqlServerFqdn string = ownSql ? newSql!.outputs.serverFqdn : sharedSql!.outputs.serverFqdn
output sqlDatabaseName string = sqlDatabaseName
output ingressSubnetCidr string = ingressSubnetCidr
output publicOrigin string = actualPublicOrigin
output keyVaultUri string = keyRing.outputs.vaultUri
output jwtSecretName string = 'taskmanager-jwt-signing'
output runtimeConfiguration object = {
  dataProtection: { provider: 'AzureBlobKeyVault', blobUri: keyRing.outputs.blobUri, keyVaultKeyUri: keyRing.outputs.keyVaultKeyUri }
  serviceBus: { provider: 'AzureServiceBus', fullyQualifiedNamespace: serviceBus.outputs.fullyQualifiedNamespace, queueName: 'TaskNotifications', reminderQueueName: 'TaskReminders', maxDeliveryCount: 3 }
  realtime: { provider: 'AzureSignalR', endpoint: signalr.outputs.endpoint }
  ai: { provider: 'Azure', endpoint: ownOpenAi ? openAi!.outputs.endpoint : openAiExisting!.outputs.endpoint, deployment: openAiDeploymentName }
  email: { provider: 'AzureCommunication', azureCommunicationEndpoint: emailRole.outputs.endpoint, fromAddress: emailFromAddress, domainResourceId: emailDomainResourceId }
  telemetry: { provider: 'AzureMonitor', connectionString: monitoring.outputs.connectionString }
}
output resourceIds object = {
  sqlServer: sqlServerId
  sqlDatabase: sqlDatabaseId
  sqlPrivateEndpoint: sqlPrivate.outputs.endpointId
  signalR: signalr.outputs.resourceId
  signalRPrivateEndpoint: signalrPrivate.outputs.endpointId
  serviceBus: serviceBus.outputs.resourceId
  keyVault: keyRing.outputs.vaultId
  storageAccount: keyRing.outputs.storageId
  keyRingContainer: keyRing.outputs.containerId
  openAi: ownOpenAi ? openAi!.outputs.resourceId : openAiExisting!.outputs.resourceId
  communicationService: emailRole.outputs.resourceId
  logAnalytics: monitoring.outputs.workspaceId
  applicationInsights: monitoring.outputs.insightsId
  actionGroup: monitoring.outputs.actionGroupId
  budget: monitoring.outputs.budgetId
  vnet: vnetId
  privateEndpointSubnet: privateSubnetId
}
