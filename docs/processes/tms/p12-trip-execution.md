# P12 Trip execution

Trip execution is the Driver working a Dispatched Trip on the road. In the Driver app the Driver starts the Trip, completes each Stop (collected, delivered with a Proof of delivery, or failed) and ends the Trip. The Driver app publishes each report as an event. A Logic App processes one Trip's reports in order and hands them to the TMS, which records them as Tracking events (P5), Proofs of delivery (P6) and changes to the Trip. The Logic App then tells the Driver app whether each report was recorded. Terms are defined in [GLOSSARY.md](../../../GLOSSARY.md).

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

The Driver app signs Drivers in with Entra ID, replacing P11's Driver picker. Each seeded Driver has an Entra test user whose token carries the Driver ID from the ERP, as an app role ([ADR 0037](../../adr/0037-drivers-sign-in-to-the-driver-app-with-entra-and-the-driver-id-is-their-app-role.md)). The Driver app shows only that Driver's Trips and puts the Driver ID from the token on every event. The TMS checks it against the Trip's Driver. This follows ADR 0005: identity comes from the token, not the payload.

The presenter panel holds the seeded users' sign-in details for demos. Locally the Driver app's sign-in page stands in for Entra and lists the seeded Drivers.

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

Who holds each role, and what else each identity may reach, is in [Identity and access](../../architecture/identity-and-access.md).

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

