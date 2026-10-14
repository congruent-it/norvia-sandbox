# Simulator

The Simulator feeds the estate with representative orders, shipments and trips at a chosen volume, so demos, the [Integration console](integration-console.md), the Planner's lists and the Shippers' views have something real to show. It acts only through the estate's real doors, so everything it makes has a Message trail ([ADR 0039](../adr/0039-the-simulator-books-only-through-the-estates-real-doors.md)). Terms are defined in [GLOSSARY.md](../../GLOSSARY.md).

| | |
|---|---|
| **Used by** | The presenter, who plays the Simulator operator |
| **Does** | Sends a Simulation plan to the estate. So far: Shipper bookings to the Norvia Partner API, then the Planner's assignments to the TMS API |
| **Owns** | Its runs, their plans and the log of every step, in its own database (`simulator`) |
| **Reaches Norvia through** | The Norvia Partner API, as each Shipper's Persona, and the TMS API, as the Planner. Never a database or Service Bus |

## Scope

The Simulator is built in slices. So far it covers **Shipper bookings** and **Planner assignment**, the first two stages of the funnel. Carrier answers, tracking and Proofs of delivery, Trips and Drivers, and Azure come after them.

## Switching it on

It does nothing unless `Simulator:Enabled` is `true`; switched off it answers every request but its health checks with 404. The AppHost turns it on for a local run (`simulator-web` on the Aspire dashboard). Its guards are settings: `Simulator:MaxOrdersPerRun` (1,000 by default, 500 locally) and `Simulator:MaxConcurrentRequests` (8 by default, 4 locally).

Locally there is no Easy Auth, so the AppHost signs you in as a local operator holding both app roles (`LocalOperator` settings), and the Simulator refuses those settings outside Development. The roles are `Simulator.Run` (configure, start, cancel, clear the history) and `Simulator.Read` (see runs and their logs). Signing in with Entra comes with the Azure slice.

## Screens

### Configure

- A Preset ("Quiet Monday", "Busy day", "Peak week") fills the numbers, which can be changed: the number of orders, the share of **bad bookings**, the share **collected today**, the Planner's shares (**assigned by the Planner**, **to Own fleet where it fits**, **reassigned**), burst or paced (with the window in minutes), and the **seed** (random by default, editable).
- It says the caps and the number the next run gets. A plan over a cap is refused here and again in the preview and when the run starts.
- It lists the runs, newest first, and has **Clear history**, which removes the finished runs and their logs. It doesn't touch what they sent, and the run numbers keep counting, so a Shipper reference is never used twice.

### Plan preview

What the settings would send, per Shipper and per booking: its Shipper reference, the Persona it is sent as, the Lane, when it is collected, its weight, whether it is good or bad, what Norvia is expected to answer and what the Planner means to do with its order. Above the tables, the assignment stage of the funnel: how many of the orders Norvia is expected to accept the Planner assigns (the percentage), how many it means to try Own fleet for and how many go to a Carrier, and how many it reassigns. It is made from the same seed, so **Start run** sends exactly what it shows. It never shows a Consignee.

### Run

The live log of the run, filterable by actor, outcome and a text (a correlation ID, a Shipper reference or a Transport Order number). Each line has the time, actor, action, outcome, the correlation ID Norvia answered with, the Shipper reference, the Transport Order number and, for the Planner's lines, the Shipment number. The filter's text finds any of those. A line's correlation ID can be copied, and **Message trail** opens it in the Integration console's search. The log is read from the database, so it survives a refresh and a second viewer sees the same.

Only one run is active at a time: a second cannot start while one is. **Cancel run** stops sending; the bookings in flight land and are logged. A run is not resumed after a restart: the Simulator fails a run it finds still running when it starts.

## What a run sends

