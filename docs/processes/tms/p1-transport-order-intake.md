# P1 Transport Order intake

Transport Order intake takes a Shipper's booking into the TMS and accepts or rejects it. This document covers the Transport-only service through the Shipper API, the only intake channel built so far. It was written from the P1 spec (GitHub issue #1). Terms are defined in [CONTEXT.md](../../../CONTEXT.md).

| | |
|---|---|
| **Trigger** | A Shipper submits a booking in the Partner portal |
| **Outcome** | An Accepted Transport Order with a Transport Order number, or a Rejected booking with every reason. The Shipper sees the outcome in the same call |
| **Owning system** | TMS. It owns Transport Orders and assigns their numbers (ADR 0002) |
| **Applies to** | Own fleet and Carriers alike. Intake happens before P3 decides who carries the goods |
| **Integrations touched** | Partner portal, API Management, intake Logic App, TMS intake API, Service Bus (outcome events) |

## Channels

The intake API records the channel each Transport Order came from.

| Channel | Service | Status |
|---|---|---|
| Shipper API | Transport-only | Built by this process |
| WMS handoff | Fulfilment | Later. It reuses the same intake API with its own channel |

BizTalk and EDI intake are not part of the sandbox.

## Status

| Status | Meaning |
|---|---|
| Accepted | The booking passed validation and has a Transport Order number. It waits for Rating (P2) and planning (P3) |
| Rejected | The booking failed validation. It is stored with its reasons and the submitted content, so Customer service can see it. It has no Transport Order number |

P1 creates no Shipments. P3 does that.

## Steps

The path runs synchronously end to end: Partner portal → API Management → intake Logic App → TMS intake API. No queue sits between API Management and the Logic App.

