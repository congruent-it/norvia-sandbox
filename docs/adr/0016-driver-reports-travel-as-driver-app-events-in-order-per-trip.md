# Driver reports travel as Driver app events, in order per Trip, with their outcome reported back

A Driver's reports from the road (starting a Trip, completing a Stop, a Proof of delivery, ending the Trip) reach the TMS differently from a Carrier's. The Driver app is an application, so under ADR 0003 it publishes what happened as events in the Canonical Model, through its own outbox to its own topic. A Proof of delivery's files wait in the Driver app's transit container, and the event carries their references, as a claim check (ADR 0009). One Driver reports Logic App reads a subscription with sessions keyed by Trip number, so each Trip's reports reach the TMS one at a time and in order. It calls the TMS, then tells the Driver app whether each report was recorded or refused. The Driver ID comes from the Driver's Entra token, never from the payload (ADR 0005).

Carriers are handled the opposite way on purpose (ADR 0008): they get `202 Accepted` and never learn more. The Driver app is Norvia's own, so Norvia can close the loop, and the demo can show both sides of the same trade-off.

## Considered Options

- **The Carrier path, through API Management, a queue and `202`:** one inbound pattern for everyone, but it puts Norvia's own staff on the partner surface and makes the Driver as blind as a Carrier.
- **A synchronous call from the Driver app to a Logic App:** the Driver sees the answer at once, but every Stop then depends on the Logic App and the TMS being up at that moment, and the Driver app would call the integration layer rather than publish events (ADR 0003).
- **No ordering, with the TMS accepting reports in any order:** simpler messaging, but the TMS would have to treat a Stop report on a Dispatched Trip as an implicit start, and an End Trip could overtake the last Stop.

## Consequences

One Trip's reports wait behind a report that is being retried, and a report that fails for good is dead-lettered so the session can move on. Later reports are then decided by the TMS's rules on their own merits. The Driver sees Sending, Recorded, Refused or Not recorded for each report, so a failure in the integration layer is visible on the road as well as on the integration side. The Driver reports Logic App needs the Driver app's outcome endpoint and read and delete access to its transit container, which needs a lifecycle rule.
