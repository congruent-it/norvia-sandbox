# TMS business processes

The catalogue of business processes the TMS supports. It is documented in full so we can plan and build each one later; the first build is a single thin end-to-end slice. Terms are defined in [CONTEXT.md](../../../CONTEXT.md).

Norvia runs a **mixed** operation: its own fleet handles regional and Benelux transport, and subcontracted Carriers handle long-haul, parcel and peak volume. Some processes apply to only one side of that split. The "Applies to" column says which.

| # | Process | Trigger → outcome | Applies to | Main integrations | Status |
|---|---|---|---|---|---|
| P1 | Transport Order intake | Transport Order received (Shipper API, WMS handoff) → accepted or rejected | Both | APIM, WMS | Adopted |
| P2 | Rating | Transport Order accepted → priced per Shipper contract | Both | ERP (contracts) | Adopted |
| P3 | Transport planning | Accepted Transport Orders → consolidated into Shipments and assigned to own fleet or a Carrier | Both | — | Adopted |
| P4 | Carrier booking | Shipment assigned to a Carrier → booking confirmed or rejected | Carrier | Carrier APIs/EDI | Adopted |
| P5 | Shipment tracking | Status events (collected, in transit, delivered) → Shipment status and ETA | Both | Driver app, Carriers, WMS ship events, Service Bus | Adopted |
| P6 | Proof of delivery | Delivery → signed POD attached to Shipment | Both | Driver app, Carriers, storage | Adopted |
| P7 | Exception handling | Delay, failed delivery or damage → re-plan or notify Shipper | Both | Shipper notifications | Adopted |
| P8 | Shipper visibility | Shipper queries → tracking status | Both | APIM tracking API | Adopted |
| P9 | Freight settlement | Delivered → Shipper invoice trigger; Carrier invoice matched against booking | Both (Carrier matching: Carrier only) | ERP | Adopted |
| P10 | Fleet capacity planning | Planning horizon opens → vehicles and drivers available, within driving-hour limits | Own fleet | — | Adopted |
| P11 | Trip dispatch | Shipments assigned to own fleet → sequenced into a Trip and sent to a driver | Own fleet | Driver app | Adopted |
| P12 | Trip execution | Driver works the Trip → collection/delivery scans, status events, POD capture | Own fleet | Driver app → P5, P6 | Adopted |

Each process gets its own document in this folder (trigger, steps, events, owning system, integrations touched) when it is designed in detail.