- **Shippers and Personas** come from the seed file: each active Shipper with a Persona and a Rate Card. A booking is sent as the Shipper's Persona, with the token and subscription key the [Partner portal](../processes/tms/p1-transport-order-intake.md) gets for it. Locally the Simulator signs the token itself.
- **Consignees, addresses, goods and dates are generated.** The Lane is chosen from the Shipper's Rate Card, the addresses lie in its Zones (NL, BE and LU), and the goods stay within what a Rate Card can price. Pickups are spread over the next seven days; the configured share is collected today.
- **Shipper references** are `SIM-<run>-<n>`, so a run's orders are recognisable.
- **Bad bookings**, exactly the configured share, in four kinds: **no Lane** (delivered to Germany, where the demo Rate Cards have no Zone: Norvia accepts it and Rating then finds no Lane), a **repeated reference** (an earlier booking sent again: *already booked*), a **conflicting reference** (an earlier reference with other goods: rejected as `ShipperReferenceReused`) and **invalid content** (no Consignee name, no goods or a zero weight: rejected). Infrastructure faults stay with the presenter's existing switches. A repeated or conflicting reference needs an earlier good booking to copy, so a bad booking that comes before any good one is no Lane or invalid content instead. "No Lane" works because the demo Rate Cards have no Zone in Germany; give a Shipper one and the booking would find a Lane.
- **Burst or paced.** Burst sends as fast as the concurrency cap allows. Paced spaces the bookings evenly over the window. Nothing is backdated: the real paths stamp real time.

Each booking is sent in a span of its own, `Send booking`, and its answer is logged with the tracking contract's keys the Simulator knows (`flow`, `partyId`, `correlationId`, `transportOrderNumber`, `outcome`), never what was booked ([coding guidelines §7](../coding-guidelines.md#tracking-contract)).

## What the Planner does

Once every booking is in, the simulated Planner assigns the Transport Orders Norvia **accepted**, through the TMS API only, as the Simulator's own identity (locally a token it signs itself, holding the TMS API's `TransportOrders.Manage` role, as the TMS UI's is). A repeated booking names the order it repeats, which is assigned once, so only an `Accepted` answer counts.

- **Which orders.** The configured share of the orders Norvia is expected to accept, exactly, at positions the seed picks. A booking with no Lane is still an accepted order: Rating's failure never blocks planning.
- **Own fleet or a Carrier.** The Planner asks the TMS API for the Own fleet's open days ([P10's load check](../processes/tms/p10-fleet-capacity-planning.md#the-load-check)) once, and keeps its own tally of what it puts on each day. Of the orders it assigns, the configured share is meant for Own fleet: those go to Own fleet when the order fits the day's room in weight and pallet places, and to an active Carrier when it doesn't. The rest go to an active Carrier. The Carriers come from the seed file, and an inactive one is never chosen. If the TMS can't tell the capacity, the Planner says so and assigns to Carriers only.
- **Reassignments.** The configured share of the assigned orders is reassigned right after: from Own fleet to a Carrier, or from a Carrier to another one (or to Own fleet when it is the only Carrier). Never to who already carries it.
- **One at a time, in plan order.** So the same seed and the same TMS state give the same choices. The run is cancelled between orders; the call in flight lands and is logged.
- **Refusals are outcomes.** A TMS refusal (`TransportOrderAlreadyPlanned`, `InactiveCarrier`, ...) is logged as *Planning refused*, with its code and the correlation ID the TMS traced it on, and the run goes on. A call that doesn't reach the TMS is *Planning not received*. The log keeps the code, never the TMS's message.
- **Retries are safe.** Each call goes under an idempotency key of its own (`SIM-<run>-<n>-assign`, `-reassign`) that every retry of it repeats, so the TMS answers a retry as before.
- **Release is the Planner's other step**, and the Simulator doesn't take it yet: the Shipments stay Planned, on the *Shipments to release* list.

Each call is sent in a span named like the TMS UI's, `Assign Transport Order` or `Reassign Shipment`, and its answer is logged with the tracking keys the Simulator knows (`flow` `transport-planning`, `correlationId`, `transportOrderNumber`, `shipmentNumber`, `outcome`, `failureCode`).

## Presenter smoke run

1. Run the AppHost and open `simulator-web`.
2. Choose *Quiet Monday* and **Preview plan**: 20 bookings, 1 of them bad, and the Planner assigning most of the orders Norvia is expected to accept.
3. **Start run**. Watch the log fill: each line names a correlation ID. When the bookings are in, the Planner's lines follow: *Assign Transport Order*, with the Shipment number.
4. Open a line's **Message trail**: the Integration console finds the trail, with the Partner API call, the intake and the TMS call; for a Planner line, the TMS API call and the `ShipmentPlanned` it published. Search the TMS UI's Transport Orders for `SIM-1-`: the assigned ones have left *Transport Orders waiting for planning* and are on *Shipments to release*, carried by Own fleet or a Carrier.
