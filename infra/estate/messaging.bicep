// Service Bus, where applications publish what happened (ADR 0003: events out). The apps send with their managed
// identities; there are no keys to use.

param location string
param tags object
param presenterPrincipalId string

@description('The P4 booking Logic App\'s identity, which receives from its own subscription and from nothing else.')
param bookingPrincipalId string

@description('The P7 Shipper notification Logic App\'s identity, which receives from its own subscription and from nothing else.')
param shipperNotificationPrincipalId string

@description('The P7 Carrier cancellation Logic App\'s identity, which receives from its own subscription and from nothing else.')
param carrierCancellationPrincipalId string

@description('The P11 Trip dispatch Logic App\'s identity, which receives from its own subscription and from nothing else.')
param tripDispatchPrincipalId string

@description('The Carrier-tracking Logic App\'s identity, which receives from the tracking queue and from nothing else.')
param trackingPrincipalId string

@description('The Carrier Proof of delivery Logic App\'s identity, which receives from the Proof of delivery queue and from nothing else.')
param proofOfDeliveryPrincipalId string

@description('API Management\'s identity, which sends to the tracking queue and the Proof of delivery queue and to nothing else.')
param apiManagementPrincipalId string

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

resource tripEvents 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: serviceBus
  name: 'trip-events'
}

// Monitoring is the first consumer of Trip events (P11).
resource tripMonitoring 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: tripEvents
  name: 'monitoring'
}

resource tripMonitoringFilter 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: tripMonitoring
  name: '$Default'
  properties: {
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: 'resubmitTo IS NULL OR resubmitTo = \'monitoring\''
    }
  }
}

// The P11 Trip dispatch Logic App's subscription: TripDispatched only, as the message's eventType property says. Its filter also
// routes resubmits from the console, like every subscription's (coding guidelines §7). The AppHost's emulator gives the same
// subscription the same condition (ResubmitAwareFilters).
resource tripDispatch 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: tripEvents
  name: 'trip-dispatch'
}

resource tripDispatchFilter 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: tripDispatch
  name: '$Default'
  properties: {
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: '(eventType = \'TripDispatched\' AND resubmitTo IS NULL) OR resubmitTo = \'trip-dispatch\''
    }
  }
}

resource shipmentEvents 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: serviceBus
  name: 'shipment-events'
}

// Monitoring is the consumer P3 names for Shipment events.
resource shipmentMonitoring 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: shipmentEvents
  name: 'monitoring'
}

resource shipmentMonitoringFilter 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: shipmentMonitoring
  name: '$Default'
  properties: {
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: 'resubmitTo IS NULL OR resubmitTo = \'monitoring\''
    }
  }
}

// The P4 booking Logic App's subscription: Carrier Shipments only, as the message's assignedTo property says. Its filter
// also routes resubmits from the console, like every subscription's (coding guidelines §7). The AppHost's emulator gives
// the same subscription the same condition (ResubmitAwareFilters).
resource carrierBooking 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: shipmentEvents
  name: 'carrier-booking'
}

resource carrierBookingFilter 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: carrierBooking
  name: '$Default'
  properties: {
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: '(assignedTo = \'Carrier\' AND resubmitTo IS NULL) OR resubmitTo = \'carrier-booking\''
    }
  }
}

// The P7 Shipper notification Logic App's subscription: ShipperNotificationRequested only, as the message's eventType property
// says. Its filter also routes resubmits from the console, like every subscription's (coding guidelines §7). The AppHost's
// emulator gives the same subscription the same condition (ResubmitAwareFilters).
resource shipperNotification 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: shipmentEvents
  name: 'shipper-notification'
}

resource shipperNotificationFilter 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: shipperNotification
  name: '$Default'
  properties: {
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: '(eventType = \'ShipperNotificationRequested\' AND resubmitTo IS NULL) OR resubmitTo = \'shipper-notification\''
    }
  }
}

// The P7 Carrier cancellation Logic App's subscription: CarrierBookingCancelled and CarrierBookingWithdrawn only, as the message's
// eventType property says. Its filter also routes resubmits from the console, like every subscription's (coding guidelines §7). The
// AppHost's emulator gives the same subscription the same condition (ResubmitAwareFilters).
resource carrierCancellation 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: shipmentEvents
  name: 'carrier-cancellation'
}

