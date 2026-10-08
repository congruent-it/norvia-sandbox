# P12 Trip execution

Trip execution is the Driver working a Dispatched Trip on the road. In the Driver app the Driver starts the Trip, completes each Stop (collected, delivered with a Proof of delivery, or failed) and ends the Trip. The Driver app publishes each report as an event. A Logic App processes one Trip's reports in order and hands them to the TMS, which records them as Tracking events (P5), Proofs of delivery (P6) and changes to the Trip. The Logic App then tells the Driver app whether each report was recorded. Terms are defined in [CONTEXT.md](../../../CONTEXT.md).

| | |
|---|---|
| **Trigger** | A Driver starts a Dispatched Trip in the Driver app on the Trip's day |
| **Outcome** | A Completed Trip. Each Shipment on it is Delivered, has an Exception open, is Awaiting re-delivery, or is back to Planned |
| **Owning system** | TMS. It owns Trips, Shipments, Tracking events and Proofs of delivery (ADR 0002). The Driver app owns nothing and holds reports only until they are recorded |
| **Applies to** | Own fleet only |
| **Integrations touched** | Driver app (outbox, topic for Driver app events, transit container, outcome endpoint), Service Bus (Driver app events with sessions, Trip events, Shipment events), Driver reports Logic App, TMS Trip API, TMS Tracking event API, TMS Proof of delivery API, Entra ID |

## What P12 decides, and what it doesn't

P12 decides **what a Driver reports and how it reaches the TMS**: starting and ending a Trip, the outcome of each Stop, and the Proof of delivery captured at a delivery. It also decides what happens to Shipments that didn't finish on their Trip.

It doesn't change the tracking model. Tracking event types, Shipment statuses, ETA and Stale are [P5](p5-shipment-tracking.md)'s, and the Proof of delivery model is [P6](p6-proof-of-delivery.md)'s. P12 is their Own fleet source. It doesn't decide what to do about an Exception, which stays with [P7](p7-exception-handling.md). It doesn't build or change Trips, which stays with [P11](p11-trip-dispatch.md).

## The Driver's actions

