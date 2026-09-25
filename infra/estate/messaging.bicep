// Service Bus, where applications publish what happened (ADR 0003: events out). The apps send with their managed
// identities; there are no keys to use.

param location string
param tags object
param presenterPrincipalId string

resource serviceBus 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: 'sb-norvia-${uniqueString(resourceGroup().id)}'
  location: location
  tags: tags
  sku: {
    // Topics need Standard.
    name: 'Standard'
    tier: 'Standard'
  }
  properties: {
    disableLocalAuth: true
    minimumTlsVersion: '1.2'
  }
}

resource transportOrderEvents 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: serviceBus
  name: 'transport-order-events'
}

// Monitoring is the consumer P1 names for Transport Order events.
resource monitoring 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: transportOrderEvents
  name: 'monitoring'
}

// Every subscription's filter also routes resubmits from the Integration console, so a resubmitted message reaches only
// the subscription it was dead-lettered from (coding guidelines §7). It replaces the $Default rule, which passes all.
resource monitoringFilter 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: monitoring
  name: '$Default'
  properties: {
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: 'resubmitTo IS NULL OR resubmitTo = \'monitoring\''
    }
  }
}

var serviceBusDataReceiver = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0')

resource presenterReadsMessages 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(presenterPrincipalId)) {
  name: guid(serviceBus.id, presenterPrincipalId, serviceBusDataReceiver)
  scope: serviceBus
  properties: {
    principalId: presenterPrincipalId
    roleDefinitionId: serviceBusDataReceiver
    principalType: 'User'
  }
}

output namespaceName string = serviceBus.name
