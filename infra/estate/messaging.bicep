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

@description('The P12 Driver reports Logic App\'s identity, which receives from its own subscription and from nothing else.')
param driverReportsPrincipalId string

@description('The P9 Carrier cost Logic App\'s identity, which receives from its own subscription and from nothing else.')
param carrierCostIdentityName string

@description('The E1 Billing Logic App\'s identity, which receives from its own subscription and from nothing else.')
param billingPrincipalId string

@description('The E1 Shipper invoice Logic App\'s identity, which receives from its own subscription and from nothing else.')
param shipperInvoicePrincipalId string

@description('The E2 Carrier invoice outcome Logic App\'s identity, which receives from its own subscription and from nothing else.')
param carrierInvoiceOutcomePrincipalId string

@description('The Driver app\'s identity, which sends to the Driver app events topic and to nothing else.')
param driverAppPrincipalId string

@description('The Carrier-tracking Logic App\'s identity, which receives from the tracking queue and from nothing else.')
param trackingPrincipalId string

@description('The Carrier Proof of delivery Logic App\'s identity, which receives from the Proof of delivery queue and from nothing else.')
param proofOfDeliveryPrincipalId string

@description('API Management\'s identity, which sends to the tracking queue and the Proof of delivery queue and to nothing else.')
param apiManagementPrincipalId string

@description('The Integration console\'s identity, which counts the dead-lettered messages of every queue and subscription (ADR 0017).')
param integrationConsolePrincipalId string

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
// the subscription it was dead-lettered from (coding guidelines §7). The rule is not named $Default, which Service Bus drops when
// it carries a SQL filter; infra/provision-estate.ps1 deletes the $Default rule, which passes all, after the deployment.
resource monitoringFilter 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: monitoring
  name: 'resubmit-aware'
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
  name: 'resubmit-aware'
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
  name: 'resubmit-aware'
  properties: {
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: '(eventType = \'TripDispatched\' AND resubmitTo IS NULL) OR resubmitTo = \'trip-dispatch\''
    }
  }
}

// The topic for Driver app events (P12). The Driver app publishes what the Driver reports to it through its outbox, with the Trip number as the
// session ID, and the Driver reports Logic App is the only consumer (ADR 0016).
resource driverAppEvents 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: serviceBus
  name: 'driver-app-events'
}

// The Driver reports Logic App's subscription uses sessions, so one Trip's reports are read one at a time and in order. Its filter also routes
// resubmits from the console, like every subscription's (coding guidelines §7). The AppHost's emulator gives the same subscription the same
// condition (ResubmitAwareFilters).
resource driverReports 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: driverAppEvents
  name: 'driver-reports'
  properties: {
    requiresSession: true
    // Long enough for the Logic App's retries of one report, which hold the session.
    lockDuration: 'PT5M'
  }
}

resource driverReportsFilter 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: driverReports
  name: 'resubmit-aware'
  properties: {
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: '((eventType = \'TripStartReported\' OR eventType = \'TripEndReported\' OR eventType = \'StopCompletedReported\' OR eventType = \'ProofOfDeliveryCaptured\') AND resubmitTo IS NULL) OR resubmitTo = \'driver-reports\''
    }
  }
}

// The E1 Billing Logic App's subscription: TransportOrderBillable only, as the message's eventType property says. Its filter also routes
// resubmits from the console, like every subscription's (coding guidelines §7). The AppHost's emulator gives the same subscription the
// same condition (ResubmitAwareFilters).
resource billing 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: transportOrderEvents
  name: 'billing'
  properties: {
    // Long enough for the Logic App's retries of the ERP call, which hold the message's lock.
    lockDuration: 'PT5M'
  }
}