For engineers and coding agents. It shows where the built part of P12 lives in the repository. It follows the [P11 section](p11-trip-dispatch.md#implementation).

### Built so far

Start Trip at the TMS boundary: `POST /v1/trips/{number}/start` with the `driverId` from the Driver's token in the body and the Driver's `X-Correlation-ID`, role `Trips.Start` (the Driver reports Logic App's, defined in [identities.bicep](../../../infra/estate/identities.bicep)). The body may also carry the `driverAppEventId`. A Dispatched Trip on its day, in Norvia's time, becomes In progress at the same version, and in the same transaction exactly one `TripStarted` is written to the outbox for the `trip-events` topic under that correlation ID. It is refused with `TripNotFound` (404, also when the Driver isn't the Trip's), `TripNotDispatched` (409, a Draft or a Trip that is In progress already) or `NotTripDay` (409), and a refusal changes nothing. A second Start is `TripNotDispatched`, unless it repeats the Driver app event ID that started the Trip (`Trip.StartedByEventId`, migration `TripStartedByEventId`): that is answered `200` with `repeat: true` and changes and publishes nothing, so the Logic App can retry a Start whose answer it never got. From Start on, every Amend but [taking off an uncollected Shipment](p11-trip-dispatch.md#taking-a-shipment-off-a-trip) is refused with `TripUnderWay`. The Trips view and the Trip detail show the status. The Flow is `trip-execution`: the call logs `correlationId`, `flow`, `tripNumber` and the `outcome` with the `failureCode` of a refusal.

| Part | Where |
|---|---|
| Rules and the status change | `Trip.EnsureCanStart` and `Trip.Start` in [Trip.cs](../../../src/Tms/Norvia.Tms.Domain/Trips/Trip.cs), raising `TripStartedEvent` |
| Command, handler, `TripStarted` | [StartTripHandler.cs](../../../src/Tms/Norvia.Tms.Application/Trips/Start/StartTripHandler.cs) |
| Endpoint | `StartAsync` in [TripEndpoints.cs](../../../src/Tms/Norvia.Tms.Api/Trips/TripEndpoints.cs) |
| Schema and samples | [trip-started.v1.schema.json](../../../contracts/canonical-model/trip-started.v1.schema.json), [samples/trip-started](../../../contracts/canonical-model/samples/trip-started/amended-before-start.json) |
| Boundary tests | [TripStartTests.cs](../../../tests/Tms/Norvia.Tms.Api.IntegrationTests/Trips/TripStartTests.cs), with the controllable clock of P10 |

Own fleet Stop reports at the TMS boundary: `POST /v1/tracking-events` (role `TrackingEvents.Record`, which the Driver reports Logic App will hold next to `Trips.Start`; no identity holds it for Own fleet yet) takes an event whose source is `{ "type": "OwnFleet", "driverId": … }`, with the Driver ID from the Driver's token, and whose `tripNumber`, `shipmentNumber` and `tripVersion` replace the Carrier booking reference. The `X-Correlation-ID` is the Driver's action trail, which the Shipment events and the Exception it opens carry. A Driver reports Collected, CollectionFailed, Delivered, DeliveryFailed and Damaged, and nothing else (`UnknownEventType`). Collected and Delivered move the Shipment Dispatched, Collected, Delivered through P5; the other three change no status and open the matching P7 Exception in the same transaction, and Delivered goes through the Billing check as it does for a Carrier. The Driver report rules are refused with `TripNotFound` (404, also for another Driver's Trip), `TripNotUnderWay`, `ShipmentNotOnTrip` (against the Trip's latest version, even when the report names an older one), `StopAlreadyCompleted` (Report damage is exempt), `CollectionBeforeDelivery` (409) and `EventTimeInTheFuture` (400). A repeated Driver app event ID returns the original result with `repeat: true` on the original trail and publishes nothing, whatever has happened on the Trip since. The Flow is `trip-execution`: the call logs `flow`, `correlationId`, `tripNumber`, `shipmentNumber` and the Driver app event ID as `sourceMessageId`, and the Shipment events carry the `tripNumber` as an application property and in the body (`tripNumber`, additive in the four Shipment event schemas).

A Stop holds no outcome of its own (ADR 0007): it is read from the Tracking events the Trip recorded, Collected or Collection failed for a collection Stop, Delivered or Delivery failed for a delivery Stop. Trips shows `Stops done` once the Trip is In progress, Trip detail shows each Stop's outcome, time and remark, and Shipments in progress shows the Trip number next to Own fleet.

| Part | Where |
|---|---|
| Rules | `TrackingEventRules.CheckOwnFleetReport` in [TrackingEventRules.cs](../../../src/Tms/Norvia.Tms.Domain/TrackingEvents/TrackingEventRules.cs), with the new codes in [TrackingEventRefusalCode.cs](../../../src/Tms/Norvia.Tms.Domain/TrackingEvents/TrackingEventRefusalCode.cs) |
| What an Own fleet report names | [OwnFleetReport.cs](../../../src/Tms/Norvia.Tms.Domain/TrackingEvents/OwnFleetReport.cs), carried by [TrackingReport.cs](../../../src/Tms/Norvia.Tms.Domain/TrackingEvents/TrackingReport.cs) and kept on [TrackingEvent.cs](../../../src/Tms/Norvia.Tms.Domain/TrackingEvents/TrackingEvent.cs) (migration `TrackingEventOwnFleetTrip`) |
| Stop outcomes | [StopOutcome.cs](../../../src/Tms/Norvia.Tms.Domain/Trips/StopOutcome.cs), [StopOutcomes.cs](../../../src/Tms/Norvia.Tms.Domain/Trips/StopOutcomes.cs), [TrackingEventsOfTripSpec.cs](../../../src/Tms/Norvia.Tms.Domain/TrackingEvents/TrackingEventsOfTripSpec.cs) and, for the views, [TripOutcomes.cs](../../../src/Tms/Norvia.Tms.Application/Trips/TripOutcomes.cs) |
| Recording | `HandleOwnFleetAsync` and the part it shares with a Carrier's event in [RecordTrackingHandler.cs](../../../src/Tms/Norvia.Tms.Application/TrackingEvents/Record/RecordTrackingHandler.cs) |
| Endpoint and request | [TrackingEventEndpoints.cs](../../../src/Tms/Norvia.Tms.Api/TrackingEvents/TrackingEventEndpoints.cs), [TrackingEventRequest.cs](../../../src/Tms/Norvia.Tms.Api/TrackingEvents/TrackingEventRequest.cs) |
| Schemas | [tracking-event.v1.schema.json](../../../contracts/canonical-model/tracking-event.v1.schema.json) and the `tripNumber` of [shipment-collected](../../../contracts/canonical-model/shipment-collected.v1.schema.json), [shipment-in-transit](../../../contracts/canonical-model/shipment-in-transit.v1.schema.json), [shipment-delivered](../../../contracts/canonical-model/shipment-delivered.v1.schema.json) and [shipment-exception-reported](../../../contracts/canonical-model/shipment-exception-reported.v1.schema.json) |
| Boundary tests | [TripStopReportTests.cs](../../../tests/Tms/Norvia.Tms.Api.IntegrationTests/Trips/TripStopReportTests.cs) |

An Own fleet Proof of delivery at the TMS boundary: `POST /v1/proofs-of-delivery` (role `ProofsOfDelivery.Record`, which the Driver reports Logic App will hold next to `TrackingEvents.Record`; no identity holds it for Own fleet yet) takes the same multipart call as a Carrier's, whose metadata names the source `{ "type": "OwnFleet", "driverId": … }`, with the Driver ID from the Driver's token, and whose `tripNumber`, `shipmentNumber` and `tripVersion` replace the Carrier booking reference. P6's content and file rules are unchanged. The `X-Correlation-ID` is the one of the Delivered Tracking event, so the two reports share a trail. The Proof of delivery is recorded as the Shipment's current one and, as in P6, makes a Delivered Shipment's Transport Order Billable (P9) when it completes the conditions; one with reservations opens a Reservations Exception (P7). The Driver report rules are refused with `TripNotFound` (404, also for another Driver's Trip), `TripNotUnderWay`, `ShipmentNotOnTrip` (against the Trip's latest version, even when the report names an older one) and `CollectionBeforeDelivery` (409), and `DeliveryTimeInTheFuture` (400). `StopAlreadyCompleted` doesn't apply: the Delivered Tracking event completes the Stop, and a Proof of delivery can follow it or, with a new document ID, replace the current one. A repeated document ID returns the original result with `isRepeat: true` and publishes nothing, whatever has happened on the Trip since. `ProofOfDeliveryReceived` carries the `tripNumber` (additive: an application property and a body field, null for a Carrier's), and the call logs `flow` `trip-execution`, `tripNumber`, `shipmentNumber` and the document ID as `sourceMessageId`.

| Part | Where |
|---|---|
| Rules | `ProofOfDeliveryRules.CheckOwnFleetReport` in [ProofOfDeliveryRules.cs](../../../src/Tms/Norvia.Tms.Domain/ProofsOfDelivery/ProofOfDeliveryRules.cs), with the new codes in [ProofOfDeliveryRefusalCode.cs](../../../src/Tms/Norvia.Tms.Domain/ProofsOfDelivery/ProofOfDeliveryRefusalCode.cs) |
| What an Own fleet report names | [OwnFleetReport.cs](../../../src/Tms/Norvia.Tms.Domain/TrackingEvents/OwnFleetReport.cs), carried by [ProofOfDeliveryReport.cs](../../../src/Tms/Norvia.Tms.Domain/ProofsOfDelivery/ProofOfDeliveryReport.cs) and kept on [ProofOfDelivery.cs](../../../src/Tms/Norvia.Tms.Domain/ProofsOfDelivery/ProofOfDelivery.cs) (migration `ProofOfDeliveryOwnFleetTrip`) |
| Recording | `FindByTripAsync` and the part it shares with a Carrier's in [RecordProofOfDeliveryHandler.cs](../../../src/Tms/Norvia.Tms.Application/ProofsOfDelivery/Record/RecordProofOfDeliveryHandler.cs) |
| Endpoint and request | [ProofOfDeliveryEndpoints.cs](../../../src/Tms/Norvia.Tms.Api/ProofsOfDelivery/ProofOfDeliveryEndpoints.cs), [ProofOfDeliveryRequest.cs](../../../src/Tms/Norvia.Tms.Api/ProofsOfDelivery/ProofOfDeliveryRequest.cs), [ProofOfDeliveryReader.cs](../../../src/Tms/Norvia.Tms.Api/ProofsOfDelivery/ProofOfDeliveryReader.cs) |
| Schemas and samples | [proof-of-delivery.v1.schema.json](../../../contracts/canonical-model/proof-of-delivery.v1.schema.json) with [samples/proof-of-delivery](../../../contracts/canonical-model/samples/proof-of-delivery/carrier.json), and the `tripNumber` of [proof-of-delivery-received.v1.schema.json](../../../contracts/canonical-model/proof-of-delivery-received.v1.schema.json) |
| Boundary tests | [TripStopReportTests.cs](../../../tests/Tms/Norvia.Tms.Api.IntegrationTests/Trips/TripStopReportTests.cs) |

End Trip at the TMS boundary: `POST /v1/trips/{number}/end` with the `driverId` from the Driver's token in the body and the Driver's `X-Correlation-ID`, role `Trips.End` (the Driver reports Logic App's, which is not built yet; the app role is defined in [identities.bicep](../../../infra/estate/identities.bicep) and no identity holds it). An In progress Trip whose Stops all have an outcome becomes Completed, at the same version, and in the same transaction exactly one `TripCompleted` is written to the outbox for the `trip-events` topic under that correlation ID, listing the Shipments delivered, not delivered and not collected. The delivery Stop of a Shipment whose collection failed needs no outcome, because a Driver can't attempt it (`CollectionBeforeDelivery`), so it doesn't keep the Trip open. In the same transaction an uncollected Shipment goes back to Planned and a collected one that wasn't delivered is flagged Awaiting re-delivery (`AwaitingRedelivery` on the Shipment, shown in the Shipment's `awaitingRedelivery`), keeping its status. Both leave the Trip, whose Stops keep only the delivered Shipments', and no `ShipmentTakenOffTrip` is published. P5's Stale rule is suspended while the flag is set (`ShipmentStaleRules.IsStale` takes it), and Late delivery (P7) works from the Shipment's status and delivery window, so it still applies. It is refused with `TripNotFound` (404, also when the Driver isn't the Trip's), `TripNotUnderWay` (409, a Dispatched Trip or one that has ended) or `StopsOpen` (409), and a refusal changes nothing. A Completed Trip can't be changed by the Planner (`TripCompleted`, 409). The Flow is `trip-execution`, logged as for Start Trip. Putting a Re-delivery on a later Trip is not built yet.

| Part | Where |
|---|---|
| Rules and the status change | `Trip.EnsureCanEnd` and `Trip.End` in [Trip.cs](../../../src/Tms/Norvia.Tms.Domain/Trips/Trip.cs), raising [TripCompletedEvent.cs](../../../src/Tms/Norvia.Tms.Domain/Trips/TripCompletedEvent.cs); which Stops are open and what became of each Shipment in [StopOutcomes.cs](../../../src/Tms/Norvia.Tms.Domain/Trips/StopOutcomes.cs) |
| The flag | `Shipment.AwaitRedelivery` and `AwaitingRedelivery` in [Shipment.cs](../../../src/Tms/Norvia.Tms.Domain/Shipments/Shipment.cs) (migration `ShipmentAwaitingRedelivery`), and [ShipmentStaleRules.cs](../../../src/Tms/Norvia.Tms.Domain/Shipments/ShipmentStaleRules.cs) |
| Command, handler, `TripCompleted` | [EndTripHandler.cs](../../../src/Tms/Norvia.Tms.Application/Trips/Complete/EndTripHandler.cs) |
| Endpoint | `EndAsync` in [TripEndpoints.cs](../../../src/Tms/Norvia.Tms.Api/Trips/TripEndpoints.cs) |
| Schema and samples | [trip-completed.v1.schema.json](../../../contracts/canonical-model/trip-completed.v1.schema.json), [samples/trip-completed](../../../contracts/canonical-model/samples/trip-completed/all-delivered.json) |
| Boundary tests | [TripEndTests.cs](../../../tests/Tms/Norvia.Tms.Api.IntegrationTests/Trips/TripEndTests.cs) |

Re-delivery on a later Trip is built in P11's calls: Shipments to put on Trips lists a Shipment Awaiting re-delivery with `redelivery` true, a Trip takes it with only its delivery Stop and with `CollectionDayMismatch` not applying, Dispatch clears the flag (`Shipment.Dispatch`), and taking it off sets the flag again, leaving it Collected. The boundary tests are in `TripRedeliveryTests`.

Driver app sign-in ([ADR 0037](../../adr/0037-drivers-sign-in-to-the-driver-app-with-entra-and-the-driver-id-is-their-app-role.md)). In Azure the Driver app's screens are behind Easy Auth, signing in to the *Norvia Driver app* registration, whose app roles are the active seeded Drivers' IDs; `infra/create-driver-test-users.sh` makes a test user for each Driver and assigns the role, and the estate's `ESTATE_DRIVER_SIGN_IN_*` parameters give the presenter panel the domain and the shared password. The Driver app reads the user from `X-MS-CLIENT-PRINCIPAL`, turns the role into the `driverId` claim, and `GetDriverTripsQuery` takes the Driver ID from that claim only. Locally `LocalSignIn__Enabled` (set by the AppHost, refused outside Development) turns on a sign-in page that lists the seeded Drivers and signs one in with a cookie holding the same claim. My Trips shows the Driver's Trips by day, each with its status (a Trip the Driver app holds is Dispatched until the Driver can start it). The Driver ID goes on every event the Driver app publishes, taken from that claim.

Start Trip through the reporting path ([ADR 0016](../../adr/0016-driver-reports-travel-as-driver-app-events-in-order-per-trip.md), [ADR 0038](../../adr/0038-locally-a-hosted-worker-plays-the-p12-driver-reports-logic-app.md)): the Trip screen offers Start Trip for a Trip the Driver app holds as Dispatched, on its day in Norvia's time, with no Start Trip waiting for an answer or recorded. Pressing it posts to `/trips/{number}/start` (antiforgery-checked, the Driver ID from the token, never the form). The Driver app saves a `DriverReport` (state **Sending**) and, in the same transaction, an outbox message for `TripStartReported` (a new correlation ID, the Driver app event ID, the Trip number, the version held, the Driver ID, the event time), which its outbox publisher sends to the `driver-app-events` topic with the Trip number as the session ID. Pressing it again while the report is Sending or Recorded publishes nothing; after a refusal or a failure it reports again. The Driver reports Logic App reads the session-enabled `driver-reports` subscription, calls the TMS Start a Trip operation with the Driver ID, the event ID and the correlation ID, retrying a timeout, 408, 429 and a server error with the bounded exponential policy (3 retries, 5 to 20 seconds), settles the message first (completed when the TMS recorded it, dead-lettered with `DeadLetterReason` the failure code and `DeadLetterErrorDescription` the message when the TMS refused it or stayed down) and then posts the outcome to the Driver app's `POST /v1/report-outcomes` (role `DriverReports.ReportOutcome`): **Recorded**, **Refused** with the TMS's code and message, or **Not recorded** (`TmsUnavailable` with the cause). The Driver app changes the report's state, once: an outcome for a report that has one is acknowledged and ignored. When the Logic App can't reach the Driver app the report stays Sending. The Trip shows In progress once its Start is Recorded, and its reports with their states on the Trip card; the page looks again every five seconds while one is Sending. Locally `LocalDriverReportsWorker` plays the Logic App.

| Part | Where |
|---|---|
| Report, its states and queries | [DriverReport.cs](../../../src/DriverApp/Norvia.DriverApp.Domain/Reports/DriverReport.cs), [ReportsOfTripsSpecification.cs](../../../src/DriverApp/Norvia.DriverApp.Domain/Reports/ReportsOfTripsSpecification.cs), [ActiveStartOfTripSpecification.cs](../../../src/DriverApp/Norvia.DriverApp.Domain/Reports/ActiveStartOfTripSpecification.cs) |
| Start Trip and the outcome, `TripStartReported` | [StartTripHandler.cs](../../../src/DriverApp/Norvia.DriverApp.Application/Reports/StartTrip/StartTripHandler.cs), [ReportOutcomeHandler.cs](../../../src/DriverApp/Norvia.DriverApp.Application/Reports/ReportOutcome/ReportOutcomeHandler.cs), [TripStartReportedV1.cs](../../../src/DriverApp/Norvia.DriverApp.Application/Events/TripStartReportedV1.cs) |
| Outbox and publisher | [OutboxPublisher.cs](../../../src/DriverApp/Norvia.DriverApp.Infrastructure/Outbox/OutboxPublisher.cs), [OutboxBuffer.cs](../../../src/DriverApp/Norvia.DriverApp.Infrastructure/Outbox/OutboxBuffer.cs) (migration `DriverReportsAndOutbox`) |
| Outcome endpoint | [ReportOutcomeEndpoints.cs](../../../src/DriverApp/Norvia.DriverApp.Api/Reports/ReportOutcomeEndpoints.cs) |
| Start Trip on the Trip screen | [ReportEndpoints.cs](../../../src/DriverApp/Norvia.DriverApp.Web/Reports/ReportEndpoints.cs), [TripCard.razor](../../../src/DriverApp/Norvia.DriverApp.Web/Components/TripCard.razor), [ReportItem.razor](../../../src/DriverApp/Norvia.DriverApp.Web/Components/ReportItem.razor) |
| The Logic App | [driver-reports/workflow.json](../../../src/Integration/DriverReportsLogicApp/driver-reports/workflow.json), [driver-reports-logic-app.bicep](../../../infra/estate/driver-reports-logic-app.bicep), [deploy-driver-reports-logic-app.sh](../../../infra/deploy-driver-reports-logic-app.sh) |
| Locally | [DriverReportsFlow.cs](../../../src/Integration/Norvia.Integration.LocalDriverReportsWorker/DriverReportsFlow.cs) |
| Estate | the topic, session subscription and its filter in [messaging.bicep](../../../infra/estate/messaging.bicep); the Logic App's identity, `Trips.Start` and the Driver app API's registration with `DriverReports.ReportOutcome` in [identities.bicep](../../../infra/estate/identities.bicep) |
| Schemas and samples | [trip-start-reported.v1.schema.json](../../../contracts/canonical-model/trip-start-reported.v1.schema.json), [driver-report-outcome.v1.schema.json](../../../contracts/canonical-model/driver-report-outcome.v1.schema.json), [sample](../../../contracts/canonical-model/samples/trip-start-reported/driver-starts-the-trip.json) |
| Boundary tests | [ReportingPathTests.cs](../../../tests/DriverApp/Norvia.DriverApp.Api.IntegrationTests/ReportingPathTests.cs) (Driver app, real database and Service Bus), [TripStartTests.cs](../../../tests/Tms/Norvia.Tms.Api.IntegrationTests/Trips/TripStartTests.cs) (TMS), [DriverReportsFlowTests.cs](../../../tests/Integration/Norvia.Integration.LocalDriverReportsWorker.UnitTests/DriverReportsFlowTests.cs) and [DriverReportsWorkflowTests.cs](../../../tests/Integration/Norvia.Integration.CarrierBooking.UnitTests/DriverReportsLogicApp/DriverReportsWorkflowTests.cs) (the Logic App) |

Only Start Trip goes through the reporting path so far; the Stop reports, the Proof of delivery and End Trip still arrive at the TMS only as direct calls, and join the path next.

| Part | Where |
|---|---|
| Both ways of signing in, the policy | [Drivers](../../../src/DriverApp/Norvia.DriverApp.Web/Drivers/DriverClaims.cs): `DriverSignIn`, `EasyAuthHandler`, `DriverClaims` |
| My Trips and the refusal | [Trips.razor](../../../src/DriverApp/Norvia.DriverApp.Web/Components/Pages/Trips.razor), [Refused.razor](../../../src/DriverApp/Norvia.DriverApp.Web/Components/Pages/Refused.razor) |
| The app registration and its roles | `driverApp` in [identities.bicep](../../../infra/estate/identities.bicep), [create-driver-test-users.sh](../../../infra/create-driver-test-users.sh), [register-sign-in-callbacks.sh](../../../infra/register-sign-in-callbacks.sh) |
| The Easy Auth gate and the AppHost's settings | [AppHost.cs](../../../src/Norvia.AppHost/AppHost.cs), [Estate.cs](../../../src/Norvia.AppHost/Estate.cs) |
| The presenter panel | [PresenterPanel.razor](../../../src/PartnerPortal/Norvia.PartnerPortal.Web/Presenter/PresenterPanel.razor), [DriverSignIns](../../../src/PartnerPortal/Norvia.PartnerPortal.Application/DriverSignIns/DriverSignInView.cs) |
| Boundary tests | [SignInTests.cs](../../../tests/DriverApp/Norvia.DriverApp.Web.IntegrationTests/SignInTests.cs) |

Not built yet: the Driver app's actions and the Driver reports Logic App.

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
