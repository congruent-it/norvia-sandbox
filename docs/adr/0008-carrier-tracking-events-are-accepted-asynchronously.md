# Carrier Tracking events are accepted asynchronously

A Carrier's Tracking event is accepted at the edge and processed later. API Management validates the token, derives the Carrier from `azp` (ADR 0005), adds a correlation ID, puts the message on a Service Bus queue for inbound Carrier tracking and returns `202 Accepted`. The Carrier-tracking Logic App reads the queue, maps the Carrier's status code to the Canonical Model and calls the TMS. This differs on purpose from the Carrier's answer to a booking (P4), which is synchronous. Tracking is high-volume and a Carrier doesn't wait on it, so the Carrier shouldn't be coupled to the TMS being up. The cost is that the Carrier never learns whether its update arrived. A mapping failure or a refused event shows up only inside Norvia's integration layer, as a failed run or a dead-lettered message. This is exactly the failure behind the scenario's headline incident.

## Considered Options

- **Synchronous, as for booking answers:** the Carrier would see every failure, so nothing could be dropped silently. But each Carrier call then depends on the Logic App and the TMS being available, and the sandbox could no longer re-enact the incident as it happened.

## Consequences

Noticing a failed Tracking event is Norvia's job alone. It needs alerts on failed Carrier-tracking Logic App runs and on the tracking queue's dead-letter queue, backed by the Stale flag on the business side. Leaving these out is how the estate reproduces the incident as found. When the TMS is briefly unavailable, messages stay on the queue and are retried, so nothing is lost.
