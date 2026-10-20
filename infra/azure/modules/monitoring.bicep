param location string
param namePrefix string
param tags object
param principalIds array
param monthlyBudget int
@minLength(1)
param budgetContactEmails array
param budgetStartDate string
param sqlDatabaseId string
param serviceBusId string

resource workspace 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: '${namePrefix}-logs'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
    workspaceCapping: { dailyQuotaGb: 1 }
    features: { enableLogAccessUsingOnlyResourcePermissions: true }
  }
}
resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${namePrefix}-insights'
  location: location
  tags: tags
  kind: 'web'
  properties: { Application_Type: 'web', WorkspaceResourceId: workspace.id, DisableLocalAuth: true }
}
resource ingestionRoles 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principalId in principalIds: {
  scope: insights
  name: guid(insights.id, principalId, 'MonitoringMetricsPublisher')
  properties: { principalId: principalId, principalType: 'ServicePrincipal', roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '3913510d-42f4-4e42-8a64-420c390055eb') }
}]
resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: '${namePrefix}-operations'
  location: 'global'
  tags: tags
  properties: {
    groupShortName: 'TaskManager'
    enabled: true
    emailReceivers: [for (email, index) in budgetContactEmails: { name: 'operator-${index}', emailAddress: email, useCommonAlertSchema: true }]
  }
}
resource dlqAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${namePrefix}-notification-dead-letter'
  location: 'global'
  tags: tags
  properties: {
    description: 'Durable notification delivery requires operator review when dead-letter messages remain.'
    severity: 2
    enabled: true
    scopes: [serviceBusId]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    targetResourceType: 'Microsoft.ServiceBus/namespaces'
    targetResourceRegion: location
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [{ name: 'dead-letter', metricName: 'DeadletteredMessages', metricNamespace: 'Microsoft.ServiceBus/namespaces', operator: 'GreaterThan', threshold: 0, timeAggregation: 'Average', criterionType: 'StaticThresholdCriterion' }]
    }
    actions: [{ actionGroupId: actionGroup.id }]
    autoMitigate: true
  }
}
resource sqlAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${namePrefix}-sql-cpu'
  location: 'global'
  tags: tags
  properties: {
    description: 'Sustained database CPU pressure.'
    severity: 2
    enabled: true
    scopes: [sqlDatabaseId]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    targetResourceType: 'Microsoft.Sql/servers/databases'
    targetResourceRegion: location
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [{ name: 'cpu', metricName: 'cpu_percent', metricNamespace: 'Microsoft.Sql/servers/databases', operator: 'GreaterThan', threshold: 80, timeAggregation: 'Average', criterionType: 'StaticThresholdCriterion' }]
    }
    actions: [{ actionGroupId: actionGroup.id }]
    autoMitigate: true
  }
}
// Budget alerts notify operators; they do not cap expenditure or stop resources.
resource budget 'Microsoft.Consumption/budgets@2023-05-01' = {
  name: '${namePrefix}-monthly'
  properties: {
    category: 'Cost'
    amount: monthlyBudget
    timeGrain: 'Monthly'
    timePeriod: { startDate: budgetStartDate }
    notifications: {
      actual80: { enabled: true, operator: 'GreaterThanOrEqualTo', threshold: 80, thresholdType: 'Actual', contactEmails: budgetContactEmails, contactGroups: [actionGroup.id] }
      actual100: { enabled: true, operator: 'GreaterThanOrEqualTo', threshold: 100, thresholdType: 'Actual', contactEmails: budgetContactEmails, contactGroups: [actionGroup.id] }
      forecast100: { enabled: true, operator: 'GreaterThanOrEqualTo', threshold: 100, thresholdType: 'Forecasted', contactEmails: budgetContactEmails, contactGroups: [actionGroup.id] }
    }
  }
}
output workspaceId string = workspace.id
output insightsId string = insights.id
output connectionString string = insights.properties.ConnectionString
output actionGroupId string = actionGroup.id
output budgetId string = budget.id
