# E2 Carrier invoice matching

Carrier invoice matching checks what a Carrier asks to be paid against the Carrier cost Norvia agreed for each Carrier booking. The TMS hands the ERP each Carrier cost once the Shipment is Delivered. The Carrier sends a Carrier invoice from its Persona through API Management. The ERP matches each line, approves an invoice that matches in full, puts the rest in front of Finance, and tells the Carrier the outcome. This is the ERP half of [P9 Freight settlement](../tms/p9-freight-settlement.md). Terms are defined in [GLOSSARY.md](../../../GLOSSARY.md).

| | |
|---|---|
| **Trigger** | A Carrier invoice is submitted by a Carrier. `CarrierCostIncurred` from the TMS (P9) supplies what it is matched against |
| **Outcome** | An Approved Carrier invoice: every line Matched, Accepted or Rejected. The Carrier told the outcome of each line |
| **Owning system** | ERP. It owns Carrier invoices (ADR 0002). The TMS owns the Carrier cost until it hands it over (ADR 0013) |
| **Applies to** | Carriers only |
| **Integrations touched** | Service Bus (Shipment events, Invoicing events), Carrier cost Logic App, Partner portal (Carrier invoice screens and endpoint), API Management, Carrier invoice Logic App, Carrier invoice outcome Logic App, Key Vault, ERP billing API |

## Carrier costs

The ERP keeps one **Carrier cost** per Carrier booking that the TMS handed over: the Carrier booking number, the Carrier booking reference, the Carrier, the Shipment number, the Transport Order number, the charge lines and total, the source (Carrier Rate Card version, or Spot with its remark) and the delivery time. The Carrier cost Logic App picks up `CarrierCostIncurred` and calls the ERP billing API to record it. A repeat for the same Carrier booking number with the same content returns the original, flagged as a repeat.

A Carrier cost is **Open** until a Carrier invoice line is Matched or Accepted against it, and then **Invoiced**.

## Carrier invoice

A Carrier sends one Carrier invoice for any number of its Carrier bookings.

| Part | Required |
|---|---|
| The Carrier's own invoice number. Unique per Carrier | Yes |
| Invoice date | Yes |
| Lines: Carrier booking reference, amount in EUR without VAT, an optional description | At least one |