1. **Book.** The presenter plays a Shipper Persona (Brouwer Dranken) in the Partner portal, fills in the booking form and submits it. The portal calls API Management with the Persona's token and subscription key.
2. **Check at the edge.** API Management checks the subscription key and the Entra token (audience "Norvia Partner API", role `TransportOrders.Submit`). A token without the role gets 403 before any Logic App runs. This is how a Carrier Persona (Van Dijk Transport) gets refused. API Management maps the token's `azp` claim to a Shipper ID through the seed-derived mapping, creates the correlation ID and passes both to the Logic App (ADR 0005).
3. **Map.** The Logic App maps the partner booking to a Canonical Model `TransportOrder`. The partner format differs on purpose: it has its own field names, flat address fields, weights as text with units and local date/times. The Canonical Model uses UTC timestamps with offsets, weights in kilograms as numbers and ISO 3166 country codes.
4. **Submit to the TMS.** The Logic App calls the TMS intake API with its managed identity, which is the only identity that holds the intake role. The API accepts only the Canonical Model (ADR 0003). The Shipper comes from the token, never from the payload.
5. **Check for a repeat.** If the Shipper has already submitted this Shipper reference with the same content, the TMS returns the original Transport Order, flags it as a repeat and stops. If the reference was used for different content, the booking is rejected.
6. **Validate.** The TMS checks the booking against the [validation rules](#validation-rules). It collects every failure and never stops at the first one.
7. **Record.** An accepted booking gets a Transport Order number and the status Accepted. A rejected one is stored with the status Rejected and its reasons. The outcome event is published in the same transaction (see [Events](#events)).
8. **Respond.** The TMS answers the Logic App, which relays the answer to the Shipper in the partner format with its meaning unchanged. The accepted Transport Order appears in the TMS UI's list of Transport Orders waiting for planning, without a page refresh.

### When the TMS can't be reached

On a TMS server error or timeout, the Logic App retries with a bounded exponential policy. When the retries are used up, it returns a gateway error that tells the Shipper the booking was not received, so they know to try again. The run shows as failed in the Logic App run history.

## Validation rules

| Rule | Codes |
|---|---|
| The Shipper exists and is active in the TMS's seed-loaded Shipper data | `UnknownShipper`, `InactiveShipper` |
| A Shipper reference is given, and it is unique per Shipper | `ShipperReferenceMissing` |
| The collection address, the delivery address and the Consignee name are given | `CollectionAddressMissing`, `DeliveryAddressMissing`, `ConsigneeNameMissing` |
| Each address has a street, postcode, city and country | `CollectionAddressIncomplete`, `DeliveryAddressIncomplete` |
| The collection window has an earliest and a latest time. Its earliest time is not in the past and is not after its latest time | `CollectionWindowMissing`, `CollectionWindowInPast`, `CollectionWindowReversed` |
| A delivery window, when given, is internally consistent and doesn't end before the collection window starts | `DeliveryWindowIncomplete`, `DeliveryWindowReversed`, `DeliveryWindowBeforeCollection` |
| There is at least one goods line | `GoodsLinesMissing` |
| Each goods line has a package type from the fixed list (pallet, parcel, crate, loose), a quantity of at least 1 and a weight greater than 0 | `PackageTypeInvalid`, `QuantityInvalid`, `WeightInvalid` |
| Dimensions, when given, are positive | `DimensionsInvalid` |
| The total weight doesn't exceed the limit for a single Transport Order (the TMS setting `TransportOrderRules:MaxTotalWeightKg`, 24,000 kg by default) | `TotalWeightOverLimit` |

Each failure has a stable code and a readable message. Codes are never renamed; new rules add new codes. The 24,000 kg limit and the package type list are sandbox defaults, chosen to be believable, not to follow any regulation.

## Responses

| Case | TMS response |
|---|---|
| Accepted | Success, with the Transport Order number and status |
| Repeat (same Shipper, same Shipper reference, same content) | Success, returning the original Transport Order, flagged as a repeat |
| Validation failure, or a reused Shipper reference with different content | Client error (422 ProblemDetails) listing every failure's code and message. Its `instance` is where the Rejected booking can be read |
| Caller without the intake role | Refused |

## Correlation ID

API Management creates the correlation ID at the edge. It travels as a header through the Logic App to the TMS, is stored on the Transport Order and is carried in the properties of the outcome event. Every intake attempt, whether accepted, rejected, refused or failed, can be found by it in Application Insights.

It is not the Transport Order number. The TMS assigns that number only on acceptance, and rejected or refused attempts need tracing too.

## Events

Both events are published to the topic for Transport Order events, through the TMS's transactional outbox, so an event exists exactly when the state change was committed (ADR 0003).

| Event | Carries | Consumers |
|---|---|---|
| `TransportOrderAccepted` | The full Canonical Model Transport Order, correlation ID | Consumers outside the TMS, later. P3 works from the TMS's own data, not from this event (ADR 0003) |
| `TransportOrderRejected` | The reasons and the Shipper reference, correlation ID | Monitoring and reporting |

A repeat submission publishes no event.

## Deferred

These were left out on purpose while designing P1, each with its reason.

- **The WMS handoff (Fulfilment Transport Orders).** The intake API records the channel so the handoff can reuse it, but no WMS work is included yet.
- **Transport Orders entered by Norvia staff in the TMS UI.** The Shipper API is enough for the first slice.
- **Amending or cancelling a Transport Order after acceptance.**
- **Dangerous goods, temperature control, multi-leg or multi-stop Transport Orders, and customs.** They are too much for the first slice.
- **Notifying the Shipper by email or webhook.** The Shipper sees the outcome in the call response and in the Partner portal's list of bookings.
- **Asynchronous intake (202 Accepted).** It was dropped along with the queue between API Management and the Logic App, so the Shipper sees the TMS's decision straight away. It can come back if a demo needs it.
- **The ERP and master-data sync.** Shippers come from the seed file for now.
- **As-found toggles and presenter-panel faults.** They are added after the fixed state is built.