resource carrierCancellationFilter 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: carrierCancellation
  name: '$Default'
  properties: {
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: '((eventType = \'CarrierBookingCancelled\' OR eventType = \'CarrierBookingWithdrawn\') AND resubmitTo IS NULL) OR resubmitTo = \'carrier-cancellation\''
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

// Scoped to the subscription, so the booking Logic App reads Carrier Shipments and no other message.
resource bookingReceivesItsMessages 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(carrierBooking.id, bookingPrincipalId, serviceBusDataReceiver)
  scope: carrierBooking
  properties: {
    principalId: bookingPrincipalId
    roleDefinitionId: serviceBusDataReceiver
    principalType: 'ServicePrincipal'
  }
}

// Scoped to the subscription, so the Shipper notification Logic App reads notification requests and no other message.
resource shipperNotificationReceivesItsMessages 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(shipperNotification.id, shipperNotificationPrincipalId, serviceBusDataReceiver)
  scope: shipperNotification
  properties: {
    principalId: shipperNotificationPrincipalId
    roleDefinitionId: serviceBusDataReceiver
    principalType: 'ServicePrincipal'
  }
}

// Scoped to the subscription, so the Carrier cancellation Logic App reads cancellations and withdrawals and no other message.
resource carrierCancellationReceivesItsMessages 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(carrierCancellation.id, carrierCancellationPrincipalId, serviceBusDataReceiver)
  scope: carrierCancellation
  properties: {
    principalId: carrierCancellationPrincipalId
    roleDefinitionId: serviceBusDataReceiver
    principalType: 'ServicePrincipal'
  }
}

// Scoped to the subscription, so the Trip dispatch Logic App reads dispatched Trips and no other message.
resource tripDispatchReceivesItsMessages 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(tripDispatch.id, tripDispatchPrincipalId, serviceBusDataReceiver)
  scope: tripDispatch
  properties: {
    principalId: tripDispatchPrincipalId
    roleDefinitionId: serviceBusDataReceiver
    principalType: 'ServicePrincipal'
  }
}

// The queue API Management puts a Carrier's tracking message on and the Carrier-tracking Logic App reads (ADR 0008). A message
// the TMS refuses is dead-lettered by the Logic App with the failure code; one that can't be recorded is delivered again, and
// after the maximum delivery count Service Bus dead-letters it itself. The AppHost's emulator queue has the same count.
resource carrierTracking 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' = {
  parent: serviceBus
  name: 'carrier-tracking'
  properties: {
    maxDeliveryCount: 10
    lockDuration: 'PT1M'
    deadLetteringOnMessageExpiration: true
  }
}

var serviceBusDataSender = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39')

// Scoped to the queue, so API Management sends nothing else and the Logic App reads nothing else.
resource apiManagementSendsTracking 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(carrierTracking.id, apiManagementPrincipalId, serviceBusDataSender)
  scope: carrierTracking
  properties: {
    principalId: apiManagementPrincipalId
    roleDefinitionId: serviceBusDataSender
    principalType: 'ServicePrincipal'
  }
}

resource trackingReceivesItsMessages 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(carrierTracking.id, trackingPrincipalId, serviceBusDataReceiver)
  scope: carrierTracking
  properties: {
    principalId: trackingPrincipalId
    roleDefinitionId: serviceBusDataReceiver
    principalType: 'ServicePrincipal'
  }
}

// The queue API Management puts a Carrier's Proof of delivery reference on and the Carrier Proof of delivery Logic App reads
// (ADR 0009). The message is only the reference to a blob in the inbound container. A message the TMS refuses is dead-lettered
// by the Logic App with the failure code; one that can't be recorded is delivered again, and after the maximum delivery count
// Service Bus dead-letters it itself. The AppHost's emulator queue has the same count. The lock outlasts a call that carries
// files of up to 25 MB.
resource carrierProofOfDelivery 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' = {
  parent: serviceBus
  name: 'carrier-proof-of-delivery'
  properties: {
    maxDeliveryCount: 10
    lockDuration: 'PT5M'
    deadLetteringOnMessageExpiration: true
  }
}

resource apiManagementSendsProofsOfDelivery 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(carrierProofOfDelivery.id, apiManagementPrincipalId, serviceBusDataSender)
  scope: carrierProofOfDelivery
  properties: {
    principalId: apiManagementPrincipalId
    roleDefinitionId: serviceBusDataSender
    principalType: 'ServicePrincipal'
  }
}

resource proofOfDeliveryReceivesItsMessages 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(carrierProofOfDelivery.id, proofOfDeliveryPrincipalId, serviceBusDataReceiver)
  scope: carrierProofOfDelivery
  properties: {
    principalId: proofOfDeliveryPrincipalId
    roleDefinitionId: serviceBusDataReceiver
    principalType: 'ServicePrincipal'
  }
}

output queueName string = carrierTracking.name
output proofOfDeliveryQueueName string = carrierProofOfDelivery.name
output namespaceName string = serviceBus.name
