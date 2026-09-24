# TMS business processes

The catalogue of business processes the TMS supports. It is documented in full so we can plan and build each one later; the first build is a single thin end-to-end slice. Terms are defined in [CONTEXT.md](../../../CONTEXT.md).

Norvia runs a **mixed** operation: its own fleet handles regional and Benelux transport, and subcontracted Carriers handle long-haul, parcel and peak volume. Some processes apply to only one side of that split. The "Applies to" column says which.

| # | Process | Trigger → outcome | Applies to | Main integrations | Status |
|---|---|---|---|---|---|
| P1 | [Transport Order intake](p1-transport-order-intake.md) | Transport Order received (Shipper API, WMS handoff) → accepted or rejected | Both | APIM, WMS | Adopted |
| P2 | [Rating](p2-rating.md) | Transport Order accepted → Freight Charge calculated from the Shipper's Rate Card | Both | ERP (Rate Cards) | Adopted |
| P3 | [Transport planning](p3-transport-planning.md) | Accepted Transport Order → turned into a Shipment and assigned to own fleet or a Carrier | Both | — | Adopted |
| P4 | [Carrier booking](p4-carrier-booking.md) | Shipment assigned to a Carrier → booking confirmed or rejected | Carrier | Carrier APIs/EDI | Adopted |
| P5 | [Shipment tracking](p5-shipment-tracking.md) | Tracking events (collected, in transit, delivered, exceptions) → Shipment status and ETA | Both | Carriers via APIM and a tracking queue, Service Bus. Driver app via P12. WMS ship events deferred | Adopted |
| P6 | [Proof of delivery](p6-proof-of-delivery.md) | Proof of delivery received → stored as the Shipment's current one, missing ones flagged | Both | Carriers via APIM, a claim-check blob container and a Proof of delivery queue. Driver app via P12 | Adopted |
| P7 | [Exception handling](p7-exception-handling.md) | Failed delivery, damage, reservations or Late delivery → Exception resolved, after re-planning or notifying the Shipper | Both (cancelling a Carrier booking: Carrier only) | Shipper notification and Carrier cancellation Logic Apps, Partner portal endpoints, Service Bus | Adopted |
| P8 | [Shipper visibility](p8-shipper-visibility.md) | Shipper queries → Tracking status, progress, ETA and Proof of delivery, read straight from the TMS | Both | APIM tracking API and its mapping policy, TMS Shipper tracking API | Adopted |
| P9 | [Freight settlement](p9-freight-settlement.md) | Delivered, Rated, with a Proof of delivery and no open Exception → Billable, handed to the ERP for invoicing. Carrier cost priced at release, handed to the ERP for Matching Carrier invoices | Both (Carrier cost: Carrier only) | Billing and Carrier cost Logic Apps, Service Bus, ERP billing API. The ERP side is [E1](../erp/e1-invoicing.md) and [E2](../erp/e2-carrier-invoice-matching.md) | Adopted |
| P10 | [Fleet capacity planning](p10-fleet-capacity-planning.md) | Planning horizon opens → Vehicles and Drivers available per day, within Driving-hour budgets, and days Over capacity flagged | Own fleet | None at runtime. ERP master data through the seed file | Adopted |
| P11 | [Trip dispatch](p11-trip-dispatch.md) | Released Own fleet Shipments → sequenced into a Trip's Stops and dispatched to its Driver | Own fleet | Trip dispatch Logic App, Service Bus, Driver app | Adopted |
| P12 | [Trip execution](p12-trip-execution.md) | Driver starts a Dispatched Trip → Stops completed as Tracking events and Proofs of delivery, Trip Completed, unfinished Shipments back to planning or Awaiting re-delivery | Own fleet | Driver app topic and transit container, Driver reports Logic App with sessions per Trip, Service Bus, TMS Trip, Tracking event and Proof of delivery APIs, Entra ID | Adopted |

Each process gets its own document in this folder (trigger, steps, events, owning system, integrations touched) when it is designed in detail.