| Action | Where | Sends |
|---|---|---|
| **Start Trip** | A Dispatched Trip, on its day | `TripStartReported` |
| **Collected** | A collection Stop | `StopCompletedReported` with a Collected Tracking event |
| **Collection failed** | A collection Stop, with a remark | `StopCompletedReported` with a Collection failed Tracking event |
| **Deliver** | A delivery Stop whose collection is done. Opens the [Proof of delivery form](#capturing-a-proof-of-delivery) | `StopCompletedReported` with a Delivered Tracking event, and `ProofOfDeliveryCaptured` |
| **Delivery failed** | A delivery Stop whose collection is done, with a remark | `StopCompletedReported` with a Delivery failed Tracking event |
| **Report damage** | Any Stop, with a remark | `StopCompletedReported` with a Damaged Tracking event. It doesn't give the Stop an outcome |
| **End Trip** | An In progress Trip whose Stops all have an outcome | `TripEndReported` |

The Driver **completes a Stop** by giving it an outcome: Collected or Collection failed for a collection Stop, and Delivered or Delivery failed for a delivery Stop. An outcome is final for that Trip. A second attempt at the same Stop happens on a later Trip (see [Re-delivery](#re-delivery)).

The Driver app lists the Stops in the Planner's sequence, but the Driver can complete them in any order. The only restriction is that a delivery Stop waits for its collection Stop.

The Driver app sends no Departed, Arrived or Out for delivery, and no ETA. The Driver app has no planned times per Stop (P11), and the TMS doesn't calculate an ETA (P5). The ETA of an Own fleet Shipment is therefore its delivery window. The location of each report is the Stop's address. The Driver app doesn't read GPS.

### Capturing a Proof of delivery

A delivery Stop can't be completed as Delivered without a Proof of delivery. The form asks for:

| Part | Required |
|---|---|
| Received by | Yes |
| Condition: clean, or with reservations | Yes |
| Remark | Only with reservations |
| Signature, drawn on screen, stored as a PNG | Yes |
| Photos, JPEG or PNG | 0 to 4 |

That gives 1 to 5 files, within [P6's rules](p6-proof-of-delivery.md#proof-of-delivery-rules). The delivery time is when the Driver confirms the form, and the place is the Stop's address. The Driver app gives each Proof of delivery a unique document ID.

The Delivered Tracking event and the Proof of delivery stay two reports to two TMS operations, as P5 and P6 keep them apart. They share one correlation ID. Because a Proof of delivery is required, "Proof of delivery missing" (P6) should almost never flag an Own fleet Shipment. When it does, a report went wrong in the integration layer.

## Status

### Trip

P12 adds two statuses to P11's.

| Status | Meaning |
|---|---|
| In progress | The Driver started it. From now on the only Amend allowed is taking off a Shipment that isn't collected ([P11](p11-trip-dispatch.md#amending-a-dispatched-trip)) |
| Completed | The Driver ended it. Final |

A Trip moves Draft → Dispatched → In progress → Completed.

### Stop

Each Stop on a Trip has an outcome, recorded by the TMS from the Driver's report:

| Stop | Outcomes |
|---|---|
| Collection | Open, Collected, Collection failed |
| Delivery | Open, Delivered, Delivery failed |

The outcome belongs to the Stop on this Trip. A Stop is still derived from its Shipment (ADR 0007), and its outcome is read from the Tracking events that Trip recorded.

### Shipment

P12 adds no Shipment status. An Own fleet Shipment moves Released → Dispatched → Collected → Delivered through P5, driven by the Driver's Tracking events. A Collection failed, Delivery failed or Damaged Tracking event never changes the status (P5). In the same transaction [P7](p7-exception-handling.md) opens an Exception for it.

At End Trip the TMS deals with each Shipment that didn't finish, in the same transaction:

| The Shipment was | It becomes |
|---|---|
| Not collected (Collection failed) | **Planned** again, off the Trip, as when the Planner takes it off (P11). In P3 the Planner can choose Own fleet or a Carrier again |
| Collected, not delivered (Delivery failed) | **Awaiting re-delivery**, off the Trip, keeping its status (see [Re-delivery](#re-delivery)) |

Neither publishes `ShipmentTakenOffTrip`, because `TripCompleted` lists both.

## Re-delivery

A Shipment that was collected but not delivered is still on Norvia's Vehicle when the Trip ends. It is re-delivered on a later Trip.

**Awaiting re-delivery** is a flag, not a status, like Stale. The Shipment keeps the status it has (Collected), because it is still true: Norvia holds the goods. The flag means:

- The Shipment appears in P11's *Shipments to put on Trips*, marked **Re-delivery**.
- On a Trip it has **only its delivery Stop**. Its collection is done.
- P11's `ShipmentNotReleased` accepts it, and `CollectionDayMismatch` doesn't apply to it.
- P5's Stale rule for a Collected or In transit Shipment that has gone quiet doesn't apply while the flag is set. Late delivery (P7) already covers the wait.

The flag clears when the Planner Dispatches a Trip that carries the Shipment. The Shipment stays Collected until its Delivered Tracking event. When the Planner takes it off a Trip again, the flag comes back. It can't go back to Planned, because the goods are already collected.

Handing a Re-delivery to a Carrier is [deferred](#deferred).

## Steps

1. **Sign in.** The Driver signs in to the Driver app with Entra ID (see [Sign-in](#sign-in)) and sees their Trips.
2. **Start.** On the Trip's day the Driver starts a Dispatched Trip. The Driver app records the report and publishes `TripStartReported` through its outbox to the topic for Driver app events.
3. **Work the Stops.** At each Stop the Driver completes it, and at a delivery captures the Proof of delivery. Each action is published the same way. A Proof of delivery's files go to the Driver app's transit container first, and the event carries their blob references (the claim check of ADR 0009).
4. **Process in order.** The Driver reports Logic App reads its topic subscription, which uses sessions keyed by Trip number. It handles one Trip's reports one at a time, in the order they were published, and calls the matching [TMS operation](#tms-api) for each.
5. **Record.** The TMS checks the [rules](#driver-report-rules), then records the change and publishes its events in one transaction:
   - a Start Trip moves the Trip to In progress
   - a Stop report records the Tracking event (P5) and the Stop's outcome
   - a Proof of delivery is recorded as in P6
   - an End Trip moves the Trip to Completed and deals with the unfinished Shipments
6. **Report back.** The Logic App calls the Driver app with the outcome: Recorded, or Refused with the TMS's code and message. For a Proof of delivery that was recorded, it then deletes the files from the transit container.
7. **End.** Once every Stop has an outcome, the Driver ends the Trip, through the same path.

### What the Driver sees

Each report in the Driver app has a state:

| State | Meaning |
|---|---|
| Sending | Published, not yet reported by the Logic App |
| Recorded | The TMS accepted it |
| Refused | The TMS refused it, with its failure code and message |
| Not recorded | The Logic App gave up after its retries |

This is the deliberate contrast with Carriers, who never learn whether a Tracking event or Proof of delivery arrived (ADR 0008, ADR 0009). The Driver app is Norvia's own, so the Driver is told (ADR 0016).

### When something fails

| Failure | What happens |
|---|---|
| The TMS refuses a report (client error) | The Logic App dead-letters the message with the TMS's failure code and message, as in P5, and reports it Refused to the Driver app. No retry |
| The TMS is unavailable (server error or timeout) | The Logic App retries the call, and later reports for that Trip wait behind it in the session. When its retries are used up, it dead-letters the message, reports it Not recorded, and the session moves on |
| A Proof of delivery's files are gone from the transit container | The Logic App run fails, and the message is dead-lettered and reported Not recorded |
| The Driver app can't reach the Logic App to report an outcome | The report stays Sending in the Driver app. The TMS has it anyway |

A report after one that failed still goes to the TMS, and the TMS's rules decide it. For example, when a Start Trip is dead-lettered, every Stop report for that Trip is refused with `TripNotUnderWay` until the Start is replayed from the dead-letter queue.

### Taking a Shipment off while the Driver collects it

The Planner may take an uncollected Shipment off an In progress Trip (P11) while the Driver is collecting it, before the new version reaches the Driver app. The Collected report then arrives for a Shipment that is no longer on the Trip.

- The TMS refuses it with `ShipmentNotOnTrip`. The Shipment stays Planned.
- The Driver app shows the Stop as Refused, with the TMS's message.
- When the new version arrives, the Driver app keeps any Stop the Driver has already completed, marked **Taken off by the Planner, call the Planner**. It doesn't silently remove it.

The Planner and the Driver sort it out by phone. The Planner then re-plans the Shipment in P3. The TMS itself doesn't see the refused report (see [Deferred](#deferred)).

## Driver report rules

The Driver ID comes from the Driver's token, never from the Driver app's payload (see [Sign-in](#sign-in)).

| Rule | Applies to | Failure code |
|---|---|---|
| The Trip exists and its Driver is the Driver from the token | All | `TripNotFound` |
| The Trip is Dispatched | Start Trip | `TripNotDispatched` |
| Today is the Trip's day | Start Trip | `NotTripDay` |
| The Trip is In progress | Stop reports, Proofs of delivery, End Trip | `TripNotUnderWay` |
| The Shipment is on the Trip's latest version | Stop reports, Proofs of delivery | `ShipmentNotOnTrip` |
| The Stop has no outcome yet. Report damage is exempt | Stop reports | `StopAlreadyCompleted` |
| The Shipment's collection Stop is Collected, or the Shipment is a Re-delivery | Delivery Stop reports, Proofs of delivery | `CollectionBeforeDelivery` |
| Every Stop has an outcome | End Trip | `StopsOpen` |
| The event time isn't in the future, beyond a small clock-skew allowance | All | Client error |

The version a report names is stored for tracing only. A report records something that has already happened, so it doesn't have to name the latest version. The Shipment only has to be on the latest version.

A Proof of delivery is also checked against [P6's rules](p6-proof-of-delivery.md#proof-of-delivery-rules) for content and files. For an Own fleet source, the rules above replace P5's and P6's rules about Carrier bookings.

A report that repeats a Driver app event ID already recorded succeeds, returns the original result flagged as a repeat, and publishes no event, as in P5 and P6.

Each failure has a stable code and a readable message.

## Driver app event format

The Driver app is Norvia's own application, so it publishes in the Canonical Model, as it already receives the Trip dispatch in it (P11). The Logic App doesn't map formats. It routes by event type.

Every event carries the Driver app event ID, the Trip number, the version the Driver app held, the Driver ID from the token, the event time with its offset, and the correlation ID.

| Event | Also carries | TMS operation |
|---|---|---|
| `TripStartReported` | — | Start a Trip |
| `StopCompletedReported` | Shipment number, Stop (collection or delivery), Tracking event type, location, remark | Record a Tracking event, with Own fleet as the source |
| `ProofOfDeliveryCaptured` | Shipment number, document ID, received by, delivery time, location, condition, remark, and for each file its name, content type and blob reference | Record a Proof of delivery, with Own fleet as the source. The Logic App fetches the files and makes P6's multipart call |
| `TripEndReported` | — | End a Trip |

The events are named *Reported* and *Captured*, not *Started* and *Completed*: the Driver app reports, and the TMS decides.

## Sign-in

The Driver app signs Drivers in with Entra ID, replacing P11's Driver picker. Each seeded Driver has an Entra test user with a claim that carries the Driver ID from the ERP. The Driver app shows only that Driver's Trips and puts the Driver ID from the token on every event. The TMS checks it against the Trip's Driver. This follows ADR 0005: identity comes from the token, not the payload.

The presenter panel holds the seeded users' sign-in details for demos.

## Driver app

P12 completes the Driver app that P11 started.

| Screen | Shows | Actions |
|---|---|---|
| My Trips | The Driver's Trips by day, with status | Open a Trip |
| Trip | The Stops in sequence, each with its outcome and report state. A banner when a new version arrives | Start Trip on its day, End Trip once every Stop has an outcome |
| Stop | The Shipment, Shipper, address, window and goods lines, and for a delivery the Consignee | Collected, Collection failed, Report damage. For a delivery: Deliver (the Proof of delivery form) and Delivery failed |

A new version from P11 replaces the Trip as before (ADR 0015). It keeps the outcomes and report states of Stops already completed, and a completed Stop for a Shipment taken off is kept, marked as [above](#taking-a-shipment-off-while-the-driver-collects-it).

The Driver app gains:

- an **outbox** that publishes its events to the topic for Driver app events, so a report is never lost between recording it and publishing it
- a **transit container** for Proof of delivery files. The Driver reports Logic App deletes each file once the TMS has accepted its Proof of delivery, and a lifecycle rule deletes anything left after 90 days, the integration layer's retention (`integrationRetentionDays`)
- an **outcome endpoint** that the Driver reports Logic App calls, role `DriverReports.ReportOutcome`

It still reaches the TMS only through Azure Integration Services (ADR 0001).

## Planner's views

P12 adds no views. It extends P11's and P5's.

| View | Adds |
|---|---|
| Trips (P11) | The statuses In progress and Completed, and the count of Stops with an outcome |
| Trip detail (P11) | Each Stop's outcome, time and remark |
| Shipments to put on Trips (P11) | Shipments Awaiting re-delivery, marked Re-delivery, with only their delivery Stop |
| Shipments in progress (P5) | Nothing new. Own fleet Shipments show Own fleet and their Trip number where a Carrier's show the Carrier |

The Shipment's timeline shows the Driver's Tracking events and Proofs of delivery like a Carrier's, with Own fleet as the source.

## TMS API

The TMS APIs accept only the Canonical Model (ADR 0003). The Driver reports Logic App's managed identity holds these roles:

| Operation | API | Role |
|---|---|---|
| Start a Trip | Trip API | `Trips.Start` |
| End a Trip | Trip API | `Trips.End` |
| Record a Tracking event | Tracking event API (P5) | `TrackingEvents.Record` |
| Record a Proof of delivery | Proof of delivery API (P6) | `ProofsOfDelivery.Record` |

For an Own fleet source the canonical Tracking event and Proof of delivery carry the Trip number and the Driver ID in place of a Carrier booking reference. P5's and P6's events carry the Trip number where they carry a Carrier booking reference.

## Correlation ID

The Driver app creates a new correlation ID for each Driver action: Start Trip, completing a Stop, Deliver and End Trip. Deliver's two reports share one. The ID travels on the event, and the Logic App uses it on its call to the TMS and on its report back to the Driver app. The TMS stores it and puts it on the events it publishes.

Tracing a Driver's report follows the Trip number and the Driver app event ID. Tracing one that never reached the TMS starts at the dead-letter queue or the failed Logic App run, which keep both.

## Events

The Trip events go to P11's topic for Trip events, through the TMS's transactional outbox (ADR 0003). The Tracking events and Proofs of delivery publish P5's and P6's Shipment events as usual, and Exceptions publish P7's. A repeat publishes nothing.

| Event | Published when | Carries | Consumers |
|---|---|---|---|
| `TripStarted` | The Trip moves to In progress | Trip number, version, Driver ID, start time, correlation ID | Monitoring |
| `TripCompleted` | The Trip moves to Completed | As `TripStarted`, plus the end time and the Shipments delivered, not delivered (now Awaiting re-delivery) and not collected (now Planned) | Monitoring |

The Driver app's own events (see [Driver app event format](#driver-app-event-format)) go to the topic for Driver app events, and the Driver reports Logic App is their only consumer.

## Demo seed

A Trip Dispatched for **today** for a seeded Driver, with three Shipments, delivered to the Driver app.

The demo:

1. The Driver signs in and starts the Trip. The Planner sees it go In progress.
2. The Driver collects all three Shipments.
3. The Driver delivers the first with a signature and a photo. The Planner sees it Delivered with its Proof of delivery on the timeline, and it becomes Billable ([P9](p9-freight-settlement.md)).
4. The Driver reports the second as Delivery failed. P7 opens an Exception.
5. The Driver delivers the third with reservations. P7 opens a Reservations Exception.
6. The Driver ends the Trip. The second Shipment appears in *Shipments to put on Trips* as a Re-delivery.

## Implementation

Not built yet.

## Deferred

These were left out on purpose while designing P12, each with its reason.

- **Capturing reports offline.** The Driver app assumes a connection, and completing a Stop fails visibly without one. An offline queue in the app brings conflicts with Amends that the demo doesn't need.
- **ETA and GPS location from the Driver app.** There are no planned times per Stop, and the TMS doesn't calculate an ETA (P5). The Stop's address is the location.
- **Departed, Arrived and Out for delivery from Drivers.** Collected and Delivered say what the Planner and the Shipper need.
- **A second attempt at the same Stop on the same Trip.** An outcome is final on its Trip. A Re-delivery goes on a later one.
- **Handing a Re-delivery to a Carrier.** A Carrier booking would have to start after collection, from Norvia's Vehicle.
- **The TMS seeing a refused Driver report**, for example a Collected for a Shipment taken off. The Driver sees it and calls the Planner.
- **Actual driving hours read back from Trips.** P10 plans with planned hours, and tachograph data is deferred there.
- **Handing a Trip over to another Driver mid-run.** Deferred in P11 (ADR 0015).
- **As-found toggles and presenter-panel faults.** They are added after the fixed state is built, as for P1 to P11. The candidates:
  - Presenter-panel fault: the Driver reports Logic App is disabled. The Driver's reports stay Sending, the Planner's Trip never goes In progress, and Stale flags its Shipments.
  - Presenter-panel fault: the TMS is unavailable for a few minutes. The reports wait in their sessions and are processed in order afterwards, so nothing is lost.
  - Toggle: no report back to the Driver app. The Driver is as blind as a Carrier.
  - Toggle: no alert on failed Driver reports Logic App runs, or on the dead-letter queue of its subscription.
  - Toggle: no lifecycle rule on the Driver app's transit container, and processed files aren't deleted.
  - Presenter-panel fault: the Driver app sends a photo as HEIC. The TMS refuses the Proof of delivery with `FileNotAccepted`, and the Driver sees it Refused.
