# Events: publishers and subscribers

The event catalogue. Every event the estate publishes, with the topic it goes to, who publishes it, the subscriptions that receive it, who reads each subscription, and the process that owns it. This page alone owns publishers and subscribers: a process doc's Events table says what the event carries and links here for who reads it. Terms are defined in [GLOSSARY.md](../../GLOSSARY.md).

An event's body is its [Canonical Model](../../contracts/canonical-model/README.md) schema, linked from the first column. Events go out as Service Bus messages ([ADR 0003](../adr/0003-http-in-events-out.md)); the topics and subscriptions are declared in [messaging.bicep](../../infra/estate/messaging.bicep) for Azure and in the [AppHost](../../src/Norvia.AppHost/AppHost.cs) for the local emulator. Every subscription's filter also passes a message resubmitted from the Integration console to the subscription it was dead-lettered from alone ([coding guidelines §7](../coding-guidelines.md)); the sentences below leave that out.

Subscriptions and subscribers marked *planned* are designed in a process doc but not built yet. The `monitoring` subscription of each topic takes every message.

## Catalogue

| Event | Topic | Publisher | Subscriptions | Subscribers | Process |
|---|---|---|---|---|---|
| [`TransportOrderAccepted`](../../contracts/canonical-model/transport-order-accepted.v1.schema.json) | `transport-order-events` | TMS | `monitoring` (every message) | Monitoring | [P1](../processes/tms/p1-transport-order-intake.md) |
| [`TransportOrderRejected`](../../contracts/canonical-model/transport-order-rejected.v1.schema.json) | `transport-order-events` | TMS | `monitoring` (every message) | Monitoring | [P1](../processes/tms/p1-transport-order-intake.md) |
| [`TransportOrderRated`](../../contracts/canonical-model/transport-order-rated.v1.schema.json) | `transport-order-events` | TMS | `monitoring` (every message) | Monitoring | [P2](../processes/tms/p2-rating.md) |
| [`TransportOrderRatingFailed`](../../contracts/canonical-model/transport-order-rating-failed.v1.schema.json) | `transport-order-events` | TMS | `monitoring` (every message) | Monitoring | [P2](../processes/tms/p2-rating.md) |
| [`TransportOrderBillable`](../../contracts/canonical-model/transport-order-billable.v1.schema.json) | `transport-order-events` | TMS | `monitoring` (every message)<br/>`billing` *planned* (`TransportOrderBillable` only) | Monitoring<br/>Billing Logic App (P9) *planned* | [P9](../processes/tms/p9-freight-settlement.md) |
| [`ShipmentPlanned`](../../contracts/canonical-model/shipment-planned.v1.schema.json) | `shipment-events` | TMS | `monitoring` (every message)<br/>`carrier-booking` (Carrier Shipments only) | Monitoring<br/>P4 booking Logic App | [P3](../processes/tms/p3-transport-planning.md) |
| [`ShipmentReassigned`](../../contracts/canonical-model/shipment-reassigned.v1.schema.json) | `shipment-events` | TMS | `monitoring` (every message)<br/>`carrier-booking` (Carrier Shipments only) | Monitoring<br/>P4 booking Logic App | [P3](../processes/tms/p3-transport-planning.md) |
| [`ShipmentReleased`](../../contracts/canonical-model/shipment-released.v1.schema.json) | `shipment-events` | TMS | `monitoring` (every message)<br/>`carrier-booking` (Carrier Shipments only) | Monitoring<br/>P4 booking Logic App | [P3](../processes/tms/p3-transport-planning.md). Carries a Carrier Shipment's Carrier cost ([P9](../processes/tms/p9-freight-settlement.md#carrier-cost)) |
| [`CarrierBookingConfirmed`](../../contracts/canonical-model/carrier-booking-confirmed.v1.schema.json) | `shipment-events` | TMS | `monitoring` (every message) | Monitoring | [P4](../processes/tms/p4-carrier-booking.md). Carries the total of the Carrier cost ([P9](../processes/tms/p9-freight-settlement.md#carrier-cost)) |
| [`CarrierBookingRejected`](../../contracts/canonical-model/carrier-booking-rejected.v1.schema.json) | `shipment-events` | TMS | `monitoring` (every message) | Monitoring | [P4](../processes/tms/p4-carrier-booking.md) |
| [`CarrierBookingNotDelivered`](../../contracts/canonical-model/carrier-booking-not-delivered.v1.schema.json) | `shipment-events` | TMS | `monitoring` (every message) | Monitoring | [P4](../processes/tms/p4-carrier-booking.md) |
| [`CarrierBookingWithdrawn`](../../contracts/canonical-model/carrier-booking-withdrawn.v1.schema.json) | `shipment-events` | TMS | `monitoring` (every message)<br/>`carrier-cancellation` (`CarrierBookingCancelled` and `CarrierBookingWithdrawn` only) | Monitoring<br/>Carrier cancellation Logic App (P7) | [P4](../processes/tms/p4-carrier-booking.md) |
| [`ShipmentCollected`](../../contracts/canonical-model/shipment-collected.v1.schema.json) | `shipment-events` | TMS | `monitoring` (every message)<br/>`carrier-booking` (Carrier Shipments only) | Monitoring<br/>P4 booking Logic App | [P5](../processes/tms/p5-shipment-tracking.md) |
| [`ShipmentInTransit`](../../contracts/canonical-model/shipment-in-transit.v1.schema.json) | `shipment-events` | TMS | `monitoring` (every message)<br/>`carrier-booking` (Carrier Shipments only) | Monitoring<br/>P4 booking Logic App | [P5](../processes/tms/p5-shipment-tracking.md) |
| [`ShipmentDelivered`](../../contracts/canonical-model/shipment-delivered.v1.schema.json) | `shipment-events` | TMS | `monitoring` (every message)<br/>`carrier-booking` (Carrier Shipments only) | Monitoring<br/>P4 booking Logic App | [P5](../processes/tms/p5-shipment-tracking.md) |
| [`ShipmentExceptionReported`](../../contracts/canonical-model/shipment-exception-reported.v1.schema.json) | `shipment-events` | TMS | `monitoring` (every message)<br/>`carrier-booking` (Carrier Shipments only) | Monitoring<br/>P4 booking Logic App | [P5](../processes/tms/p5-shipment-tracking.md) |
| [`ProofOfDeliveryReceived`](../../contracts/canonical-model/proof-of-delivery-received.v1.schema.json) | `shipment-events` | TMS | `monitoring` (every message)<br/>`carrier-booking` (Carrier Shipments only) | Monitoring<br/>P4 booking Logic App | [P6](../processes/tms/p6-proof-of-delivery.md) |
| [`ExceptionOpened`](../../contracts/canonical-model/exception-opened.v1.schema.json) | `shipment-events` | TMS | `monitoring` (every message)<br/>`carrier-booking` (Carrier Shipments only) | Monitoring<br/>P4 booking Logic App | [P7](../processes/tms/p7-exception-handling.md) |
| [`ExceptionResolved`](../../contracts/canonical-model/exception-resolved.v1.schema.json) | `shipment-events` | TMS | `monitoring` (every message) | Monitoring | [P7](../processes/tms/p7-exception-handling.md) |
| [`ShipperNotificationRequested`](../../contracts/canonical-model/shipper-notification-requested.v1.schema.json) | `shipment-events` | TMS | `monitoring` (every message)<br/>`shipper-notification` (`ShipperNotificationRequested` only) | Monitoring<br/>Shipper notification Logic App (P7) | [P7](../processes/tms/p7-exception-handling.md) |
| [`ShipperNotificationNotDelivered`](../../contracts/canonical-model/shipper-notification-not-delivered.v1.schema.json) | `shipment-events` | TMS | `monitoring` (every message) | Monitoring | [P7](../processes/tms/p7-exception-handling.md) |
| [`CarrierBookingCancelled`](../../contracts/canonical-model/carrier-booking-cancelled.v1.schema.json) | `shipment-events` | TMS | `monitoring` (every message)<br/>`carrier-cancellation` (`CarrierBookingCancelled` and `CarrierBookingWithdrawn` only) | Monitoring<br/>Carrier cancellation Logic App (P7) | [P7](../processes/tms/p7-exception-handling.md) |
| [`CarrierCancellationNotDelivered`](../../contracts/canonical-model/carrier-cancellation-not-delivered.v1.schema.json) | `shipment-events` | TMS | `monitoring` (every message) | Monitoring | [P7](../processes/tms/p7-exception-handling.md) |
| [`ShipmentDeliveryWindowAgreed`](../../contracts/canonical-model/shipment-delivery-window-agreed.v1.schema.json) | `shipment-events` | TMS | `monitoring` (every message) | Monitoring | [P7](../processes/tms/p7-exception-handling.md) |
| [`CarrierCostIncurred`](../../contracts/canonical-model/carrier-cost-incurred.v1.schema.json) | `shipment-events` | TMS | `carrier-cost` *planned* (`CarrierCostIncurred` only) | Carrier cost Logic App (P9) *planned* | [P9](../processes/tms/p9-freight-settlement.md) |
| [`TripDispatched`](../../contracts/canonical-model/trip-dispatched.v1.schema.json) | `trip-events` | TMS | `monitoring` (every message) | Monitoring | [P11](../processes/tms/p11-trip-dispatch.md). Carries the whole Trip with its Stops in order; the Trip dispatch Logic App's subscription comes with that Logic App |

## Reading the table

- The P4 booking Logic App's `carrier-booking` subscription receives every event that carries the Shipment's `assignedTo` property when it is `Carrier`. The Logic App acts on `ShipmentReleased` and ignores the rest.
- The Carrier-tracking and Carrier Proof of delivery Logic Apps read queues, not topics: Carriers' tracking messages and Proof of delivery references are commands that come in, not events that go out ([ADR 0008](../adr/0008-carrier-tracking-events-are-accepted-asynchronously.md), [ADR 0009](../adr/0009-carrier-proofs-of-delivery-use-a-claim-check.md)).
- Events of processes that are only designed so far, with no schema yet, are listed in their process doc: the trip events in [P11](../processes/tms/p11-trip-dispatch.md) and [P12](../processes/tms/p12-trip-execution.md), and the ERP's invoice events in [E1](../processes/erp/e1-invoicing.md) and [E2](../processes/erp/e2-carrier-invoice-matching.md).

## Keeping it true

[EventCatalogueTests](../../tests/Tms/Norvia.Tms.ArchitectureTests/EventCatalogueTests.cs) fails when this page and the estate disagree:

- every event schema in `contracts/canonical-model` has a row;
- every subscription in `messaging.bicep` and in the AppHost has a row;
- no row that is not *planned* names a subscription that does not exist.

Publishers are not checked.
