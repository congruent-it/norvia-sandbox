# P8 Shipper visibility

Shipper visibility lets a Shipper see where its Transport Orders stand. The Shipper asks, and the TMS answers straight away through API Management. What the Shipper sees is a Tracking status and the progress so far, not the workings of the Shipment: which Carrier carries it, what went wrong and how the Planner handles it stay inside Norvia (ADR 0011). Terms are defined in [CONTEXT.md](../../../CONTEXT.md).

| | |
|---|---|
| **Trigger** | A Shipper queries its Transport Orders, or one of them |
| **Outcome** | The Shipper sees each Transport Order's Tracking status, progress, ETA and current Proof of delivery, exactly as the TMS holds them at that moment |
| **Owning system** | TMS. It owns Transport Orders, Shipments, Tracking events and Proofs of delivery (ADR 0002). P8 only reads them |
| **Applies to** | Both. The Shipper can't tell Own fleet from a Carrier |
| **Integrations touched** | Partner portal (Shipper Personas), API Management (tracking API and its mapping policy), TMS Shipper tracking API |

## Scope

P8 is **pull only**. The Shipper queries, and nothing is sent to it. A query goes from API Management straight to the TMS, because Logic Apps are never used for read actions (ADR 0003). No Logic App, queue or read model sits in between, so the Shipper always sees what the TMS believes.

