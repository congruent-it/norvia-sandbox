# A dispatched Trip is sent to the Driver app as whole versions

Dispatching a Trip, and every Amend after it, publishes the whole Trip (Vehicle, Driver, times and every Stop in order) with a version number. The Trip dispatch Logic App pushes it to the Driver app, and the latest version replaces whatever the Driver app held. A Driver always has exactly one current copy of each Trip, a retry or a duplicate is harmless, and neither the Logic App nor the Driver app has to apply changes in order.

## Considered Options

- **Recall and dispatch again:** the Planner takes the Trip back to Draft and the Driver app drops it until it comes back. Simple for the TMS, but the Trip vanishes from the Driver's screen during an edit, and a lost recall leaves the Driver working a stale Trip.
- **Send only the changes** (Stop added, Stop removed, Driver changed): smaller messages, but every change depends on the ones before it arriving in order, and a lost message corrupts the Driver's Trip.
- **No changes after dispatch:** the simplest, but real days need a Shipment added or taken off after the Driver has the Trip.

## Consequences

The Trip dispatch is the Driver app's contract, and changing it means changing both sides. Delivery is reported per version, so the Planner can see which version the Driver has. Once P12 starts a Trip, only taking off an uncollected Shipment produces a new version. Handing a Trip over to another Driver mid-run is deferred.
