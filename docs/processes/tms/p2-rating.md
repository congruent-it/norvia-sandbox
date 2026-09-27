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
| Rating failed | The Rate Card couldn't price the order. The result carries a failure reason: a stable code and a message, which the query API shows. The order stays Accepted |
| Not rated separately | A Fulfilment Transport Order (WMS channel). It is billed through the Fulfilment Contract, not per transport |

Rating failed can move to Rated through a re-rate. Rated and Not rated separately are final.

## Steps

1. **Acceptance.** P1 accepts a Transport Order and commits it. A Transport-only order starts as *Not yet rated*. An order that came in through the WMS channel is set straight to *Not rated separately*, and P2 does nothing more with it.
2. **Pick-up.** A background worker in the TMS picks up orders that are *Not yet rated*. It is triggered right after the acceptance commit and also sweeps on a timer, so a crash between acceptance and Rating only delays Rating. No order stays *Not yet rated* for good.
3. **Choose the Rate Card version.** Take the Shipper's Rate Card version that is valid on the collection date: the date of the earliest collection time in the collection window, in the Europe/Amsterdam timezone.
4. **Match the Lane.** Find the origin Zone (collection address) and the destination Zone (delivery address) by country and postcode prefix. Postcodes and prefixes are compared without spaces and in upper case. The most specific prefix wins: when an address lies in a Zone for `35` and in one for `3584`, it is in the `3584` Zone. The Lane is that pair of Zones.
5. **Choose the Weight Break.** Take the order's total gross weight in kg and find the Lane's Weight Break it falls in. Each Weight Break runs from just above the next lighter one (or 0 kg) up to and including its own upper limit, so a weight exactly on a boundary falls in the lighter band: with breaks up to 500 kg and up to 2,000 kg, 500 kg is priced at the up-to-500 kg price.
6. **Calculate.** Freight = weight ÷ 100 × the price per 100 kg for that Weight Break. If the Freight, rounded to cents, is below the Lane's minimum charge, add a charge line that makes up the difference, so the total is exactly the minimum charge.
7. **Record.** Store the Freight Charge (its charge lines, the total, and a reference to the Rate Card version used) and set the result to *Rated*. If any of steps 3–5 failed, set the result to *Rating failed* with the reason instead. Publish the outcome event in the same transaction.

### Re-rate