The Carrier comes from the token, never from the payload (ADR 0005). Amounts are without VAT, and VAT on Carrier invoices is deferred with Dutch VAT rules ([E1](e1-invoicing.md#deferred)).

## Matching

The ERP matches each line on its own, against the Carrier cost with that Carrier booking reference.

| Line result | When |
|---|---|
| Matched | The Carrier cost exists for this Carrier, is Open, and the amount is within tolerance of its total |
| Price mismatch | The Carrier cost exists for this Carrier and is Open, but the amount is outside tolerance |
| Unknown booking | No Carrier cost with that reference has arrived for this Carrier. That includes a reference that belongs to another Carrier |
| Duplicate | The Carrier cost is already Invoiced by an earlier line |

The **tolerance** is an ERP setting, in EUR, €0 by default: the amount must equal the Carrier cost to the cent.

Matching runs in the same transaction that records the Carrier invoice. A Matched line is accepted at once and its Carrier cost becomes Invoiced. The other lines wait for Finance.

Carrier costs and Carrier invoices arrive on separate paths, so a Carrier invoice can arrive before the Carrier cost it refers to. When a Carrier cost is recorded, the ERP matches again every undecided Unknown booking line with that Carrier and Carrier booking reference, in the same transaction. The line gets its new result, and it can be Matched and accepted then.

## Status

### Carrier invoice line

| Status | Meaning |
|---|---|
| Matched | Accepted automatically at the invoiced amount. Final |
| To decide | Price mismatch, Unknown booking or Duplicate. It waits for Finance, and an Unknown booking line can still become Matched |
| Accepted | Finance accepted it, at the invoiced amount or an adjusted amount, with a remark when adjusted. Its Carrier cost becomes Invoiced. Final |
| Rejected | Finance rejected it, with a reason. Its Carrier cost, if any, stays as it was. Final |

Finance can accept only a line that has an Open Carrier cost for this Carrier. An Unknown booking or Duplicate line can only be rejected, or left for a late Carrier cost to arrive.

### Carrier invoice

| Status | Meaning |
|---|---|
| Received | Recorded, with at least one line To decide |
| Approved | Every line is Matched, Accepted or Rejected. The approved amount is the sum of the Matched and Accepted amounts. Final |

A Carrier invoice whose lines all match is Approved in the transaction that records it. Paying the approved amount is out of scope, and Approved is the end of E2.

## Steps

1. **Draft.** The presenter plays a Carrier Persona in the Partner portal and opens its **draft invoice** screen. It lists the Persona's Confirmed bookings that it has reported Delivered and hasn't invoiced yet, each with the price from the booking request (P4). The presenter selects bookings and can edit an amount, to show a mismatch on purpose.
2. **Submit.** The portal calls API Management with the Persona's token and subscription key.
3. **Check at the edge.** API Management checks the token (audience "Norvia Partner API", role `CarrierInvoices.Submit`). A Shipper Persona's token gets 403. API Management maps the `azp` claim to a Carrier ID (ADR 0005), creates a correlation ID and passes both to the Carrier invoice Logic App.
4. **Map.** The Logic App maps the [Carrier invoice format](#carrier-invoice-format) to the Canonical Model and calls the ERP billing API.
5. **Record and match.** The ERP checks the [receiving rules](#receiving-rules), records the Carrier invoice and matches its lines, all in one transaction. The Logic App relays the ERP's answer to the Carrier: received, with each line's result. The path is synchronous, as the Carrier's booking answer is in P4, so the Carrier knows its invoice arrived.
6. **Decide.** A Carrier invoice with lines To decide appears in Finance's list. Finance accepts or rejects each line. When the last line is decided, the invoice becomes Approved.
7. **Tell the Carrier.** When a Carrier invoice becomes Approved, whether on receipt or after Finance, the ERP publishes `CarrierInvoiceApproved`. The Carrier invoice outcome Logic App maps it and calls the Carrier Persona's invoice outcome endpoint with credentials from Key Vault, and reports it to the ERP as told, as in P7.

On a server error or timeout from the Carrier's endpoint, the outcome Logic App retries as in P4. When the retries are used up, or on a client error, it reports the outcome as not delivered. The ERP flags the invoice **Carrier not told** with the technical cause, and Finance phones the Carrier.

A Carrier with a Rejected line sends a new Carrier invoice, with a new invoice number, for whatever it still claims. The Duplicate rule makes sure no Carrier cost is paid twice.

## Receiving rules

| Rule | Failure code |
|---|---|
| The Carrier exists in the ERP's seed-loaded Carrier data | `CarrierNotFound` |
| The Carrier hasn't used this invoice number for different content | `InvoiceNumberUsed` |
| There is at least one line, and each line has a Carrier booking reference and an amount above zero | Client error |
| No Carrier booking reference appears twice in the same invoice | Client error |

A submission that repeats one already recorded (the same Carrier, invoice number and content) succeeds, returns the original result flagged as a repeat, and publishes no event. Each failure has a stable code and a readable message. A line whose Carrier booking reference is unknown doesn't fail the invoice. It becomes Unknown booking.

## Carrier invoice format

The Carrier invoice format follows the conventions of the [Carrier booking format](../tms/p4-carrier-booking.md#carrier-booking-format): JSON with its own field names and local dates. The Carrier invoice Logic App maps Carrier → Canonical, and the outcome Logic App maps Canonical → Carrier.

The outcome carries the Carrier's invoice number, the approved amount and, per line, the Carrier booking reference, the result (`MATCHED`, `ACCEPTED`, `ACCEPTED_ADJUSTED` or `REJECTED`), the accepted amount, and Finance's remark or reason.

## ERP UI

| List | Shows | Actions |
|---|---|---|
| Carrier invoices to decide | Received Carrier invoices, oldest first: Carrier, Carrier invoice number, date, total, the number of lines per result | Open. Per line: accept, accept at an adjusted amount (with a remark), reject (with a reason) |
| Carrier invoices | All Carrier invoices, newest first, with their status, approved amount, and a Carrier told time or "Carrier not told" | Open |
| Carrier cost Logic App | [src/Integration/CarrierCostLogicApp](../../../src/Integration/CarrierCostLogicApp/), [carrier-cost-logic-app.bicep](../../../infra/estate/carrier-cost-logic-app.bicep) | A stateful workflow, `carrier-costs`, on the `carrier-cost` subscription of `shipment-events` (the filter is `(eventType = 'CarrierCostIncurred' AND resubmitTo IS NULL) OR resubmitTo = 'carrier-cost'`, and only Carrier Shipments publish the event). It posts the message body to `POST /v1/carrier-costs` as it is, because the ERP accepts the Canonical Model and there is nothing to map, as the Carrier cost Logic App's identity with the event's correlation ID in `X-Correlation-ID` and as the run's client tracking ID. A `2xx` completes the message, a repeat (`200`) included. A timeout, `408`, `429` or a server error is retried three times (exponential, 5 s up to 20 s); another client error and a conflict are not. When the Carrier cost is refused or the retries are used up, the message is dead-lettered with the ERP's code as `DeadLetterReason` (`CarrierNotFound`, `CarrierCostConflict`, `CarrierCostWithoutChargeLines`, `CarrierCostTotalMismatch`, or `ErpCallRefused` and `ErpUnavailable`) and its message as `DeadLetterErrorDescription`, and the run fails. A message that isn't a `CarrierCostIncurred` is dead-lettered as `CarrierCostUnreadable`. The run tracks `carrier-costs` with the Carrier booking number, Shipment number, Transport Order number, the Carrier as `partyId` and the event ID as `sourceMessageId`. The identity holds `CarrierCosts.Record` and the receiver role on its subscription, and nothing else. It has no Key Vault and no local function. Locally the AppHost runs `Norvia.Integration.LocalCarrierCostWorker` in its place ([ADR 0045](../../adr/0045-locally-a-hosted-worker-plays-the-p9-carrier-cost-logic-app.md)), so a delivered Carrier Shipment reaches the Open Carrier costs list; CI tests the workflow's definition and the [sample messages](../../../contracts/canonical-model/samples/carrier-cost-incurred/) without Azure |
| Open Carrier costs | Carrier costs not yet Invoiced, per Carrier, oldest delivery first, with their source. A Spot price is marked | Open |

On opening a line, Finance sees its Carrier cost next to it: the charge lines, the source and, for a Spot price, the Planner's remark. The lists update without a page refresh.

## Partner portal

- Each Carrier Persona gets the **draft invoice** screen from step 1. It works from the Persona's own records of its bookings and Tracking events, so it needs no call to Norvia.
- Each Carrier Persona gets an **invoice outcome endpoint** and an **invoices** page listing its submitted Carrier invoices with the outcome per line once told.

## ERP API

| Operation | Caller | Role |
|---|---|---|
| Record a Carrier cost | Carrier cost Logic App | `CarrierCosts.Record` |
| Receive a Carrier invoice | Carrier invoice Logic App | `CarrierInvoices.Receive` |
| Report a Carrier invoice outcome as told or not delivered | Carrier invoice outcome Logic App | `CarrierInvoices.Report` |

Finance decides lines in the ERP UI, with Finance permissions. The ERP billing API accepts only the Canonical Model (ADR 0003).

## Correlation ID

API Management creates a new correlation ID for each Carrier invoice, as it does for each Carrier answer in P4. The ERP stores it on the Carrier invoice, and `CarrierInvoiceApproved` carries it. The Carrier cost keeps the correlation ID from `CarrierCostIncurred`. Tracing a Carrier booking from release to payment approval follows the Carrier booking number in the TMS and the Carrier booking reference on the invoice line.

## Events

All events go to the topic for Invoicing events, through the ERP's transactional outbox (ADR 0003).

| Event | Published when | Carries | Consumers |
|---|---|---|---|
| `CarrierInvoiceApproved` | A Carrier invoice becomes Approved | Carrier ID, the Carrier's invoice number, approved amount, per line the Carrier booking reference, result, accepted amount and remark or reason, correlation ID | Carrier invoice outcome Logic App. Monitoring |
| `CarrierInvoiceOutcomeNotDelivered` | The outcome is reported not delivered | Carrier ID, the Carrier's invoice number, technical cause, correlation ID | Monitoring. An alert later |

Recording a Carrier cost, receiving a Carrier invoice that still has lines To decide, and deciding a line that isn't the last publish nothing.

## Implementation

For engineers and coding agents. Built so far: the Carrier cost Logic App, recording a Carrier cost and the Open Carrier costs list, receiving and matching a Carrier invoice with the Carrier invoices list, and Finance deciding lines. The Carrier invoice Logic App and its API Management operation, matching again when a Carrier cost arrives, and the outcome are not built yet.

| Piece | Where | Does |
|---|---|---|
| Record a Carrier cost | [src/Erp/Norvia.Erp.Application/CarrierCosts](../../../src/Erp/Norvia.Erp.Application/CarrierCosts/Record/) | `RecordCarrierCostCommand` and its handler: idempotent on the Carrier booking number, `CarrierNotFound` for an unknown Carrier, `CarrierCostConflict` for different content. The domain refuses charge lines that are missing or don't add up to the total. It publishes nothing |
| Open Carrier costs | [src/Erp/Norvia.Erp.Application/CarrierCosts/ListOpen](../../../src/Erp/Norvia.Erp.Application/CarrierCosts/ListOpen/), [src/Erp/Norvia.Erp.Web/CarrierCosts](../../../src/Erp/Norvia.Erp.Web/CarrierCosts/) | The `/carrier-costs` page, for Finance only: Open Carrier costs per Carrier, oldest delivery first, each opening to its charge lines and source. A Spot price is marked with the Planner's remark. The list updates without a refresh |
| ERP billing API | [src/Erp/Norvia.Erp.Api/CarrierCosts](../../../src/Erp/Norvia.Erp.Api/CarrierCosts/) | `POST /v1/carrier-costs` takes only `CarrierCostIncurred` (the Canonical Model, so the Carrier cost Logic App has nothing to map) and needs the `CarrierCosts.Record` app role. 201 for a new Carrier cost, 200 with `repeat: true` for the same content again. Refusals are `ProblemDetails` with the stable `code`: `CarrierNotFound` (422), `CarrierCostConflict` (409), `CarrierCostWithoutChargeLines` and `CarrierCostTotalMismatch` (400). A body outside the Canonical Model is a 400 |
| Receive a Carrier invoice | [src/Erp/Norvia.Erp.Application/CarrierInvoices/Receive](../../../src/Erp/Norvia.Erp.Application/CarrierInvoices/Receive/), [src/Erp/Norvia.Erp.Domain/CarrierInvoices](../../../src/Erp/Norvia.Erp.Domain/CarrierInvoices/) | `ReceiveCarrierInvoiceCommand` and its handler: idempotent on the Carrier and its invoice number (`InvoiceNumberUsed` for different content), `CarrierNotFound` for an unknown Carrier. `CarrierInvoice.Receive` in the domain checks the receiving rules, matches every line against the Carrier's own Carrier costs, and makes a Matched line's Carrier cost Invoiced. An invoice whose lines all match is Approved in that transaction and `CarrierInvoiceApproved` is written to the outbox in it, so it is published once and only if the change committed. A concurrent invoice for the same Carrier cost conflicts on the Carrier cost's row version and is run again, finding it Invoiced (Duplicate) |
| Matching tolerance | [src/Erp/Norvia.Erp.Infrastructure/CarrierInvoices](../../../src/Erp/Norvia.Erp.Infrastructure/CarrierInvoices/) | The ERP setting `CarrierInvoices:MatchingTolerance`, in EUR, €0 by default and never negative; a bad value fails the ERP's start. A line matches when its amount is within the tolerance of the Carrier cost's total |
| Carrier invoices list | [src/Erp/Norvia.Erp.Application/CarrierInvoices/List](../../../src/Erp/Norvia.Erp.Application/CarrierInvoices/List/), [src/Erp/Norvia.Erp.Web/CarrierInvoices](../../../src/Erp/Norvia.Erp.Web/CarrierInvoices/) | The `/carrier-invoices` page, for Finance only: every Carrier invoice, newest first, with its Carrier, number, date, status, total, the number of lines per result and the approved amount. It updates without a refresh. The Carrier told time or flag follows with the outcome |
| Decide Carrier invoice lines | [src/Erp/Norvia.Erp.Application/CarrierInvoices/Decide](../../../src/Erp/Norvia.Erp.Application/CarrierInvoices/Decide/), [src/Erp/Norvia.Erp.Application/CarrierInvoices/List](../../../src/Erp/Norvia.Erp.Application/CarrierInvoices/List/), [src/Erp/Norvia.Erp.Web/CarrierInvoices](../../../src/Erp/Norvia.Erp.Web/CarrierInvoices/) | The `/carrier-invoices/to-decide` page, for Finance only: Received Carrier invoices, oldest first, with the number of lines per result, updating without a refresh. Opening a line shows its Carrier cost next to it (charge lines, source and, for Spot, the Planner's remark). `AcceptCarrierInvoiceLineCommand` accepts a line at the invoiced amount or at an adjusted amount with a remark, and `RejectCarrierInvoiceLineCommand` rejects it with a reason. Accept is offered only for a line with an Open Carrier cost for that Carrier, so an Unknown booking or Duplicate line offers reject only; the domain refuses the rest with stable codes (`CarrierInvoiceLineHasNoOpenCarrierCost`, `CarrierInvoiceAdjustedAmountNeedsRemark`, `CarrierInvoiceRejectionNeedsReason`, `CarrierInvoiceLineAlreadyDecided`). Accepting makes the Carrier cost Invoiced in the same save; rejecting leaves it as it was. When the last line is decided the invoice becomes Approved with the sum of the Matched and Accepted amounts, and `CarrierInvoiceApproved` is written to the outbox in that save; deciding any other line publishes nothing. Two decisions on one invoice conflict on its row version and the loser is run again |
| Carrier invoices API | [src/Erp/Norvia.Erp.Api/CarrierInvoices](../../../src/Erp/Norvia.Erp.Api/CarrierInvoices/) | `POST /v1/carrier-invoices` takes only the Canonical Model's [Carrier invoice](../../../contracts/canonical-model/carrier-invoice-submission.v1.schema.json) and needs the `CarrierInvoices.Receive` app role. The `carrierId` in the body is the Carrier API Management took from the token (ADR 0005); the Carrier invoice Logic App sets it, and what the Carrier wrote never does. 201 for a new Carrier invoice, 200 with `repeat: true` for the same content again, which publishes nothing. Refusals are `ProblemDetails` with the stable `code`: `CarrierNotFound` (422), `InvoiceNumberUsed` (409), and 400 for `CarrierInvoiceWithoutLines`, `CarrierInvoiceLineWithoutReference`, `CarrierInvoiceLineAmountNotAboveZero` and `CarrierInvoiceReferenceRepeated`. The ERP stores the correlation ID from the body. The Carrier invoices flow is `carrier-invoices`, with the Carrier invoice number as `documentNumber` and the Carrier as `partyId` |
| Canonical schemas | [contracts/canonical-model](../../../contracts/canonical-model/) | [carrier-cost-record.v1](../../../contracts/canonical-model/carrier-cost-record.v1.schema.json) is the ERP's record of a Carrier cost, and [carrier-cost-recording.v1](../../../contracts/canonical-model/carrier-cost-recording.v1.schema.json) is the API's answer. For Carrier invoices, [carrier-invoice-submission.v1](../../../contracts/canonical-model/carrier-invoice-submission.v1.schema.json) is what the API takes, [carrier-invoice.v1](../../../contracts/canonical-model/carrier-invoice.v1.schema.json) the ERP's record, [carrier-invoice-receipt.v1](../../../contracts/canonical-model/carrier-invoice-receipt.v1.schema.json) the API's answer and [carrier-invoice-approved.v1](../../../contracts/canonical-model/carrier-invoice-approved.v1.schema.json) the event |

The Carrier cost is traced on the `carrier-costs` flow, on the correlation ID of the Delivered Tracking event, with the Carrier booking number, Shipment number, Transport Order number and the Carrier as `partyId`.

## Deferred

These were left out on purpose while designing E2, each with its reason.

- **Paying Carriers.** Approved is the end of E2. Payment runs and bank files are a separate process.
- **VAT on Carrier invoices,** and Dutch VAT rules. Lines are without VAT.
- **Carrier credit notes.** A Carrier claims less by sending a new Carrier invoice. Rejected lines are simply not paid.
- **Cancellation fees, waiting time and other surcharges.** The TMS hands over only the Carrier cost of the booking that carried the Shipment (P9).
- **A percentage tolerance, and a tolerance per Carrier.** One fixed amount is enough for the demo.
- **EDI invoices (INVOIC).** All Carriers use the one JSON format, as for bookings in P4.
- **As-found toggles and presenter-panel faults.** They are added after the fixed state is built. The candidates:
  - Presenter-panel fault: a Carrier sends its invoice with the Carrier booking reference in a format the Carrier invoice Logic App mangles (for example with a prefix stripped), so every line lands as Unknown booking.
  - Toggle: no alert on the dead-letter queue of the `CarrierCostIncurred` subscription, so Carrier invoices wait on Unknown booking with no one told why.
  - Presenter-panel fault: a Carrier's invoice outcome endpoint is down, so the invoice is flagged Carrier not told.