resource billingFilter 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: billing
  name: 'resubmit-aware'
  properties: {
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: '(eventType = \'TransportOrderBillable\' AND resubmitTo IS NULL) OR resubmitTo = \'billing\''
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
  name: 'resubmit-aware'
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
  name: 'resubmit-aware'
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
  name: 'resubmit-aware'
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
  name: 'resubmit-aware'
  properties: {
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: '((eventType = \'CarrierBookingCancelled\' OR eventType = \'CarrierBookingWithdrawn\') AND resubmitTo IS NULL) OR resubmitTo = \'carrier-cancellation\''
    }
  }
}

resource carrierCostIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' existing = {
  name: carrierCostIdentityName
}

// The P9 Carrier cost Logic App's subscription: CarrierCostIncurred only, as the message's eventType property says. Its filter also routes
// resubmits from the console, like every subscription's (coding guidelines §7). The AppHost's emulator gives the same subscription the same
// condition (ResubmitAwareFilters).
resource carrierCost 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: shipmentEvents
  name: 'carrier-cost'
  properties: {
    // Long enough for the Logic App's retries of the ERP call, which hold the message's lock.
    lockDuration: 'PT5M'
  }
}

resource carrierCostFilter 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: carrierCost
  name: 'resubmit-aware'
  properties: {
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: '(eventType = \'CarrierCostIncurred\' AND resubmitTo IS NULL) OR resubmitTo = \'carrier-cost\''
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

// The Integration console's Dead-letter queues page lists every queue and subscription and counts their dead-lettered messages as its
// own identity, never with the operator's token (ADR 0017). Reader finds the entities and their counts. A peek of the oldest
// dead-lettered message, for its age, and a peek of a queue's messages and of one message in full, are data operations and need the receiver
// role; the console peeks and never receives.
var reader = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'acdd72a7-3385-48ef-bd42-f606fba81ae7')

resource integrationConsoleListsTheNamespace 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(serviceBus.id, integrationConsolePrincipalId, reader)
  scope: serviceBus
  properties: {
    principalId: integrationConsolePrincipalId
    roleDefinitionId: reader
    principalType: 'ServicePrincipal'
  }
}

resource integrationConsolePeeksDeadLetters 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(serviceBus.id, integrationConsolePrincipalId, serviceBusDataReceiver)
  scope: serviceBus
  properties: {
    principalId: integrationConsolePrincipalId
    roleDefinitionId: serviceBusDataReceiver
    principalType: 'ServicePrincipal'
  }
}

// Resubmitting a dead-lettered queue message sends a copy to its queue; the receiver role above completes the original once the send
// succeeds. Sending is a data operation of its own, so the console's identity is granted the sender role.
resource integrationConsoleResubmitsDeadLetters 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(serviceBus.id, integrationConsolePrincipalId, serviceBusDataSender)
  scope: serviceBus
  properties: {
    principalId: integrationConsolePrincipalId
    roleDefinitionId: serviceBusDataSender
    principalType: 'ServicePrincipal'
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

// Scoped to the subscription, so the Carrier cost Logic App reads Carrier costs and no other message.
resource carrierCostReceivesItsMessages 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(carrierCost.id, carrierCostIdentity.id, serviceBusDataReceiver)
  scope: carrierCost
  properties: {
    principalId: carrierCostIdentity.properties.principalId
    roleDefinitionId: serviceBusDataReceiver
    principalType: 'ServicePrincipal'
  }
}

// Scoped to the subscription, so the Billing Logic App reads Billable Transport Orders and no other message.
resource billingReceivesItsMessages 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(billing.id, billingPrincipalId, serviceBusDataReceiver)
  scope: billing
  properties: {
    principalId: billingPrincipalId
    roleDefinitionId: serviceBusDataReceiver
    principalType: 'ServicePrincipal'
  }
}

// Scoped to the subscription, so the Driver reports Logic App reads the Drivers' reports and no other message.
resource driverReportsReceivesItsMessages 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(driverReports.id, driverReportsPrincipalId, serviceBusDataReceiver)
  scope: driverReports
  properties: {
    principalId: driverReportsPrincipalId
    roleDefinitionId: serviceBusDataReceiver
    principalType: 'ServicePrincipal'
  }
}

