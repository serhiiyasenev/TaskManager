targetScope = 'resourceGroup'
param location string = resourceGroup().location
@description('Flattened, validated foundation deployment outputs; never secret values.')
param foundationOutputs object
@minLength(71)
@maxLength(71)
param migrationsImageDigest string
@minLength(71)
@maxLength(71)
param sqlBootstrapImageDigest string
@allowed(['Prepare', 'Finalize'])
param sqlBootstrapPhase string = 'Prepare'
var prefix = take(foundationOutputs.appNames.api, length(foundationOutputs.appNames.api) - 4)
var bootstrapIdentity = foundationOutputs.identityIds.sqlBootstrap
var migrationIdentity = foundationOutputs.identityIds.migration
var userMappings = [
  { name: foundationOutputs.identityIds.api.name, objectId: foundationOutputs.identityIds.api.objectId, clientId: foundationOutputs.identityIds.api.clientId, role: 'Api' }
  { name: foundationOutputs.identityIds.notifier.name, objectId: foundationOutputs.identityIds.notifier.objectId, clientId: foundationOutputs.identityIds.notifier.clientId, role: 'Notifier' }
  { name: migrationIdentity.name, objectId: migrationIdentity.objectId, clientId: migrationIdentity.clientId, role: 'Migration' }
]
var bootstrapConnection = 'Server=tcp:${foundationOutputs.sqlServerFqdn},1433;Database=${foundationOutputs.sqlDatabaseName};Authentication=Active Directory Managed Identity;User Id=${bootstrapIdentity.clientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
var migrationConnection = 'Server=tcp:${foundationOutputs.sqlServerFqdn},1433;Database=${foundationOutputs.sqlDatabaseName};Authentication=Active Directory Managed Identity;User Id=${migrationIdentity.clientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
module bootstrap 'modules/manual-job.bicep' = {
  name: '${prefix}-bootstrap-job'
  params: {
    location: location
    name: '${prefix}-sql-bootstrap'
    environmentId: foundationOutputs.managedEnvironmentId
    registryLoginServer: foundationOutputs.registryLoginServer
    identityResourceId: bootstrapIdentity.resourceId
    image: '${foundationOutputs.registryLoginServer}/taskmanager/sql-bootstrap@${sqlBootstrapImageDigest}'
    environmentVariables: [
      { name: 'DOTNET_ENVIRONMENT', value: 'Production' }
      { name: 'ConnectionStrings__DbConnection', value: bootstrapConnection }
      { name: 'SqlBootstrap__UsersJson', value: string(userMappings) }
      { name: 'SqlBootstrap__Phase', value: sqlBootstrapPhase }
    ]
  }
}
module migration 'modules/manual-job.bicep' = {
  name: '${prefix}-migration-job'
  params: {
    location: location
    name: '${prefix}-migrations'
    environmentId: foundationOutputs.managedEnvironmentId
    registryLoginServer: foundationOutputs.registryLoginServer
    identityResourceId: migrationIdentity.resourceId
    image: '${foundationOutputs.registryLoginServer}/taskmanager/migrations@${migrationsImageDigest}'
    environmentVariables: [
      { name: 'DOTNET_ENVIRONMENT', value: 'Production' }
      { name: 'ConnectionStrings__DbConnection', value: migrationConnection }
    ]
  }
}
output sqlBootstrapJobName string = bootstrap.outputs.jobName
output migrationJobName string = migration.outputs.jobName
