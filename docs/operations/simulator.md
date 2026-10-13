# Simulator

The Simulator feeds the estate with representative orders, shipments and trips at a chosen volume, so demos, the [Integration console](integration-console.md), the Planner's lists and the Shippers' views have something real to show. It acts only through the estate's real doors, so everything it makes has a Message trail ([ADR 0039](../adr/0039-the-simulator-books-only-through-the-estates-real-doors.md)). Terms are defined in [GLOSSARY.md](../../GLOSSARY.md).

| | |
|---|---|
| **Used by** | The presenter, who plays the Simulator operator |
| **Does** | Sends a Simulation plan to the estate. So far: Shipper bookings to the Norvia Partner API |
| **Owns** | Its runs, their plans and the log of every step, in its own database (`simulator`) |
| **Reaches Norvia through** | The Norvia Partner API, as each Shipper's Persona. Never a database or Service Bus |

## Scope

The Simulator is built in slices. This one covers the skeleton and **Shipper bookings** (the first stage of the funnel). Planner assignment, Carrier answers, tracking and Proofs of delivery, Trips and Drivers, and Azure come after it.

## Switching it on

It does nothing unless `Simulator:Enabled` is `true`; switched off it answers every request but its health checks with 404. The AppHost turns it on for a local run (`simulator-web` on the Aspire dashboard). Its guards are settings: `Simulator:MaxOrdersPerRun` (1,000 by default, 500 locally) and `Simulator:MaxConcurrentRequests` (8 by default, 4 locally).

Locally there is no Easy Auth, so the AppHost signs you in as a local operator holding both app roles (`LocalOperator` settings), and the Simulator refuses those settings outside Development. The roles are `Simulator.Run` (configure, start, cancel, clear the history) and `Simulator.Read` (see runs and their logs). Signing in with Entra comes with the Azure slice.

## Screens

### Configure

- A Preset ("Quiet Monday", "Busy day", "Peak week") fills the numbers, which can be changed: the number of orders, the share of **bad bookings**, the share **collected today**, burst or paced (with the window in minutes), and the **seed** (random by default, editable).
- It says the caps and the number the next run gets. A plan over a cap is refused here and again in the preview and when the run starts.
- It lists the runs, newest first, and has **Clear history**, which removes the finished runs and their logs. It doesn't touch what they sent, and the run numbers keep counting, so a Shipper reference is never used twice.

### Plan preview

What the settings would send, per Shipper and per booking: its Shipper reference, the Persona it is sent as, the Lane, when it is collected, its weight, whether it is good or bad and what Norvia is expected to answer. It is made from the same seed, so **Start run** sends exactly what it shows. It never shows a Consignee.

### Run

The live log of the run, filterable by actor, outcome and a text (a correlation ID, a Shipper reference or a Transport Order number). Each line has the time, actor, action, outcome, the correlation ID Norvia answered with, the Shipper reference and the Transport Order number. A line's correlation ID can be copied, and **Message trail** opens it in the Integration console's search. The log is read from the database, so it survives a refresh and a second viewer sees the same.

Only one run is active at a time: a second cannot start while one is. **Cancel run** stops sending; the bookings in flight land and are logged. A run is not resumed after a restart: the Simulator fails a run it finds still running when it starts.

## What a run sends

- **Shippers and Personas** come from the seed file: each active Shipper with a Persona and a Rate Card. A booking is sent as the Shipper's Persona, with the token and subscription key the [Partner portal](../processes/tms/p1-transport-order-intake.md) gets for it. Locally the Simulator signs the token itself.
- **Consignees, addresses, goods and dates are generated.** The Lane is chosen from the Shipper's Rate Card, the addresses lie in its Zones (NL, BE and LU), and the goods stay within what a Rate Card can price. Pickups are spread over the next seven days; the configured share is collected today.
- **Shipper references** are `SIM-<run>-<n>`, so a run's orders are recognisable.
- **Bad bookings**, exactly the configured share, in four kinds: **no Lane** (delivered to Germany, where the demo Rate Cards have no Zone: Norvia accepts it and Rating then finds no Lane), a **repeated reference** (an earlier booking sent again: *already booked*), a **conflicting reference** (an earlier reference with other goods: rejected as `ShipperReferenceReused`) and **invalid content** (no Consignee name, no goods or a zero weight: rejected). Infrastructure faults stay with the presenter's existing switches. A repeated or conflicting reference needs an earlier good booking to copy, so a bad booking that comes before any good one is no Lane or invalid content instead. "No Lane" works because the demo Rate Cards have no Zone in Germany; give a Shipper one and the booking would find a Lane.
- **Burst or paced.** Burst sends as fast as the concurrency cap allows. Paced spaces the bookings evenly over the window. Nothing is backdated: the real paths stamp real time.

Each booking is sent in a span of its own, `Send booking`, and its answer is logged with the tracking contract's keys the Simulator knows (`flow`, `partyId`, `correlationId`, `transportOrderNumber`, `outcome`), never what was booked ([coding guidelines §7](../coding-guidelines.md#tracking-contract)).

## Presenter smoke run

1. Run the AppHost and open `simulator-web`.
2. Choose *Quiet Monday* and **Preview plan**: 20 bookings, 1 of them bad.
3. **Start run**. Watch the log fill: each line names a correlation ID.
4. Open a line's **Message trail**: the Integration console finds the trail, with the Partner API call, the intake and the TMS call. Search the TMS UI's Transport Orders for `SIM-1-`.
