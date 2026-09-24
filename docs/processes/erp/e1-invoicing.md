# E1 Invoicing

Invoicing turns the Billable Transport Orders the TMS hands over into Shipper invoices, one per Shipper per Invoice run, and sends them to the Shipper. Finance corrects an issued invoice with a Credit note. This is the ERP half of [P9 Freight settlement](../tms/p9-freight-settlement.md). Terms are defined in [CONTEXT.md](../../../CONTEXT.md).

| | |
|---|---|
| **Trigger** | `TransportOrderBillable` from the TMS (P9). The monthly Invoice run, or Finance choosing **Invoice now** |
| **Outcome** | A Shipper invoice for each Shipper with billable lines, sent to the Shipper's Persona. Credit notes where Finance corrects one |
| **Owning system** | ERP. It owns Shipper invoices and Credit notes (ADR 0002, ADR 0012) |
| **Applies to** | Transport-only orders, whether Own fleet or a Carrier carried them. Fulfilment orders are not invoiced here |
| **Integrations touched** | Service Bus (Transport Order events, Invoicing events), Billing Logic App, ERP billing API, Shipper invoice Logic App, Key Vault, Partner portal (Shipper invoice endpoint and inbox) |

## Billable lines

The ERP keeps one **billable line** per Transport Order that the TMS made Billable. It holds what `TransportOrderBillable` carries: the Transport Order number, the Shipper, the Shipper reference, the Shipment number, the delivery time, the Freight Charge with its charge lines and Rate Card version, and whether it was released without a Proof of delivery. The ERP never recalculates a Freight Charge.

A billable line is either **Waiting**, until an Invoice run puts it on a Shipper invoice, or **Invoiced**, with that invoice's number. A line is invoiced once and only once.

## Steps

