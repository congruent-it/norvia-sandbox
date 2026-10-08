# P11 Trip dispatch

Trip dispatch brings Released Own fleet Shipments together on Trips and sends each Trip to its Driver. The Planner builds a Trip by hand in the TMS UI: picks its Shipments, puts their Stops in order, and gives it a Vehicle, a Driver, a planned start and a planned driving duration from what P10 says is available. Dispatching it pushes the whole Trip to the Driver app through a Logic App. Terms are defined in [CONTEXT.md](../../../CONTEXT.md).

| | |
|---|---|
| **Trigger** | An Own fleet Shipment is Released (P3) and appears in the Planner's list of Shipments to put on Trips |
| **Outcome** | A Dispatched Trip, delivered to its Driver in the Driver app, and its Shipments Dispatched |
| **Owning system** | TMS. It owns Trips and Shipments (ADR 0002). Availability comes from [P10](p10-fleet-capacity-planning.md), inside the TMS |
| **Applies to** | Own fleet only |
| **Integrations touched** | Service Bus (Trip events and Shipment events), Trip dispatch Logic App, Driver app (Trip endpoint), TMS Trip API |

## What P11 decides, and what it doesn't

P11 decides **which Shipments go on which Trip, in what order, with which Vehicle and Driver**, and gets the Trip to the Driver. It doesn't choose between Own fleet and a Carrier. That stays in [P3](p3-transport-planning.md). It doesn't say what the Own fleet can do on a day. That is [P10](p10-fleet-capacity-planning.md), whose availability rules P11 applies. It doesn't record what happens on the road. Starting and ending a Trip, completing its Stops, and the Tracking events and Proofs of delivery that come from it are [P12](p12-trip-execution.md).

The Planner does P11, with the Planner permissions from P3. A separate dispatcher role is deferred.

## Trips and Stops

A Trip is one run on one day by one Vehicle and one Driver. A Vehicle or Driver has at most one Trip a day, as P10's load check already assumes.

