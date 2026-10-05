# P5 Shipment tracking

Shipment tracking records what happens to a Shipment after it has been handed over, and keeps its status and ETA up to date. Carriers, and later Drivers, report Tracking events. The TMS keeps every Tracking event and works out the Shipment's status from them. A Carrier's Tracking events are accepted asynchronously, so when one fails inside the integration layer, only Norvia can notice. Terms are defined in [CONTEXT.md](../../../CONTEXT.md).

| | |
|---|---|
| **Trigger** | A Carrier or Driver reports a Tracking event for a Shipment that is on its way |
| **Outcome** | The Tracking event recorded, the Shipment's status moved forward where the event warrants it, and its ETA updated |
| **Owning system** | TMS. It owns Shipments and their Tracking events (ADR 0002) |
| **Applies to** | Both. This design covers the Carrier source end to end. Own fleet feeds the same model through P12 |
| **Integrations touched** | Partner portal (Carrier Personas), API Management, Service Bus (tracking queue, Shipment events), Carrier-tracking Logic App, TMS Tracking event API |

## Scope

P5 defines the tracking model (Tracking events, Shipment statuses, ETA, Stale) and the Carrier path into it.

- **Carriers** are designed here in full.
- **Own fleet** is designed in [P12](p12-trip-execution.md). The Driver reports Logic App sends a Driver's Tracking events to the same TMS operation, with Own fleet as the source (see [Own fleet](#own-fleet)).
- **WMS ship events** are deferred, like the WMS handoff in P1.

## Tracking events and Shipment status

The TMS keeps every Tracking event it accepts as a log on the Shipment: what happened, when, where, the ETA if one was given, who reported it and a remark. The Shipment status follows from that log, but not every Tracking event changes it. The log keeps what the Carrier said separate from what the TMS concluded from it.

### Tracking event types

| Type | Moves the Shipment to | Kind |
|---|---|---|
| Collected | Collected | Progress |
| Departed | In transit | Progress |
| Arrived | In transit | Progress |
| Out for delivery | In transit | Progress |
| Delivered | Delivered | Progress |
| Collection failed | — | Exception |
| Delivery failed | — | Exception |
| Damaged | — | Exception |

An exception Tracking event never changes the status. It is recorded, and in the same transaction [P7](p7-exception-handling.md) opens an Exception for it, or adds it to an open one of the same type. The Planner decides what happens next.

### Shipment

P5 adds three Shipment statuses after P4's Booked.

| Status | Meaning |
|---|---|
| Collected | The goods have been picked up |
| In transit | The goods are on their way to the delivery address |
| Delivered | The goods have been delivered. Final for P5 |

A Carrier Shipment moves Booked → Collected → In transit → Delivered. An Own fleet Shipment has Dispatched in place of Booked and moves Released → Dispatched ([P11](p11-trip-dispatch.md)) → Collected → Delivered ([P12](p12-trip-execution.md)). Drivers send no Departed, Arrived or Out for delivery, so an Own fleet Shipment is never In transit.

The status **only moves forward**. A Tracking event moves the Shipment to its type's status when that status is further along than the current one. Otherwise it is only logged. So:

- **Steps can be skipped.** A Delivered straight after Booked moves the Shipment to Delivered, because some Carriers send only the final event.
- **Order comes from the event time**, not from when the event arrived. A late Collected after In transit is logged and changes nothing.
- **After Delivered**, Tracking events are still accepted and logged, with no status change. A late Delivery failed after a mistaken Delivered is therefore still on record, and P7 gets its event.

### ETA

The ETA comes from the Tracking event that **arrived last** and carried one. When no Tracking event has given an ETA, the ETA is the delivery window: the agreed delivery window if the Planner has set one (P7), otherwise the Transport Order's. The TMS doesn't calculate an ETA. An ETA after the delivery window opens a Late delivery Exception (P7).

## Steps

