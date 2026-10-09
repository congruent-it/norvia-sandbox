# P9 Freight settlement

Freight settlement makes sure Norvia is paid for each Transport Order it delivers, and pays each Carrier the price it agreed. The TMS decides when a Transport Order is Billable and hands it to the ERP with its frozen Freight Charge. It prices each Carrier booking at release and hands its Carrier cost to the ERP once the Shipment is Delivered. The ERP does the rest: Invoice runs, Credit notes and Matching Carrier invoices. This document covers the TMS side. The ERP side is [E1 Invoicing](../erp/e1-invoicing.md) and [E2 Carrier invoice matching](../erp/e2-carrier-invoice-matching.md). Terms are defined in [GLOSSARY.md](../../../GLOSSARY.md).

| | |
|---|---|
| **Trigger** | A Transport Order is Delivered (P5). For the Carrier cost, a Carrier Shipment is Released (P3, P4) |
| **Outcome** | A Billable Transport Order handed to the ERP with its Freight Charge. For a Carrier Shipment, a frozen Carrier cost on its Carrier booking, handed to the ERP once Delivered |
| **Owning system** | TMS for Billable and the Carrier cost (ADR 0012, ADR 0013). The ERP owns invoicing, Carrier Rate Cards and Carrier invoices (ADR 0002) |
| **Applies to** | Both for the Shipper side. The Carrier cost is Carriers only. Own fleet has no Carrier cost |
| **Integrations touched** | Service Bus (Transport Order and Shipment events), Billing Logic App, Carrier cost Logic App, ERP billing API. Partner portal (Carrier Personas) through P4. ERP, through the seed file for now (Carrier Rate Cards) |

## Scope

P9 is the first process that needs the ERP, and it is where the ERP is first built (ADR 0001), as an invoicing slice. The two halves are split by owner:

| Part | Where it is designed |
|---|---|
| Billable, the Carrier cost, the events that hand them over | Here |
| The Invoice run, Shipper invoices, Credit notes, sending invoices to the Shipper | [E1 Invoicing](../erp/e1-invoicing.md) |
| Receiving Carrier invoices, Matching, telling the Carrier | [E2 Carrier invoice matching](../erp/e2-carrier-invoice-matching.md) |

Nothing flows back from the ERP to the TMS (ADR 0012). The TMS never learns an invoice number, and Finance never changes anything in the TMS.

## Billable

### Billing state

Each Transport Order has a billing state that is separate from its status and its rating result, as the rating result is separate from the status in P2.

| Billing state | Meaning |
|---|---|
| Not billed separately | A Fulfilment Transport Order (WMS channel), whose rating result is Not rated separately (P2). It is billed through the Fulfilment Contract. P9 does nothing more with it |
| Not yet billable | A Transport-only order that doesn't meet the conditions below yet. It carries its blocking reasons |
| Billable | It met the conditions. `TransportOrderBillable` has been published with the frozen Freight Charge. Final |

A Transport-only order starts as Not yet billable when P1 accepts it. A Fulfilment order starts as Not billed separately.

### Conditions

A Transport Order becomes Billable when all of these hold ("no POD, no invoice", ADR 0012):

| Condition | Blocking reason while it doesn't hold |
|---|---|
| Its Shipment is Delivered (P5) | `NotDelivered` |
| Its rating result is Rated (P2) | `NotRated`, for Not yet rated or Rating failed |
| Its Shipment has a current Proof of delivery (P6), or Customer service has released it without one | `NoProofOfDelivery` |
| Its Shipment has no open Exception (P7) | `OpenException` |

The TMS checks the conditions in the same transaction as each change that can complete them, and never from a Service Bus event (ADR 0003):

- The Shipment moves to Delivered (P5).
- The Transport Order is Rated, or re-rated after Rating failed (P2).
- A Proof of delivery is recorded (P6). If it has reservations, P7 opens the Exception in the same transaction first, so the order stays blocked.
- An Exception is resolved (P7), whatever its outcome.
- Customer service releases the order without a Proof of delivery.

