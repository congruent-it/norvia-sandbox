# P10 Fleet capacity planning

Fleet capacity planning keeps track, day by day, of which Vehicles and Drivers Norvia's Own fleet can use, and how many driving hours each Driver has left. It compares that capacity with the Own fleet work already planned, so the Planner sees a day that is Over capacity before it happens. The Planner keeps it up to date in the TMS UI. Trip dispatch (P11) draws its Vehicles and Drivers from it. Terms are defined in [GLOSSARY.md](../../../GLOSSARY.md).

| | |
|---|---|
| **Trigger** | A new day enters the Planning horizon (the daily horizon job). After that, the Planner records Absences, Off the road days and Roster changes |
| **Outcome** | For each day in the Planning horizon: the available Vehicles and Drivers with their Shifts, each Driver's Driving-hour budget, and the day's capacity against its Own fleet demand |
| **Owning system** | TMS for availability: Rosters, Shifts, Absences, Off the road days and Driving-hour budgets. The ERP owns Vehicle and Driver master data (ADR 0014) |
| **Applies to** | Own fleet only |
| **Integrations touched** | None at runtime. ERP, through the seed file for now (Vehicles and Drivers) |

## What P10 decides, and what it doesn't

P10 says **what the Own fleet can do on a day**. It doesn't put a Shipment on a Trip, pick a Vehicle or Driver for a Trip, or sequence Stops. That is P11. It doesn't choose between Own fleet and a Carrier either. That stays with the Planner in P3.

P10 constrains the other processes in two different ways:

