# P2 Rating

Rating calculates the Freight Charge for an accepted Transport Order from the Shipper's Rate Card. It covers only what Norvia charges the Shipper. What a Carrier costs Norvia is not part of it. Terms are defined in [CONTEXT.md](../../../CONTEXT.md).

| | |
|---|---|
| **Trigger** | A Transport Order is accepted (P1) |
| **Outcome** | The Transport Order is Rated with a frozen Freight Charge, or Rating failed with a reason |
| **Owning system** | TMS (ADR 0006). The ERP owns Contracts and Rate Cards |
| **Applies to** | Own fleet and Carriers alike. Rating happens before P3 decides who carries the goods |
| **Integrations touched** | Service Bus (outcome events). ERP, through the seed file for now |

## Rating result

Each Transport Order has a rating result that is separate from its status. The status (Accepted, then planned, and so on) belongs to P3 and later processes, and a Transport Order can be planned whatever its rating result.

| Rating result | Meaning |
|---|---|
| Not yet rated | Accepted, and Rating hasn't run yet |
| Rated | A Freight Charge has been calculated and is frozen |
| Rating failed | The Rate Card couldn't price the order. The result carries a failure reason |
| Not rated separately | A Fulfilment Transport Order (WMS channel). It is billed through the Fulfilment Contract, not per transport |

Rating failed can move to Rated through a re-rate. Rated and Not rated separately are final.

## Steps

1. **Acceptance.** P1 accepts a Transport Order and commits it. A Transport-only order starts as *Not yet rated*. An order that came in through the WMS channel is set straight to *Not rated separately*, and P2 does nothing more with it.
2. **Pick-up.** A background worker in the TMS picks up orders that are *Not yet rated*. It is triggered right after the acceptance commit and also sweeps on a timer, so a crash between acceptance and Rating only delays Rating. No order stays *Not yet rated* for good.
3. **Choose the Rate Card version.** Take the Shipper's Rate Card version that is valid on the collection date: the date of the earliest collection time in the collection window, in the Europe/Amsterdam timezone.
4. **Match the Lane.** Find the origin Zone (collection address) and the destination Zone (delivery address) by country and postcode prefix. The most specific prefix wins. The Lane is that pair of Zones.
5. **Choose the Weight Break.** Take the order's total gross weight in kg and find the Lane's Weight Break it falls in.
6. **Calculate.** Freight = weight ÷ 100 × the price per 100 kg for that Weight Break. If the Freight is below the Lane's minimum charge, add a charge line that makes up the difference.
7. **Record.** Store the Freight Charge (its charge lines, the total, and a reference to the Rate Card version used) and set the result to *Rated*. If any of steps 3–5 failed, set the result to *Rating failed* with the reason instead. Publish the outcome event in the same transaction.

### Re-rate

Customer service sees orders with *Rating failed* in a rating exceptions list in the TMS UI. They can re-rate one, typically after the Rate Card has been corrected. A re-rate runs steps 3–7 again with the same collection-date rule, not today's Rate Card. Only orders with *Rating failed* can be re-rated. A Rated order is never re-rated: a wrong charge is corrected in invoicing with a Credit note ([E1](../erp/e1-invoicing.md#credit-notes)).

In a real operation a pricing desk would work these exceptions. The sandbox gives the job to Customer service, to keep the number of demo roles small.

## Calculation

- **Money:** EUR only, without VAT. The ERP adds VAT when it invoices. Each charge line is rounded to cents, half away from zero, and the total is the sum of the rounded lines.
- **Charge lines:** always a *Freight* line. A *Minimum charge adjustment* line is added only when the minimum applies.
- **Weight basis:** total gross weight. Volumetric weight isn't used yet.

## Failure reasons

| Code | When |
|---|---|
| `NoRateCard` | The Shipper has no Rate Card |
| `NoValidRateCardVersion` | No version of the Rate Card is valid on the collection date |
| `NoMatchingLane` | No Lane covers the origin and destination Zones |
| `WeightAboveHighestBreak` | The total weight is above the Lane's highest Weight Break |

## Rate Cards in the TMS

The TMS holds a read copy of each Shipper's Rate Card and never edits it (ADR 0006). Until the ERP exists, the TMS loads Rate Cards from the seed file at startup, the same way it loads Shippers. A Rate Card whose Zones overlap is rejected when the seed loads. A Rate Card has versions, and each version has a validity period.

The demo seed gives Brouwer Dranken a Rate Card with Benelux Lanes and a deliberate gap: no Lane into Germany. A booking from Rotterdam to Düsseldorf shows `NoMatchingLane` and lands in the rating exceptions list. The live "fix the Rate Card, then re-rate" demo waits for the ERP sync. Until then, tests cover that path.

## Events

Both events are published to the topic for Transport Order events, through the TMS's transactional outbox (ADR 0003).

| Event | Carries | Consumers |
|---|---|---|
| `TransportOrderRated` | Transport Order number, Shipper, Freight Charge with charge lines, Rate Card version, correlation ID | Monitoring. [P9](p9-freight-settlement.md) checks whether the order is Billable inside the TMS, not from this event (ADR 0003) |
| `TransportOrderRatingFailed` | Transport Order number, Shipper, failure code and message, correlation ID | Monitoring |

*Not rated separately* publishes no event. Each successful rating or re-rating publishes exactly one event.

## Deferred

These were left out on purpose while designing P2, each with its reason.

- **A fuller Rate Card**: fuel surcharge, per-pallet pricing, volumetric weight, tail-lift and time-window surcharges, volume discounts. The minimal Rate Card is enough for a believable demo. Add the fuel surcharge first, together with the ERP sync.
- **ERP → TMS Rate Card sync.** The ERP publishes Rate Cards and a Logic App calls a command on the TMS to replace the Rate Card. This is the live "fix the Rate Card, then re-rate" demo. It waits until the ERP is built.
- **Automatic re-rating when a new Rate Card arrives.** It needs the sync first.
- **Correcting the charge on a Rated order.** Handled as a Credit note in the ERP ([E1](../erp/e1-invoicing.md#credit-notes)).
- **Showing the Freight Charge to Shippers, and Quotes before booking.** Out of scope because a displayed price reads as a commitment.
- **Carrier cost.** Designed in [P9](p9-freight-settlement.md#carrier-cost) with the same method as Rating, against a Carrier Rate Card (ADR 0013).
- **A pricing desk role** separate from Customer service.
- **As-found toggles**, for example no alert on `TransportOrderRatingFailed`, or an event nobody subscribes to. They are added after the fixed state is built, as for P1.
