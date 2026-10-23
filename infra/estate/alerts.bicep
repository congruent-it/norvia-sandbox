// The estate's alert rules. Every Logic App workflow gets a failed-run alert and every queue and subscription a Logic App reads a
// dead-letter alert, except those the as-found toggles list: a "no alert" is a rule that is absent, never one that is disabled
// (docs/operations/as-found.md). Both are metric alerts, which need no query and no workspace: the Logic App's failed runs are a
// platform metric, and so are the dead-lettered messages of a Service Bus entity. A new Logic App or entity without an alert, or an
// exemption in alert-exemptions.json, fails the drift guard (AlertDriftGuardTests).

param tags object

@description('Where an alert is sent.')
param actionGroupId string

@description('The as-found toggles (infra/presets), in one object.')
param asFound object

param serviceBusName string

@description('The estate\'s Logic Apps: the name the toggles and the alert use (the workflow\'s), the Logic App and the workflow in it.')
param flows array

@description('The estate\'s Service Bus queues and subscriptions that a Logic App reads, by the name Service Bus reports them under.')
param entities array

resource serviceBus 'Microsoft.ServiceBus/namespaces@2024-01-01' existing = {
  name: serviceBusName
}

resource logicApps 'Microsoft.Web/sites@2024-11-01' existing = [for flow in flows: {
  name: flow.logicAppName
}]

// Any failed run in the last five minutes, checked every minute.
resource failedRunAlerts 'Microsoft.Insights/metricAlerts@2018-03-01' = [for (flow, i) in flows: if (!contains(asFound.flowsWithoutFailedRunAlert, flow.name)) {
  name: 'alert-failed-runs-${flow.name}'
  location: 'global'
  tags: union(tags, { 'norvia-alert': 'failed-runs', 'norvia-alert-for': flow.name })
  properties: {
    description: 'A run of the ${flow.name} Logic App workflow failed.'
    severity: 2
    enabled: true
    scopes: [logicApps[i].id]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'FailedRuns'
          criterionType: 'StaticThresholdCriterion'
          metricName: 'WorkflowRunsCompleted'
          metricNamespace: 'Microsoft.Web/sites'
          dimensions: [
            { name: 'workflowName', operator: 'Include', values: [flow.workflowName] }
            { name: 'status', operator: 'Include', values: ['Failed'] }
          ]
          operator: 'GreaterThan'
          threshold: 0
          timeAggregation: 'Total'
        }
      ]
    }
    actions: [{ actionGroupId: actionGroupId }]
  }
}]

// Any message in the dead-letter queue of a queue or subscription, checked every minute. The metric is a gauge, so the alert stays until the queue is emptied.
resource deadLetterAlerts 'Microsoft.Insights/metricAlerts@2018-03-01' = [for entity in entities: if (!contains(asFound.entitiesWithoutDeadLetterAlert, entity)) {
  name: 'alert-dead-letter-${entity}'
  location: 'global'
  tags: union(tags, { 'norvia-alert': 'dead-letter', 'norvia-alert-for': entity })
  properties: {
    description: 'The dead-letter queue of ${entity} holds a message.'
    severity: 2
    enabled: true
    scopes: [serviceBus.id]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'DeadLettered'
          criterionType: 'StaticThresholdCriterion'
          metricName: 'DeadletteredMessages'
          metricNamespace: 'Microsoft.ServiceBus/namespaces'
          dimensions: [
            { name: 'EntityName', operator: 'Include', values: [entity] }
          ]
          operator: 'GreaterThan'
          threshold: 0
          timeAggregation: 'Maximum'
        }
      ]
    }
    actions: [{ actionGroupId: actionGroupId }]
  }
}]