1. **Report.** The Carrier sends a Tracking event from its Persona in the Partner portal, by hand or through its tracking mode (see [Partner portal](#partner-portal)). The portal calls API Management with the Persona's token and subscription key.
2. **Accept.** API Management checks the token (audience "Norvia Partner API", role `Tracking.Submit`), maps the `azp` claim to a Carrier ID (ADR 0005) and creates a correlation ID. It puts the message with both on the Service Bus queue for inbound Carrier tracking and returns `202 Accepted`. The Carrier learns nothing more about this Tracking event (ADR 0008).
3. **Map.** The Carrier-tracking Logic App reads the queue. It maps the [Carrier tracking format](#carrier-tracking-format) to the Canonical Model, translating the Carrier's status code through its mapping table. A code that isn't in the table fails the run.
4. **Record.** The Logic App calls the TMS Tracking event API. The TMS checks the [Tracking event rules](#tracking-event-rules), records the Tracking event, moves the Shipment's status and ETA where the event warrants it, and publishes the matching [event](#events), all in one transaction.
5. **Follow up.** The Planner sees the Shipment in the list of Shipments in progress, with its last Tracking event, ETA and Stale flag (see [Planner's list](#planners-list)).

### When something fails

| Failure | What happens |
|---|---|
| The Carrier's status code isn't in the mapping table | The Logic App run fails. The message goes to the tracking queue's dead-letter queue |
| The TMS refuses the Tracking event (client error) | The Logic App dead-letters the message with the TMS's failure code and message. No retry |
| The TMS is unavailable (server error or timeout) | The message is retried from the queue. After the queue's maximum delivery count it goes to the dead-letter queue |

None of these reaches the Carrier. Nothing is lost, but it's only seen if someone watches: alerts on failed Carrier-tracking Logic App runs and on the dead-letter queue on the integration side, and the Stale flag on the business side.

## Carrier tracking format

The Carrier tracking format follows the conventions of the [Carrier booking format](p4-carrier-booking.md#carrier-booking-format): JSON with its own field names, flat address fields, local date/times without an offset, and ISO 3166 alpha-3 country codes. The Carrier-tracking Logic App maps Carrier → Canonical.

A Tracking event carries:

| Field | Required |
|---|---|
| Carrier booking reference | Yes |
| Carrier booking number | No. Used only when the reference doesn't match |
| Carrier's event ID | Yes |
| Status code | Yes |
| Event time, local to the location | Yes |
| Location: place, postcode, country | Yes |
| ETA, with its UTC offset | No |
| Remark | No |

The event time is read in the time zone of the event's location. The Canonical Model carries it with its offset. The ETA is the exception to the local times of the booking format: the message holds no delivery address, so the Carrier sends it with its UTC offset, and it is carried as UTC.

All Carriers use one shared code list:

| Carrier code | Tracking event type |
|---|---|
| `PU` | Collected |
| `DEP` | Departed |
| `ARR` | Arrived |
| `OFD` | Out for delivery |
| `POD` | Delivered |
| `NPU` | Collection failed |
| `NDL` | Delivery failed |
| `DMG` | Damaged |

The mapping table lives in the Carrier-tracking Logic App, not in the TMS. A Carrier that starts sending a new code breaks in the integration layer, where the health check looks.

## Tracking event rules

| Rule | Failure code |
|---|---|
| The Carrier booking reference (or number) is known for the Carrier from the token | `ShipmentNotFound` |
| That Carrier is the Carrier on the Shipment's Confirmed Carrier booking | `ShipmentNotFound` |
| The Shipment is Booked or later | `ShipmentNotInTransport` |
| The event time is not in the future, beyond a small clock-skew allowance | `EventTimeInTheFuture` (client error) |
| The status code maps to a known Tracking event type, and the required fields are present | `UnknownEventType` or `MissingRequiredField` (client error) |

A Carrier's events for one of its old bookings, one that was Rejected, Withdrawn or Cancelled (P7) before the Shipment went to someone else, are refused, not logged. For a Cancelled booking that holds even when the event time is before the cancellation (ADR 0010).

A Tracking event that repeats a Carrier's event ID already recorded for that Carrier succeeds, returns the original result flagged as a repeat, and publishes no event.

Each failure has a stable code and a readable message.

## Stale

A Shipment is **Stale** when its next expected Tracking event hasn't arrived in time. Each threshold is a TMS setting.

| Shipment is | Stale when | Default |
|---|---|---|
| Booked (or Dispatched, for Own fleet) | No Collected, this long after the collection window's latest time | 2 hours |
| Collected or In transit, and not Awaiting re-delivery ([P12](p12-trip-execution.md#re-delivery)) | No Tracking event of any kind for this long | 24 hours |
| Not yet Delivered | This long after the delivery window's latest time. The agreed delivery window counts when there is one (P7) | 2 hours |

Stale is only a flag. Nothing happens automatically, and it clears when a Tracking event arrives that makes the rule no longer true. The same time-based check also opens Late delivery Exceptions (P7), but Stale itself never opens one.

## Planner's list

P5 adds a fourth list to the Planner's three from P3 and P4.

| List | Shows | Actions |
|---|---|---|
| Shipments in progress (P5) | Booked (or Dispatched, for Own fleet), Collected and In transit Shipments: Shipment, Carrier (or Own fleet and the Trip number), status, last Tracking event (type, time, place), ETA and the Stale flag. Stale Shipments first, then by ETA | Open the timeline |

The timeline shows the Shipment's Tracking events in event-time order, with the time each one arrived and whether it moved the status. [P6](p6-proof-of-delivery.md#planners-lists) adds Proofs of delivery to the timeline and a list of Delivered Shipments without one. The list is read-only. A Delivered Shipment leaves it. It updates without a page refresh, as the other lists do.

## Partner portal

Each Carrier Persona gets a list of its Shipments that are Booked, Collected or In transit. Each Shipment offers buttons for the valid next progress Tracking events and for the exception Tracking events. Collection failed (`NPU`) is offered only while the Shipment is Booked.

Next to its answer mode from P4, each Persona has a **tracking mode**, set on its own Carrier page:

| Tracking mode | Behaviour | Default for |
|---|---|---|
| Manual | The presenter sends each Tracking event by hand | Van Dijk Transport |
| Auto-progress | After confirmation, the Persona sends `PU`, `DEP`, `OFD` and `POD` in turn, with a delay between each | Kessler Spedition, PakketPunt Express |

The delay is a Partner portal setting per Persona, a few seconds by default. Automatic Tracking events go through exactly the same path as manual ones. The portal gives each Tracking event a unique event ID and a location from the Shipment's addresses.

## TMS API

The Tracking event API accepts only the Canonical Model (ADR 0003).

| Operation | Caller | Role |
|---|---|---|
| Record a Tracking event | Carrier-tracking Logic App. Driver reports Logic App ([P12](p12-trip-execution.md)) | `TrackingEvents.Record` |

The canonical Tracking event names its source: a Carrier ID, or Own fleet. For a Carrier, the Carrier comes from API Management through the Logic App, never from the payload.

Reading tracking for Shippers belongs to [P8](p8-shipper-visibility.md).

## Own fleet

Own fleet Shipments use the same model: the same Tracking event types, statuses, ETA and Stale rules. [P12](p12-trip-execution.md) adds a second caller of "Record a Tracking event", the Driver reports Logic App, with Own fleet as the source. An Own fleet Tracking event names its Trip and Shipment, and P12's [Driver report rules](p12-trip-execution.md#driver-report-rules) replace the Carrier booking rules above. It reaches the TMS as a Driver app event, in order per Trip, and the Driver is told whether it was recorded (ADR 0016). The events below carry the Trip number where they carry a Carrier booking reference.

## Correlation ID

API Management creates a new correlation ID for each Carrier Tracking event, as it does for each Carrier answer in P4. The TMS stores it on the Tracking event, and the events it publishes carry it.

Tracing a Tracking event follows the Shipment number and the Carrier's event ID. Tracing one that never reached the TMS starts at the dead-letter queue or the failed Logic App run, which keep the correlation ID and the Carrier's event ID.

## Events

All events go to the topic for Shipment events, through the TMS's transactional outbox (ADR 0003). A Tracking event that doesn't change the status and isn't an exception publishes nothing, and neither does an ETA update or a repeat. P8 reads those through its tracking API.

| Event | Published when | Carries | Consumers |
|---|---|---|---|
| `ShipmentCollected` | The Shipment moves to Collected | Shipment number, Transport Order number, Carrier ID or Own fleet, Carrier booking reference, event time, location, ETA, correlation ID | Monitoring |
| `ShipmentInTransit` | The Shipment moves to In transit | As `ShipmentCollected` | Monitoring |
| `ShipmentDelivered` | The Shipment moves to Delivered | As `ShipmentCollected` | Monitoring. [P9](p9-freight-settlement.md) acts inside the TMS in the same transaction, not on this event (ADR 0003) |
| `ShipmentExceptionReported` | An exception Tracking event is recorded | As `ShipmentCollected`, plus the exception type and remark | Monitoring. P7 opens its Exception inside the TMS, not from this event (ADR 0003) |

A Tracking event that skips steps publishes only the event for the status it lands on. For example, Delivered straight after Booked publishes only `ShipmentDelivered`.

Delivered doesn't wait for a Proof of delivery. [P6](p6-proof-of-delivery.md) attaches it separately and never changes the status. Invoicing does wait for it: a Transport Order is Billable only once it has a current Proof of delivery ([P9](p9-freight-settlement.md#conditions), ADR 0012).

## Deferred

These were left out on purpose while designing P5, each with its reason.

- **Polling Carrier tracking APIs.** Push through the Partner portal re-enacts the headline incident as told. Polling can be added as a variant.
- **Per-Carrier status code lists.** One shared list, as P4 has one Carrier booking format.
- **WMS ship events.** The WMS handoff is deferred in P1.
- **The Planner recording a Tracking event by hand**, for example when a Carrier phones in. It would add a third source with its own permissions, and it would cover up exactly the silent failure the demo exists to expose.
- **A TMS-calculated ETA.** It would need routing data the sandbox doesn't have.
- **What to do about an exception.** P5 records Collection failed, Delivery failed and Damaged. Re-planning or notifying the Shipper is [P7](p7-exception-handling.md).
- **Showing the Shipper its progress.** That is [P8](p8-shipper-visibility.md), which also defers pushing progress and Consignee tracking.
- **Driver and vehicle details from the Carrier.** Tracking doesn't need them either.
- **As-found toggles and presenter-panel faults.** They are added after the fixed state is built, as for P1 to P4. The candidates:
  - Presenter-panel fault: a Carrier sends a status code outside the mapping table (for example `DLV` instead of `POD`), so the Carrier-tracking Logic App run fails.
  - Toggle: no alert on failed Carrier-tracking Logic App runs. Together with the fault above, this re-enacts the headline incident: the Carrier posts "delivered", gets `202 Accepted`, and the Shipment stays In transit with no one told.
  - Toggle: no alert on the dead-letter queue of the tracking queue.
  - Presenter-panel fault: the TMS Tracking event API fails for a few minutes. Messages wait on the queue and are retried, so nothing is lost. This contrasts with the fault above.
  - Toggle: the Stale thresholds unset, so the business-side safety net is missing too, and only a Consignee's phone call reveals the problem.