- **Shippers** are designed here in full, through their Personas.
- **Consignees** get no tracking view. See [Deferred](#deferred).
- **Telling the Shipper** about a problem stays with [P7](p7-exception-handling.md): the Planner decides what the Shipper hears, through a Shipper notification.

## Tracking status

The Shipper sees a **Tracking status**, not the Shipment status. It is worked out from the Transport Order and its Shipment when the query runs, and it isn't stored.

| Tracking status | When |
|---|---|
| Received | The Transport Order is Accepted and has no Shipment yet |
| Scheduled | The Shipment is Planned, Released, Booked or Dispatched |
| Collected | The Shipment is Collected |
| In transit | The Shipment is In transit |
| Delivered | The Shipment is Delivered |

Scheduled covers everything before collection, so the Shipper can't tell whether Own fleet or a Carrier carries the goods. A Shipment that goes back to Planned after a rejection (P4) or a cancellation (P7) stays Scheduled, so the Tracking status never moves backwards.

Rejected bookings aren't included. They have no Transport Order number, and the Shipper got every reason in the P1 response.

## What the Shipper sees

For each Transport Order:

| Item | Content |
|---|---|
| Transport Order | Transport Order number, Shipper reference, collection and delivery places, collection window |
| Tracking status | See [Tracking status](#tracking-status) |
| ETA | As P5 defines it: the ETA from the last Tracking event that carried one, otherwise the delivery window |
| Delivery window | The agreed delivery window when the Planner has set one (P7), otherwise the Transport Order's |
| Last update | The event time of the latest progress Tracking event. Empty before collection |
| Progress | The progress Tracking events (Collected, Departed, Arrived, Out for delivery, Delivered) in event-time order, each with its type, event time and place (city and country) |
| Proof of delivery | The current one only: received by, delivery time, condition and its files to download |

What the Shipper doesn't see, each with its reason:

- **The Carrier.** Towards the Shipper, Norvia carries the goods, as in P7's Shipper notification.
- **The Shipment number.** The Shipper deals in Transport Orders.
- **Exception Tracking events and Exceptions.** Delivery failed, Damaged and every Exception stay with the Planner, who decides what the Shipper is told (P7). A late Delivery failed after Delivered leaves the Shipper on Delivered.
- **Remarks** on Tracking events. They are the Carrier talking to Norvia.
- **Stale.** It is about the integration's health, not news for the Shipper (P7). The last update shows the silence without calling it anything.
- **Superseded Proofs of delivery.**
- **The Freight Charge.** A displayed price reads as a commitment (P2).

The last update is how a Shipper notices the headline incident: "In transit, last update 4 days ago".

## Steps

1. **Ask.** The presenter plays a Shipper Persona in the Partner portal and opens its tracking screen, or the screen polls (see [Partner portal](#partner-portal)). The portal calls API Management with the Persona's token and subscription key.
2. **Check at the edge.** API Management checks the subscription key and the Entra token (audience "Norvia Partner API", role `Tracking.Read`). A token without the role gets 403. This is how a Carrier Persona gets refused. API Management maps the `azp` claim to a Shipper ID (ADR 0005) and creates a correlation ID.
3. **Query.** API Management calls the TMS Shipper tracking API with its managed identity, passing the Shipper ID and the correlation ID as headers.
4. **Project.** The TMS reads only that Shipper's Transport Orders and returns the Shipper's view of them, as described in [What the Shipper sees](#what-the-shipper-sees), in the Canonical Model.
5. **Map.** An API Management policy maps the Canonical Model to the [Shipper tracking format](#shipper-tracking-format) and returns it.

A Proof of delivery file follows the same path. The TMS streams it and API Management passes it through unchanged.

### When something fails

| Failure | What happens |
|---|---|
| The Transport Order doesn't exist, or belongs to another Shipper | `TransportOrderNotFound`, the same in both cases, so the API doesn't reveal whose it is |
| The Proof of delivery file isn't the current one, or the Transport Order has none | `ProofOfDeliveryNotFound` |
| The TMS is unavailable (server error or timeout) | API Management waits a short time and doesn't retry. It returns a gateway error saying tracking is temporarily unavailable. The portal keeps its last answer, shows when it was fetched, and shows the error |

Unlike P5's write path, a failure here reaches the Shipper at once.

## Queries

| Query | Input | Returns |
|---|---|---|
| List Transport Orders | Optional Tracking status and collection-date range. Paged | Per Transport Order: number, Shipper reference, places, Tracking status, ETA, last update |
| Get a Transport Order | Transport Order number | Everything in [What the Shipper sees](#what-the-shipper-sees) |
| Find a Transport Order | Shipper reference | As "Get a Transport Order". The Shipper reference is unique per Shipper (P1) |
| Download a Proof of delivery file | Transport Order number and file ID | The file, streamed |

The list is sorted by collection date, newest first.

## Shipper tracking format

The Shipper tracking format follows the conventions of the Shipper booking format in P1: JSON with its own field names, flat address fields, and local date/times without an offset. Event times are in the time zone of their place. The ETA and delivery window are in the time zone of the delivery address. The API Management policy maps Canonical → Shipper.

The Tracking statuses map to the Shipper codes `RECEIVED`, `SCHEDULED`, `COLLECTED`, `IN_TRANSIT` and `DELIVERED`. Progress Tracking event types map to the Shipper codes `COLLECTED`, `DEPARTED`, `ARRIVED`, `OUT_FOR_DELIVERY` and `DELIVERED`.

The mapping lives in the policy because no Logic App runs on this path. What the Shipper may see is decided in the TMS. The policy only changes the format.

## Partner portal

Each Shipper Persona gets a **tracking screen**:

- **The list** of its Transport Orders, from "List Transport Orders", with the filters.
- **The detail** of one Transport Order: Tracking status, ETA, delivery window, last update, progress and the Proof of delivery with its files to download.
- **A search** by Transport Order number or Shipper reference.

While the screen is open, it polls the API every few seconds. The interval is a Partner portal setting per Persona. Each poll is a real API Management call, so a Carrier in auto-progress (P5) visibly moves the Shipper's screen along, until something stops it.

- **The interval** is `shipper.trackingPollSeconds` of the Persona in the seed file, at least 1 second, and 5 seconds for a Persona that sets none. A poll starts its wait when the last one finished, so a slow answer never piles polls up.
- **Times** are shown as the API returns them: local to the place they are about, with no conversion in the portal.
- **A Proof of delivery file** downloads through the portal (`/personas/{id}/tracking/{Transport Order number}/proof-of-delivery/files/{file ID}`), which fetches it from API Management as the Persona, so the Persona's token and subscription key never reach the browser.
- **On an error** (the portal can't reach API Management, or it answers with a gateway error, a refusal or an answer in no known format) the screen keeps the last answer, shows when it was fetched and shows the error with Norvia's reference when there is one. A Transport Order the API says isn't there is an answer, not an error.

The notification inbox from P7 stays where it is. The tracking screen doesn't show notifications.

## TMS API

The Shipper tracking API returns only the Canonical Model (ADR 0003).

| Operation | Caller | Role |
|---|---|---|
| List, get and find Transport Orders for a Shipper | API Management's managed identity | `ShipperTracking.Read` |
| Read a current Proof of delivery file for a Shipper | API Management's managed identity | `ShipperTracking.Read` |

Only API Management holds `ShipperTracking.Read`. The TMS trusts the Shipper ID header only from that caller, as it trusts the Shipper from the intake Logic App in P1. Every operation is scoped to that Shipper. The role is separate from the Planner permissions, so a Shipper-scoped call can never reach the Planner's views. P6's "Read a Proof of delivery file" stays the Planner's.

## Rate limiting

The Shipper product in API Management has a rate limit per subscription. Polling Personas stay well inside it.

## Correlation ID

API Management creates a correlation ID for each query and passes it to the TMS as a header. It isn't stored, because a query changes nothing, but it is in the logs, so a slow or failed query can be found in Application Insights.

## Events

P8 publishes no events and consumes none.

## Deferred

These were left out on purpose while designing P8, each with its reason.

- **Pushing progress to the Shipper**, including telling it a Shipment is booked (P4). Pull shows the headline incident as it happened: the Shipper's screen stays on In transit. Push would add a second outbound Logic App path next to P7's Shipper notifications. When it is built, it can consume the Shipment events of P3 to P7.
- **Consignee tracking.** It needs another identity model, such as a link with a tracking code and no token, and a new kind of Persona.
- **Exceptions and exception Tracking events.** The Planner decides what the Shipper hears (P7, ADR 0011).
- **Goods going back to the Shipper.** P7's `ReturnedToShipper` doesn't change the Shipment, so the Shipper stays on its last Tracking status. It needs return legs, deferred with several Shipments per Transport Order (ADR 0007). The Shipper hears through a P7 notification.
- **A tracking read model or response caching.** Reading the TMS directly keeps the Shipper's view equal to the TMS's. Caching comes back only as an as-found toggle.
- **The Freight Charge.** As in P2.
- **Fulfilment Transport Orders.** They appear once the WMS handoff exists (P1), with no change to P8.
- **As-found toggles and presenter-panel faults.** They are added after the fixed state is built, as for P1 to P7. P8 needs no fault of its own to re-enact the headline incident: P5's mapping fault already leaves the Shipper's screen on In transit. The candidates:
  - Toggle: API Management response caching with a long lifetime. The TMS is right but the Shipper still sees an old Tracking status, which is a look-alike of the headline incident with a different root cause.
  - Presenter-panel fault: the TMS Shipper tracking API fails for a few minutes. The Shipper sees the error straight away, which contrasts with P5's silent failure.
  - Toggle: no rate limit on the Shipper product, so polling Personas can load the TMS.