// Scoped to the topic, so the Driver app sends the Drivers' reports and nothing else. Aspire gives its API the same role (WithRoleAssignments).
resource driverAppSendsItsEvents 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(driverAppEvents.id, driverAppPrincipalId, serviceBusDataSender)
  scope: driverAppEvents
  properties: {
    principalId: driverAppPrincipalId
    roleDefinitionId: serviceBusDataSender
    principalType: 'ServicePrincipal'
  }
}

// Scoped to the subscription: the Driver app peeks the Drivers' reports subscription, without taking a message from it, so the Simulator's Reset
// estate can wait until the Driver reports Logic App has caught up (ADR 0039). The TMS API's own identity is Aspire's and gets receive rights there.
resource driverAppPeeksItsEvents 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(driverReports.id, driverAppPrincipalId, serviceBusDataReceiver)
  scope: driverReports
  properties: {
    principalId: driverAppPrincipalId
    roleDefinitionId: serviceBusDataReceiver
    principalType: 'ServicePrincipal'
  }
}

// The topic for the ERP's invoicing events (E1). The ERP publishes through its outbox; its identity is Aspire's and gets the sender role on the topic
// (WithRoleAssignments). Monitoring is the first consumer, then the Shipper invoice Logic App with a subscription of its own.
resource invoicingEvents 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: serviceBus
  name: 'invoicing-events'
}

resource invoicingMonitoring 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: invoicingEvents
  name: 'monitoring'
}

resource invoicingMonitoringFilter 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: invoicingMonitoring
  name: 'resubmit-aware'
  properties: {
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: 'resubmitTo IS NULL OR resubmitTo = \'monitoring\''
    }
  }
}

// The E1 Shipper invoice Logic App's subscription: ShipperInvoiceIssued and CreditNoteIssued only, as the message's eventType property says. Its filter also
// routes resubmits from the console, like every subscription's (coding guidelines §7). The AppHost's emulator gives the same subscription
// the same condition (ResubmitAwareFilters).
resource shipperInvoice 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: invoicingEvents
  name: 'shipper-invoice'
}

resource shipperInvoiceFilter 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: shipperInvoice
  name: 'resubmit-aware'
  properties: {
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: '(eventType IN (\'ShipperInvoiceIssued\', \'CreditNoteIssued\') AND resubmitTo IS NULL) OR resubmitTo = \'shipper-invoice\''
    }
  }
}

// Scoped to the subscription, so the Shipper invoice Logic App reads issued Shipper invoices and no other message.
resource shipperInvoiceReceivesItsMessages 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(shipperInvoice.id, shipperInvoicePrincipalId, serviceBusDataReceiver)
  scope: shipperInvoice
  properties: {
    principalId: shipperInvoicePrincipalId
    roleDefinitionId: serviceBusDataReceiver
    principalType: 'ServicePrincipal'
  }
}

// The E2 Carrier invoice outcome Logic App's subscription: CarrierInvoiceApproved only, as the message's eventType property says. Its filter
// also routes resubmits from the console, like every subscription's (coding guidelines §7).
resource carrierInvoiceOutcome 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: invoicingEvents
  name: 'carrier-invoice-outcome'
}

resource carrierInvoiceOutcomeFilter 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: carrierInvoiceOutcome
  name: 'resubmit-aware'
  properties: {
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: '(eventType = \'CarrierInvoiceApproved\' AND resubmitTo IS NULL) OR resubmitTo = \'carrier-invoice-outcome\''
    }
  }
}

// Scoped to the subscription, so the Carrier invoice outcome Logic App reads approved Carrier invoices and no other message.
resource carrierInvoiceOutcomeReceivesItsMessages 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(carrierInvoiceOutcome.id, carrierInvoiceOutcomePrincipalId, serviceBusDataReceiver)
  scope: carrierInvoiceOutcome
  properties: {
    principalId: carrierInvoiceOutcomePrincipalId
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