A Trip visits an ordered sequence of **Stops**. Each Shipment on a Trip has exactly two: its collection and its delivery, and the collection always comes before the delivery. A Shipment Awaiting [re-delivery](p12-trip-execution.md#re-delivery) has only its delivery Stop, because its collection is done. A Stop is derived from its Shipment (address, window, goods lines). It isn't a record with a life of its own (ADR 0007). Two Stops at the same address stay two Stops.

The Planner puts the Stops in order by hand. The TMS doesn't suggest a sequence or estimate driving time.

A Trip carries:

| Field | Notes |
|---|---|
| Trip number | Given when the Trip is created |
| Day | An open day in the Planning horizon |
| Vehicle and Driver | From that day's available ones. Both may be empty on a Draft |
| Planned start | A time on the Trip's day, inside the Driver's Shift |
| Planned driving duration | Entered by the Planner. It uses up the Driver's [Driving-hour budget](p10-fleet-capacity-planning.md#driving-hour-budget) |
| Stops | The ordered sequence |
| Version | Starts at 1 on Dispatch and goes up with each Amend (ADR 0015) |

There are no planned times per Stop.

## Status

### Trip

| Status | Meaning |
|---|---|
| Draft | Being built. The Planner can change anything or discard it. A Draft still holds its Vehicle, Driver and driving hours, for the one-Trip-a-day rule and the Driving-hour budget |
| Dispatched | Sent to its Driver. It changes only by Amend |

[P12](p12-trip-execution.md) adds **In progress**, from the Driver's Start Trip, and **Completed**, from End Trip. A Dispatched Trip is never deleted.

A Trip is Dispatched as soon as the Planner dispatches it. Whether its Driver has it is a separate [delivery state](#delivery-to-the-driver-app) on each version.

### Shipment

P11 adds one Shipment status.

| Status | Meaning |
|---|---|
| Dispatched | Its Trip is Dispatched. The Own fleet counterpart of Booked. [P5](p5-shipment-tracking.md) takes it from here |

An Own fleet Shipment moves Planned → Released → Dispatched → Collected. A Shipment on a Draft Trip stays Released. When the Planner takes a Shipment off its Trip, it goes back to **Planned** in the same transaction, as a Carrier Shipment does after a rejection (P4) or a cancellation (P7). Its Tracking status stays Scheduled, since that never moves backwards.

## Steps

1. **See what needs a Trip.** The Planner opens the list of Shipments to put on Trips: Released Own fleet Shipments not yet on a Trip, and Shipments Awaiting re-delivery (P12), sorted by collection time and filtered by day.
2. **Build.** The Planner selects Shipments and creates a new Trip for their collection day, or adds them to an existing Trip that day. An empty Trip can be created too. Each Shipment adds its collection and delivery Stops at the end of the sequence. The Planner drags Stops into order, chooses the Vehicle and Driver from that day's available ones, and enters the planned start and driving duration. Every change is checked against the [Trip rules](#trip-rules) and refused if it breaks one.
3. **Dispatch.** The Planner dispatches the Trip. The TMS re-checks every rule, sets the Trip and its Shipments to Dispatched, and publishes `TripDispatched` with version 1, in one transaction.
4. **Deliver.** The Trip dispatch Logic App picks up `TripDispatched`, sends the Trip to the Driver app for its Driver, and reports back to the TMS whether it was delivered.
5. **Amend, if needed.** The Planner changes the Dispatched Trip. Each Amend is checked, raises the version and publishes `TripDispatched` again with the whole Trip.

### Amending a Dispatched Trip

Until P12 marks the Trip In progress, the Planner can change anything on a Dispatched Trip, under the same rules as a Draft: add or take off Shipments, reorder Stops, change the Vehicle, Driver, planned start or duration. Once it is In progress, the only change allowed is [taking off a Shipment](#taking-a-shipment-off-a-trip) whose collection hasn't happened.

Each Amend sends the whole Trip as a new version, and the latest version replaces whatever the Driver app held (ADR 0015). A Shipment added to a Dispatched Trip becomes Dispatched with that version.

### Taking a Shipment off a Trip

This is how an Own fleet Shipment is re-planned, for example to resolve a [P7](p7-exception-handling.md) Exception when the Consignee can't take the goods until next week. The Planner can take a Shipment off its Trip until its collection Stop is done, that is, until the Shipment is Collected. Its two Stops leave the Trip, the Shipment goes back to Planned, and `ShipmentTakenOffTrip` is published. A Re-delivery can be taken off until it is delivered. Its delivery Stop leaves the Trip and it is Awaiting re-delivery again (P12), since its goods are already collected. In P3 the Planner can then release it again for another Trip or reassign it to a Carrier.

On a Draft this is just an edit. On a Dispatched Trip it is an Amend and sends a new version. After collection a Shipment can't be taken off: a failed delivery goes through P7.

### When availability changes under a Trip

P10 always accepts an Absence, an Off the road day, a Roster change, or a Driver or Vehicle becoming inactive in the ERP, even when a Trip uses that Driver or Vehicle. The TMS then flags the Trip **Needs a Driver** or **Needs a Vehicle**.

The flag is not a status, and nothing happens automatically, like Stale in P5. A flagged Draft can't be dispatched, because Dispatch re-checks the rules. A flagged Dispatched Trip stays with its Driver until the Planner Amends it with another Driver or Vehicle. The flag clears as soon as the rule passes again, for example when the Absence is removed.

## Trip rules

Each failure has a stable code and a readable message, as in P3. Every change to a Trip is refused if it breaks a rule. A Draft may still lack a Vehicle, a Driver, a planned start or a duration, and the rules about them apply once they are set.

From P10 ([Availability rules for Trips](p10-fleet-capacity-planning.md#availability-rules-for-trips)):

| Code | Rule |
|---|---|
| `VehicleNotAvailable` | The Vehicle is active and not Off the road that day, and has no other Trip that day |
| `DriverNotAvailable` | The Driver is active, has a Shift that day and no Absence, and has no other Trip that day |
| `LicenceNotHeld` | The Driver's licence covers the Vehicle type |
| `DrivingHoursExceeded` | The planned driving duration fits in the Driver's Driving-hour budget: daily (with an extension if one is left), weekly and fortnightly. Draft and Dispatched Trips both count |
| `PayloadExceeded` | The total weight of the Trip's Shipments fits the Vehicle's payload |
| `PalletPlacesExceeded` | The Trip's pallets fit the Vehicle's pallet places |
| `DayNotOpen` | The Trip's day is in the Planning horizon |

Added by P11:

| Code | Rule |
|---|---|
| `ShipmentNotReleased` | A Shipment added to a Trip is an Own fleet Shipment and Released, or Awaiting re-delivery (P12) |
| `ShipmentOnAnotherTrip` | A Shipment is on no other Trip |
| `CollectionBeforeDelivery` | Each Shipment's collection Stop comes before its delivery Stop |
| `CollectionDayMismatch` | Each Shipment's collection day, as P10 defines it, is the Trip's day. It doesn't apply to a Re-delivery |
| `OutsideShift` | The planned start is inside the Driver's Shift that day |
| `TripUnderWay` | Once the Trip is In progress (P12), only an uncollected Shipment can be taken off |

The load rules count the whole Trip as loaded at once, as P10's load check does. The TMS doesn't work out the load between Stops.

Dispatch also needs a Vehicle, a Driver, a planned start, a planned driving duration and at least one Shipment (`TripIncomplete`).

One check only **warns**: a Shipment whose delivery window doesn't include the Trip's day. The Planner may deliver early or late by agreement, and P7's Late delivery catches the rest.

## Delivery to the Driver app

Each version of a Trip has a delivery state:

| State | Meaning |
|---|---|
| Sending | Published, not yet reported by the Logic App |
| Delivered to Driver app | The Driver app accepted this version |
| Not delivered | The Logic App gave up after its retries. `TripNotDelivered` is published |

The Planner can **send again** a version that is Not delivered, which publishes `TripDispatched` again with the same version. A report for a version older than the Trip's latest is stored against that version and changes nothing else. So the Planner always sees which version the Driver has.

## Trip dispatch format

`TripDispatched` carries the whole Trip, in the Canonical Model:

| Part | Fields |
|---|---|
| Trip | Trip number, version, day, planned start, planned driving duration |
| Vehicle | ID, registration, Vehicle type |
| Driver | ID, name |
| Each Stop, in order | Sequence number, collection or delivery, Shipment number, Transport Order number, Shipper, address, window, goods lines. For a delivery Stop also the Consignee |
| Tracing | Correlation ID |

A Shipment taken off appears only as a version without its Stops. The Driver app never calls back for more.

## Driver app

P11 builds the smallest Driver app the demo needs. [P12](p12-trip-execution.md#driver-app) adds the rest.

| In P11 | In P12 |
|---|---|
| An API that accepts a Trip version from the Trip dispatch Logic App and keeps the latest per Trip | Completing each Stop, and capturing a Proof of delivery, published as events (ADR 0016) |
| Signing in by picking one of the seeded Drivers, as a Persona is picked in the Partner portal | Sign-in with Entra ID |
| A read-only list of that Driver's Trips, with their Stops in order | Starting and ending a Trip |

The Driver app is its own application and reaches the TMS only through Azure Integration Services (ADR 0001). In P11 nothing flows back from it except the Logic App's delivery report.

## Planner's views

| View | Shows | Actions |
|---|---|---|
| Shipments to put on Trips | Released Own fleet Shipments not on a Trip, and Shipments Awaiting re-delivery, marked Re-delivery (P12): Shipment, Shipper, collection and delivery places and windows, weight and pallets. By collection time, filtered by day | Create a Trip, add to a Trip |
| Trips | One row per Trip per day: status, Vehicle, Driver, Stop count, load against the Vehicle's payload and pallet places, the P10 flag, and the delivery state of the latest version | Open a Trip |
| Trip detail | The Stops in order, the Vehicle, Driver, planned start and duration, rule failures and warnings, and each version with its delivery state | Reorder Stops, set the Vehicle, Driver and times, Dispatch, take off a Shipment, send again, discard a Draft |

The views update without a page refresh, as P3's lists do. P10's capacity view links to flagged Trips.

## TMS API

The Trip API accepts only the Canonical Model (ADR 0003). The Planner's actions are in the TMS UI.

| Operation | Caller | Role |
|---|---|---|
| Report a Trip version as delivered or not delivered | Trip dispatch Logic App | `Trips.ReportDelivery` |

## Correlation ID

A Planner's action starts in the TMS UI, so the TMS creates a new correlation ID for each Dispatch, Amend, send again and Shipment taken off, as in P3. `TripDispatched` carries it, and the Trip dispatch Logic App passes it on to the Driver app and uses it on its report to the TMS.

Tracing a Trip follows the Trip number and version. Tracing a Shipment onto its Trip follows the Shipment number, which every Stop carries.

## Events

Trip events go to a new topic for Trip events, and `ShipmentTakenOffTrip` to the topic for Shipment events, both through the TMS's transactional outbox (ADR 0003). Creating, editing or discarding a Draft publishes nothing. There is no event per Shipment on Dispatch, because `TripDispatched` lists them.

| Event | Published when | Carries | Consumers |
|---|---|---|---|
| `TripDispatched` | The Planner dispatches, Amends or sends a version again | The [whole Trip](#trip-dispatch-format) | Trip dispatch Logic App. Monitoring |
| `TripNotDelivered` | A version is reported not delivered | Trip number, version, Driver ID, technical cause, correlation ID | Monitoring. An alert later |
| `ShipmentTakenOffTrip` | The Planner takes a Shipment off a Trip | Shipment number, Transport Order number, Trip number, the new version (if Dispatched), reason text, correlation ID | Monitoring |

## Demo seed

Released Own fleet Shipments collecting on one day in the first week of the Planning horizon, enough for two Trips: one on a rigid truck and one on a van. One more Shipment that day is too heavy for the van, so adding it shows `PayloadExceeded`.

The demo: the Planner builds a Trip, dispatches it, and it appears in the Driver app for that Driver. The Planner then takes a Shipment off, and the Driver app shows version 2.

As-found: a presenter-panel fault, **Driver app unavailable**, makes the Driver app refuse Trips. The Planner sees Not delivered, and the failed Logic App run shows where it broke. The fault is switched off and the Planner sends it again.

## Implementation

Not built yet.

## Deferred

These were left out on purpose while designing P11, each with its reason.

- **Suggested sequences and route optimisation.** Planning is manual, as in P3 and P10, and the TMS doesn't estimate driving time.
- **Planned times per Stop.** Typing them in would be a routing engine's job done by hand. The planned start and duration are enough for P10's budget.
- **Several Trips per Vehicle or Driver a day, and Trips across midnight.** They need time-overlap rules. P10's load check assumes one Trip per Vehicle a day too.
- **Handing a Trip over to another Driver mid-run.** It needs the two Drivers' parts of the Trip and their driving hours split.
- **The Driver app pulling its Trip** as a fallback to the push. The push with a report back is the hop the demo shows.
- **Breaks and rest periods in the sequence.** The Driving-hour budget covers the driving limits only (P10).
- **Load between Stops.** The whole Trip counts as loaded at once.
- **Depots**, and collection and delivery runs through them. The Own fleet is one pool (P10).
- **A separate dispatcher role.** The Planner does P11. The role can be added without changing the model.
- **Real sign-in for the Driver app.** Picking a seeded Driver covers the demo. [P12](p12-trip-execution.md#sign-in) designs it.
- **As-found toggles** in the Bicep, for example no alert on the Trip dispatch Logic App's failed runs. They are added after the fixed state is built, as for P1 and P2.