| Process | What P10 does there |
|---|---|
| [P3 Transport planning](p3-transport-planning.md) | **Warns, never blocks.** Assigning a Shipment to Own fleet on a day that is, or would become, Over capacity shows a warning. The Planner can go ahead |
| [P11 Trip dispatch](p11-trip-dispatch.md) | **Hard rules.** A Trip can only get a Vehicle and a Driver that are available that day and can take the Trip (see [Availability rules for Trips](#availability-rules-for-trips)) |

The load check is a rough figure for the Planner, not a plan. It doesn't know which Shipments will fit together on a Trip.

## Vehicles and Drivers in the TMS

The ERP owns Vehicle and Driver master data (ADR 0014). The TMS holds a read copy. Until the ERP exists, it loads that copy from the seed file at startup, the same way it loads Carriers.

| Master data (ERP) | Fields |
|---|---|
| Vehicle | ID, registration, Vehicle type, payload in kg, pallet places, active flag |
| Driver | ID, name, licence categories, active flag |

There are three Vehicle types, each needing a licence category:

| Vehicle type | Licence needed |
|---|---|
| Van (up to 3.5 t) | B |
| Rigid truck | C |
| Tractor with trailer, treated as one Vehicle | CE |

CE covers C, and C covers B. A Driver can drive a Vehicle when their highest licence covers what the Vehicle type needs.

Everything about availability belongs to the TMS: Rosters, Shifts, Absences, Off the road days and Driving-hour budgets. It is all edited in the TMS, never in the ERP.

## The Planning horizon

The Planning horizon is today and the next 14 days (a TMS setting). Days in it are **open**. Days before today are **closed** and read-only. Days after it don't exist yet.

### The horizon job

A background job inside the TMS runs every day at 00:00 Europe/Amsterdam. It opens every missing day up to the end of the Planning horizon, so it catches up after downtime, and running it twice changes nothing. Opening a day:

1. Gives each active Driver a Shift for that day from their Roster, or none if the Roster has no work that weekday.
2. Marks each active Vehicle as available.
3. Closes yesterday.

The trigger in the process catalogue, "Planning horizon opens", is this job opening a day.

### Rosters and Shifts

A Driver's Roster is their standard weekly working pattern: for each weekday, either a Shift start and end time or no work. The horizon job copies it onto each new day as that day's Shift.

The Planner can edit a single open day's Shift, for example to start early. The day is then marked as edited by hand.

When the Planner changes a Roster, the TMS rewrites every open day that isn't edited by hand and has no Absence. Days edited by hand keep their Shift. A day that already has a Trip also keeps its Shift. If the new Roster no longer covers that day, the Trip is flagged, as in [When availability changes under a Trip](#when-availability-changes-under-a-trip).

### Absences and Off the road days

| Record | For | Granularity | Reasons |
|---|---|---|---|
| Absence | A Driver | Whole days, one or several | Leave, sickness, training |
| Off the road | A Vehicle | Whole days, one or several | Maintenance, repair |

The Planner can record them on any open day. Absences can also be recorded for days beyond the Planning horizon, because leave is often known weeks ahead. The horizon job honours them when it opens those days. Off the road days can be too. Each record has a free-text remark besides its reason. Removing one makes the Driver or Vehicle available again, with the Shift from their Roster.

### When availability changes under a Trip

Recording an Absence or an Off the road day is always accepted, even when that Driver or Vehicle is already on a Trip that day. The TMS then flags the Trip as needing a new Driver or a new Vehicle. The flag is *Needs a Driver* or *Needs a Vehicle*. Nothing happens automatically: the Planner fixes the Trip in [P11](p11-trip-dispatch.md#when-availability-changes-under-a-trip).

## Driving-hour budget

Each Driver has a Driving-hour budget, a simplified form of EU Regulation 561/2006 that uses planned hours only:

| Limit | Default (TMS setting) |
|---|---|
| Daily | 9 h, extended to 10 h no more than twice a week |
| Weekly | 56 h in a fixed week, Monday 00:00 to Sunday 24:00, Europe/Amsterdam |
| Fortnightly | 90 h over this week and the previous one |

Driving hours come from Trips. In P11 the Planner enters a planned driving duration on each Trip. P10 adds up the durations of each Driver's Trips, Draft and Dispatched, per day, per week and per fortnight. The TMS doesn't estimate driving time.

A Trip that would take a Driver over 9 h in a day uses one of that week's two extensions automatically, if one is left. The budget shows how many hours are left today, this week and this fortnight, and how many extensions are left this week.

The limits are TMS settings, so a demo can make them tight. Breaks, daily and weekly rest periods, and actual hours from tachographs or from P12 are deferred.

## The load check

For each open day, the TMS compares what the Own fleet can carry with what it has been given.

**Capacity.** The TMS pairs the day's available Vehicles with its available Drivers, the largest Vehicle first, each with a Driver whose licence covers it, preferring the Driver with the lowest licence that does. Each paired Vehicle counts once, with its payload and pallet places. This assumes one Trip per Vehicle per day. A Vehicle without a Driver adds nothing.

**Demand.** Every Own fleet Shipment that is Planned or Released and collects that day counts. The collection day is the date of the collection window's earliest time in Europe/Amsterdam. Planned Shipments count too, because the warning is most useful while the Planner can still reassign. Demand is the total weight of the Shipments' goods lines, and the pallet places they take up: the quantity of their pallet goods lines. Parcels, crates and loose goods count only toward weight.

**Over capacity.** A day is Over capacity when its demand is more than its capacity in weight or in pallet places.

## Availability rules for Trips

P10 supplies these checks. P11 applies them to every change to a Trip and again at Dispatch, together with its own rules ([Trip rules](p11-trip-dispatch.md#trip-rules)). Each failure has a stable code and a readable message, as in P3.

| Code | Rule |
|---|---|
| `VehicleNotAvailable` | The Vehicle is active and not Off the road that day, and has no other Trip that day |
| `DriverNotAvailable` | The Driver is active, has a Shift that day and no Absence, and has no other Trip that day |
| `LicenceNotHeld` | The Driver's licence covers the Vehicle type |
| `DrivingHoursExceeded` | The Trip's planned driving duration fits in the Driver's Driving-hour budget: daily (with an extension if one is left), weekly and fortnightly |
| `PayloadExceeded` | The Trip's total weight fits the Vehicle's payload |
| `PalletPlacesExceeded` | The Trip's pallets fit the Vehicle's pallet places |
| `DayNotOpen` | The Trip's day is in the Planning horizon |

## Steps

1. **Open the day.** The horizon job opens the new day at 00:00 ([The horizon job](#the-horizon-job)).
2. **Keep availability up to date.** The Planner records Absences and Off the road days as they become known, edits single Shifts, and changes Rosters.
3. **Watch the load.** The Planner watches the capacity view. A day that is Over capacity is a cue to move Own fleet Shipments to a Carrier in P3 before release, or to find more Drivers or Vehicles.
4. **Warn in P3.** When the Planner assigns or reassigns Shipments to Own fleet, P3 shows the load check for each Shipment's collection day, including the Shipment being assigned.
5. **Draw on it in P11.** Trip dispatch picks Vehicles and Drivers under the [availability rules](#availability-rules-for-trips), and each Trip's planned driving duration uses up its Driver's Driving-hour budget.

## Planner's views

| View | Shows | Actions |
|---|---|---|
| Capacity | One row per open day: available Vehicles and Drivers, capacity against demand in kg and pallet places, an Over capacity flag, and any flagged Trips | Open a day's detail |
| Vehicles | Each Vehicle with its type, payload, pallet places, and availability across the Planning horizon | Record or remove Off the road days |
| Drivers | Each Driver with their licences, Roster, Shifts and Absences across the Planning horizon, and their Driving-hour budget | Edit the Roster, edit a single Shift, record or remove Absences |

The views update without a page refresh, as P3's lists do.

In P3, assigning to Own fleet shows the load check for each affected day. For example: "Fri 26 Sep: Over capacity, 34 of 30 pallet places after this assignment." A collection day after the Planning horizon shows "Outside the Planning horizon". Neither blocks the assignment.

## Events

P10 publishes no Service Bus events. Nothing outside the TMS needs availability, and P11 reads it inside the TMS (ADR 0003). The Planner's actions in P10 have no correlation ID, for the same reason. The horizon job logs each run.

## Demo seed

| Vehicle | Type | Payload | Pallet places |
|---|---|---|---|
| Two vans | Van | 1,200 kg | 4 |
| Three rigid trucks | Rigid truck | 10,000 kg | 18 |
| One tractor with trailer | Tractor with trailer | 24,000 kg | 33 |

Seven Drivers: two with CE, three with C and two with B. All have a Monday to Friday Roster, with different Shift times.

Within the first week of the Planning horizon, one rigid truck is Off the road for maintenance and one CE Driver is on leave. Together with the seeded Own fleet Transport Orders, this makes one day Over capacity. The demo shows the warning in P3 and the Planner moving a Shipment to a Carrier. The seed gives these dates relative to the day the demo starts.

## Implementation

The Driving-hour budget is built: `DrivingHourBudget` in the TMS domain works out the hours left today, this week and this fortnight, and the extensions left this week, from the day it is and the Trip durations it is given. The limits are the `DrivingHours` TMS settings. Until P11 supplies Trip durations, every budget is full, and the Drivers tab shows it. The rest of P10 is not built yet.

## Deferred

These were left out on purpose while designing P10, each with its reason.

- **Tachograph data, and actual driving hours read back from P12.** Planned hours are enough to show the rule without a tachograph integration.
- **Breaks and daily and weekly rest periods.** They matter for sequencing a Trip, which is P11. The budget covers the driving limits only.
- **Part-day Absences, and a Vehicle breaking down during the day.** Whole days cover the demo. A breakdown on the road belongs to P7 and P11.
- **Estimating driving hours** from distances. The Planner enters them on the Trip for now.
- **More than one Trip per Vehicle per day in the load check.** The rough figure is good enough to warn.
- **Depots.** The Own fleet is one pool. Rotterdam and Antwerp would each need their own capacity once depots exist.
- **Suggesting what to move to a Carrier**, or rostering automatically. Planning stays manual, as in P3.
- **Loading metres and volume.** Goods lines don't carry them.
- **The ERP and the Vehicle and Driver master-data sync.** They come from the seed file for now.
- **As-found toggles.** They are added after the fixed state is built, as for P1 and P2.