When the last condition is met, the TMS sets the billing state to Billable and publishes `TransportOrderBillable` in that transaction.

### Billable is final

Once Billable, a Transport Order stays Billable. Nothing that happens later reopens it or publishes the event again: a Proof of delivery that replaces the current one, a Delivery failed or Damaged Tracking event after Delivered, or a new Exception. A charge that turns out to be wrong is corrected with a Credit note in the ERP ([E1](../erp/e1-invoicing.md#credit-notes)). Claims for damage or a shortage are settled outside the TMS and the ERP, as P7's `ClaimHandledOutsideTms` says. They never create a Credit note automatically.

### Releasing without a Proof of delivery

A Carrier that never sends a Proof of delivery would otherwise hold up billing for good. So Customer service can release a Delivered Transport Order without one, from the list of [Transport Orders not yet billable](#customer-services-list), with a required remark, for example "CMR received by post, scanned in the dossier". The TMS records who released it, when, and the remark. The release lifts only `NoProofOfDelivery`. The other conditions still apply. A Proof of delivery that arrives later is recorded as usual (P6) and changes nothing for billing.

The release is allowed only for a Transport Order that is Not yet billable and whose Shipment is Delivered.

## Carrier cost

### Carrier Rate Cards in the TMS

The ERP owns Carrier Rate Cards (ADR 0013). The TMS holds a read copy of each Carrier's Carrier Rate Card and never edits it. Until the ERP sync exists, the TMS loads them from the seed file at startup, the same way it loads Rate Cards (P2). A Carrier Rate Card has the same shape as a Rate Card: Zones, Lanes, Weight Breaks with a price per 100 kg, a minimum charge per Lane, and versions with validity periods. Overlapping Zones are rejected when the seed loads, as in P2.

### Pricing a Carrier booking at release

When the Planner releases a Carrier Shipment (P3), the TMS calculates its Carrier cost in the same transaction that creates the Carrier booking (P4). It runs P2's steps 3 to 6 against the Carrier's Carrier Rate Card instead of the Shipper's Rate Card:

1. Take the Carrier Rate Card version that is valid on the collection date, with the same collection-date rule as P2.
2. Match the Lane from the collection and delivery addresses.
3. Choose the Weight Break from the total gross weight.
4. Calculate the Freight line, and add a Minimum charge adjustment line when the minimum applies. The money rules are the same as P2's: EUR, without VAT, each line rounded to cents.

The Carrier cost is frozen on the Carrier booking, with its charge lines, its total, its source (*Carrier Rate Card*, with the version used) and the time it was calculated. It is never recalculated. A new release after a rejection creates a new Carrier booking with a newly calculated Carrier cost.

### When the Carrier Rate Card can't price the Shipment

If any step fails, the release of that Shipment fails with the same failure codes as P2 (`NoRateCard`, `NoValidRateCardVersion`, `NoMatchingLane`, `WeightAboveHighestBreak`), naming the Carrier. In a multi-select release, each Shipment succeeds or fails on its own, as in P3.

The Planner can then release that Shipment on its own with a **Spot price**: an amount in EUR, without VAT, and a required remark, for example "agreed with Van Dijk by phone, €480". The Carrier cost then has one *Freight* charge line, the source *Spot*, and the remark. A Spot price is allowed only after the Carrier Rate Card has failed for that release. The Planner can't use one to override a Carrier Rate Card that prices the Shipment.

### What the Carrier sees

The Carrier cost goes to the Carrier in the booking request: the total, its charge lines and whether it is a Spot price ([P4](p4-carrier-booking.md#carrier-booking-format)). When the Carrier confirms, it has accepted that price. When it doesn't accept the price, it rejects the booking with the reason code `PriceNotAccepted`, and the Shipment goes back to Planned as for any rejection. The Planner can release it again, to the same Carrier with a Spot price if the Carrier Rate Card failed, or to another Carrier. There is no counter-offer.

The Shipper's Freight Charge is still never sent to the Carrier.

### Handing the Carrier cost to the ERP

When a Carrier Shipment moves to Delivered (P5), the TMS publishes `CarrierCostIncurred` in the same transaction, for the Confirmed Carrier booking that carried it. The Carrier has earned its price once it delivers, so this doesn't wait for a Proof of delivery or for the Transport Order to be Billable.

Only that Confirmed Carrier booking has a Carrier cost to pay. Rejected, Not delivered, Withdrawn and Cancelled bookings never publish one. Cancellation fees and waiting time are deferred. The Carrier cost is final in the TMS. Any difference with the Carrier's invoice is settled during Matching in the ERP ([E2](../erp/e2-carrier-invoice-matching.md)).

## Steps

### Shipper side

1. **Deliver.** The Shipment moves to Delivered (P5, or P12 for Own fleet). The TMS checks the [conditions](#conditions).
2. **Complete.** The remaining conditions are met, in any order: Rating (P2), a Proof of delivery (P6), Exceptions resolved (P7), or Customer service releasing the order without a Proof of delivery. Each of them checks the conditions again.
3. **Billable.** The last condition is met. The TMS sets the billing state to Billable and publishes `TransportOrderBillable` in the same transaction. The Transport Order leaves Customer service's list.
4. **Hand over.** The Billing Logic App picks up the event and calls the ERP billing API to add a billable line for the Shipper ([E1](../erp/e1-invoicing.md#steps)).

### Carrier side

1. **Price.** The Planner releases a Carrier Shipment (P3). The TMS calculates the Carrier cost, or the Planner enters a Spot price, and it is frozen on the new Carrier booking.
2. **Book.** The booking request carries the Carrier cost (P4). The Carrier confirms it or rejects it, possibly with `PriceNotAccepted`.
3. **Deliver.** The Shipment moves to Delivered (P5). The TMS publishes `CarrierCostIncurred` in the same transaction.
4. **Hand over.** The Carrier cost Logic App picks up the event and calls the ERP billing API to add the Carrier cost ([E2](../erp/e2-carrier-invoice-matching.md#steps)).

### When the ERP can't be reached

On a server error or timeout from the ERP billing API, the Billing and Carrier cost Logic Apps retry with a bounded exponential policy, as in P4. When the retries are used up, the message goes to the subscription's dead-letter queue and the run fails. The ERP billing API is idempotent on the Transport Order number and the Carrier booking number, so a message that is resubmitted from the dead-letter queue is safe. The TMS isn't told either way: it has already done its part (ADR 0012).

## Customer service's list

P9 adds a list for Customer service in the TMS UI, next to P2's rating exceptions list.

| List | Shows | Actions |
|---|---|---|
| Transport Orders not yet billable (P9) | Transport-only orders whose Shipment is Delivered and whose billing state is Not yet billable: Transport Order number, Shipper, Shipper reference, delivery time, and the blocking reasons (`NotRated`, `NoProofOfDelivery`, `OpenException`). Oldest delivery first | Release without Proof of delivery (only when `NoProofOfDelivery` is one of the reasons). Open the rating exception (P2) or the Shipment's timeline |

Transport Orders that aren't Delivered yet aren't listed. They aren't late for billing, just not finished. The list updates without a page refresh, as the other lists do.

The Shipment's timeline shows the billing state, when it became Billable, and any release without a Proof of delivery with its remark. The Carrier booking on the timeline shows its Carrier cost and source.

## Planner's release

P9 changes one thing for the Planner: releasing a Carrier Shipment now prices it. A release that fails on the Carrier Rate Card shows the failure code in the list of Shipments to release, with a **Release with Spot price** action for that Shipment. The list of bookings awaiting an answer (P4) shows each booking's Carrier cost and source.

## TMS API

P9 adds no operations for Logic Apps. Everything happens in TMS transactions or in the TMS UI:

| Action | Who | Permissions |
|---|---|---|
| Release without Proof of delivery | Customer service, in the TMS UI | Customer service permissions from P2 |
| Release with Spot price | Planner, in the TMS UI | Planner permissions from P3 |

## Correlation ID

`TransportOrderBillable` carries the correlation ID of the transaction that made the order Billable: that of the Tracking event, Proof of delivery or re-rate, or a new one from the TMS for a Planner or Customer service action, as in P3. The Transport Order's intake correlation ID travels with it, so a Transport Order can be traced from intake to its Shipper invoice.

`CarrierCostIncurred` carries the Delivered Tracking event's correlation ID and the Carrier booking number. The Logic Apps carry both on to the ERP, which stores them on the billable line and the Carrier cost.

## Events

Both events go through the TMS's transactional outbox (ADR 0003).

| Event | Topic | Published when | Carries | Consumers |
|---|---|---|---|---|
| `TransportOrderBillable` | Transport Order events | A Transport Order becomes Billable | Transport Order number, Shipper, Shipper reference, Shipment number, delivery time, Freight Charge with charge lines and Rate Card version, whether it was released without a Proof of delivery, intake and billing correlation IDs | [Subscribers](../../architecture/events.md#catalogue) |
| `CarrierCostIncurred` | Shipment events, Carrier only | A Carrier Shipment moves to Delivered | Carrier booking number, Carrier booking reference, Shipment number, Transport Order number, Carrier ID, Carrier cost with charge lines, source (Carrier Rate Card version, or Spot with its remark), delivery time, correlation ID | [Subscribers](../../architecture/events.md#catalogue) |

The release without a Proof of delivery publishes nothing of its own. If it makes the order Billable, `TransportOrderBillable` says so.

## Demo seed

The seed file gets Carrier Rate Cards, in the same shape as the Rate Cards from P2:

| Carrier | Carrier Rate Card | Role in the demo |
|---|---|---|
| Van Dijk Transport | Benelux Lanes, and a deliberate gap: no Lane into Germany | Releasing Rotterdam → Düsseldorf to Van Dijk fails with `NoMatchingLane`, and the Planner enters a Spot price. Van Dijk answers by hand, so the presenter can confirm the Spot price or reject it with `PriceNotAccepted` |
| Kessler Spedition | Benelux and German Lanes | The Carrier Rate Card prices Rotterdam → Düsseldorf |
| PakketPunt Express | Every Lane in the seed's Zones | Always priced |

Each Carrier cost sits about 15–25% below the Freight Charge for the same Lane and weight, so the margin in the ERP looks believable.

## Deferred

These were left out on purpose while designing P9, each with its reason.

- **Invoicing Fulfilment orders.** They are billed through the Fulfilment Contract, which needs WMS data and a warehouse tariff.
- **Sending invoice numbers back to the TMS.** Nothing in the TMS needs them (ADR 0012).
- **The ERP → TMS sync of Rate Cards and Carrier Rate Cards.** Both come from the seed file for now, as in P2.
- **Cancellation fees, waiting time and other Carrier surcharges.** Only the Confirmed Carrier booking that carried the Shipment has a Carrier cost.
- **Price negotiation with a Carrier.** A Carrier that doesn't accept the price rejects the booking with `PriceNotAccepted`.
- **A Spot price that overrides a Carrier Rate Card,** for example in peak weeks. It weakens the baseline for Matching. It can be added with its own permission.
- **Choosing a Carrier on cost in P3.** The Carrier costs exist now, but planning stays manual.
- **Carrier costs for Own fleet** (fuel, Driver hours). Own fleet has no Carrier invoice to match.
- **Holding billing on other conditions,** such as a Shipper credit block. Credit management belongs to the ERP and is deferred with payments.
- **As-found toggles and presenter-panel faults.** They are added after the fixed state is built, as for P1 to P8. The candidates on the TMS side:
  - Toggle: no alert on the dead-letter queue of the `TransportOrderBillable` subscription. Together with the fault below, this replays the headline incident for revenue: the order is delivered, and it is never invoiced.
  - Presenter-panel fault: the ERP billing API is down, so the Billing and Carrier cost Logic Apps dead-letter their messages.
  - The ERP side has its own candidates, in [E1](../erp/e1-invoicing.md#deferred) and [E2](../erp/e2-carrier-invoice-matching.md#deferred).
