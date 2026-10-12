param registryName string
param principalIds array
@allowed(['RbacRegistryPermissions', 'AbacRepositoryPermissions'])
param roleAssignmentMode string

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = { name: registryName }
// Existing ACR mode is discovered by preflight and is never changed here.
resource pulls 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principalId in principalIds: {
  scope: registry
  name: guid(registry.id, principalId, 'TaskManagerPull', roleAssignmentMode)
  properties: union({
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleAssignmentMode == 'AbacRepositoryPermissions'
      ? 'b93aa761-3e63-49ed-ac28-beffa264f7ac' : '7f951dda-4ed3-4680-a7ca-43fe172d538d')
  }, roleAssignmentMode == 'AbacRepositoryPermissions' ? {
    conditionVersion: '2.0'
    condition: '((!(ActionMatches{\'Microsoft.ContainerRegistry/registries/repositories/content/read\'}) AND !(ActionMatches{\'Microsoft.ContainerRegistry/registries/repositories/metadata/read\'})) OR (@Request[Microsoft.ContainerRegistry/registries/repositories:name] StringStartsWithIgnoreCase \'taskmanager/\'))'
  } : {})
}]
output resourceId string = registry.id
output loginServer string = registry.properties.loginServer