1. **Receive.** The Billing Logic App picks up `TransportOrderBillable` and maps it to the ERP's API. It calls the ERP billing API to record the billable line. The ERP checks the [billing rules](#billing-rules), records the line as Waiting and answers. A repeat for the same Transport Order number with the same content returns the original line, flagged as a repeat.
2. **Run.** An Invoice run collects every Waiting line for a Shipper (see [Invoice run](#invoice-run)).
3. **Issue.** For each Shipper with at least one Waiting line, the ERP creates a Shipper invoice with the next invoice number, sets its lines to Invoiced and publishes `ShipperInvoiceIssued`, all in one transaction. A Shipper without Waiting lines gets no invoice.
4. **Send.** The Shipper invoice Logic App picks up the event, maps it to the [Shipper invoice format](#shipper-invoice-format) and calls the Shipper Persona's invoice endpoint in the Partner portal, with credentials it reads from Key Vault. The endpoint acknowledges receipt. The Partner portal treats the invoice number as the key, so a repeated call is ignored.
5. **Report.** The Logic App reports the invoice to the ERP as Sent, and the ERP records the time.

On a server error or timeout, the Shipper invoice Logic App retries with a bounded exponential policy, as in P4. It doesn't retry a client error. When the retries are used up, or on a client error, it reports the invoice as **Not delivered**, with the technical cause, and the ERP publishes `ShipperInvoiceNotDelivered`. Finance sees the flag in the list of Shipper invoices and can **send again**, which publishes `ShipperInvoiceIssued` once more with the same invoice. If the Logic App can't reach the ERP either, its run fails and the invoice stays Issued without a sent time, which the list shows.

## Invoice run

| | |
|---|---|
| **Monthly** | On the 1st of each month at 06:00 Europe/Amsterdam, the ERP runs for every Shipper, over the Waiting lines whose delivery time is before the 1st |
| **Invoice now** | Finance chooses **Invoice now** for one Shipper in the ERP UI. It runs for that Shipper over all its Waiting lines. It exists so a demo doesn't wait a month |

A line that arrives after a run waits for the next one. Two runs can't invoice the same line: each run takes the Shipper's Waiting lines and sets them to Invoiced in one transaction.

## Shipper invoice

| Part | |
|---|---|
| Invoice number | `INV-<year>-<6 digits>`, from one gapless sequence per calendar year, as Dutch invoicing rules require. A number is only taken in the transaction that issues the invoice |
| Invoice date | The date of the run, Europe/Amsterdam |
| Shipper | From the ERP's seed-loaded Shipper data |
| Lines | One per Transport Order: Transport Order number, Shipper reference, delivery date, the charge lines and the line total. The Shipment number is kept on the line for Finance, and isn't sent to the Shipper |
| Total without VAT | The sum of the line totals |
| VAT | 21% of the total without VAT, rounded to cents, half away from zero, as in P2. Dutch VAT rules, including reverse charge, are deferred. Every seed Shipper is Dutch |
| Total with VAT | The two added up |
| Status | Issued, final. Sent, or Not delivered with the technical cause |

The Shipper invoice is never changed or deleted after it is issued. Corrections are Credit notes.

## Credit notes

Finance issues a Credit note from an issued Shipper invoice, for one of its lines:

- **Full or partial.** The amount without VAT is at most the line's total minus any earlier Credit notes on the same line.
- **A reason is required,** for example "Rate Card error on Lane NL-BE, see ticket 4411".
- **VAT** is 21% of the credited amount, rounded as for the invoice.
- **Numbering** is `CN-<year>-<6 digits>`, from its own gapless sequence per year.

The ERP issues the Credit note and publishes `CreditNoteIssued` in one transaction. The Shipper invoice Logic App sends it to the Shipper's Persona the same way as an invoice, with the same reports and the same Not delivered handling. The TMS never learns about it (ADR 0012).

Claims for damage or a shortage are settled outside the TMS and the ERP (P7). They never create a Credit note automatically. Finance can issue one by hand when a claim is settled against the invoice.

## Billing rules

| Rule | Failure code |
|---|---|
| The Shipper exists in the ERP's seed-loaded Shipper data | `ShipperNotFound` |
| The Freight Charge has at least one charge line, and its total is the sum of its lines | Client error |
| A Transport Order number already recorded with different content | `BillableLineConflict` |

Each failure has a stable code and a readable message. A client error or conflict fails the Billing Logic App's run, and the message is dead-lettered.

## Shipper invoice format

The Shipper invoice format follows the conventions of the Shipper booking format in P1: JSON with its own field names and local dates. The Shipper invoice Logic App maps ERP → Shipper. Towards the Shipper, Norvia carries the goods, so the format names neither the Carrier nor the Shipment number (ADR 0011).

| Field | Required |
|---|---|
| Document type: `INVOICE` or `CREDIT_NOTE` | Yes |
| Document number and date | Yes |
| For a Credit note: the invoice number and line it credits, and the reason | Yes |
| Lines: Transport Order number, Shipper reference, delivery date, charge lines with code and amount, line total | Yes |
| Totals without VAT, VAT and with VAT, in EUR | Yes |

The charge lines use the codes `FREIGHT` and `MINIMUM_CHARGE_ADJUSTMENT`.

## ERP UI

| List | Shows | Actions |
|---|---|---|
| Waiting billable lines | Per Shipper: the number of Waiting lines and their total, and each line on opening. A line released without a Proof of delivery is marked | Invoice now, per Shipper |
| Shipper invoices | Issued invoices and Credit notes, newest first: number, Shipper, date, totals, sent time or "Not delivered" | Open. Issue a Credit note (on an invoice line). Send again (when Not delivered) |
| Master data | Shippers, Carriers, Rate Cards and Carrier Rate Cards from the seed file | View only |

The lists update without a page refresh, as the TMS's do. Finance uses them with Finance permissions.

## Partner portal

Shipper Personas get an **invoice endpoint** and an **invoices inbox** that lists the Shipper invoices and Credit notes received, newest first, with their totals and lines. The endpoint has no answer modes. It acknowledges every valid message.

## ERP API

The ERP billing API accepts only the ERP's own model, which is the Canonical Model (ADR 0003). Each Logic App's managed identity holds only the role it needs.

| Operation | Caller | Role |
|---|---|---|
| Record a billable line | Billing Logic App | `BillableLines.Record` |
| Report a Shipper invoice or Credit note as sent or not delivered | Shipper invoice Logic App | `ShipperInvoices.Report` |

A sent or not-delivered report for a document that was already reported is ignored and logged, as in P7.

## Correlation ID

The billable line keeps the intake and billing correlation IDs from `TransportOrderBillable`. The ERP creates a new correlation ID for each Shipper invoice and each Credit note, and the Shipper invoice Logic App carries it on to the Partner portal and on its reports. Tracing a Transport Order from intake to its invoice follows the Transport Order number, which is on the invoice line.

## Events

All events go to a new topic for Invoicing events, through the ERP's transactional outbox (ADR 0003).

| Event | Published when | Carries | Consumers |
|---|---|---|---|
| `ShipperInvoiceIssued` | A Shipper invoice is issued, or Finance sends it again | The whole invoice with its lines and totals, Shipper, correlation ID | Shipper invoice Logic App |
| `CreditNoteIssued` | Finance issues a Credit note, or sends it again | The Credit note with the invoice and line it credits, amounts, reason, Shipper, correlation ID | Shipper invoice Logic App |
| `ShipperInvoiceNotDelivered` | An invoice or Credit note is reported not delivered | Document type and number, Shipper, technical cause, correlation ID | Monitoring. An alert later |

Recording a billable line publishes nothing. The Invoice run is what the Shipper sees.

## Deferred

These were left out on purpose while designing E1, each with its reason.

- **Payments, receivables, due dates and dunning.** Issuing the invoice is enough for the demo. Payment terms belong to the Contract, which the ERP doesn't model yet.
- **Dutch VAT rules,** including reverse charge for Shippers outside the Netherlands. Every seed Shipper is Dutch.
- **Invoicing Fulfilment orders.** They are billed through the Fulfilment Contract.
- **Invoice layouts and PDFs.** The Shipper receives the invoice as data in its Persona's inbox.
- **Credit notes across several lines, and re-invoicing.** One Credit note credits one line. A corrected charge is a new Credit note, not a new invoice.
- **Editing master data in the ERP, and syncing it to the TMS.** That is E3.
- **As-found toggles and presenter-panel faults.** They are added after the fixed state is built. The candidates:
  - Presenter-panel fault: the Shipper invoice endpoint is down, so the invoice ends Not delivered.
  - Toggle: no alert on failed Shipper invoice Logic App runs.
  - Toggle: no alert on the dead-letter queue of the `TransportOrderBillable` subscription (also listed in [P9](../tms/p9-freight-settlement.md#deferred)).
