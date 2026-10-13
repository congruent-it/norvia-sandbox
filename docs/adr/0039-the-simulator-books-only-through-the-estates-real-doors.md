# The Simulator is its own application and sends only through the estate's real doors

The estate holds only a few hand-seeded days. The Simulator feeds it with representative orders, shipments and trips at a chosen volume, so demos, the Integration console, the Planner's lists and the Shipper views have something real to show. A Simulation run sends a Simulation plan, made from the run's settings and a seed, to the estate.

- **Its own application.** The Simulator is `src/Simulator` (Domain, Application, Infrastructure, Migrations and a Blazor Web project) with its own database for its runs, their plans and every step's outcome (ADR 0002). It is not the Integration console, which stores nothing and writes only dead-letter resubmits ([ADR 0017](0017-the-integration-console-reads-the-estates-telemetry-and-stores-nothing.md)). It is styled in Darkly's look like the console (the amendment to [ADR 0025](0025-the-integration-console-is-styled-with-tailwind-in-darklys-look.md)).
- **Only the estate's real doors** ([ADR 0001](0001-realistic-applications-integrated-only-through-ais.md), [ADR 0003](0003-http-in-events-out.md)). Shipper bookings go to the Norvia Partner API as the Shipper's Persona, with the token and subscription key the Partner portal gets for it ([ADR 0005](0005-partner-identity-comes-from-the-token.md)). Later slices add the TMS API for the Planner's and Customer service's actions, and the Driver app API for the Driver's. It never writes to a database or publishes to Service Bus itself, which an architecture test enforces. So every record it makes has a real Message trail.
- **The same code locally and in Azure.** Only the endpoints and credentials differ. Locally it signs the Persona tokens itself, as the Partner portal does ([ADR 0023](0023-locally-a-stand-in-plays-the-norvia-partner-api.md)). The Azure identity and sign-in come with a later slice.
- **Master data stays in the seed file** (ADR 0002), extended where the Simulator needs it: Tuincentrum Groenrijk gets a Rate Card, so the Simulator can book for it. The Simulator generates only transactional data: Consignees, addresses, goods and dates. It chooses Lanes from the Shipper's Rate Card.
- **Reproducible.** The plan is made first, deterministically, from a visible seed; the preview is the plan that is sent. A run is not backdated: real paths stamp real time.
- **Guards.** One run is active at a time; a hard cap on the orders of a run and a cap on requests in flight, both settings with lower local defaults; and `Simulator:Enabled`, which is off unless the estate turns it on. A run is identifiable by its Shipper references, `SIM-<run>-<n>`. There is no wipe: a fresh local run or a new azd environment is the reset.
- **Business outcomes only.** The failure mix is bookings Norvia refuses for business reasons: no Lane, a repeated or conflicting Shipper reference, invalid content. Infrastructure faults stay with the presenter's existing switches.

## Considered Options

- **Inside the Integration console:** one tool fewer, but the console would then store runs and write to the estate, which ADR 0017 rules out.
- **A seed script writing to the databases:** quick, but nothing it made would have a Message trail, and it would break the one-system-of-record rule (ADR 0002).
- **A load-testing tool:** it makes volume, not representative data with a log an audience can follow.

## Consequences

- A change to the Partner API's booking contract is made in the Simulator's format too, as it is in the Partner portal's (`PartnerBookingFormat`). A unit test checks every good booking against the booking schema.
- The Simulator needs a database, so the AppHost starts a migrations project for it before the Simulator, as for the other applications.
- Its slices: Shipper bookings (this one), Planner assignment, Carriers, Trips and Drivers, then Azure.