Customer service sees orders with *Rating failed* in a rating exceptions list in the TMS UI, with each order's Shipper, failure code and reason. They can re-rate one from the list, typically after the Rate Card has been corrected. A re-rate runs steps 3–7 again with the same collection-date rule, not today's Rate Card. Only orders with *Rating failed* can be re-rated. A Rated order is never re-rated: a wrong charge is corrected in invoicing with a Credit note ([E1](../erp/e1-invoicing.md#credit-notes)).

The TMS API has a list query for the rating exceptions (`GET /v1/transport-orders/rating-exceptions`, each with its failure code and message) and the re-rate command (`POST /v1/transport-orders/{number}/re-rate`). The command answers with the rating result the order has afterwards, and refuses an order that isn't *Rating failed* with a 409 that changes nothing. Each re-rate carries an `Idempotency-Key`. The order keeps the key of its last re-rate, so that re-rate sent again, as a retry, gets the same answer, flagged as a repeat, and rates and publishes nothing. A new key on a Rated order is still refused. A re-rate that fails again stays *Rating failed* with the new reason and publishes another `TransportOrderRatingFailed`. The list query takes the same authorization as the TMS API's other queries. The re-rate answers only the TMS UI, which calls it as its own managed identity, the one principal holding the TMS API's `TransportOrders.Manage` role.

In a real operation a pricing desk would work these exceptions. The sandbox gives the job to Customer service, to keep the number of demo roles small.

## Calculation

- **Money:** EUR only, without VAT. The ERP adds VAT when it invoices. Each charge line is rounded to cents, half away from zero, and the total is the sum of the rounded lines. The Minimum charge adjustment is worked out from the rounded Freight line: 100.25 kg at 14.00 per 100 kg is a Freight line of 14.04 (from 14.035), and with a minimum of 45.00 an adjustment of 30.96.
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

The TMS holds a read copy of each Shipper's Rate Card and never edits it (ADR 0006). Until the ERP exists, the TMS loads Rate Cards from the seed file at startup, the same way it loads Shippers. A Rate Card has versions, and each version has a validity period and its own Zones and Lanes, each Lane with its Weight Breaks and minimum charge.

Zones may nest, a narrower prefix inside a wider one, because the most specific prefix decides. Two Zones of one version overlap when they claim the same prefix in the same country, as then neither is more specific. A Rate Card whose Zones overlap is rejected when the seed loads, and the TMS doesn't start.

The demo seed gives Brouwer Dranken a Rate Card with Lanes from Rotterdam into the Benelux and a deliberate gap: no Lane into Germany. A booking from Rotterdam to Brouwer Dranken's Antwerp address is Rated. A booking from Rotterdam to Düsseldorf shows `NoMatchingLane` and lands in the rating exceptions list. The live "fix the Rate Card, then re-rate" demo waits for the ERP sync. Until then, tests cover that path.

## Events

Both events are published to the topic for Transport Order events, through the TMS's transactional outbox (ADR 0003).

| Event | Carries | Consumers |
|---|---|---|
| `TransportOrderRated` | Transport Order number, Shipper, Freight Charge with charge lines, Rate Card version, correlation ID | Monitoring. [P9](p9-freight-settlement.md) checks whether the order is Billable inside the TMS, not from this event (ADR 0003) |
| `TransportOrderRatingFailed` | Transport Order number, Shipper, failure code and message, correlation ID | Monitoring |

*Not rated separately* publishes no event. Each successful rating or re-rating publishes exactly one event.

## Tracking

Rating continues the Message trail of the Transport Order's intake: its events carry the intake's correlation ID. A re-rate continues it too, so a Transport Order's whole rating, re-rates included, is found by its intake's correlation ID. The rating worker, and the TMS API for a re-rate, put the tracking-contract keys in the logging scope and on the trace of each rating: `correlationId`, `flow` (`transport-order-rating`), `partyId` (the Shipper ID) and `transportOrderNumber`. The outcome event carries the same keys as application properties. When Rating failed, the log scope, the trace and `TransportOrderRatingFailed` also carry `failureCode`.

A rating that gives its answer, Rated or Rating failed, and a re-rate that does, records the `outcome` `Succeeded` in its log scope and on its trace, next to its `failureCode` when Rating failed. Rating failed is Rating's answer, not a failed flow: the order stays Accepted, and nothing is retried or dead-lettered, so the Integration console shows the failure code on the rating's hop without counting the trail as one with a failure. Customer service works these from the rating exceptions. A rating that doesn't give its answer, because it threw, records `Retrying`: the order stays Not yet rated and the next sweep rates it. A re-rate the TMS refuses, because the order isn't Rating failed or doesn't exist, records `Refused`.

## Presenter smoke run

After a deployment, and not in CI, the presenter checks Rating on the deployed estate with the demo seed's two bookings. Do it after the demo smoke run of the Norvia Partner API ([infra/smoke-test-partner-api.sh](../../../infra/smoke-test-partner-api.sh)), signed in as the presenter the estate was provisioned for. The postdeploy hook prints each UI's address. Open the Partner portal and the TMS UI side by side.

1. **Book to Antwerp.** In the Partner portal, choose Brouwer Dranken in the Persona switcher. The booking form opens with Brouwer Dranken's Rotterdam warehouse as the collection and Café De Zwaan in Utrecht as the delivery, with 2 pallets of 850 kg. Change the delivery to Drankenhandel De Schelde BV (contact Pieter Janssens, +32 3 555 0117), Noorderlaan 120, 2030 Antwerpen, BE, and choose *Book transport*. Note the Transport Order number and Norvia's reference for the attempt under it, which is the booking's correlation ID.
2. **Book to Düsseldorf.** Choose *Start a new booking*, change the delivery to Getränke Schmitz GmbH (contact Jonas Schmitz, +49 211 555 0190), Kölner Landstraße 301, 40591 Düsseldorf, DE, and book it. Note its Transport Order number and reference too.
3. **Check the TMS.** Run `infra/smoke-test-rating.sh <Antwerp's number> <Düsseldorf's number>`. It reads the TMS as the intake identity, from a throwaway Container Apps job, and reads each order's Message trail in Application Insights, which is usually 1 to 5 minutes behind. It checks that:
   - the Antwerp order is Rated, with a Freight Charge in EUR that has a Freight line, and is waiting for planning;
   - the Düsseldorf order is Rating failed with `NoMatchingLane` and has no Freight Charge, is in the rating exceptions, and is still waiting for planning;
   - each order's intake and rating are on one Message trail under the intake's correlation ID, the rating for Brouwer Dranken (`SHP-0001`) with the outcome `Succeeded`, and with `failureCode` `NoMatchingLane` for Düsseldorf's only.

   It prints `PASS` or `FAIL` per check, never a body, and ends with the pages to open below.
4. **See it Rated.** In the TMS UI, as the Planner, both orders are in *Waiting for planning*: Antwerp's with the rating result Rated, Düsseldorf's with Rating failed. Open Antwerp's. Its Rating section shows Rated and a Freight Charge of EUR 105.40: one Freight line, 850 kg at EUR 12.40 per 100 kg on the Rotterdam → Antwerpen Lane, which is above the Lane's EUR 85.00 minimum.
5. **See the exception.** Switch to Customer service and open *Rating exceptions*. Düsseldorf's order is listed for Brouwer Dranken with the failure *No matching Lane* and a reason that Rate Card `RC-0001` version 1 has no Lane from Rotterdam to the delivery address. Antwerp's isn't listed. Don't re-rate it: the Rate Card still has no Lane into Germany, so a re-rate fails again with the same reason until the ERP sync can correct it.
6. **Follow the rating.** In the Integration console, search for each order's correlation ID and open the one trail it finds. Its hops hold the intake (`transport-order-intake`) and, after it, the rating (`transport-order-rating`). The rating's hop shows the outcome `Succeeded`, and on Düsseldorf's trail also the failure code `NoMatchingLane`: Rating failed is Rating's answer, not a failed flow, so neither trail is among the trails with a failure.

If a check fails, file the gap as its own issue against P2, rather than working around it in the run book.

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
