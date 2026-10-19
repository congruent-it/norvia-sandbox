# E1 Invoicing

Invoicing turns the Billable Transport Orders the TMS hands over into Shipper invoices, one per Shipper per Invoice run, and sends them to the Shipper. Finance corrects an issued invoice with a Credit note. This is the ERP half of [P9 Freight settlement](../tms/p9-freight-settlement.md). Terms are defined in [GLOSSARY.md](../../../GLOSSARY.md).

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
| The Freight Charge has at least one charge line | `FreightChargeWithoutChargeLines`, a client error |
| The Freight Charge's total is the sum of its lines | `FreightChargeTotalMismatch`, a client error |
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

## Implementation

For engineers and coding agents. It shows where E1 lives in the repository. Built so far: the ERP application with its seed-loaded master data, recording Billable lines through the ERP billing API with the list of Waiting lines, the Billing Logic App that hands `TransportOrderBillable` to it, **Invoice now**, which issues a Shipper invoice and publishes `ShipperInvoiceIssued`, the monthly Invoice run, the Partner portal's Shipper invoice endpoint and invoices inbox, and the Shipper invoice Logic App, which sends the invoice and reports it Sent (the happy path). Not delivered handling, Credit notes and the other Logic Apps are designed above and not built yet.

### What is built

| Piece | Where | Does |
|---|---|---|
| Domain | [src/Erp/Norvia.Erp.Domain](../../../src/Erp/Norvia.Erp.Domain/) | The aggregates `Shipper`, `Carrier`, `RateCard` and `CarrierRateCard`, with the IDs the TMS uses, and their specifications. A card keeps its versions, Zones and Lanes as the seed file has them; the ERP shows them and never rates with them |
| Application | [src/Erp/Norvia.Erp.Application](../../../src/Erp/Norvia.Erp.Application/) | The queries behind the four lists, and `LoadMasterDataCommand`, which adds what the seed file has and reloads what is there already, so loading twice changes nothing |
| Infrastructure | [src/Erp/Norvia.Erp.Infrastructure](../../../src/Erp/Norvia.Erp.Infrastructure/) | The ERP's own `ErpDbContext` and database (`erp`), the transactional outbox and its publisher, and the seed loader that runs as the ERP starts |
| Migrations | [src/Erp/Norvia.Erp.Migrations](../../../src/Erp/Norvia.Erp.Migrations/) | A Container Apps job that applies the database's migrations; azd's postdeploy hook runs it |
| Billable lines | [src/Erp/Norvia.Erp.Domain/BillableLines](../../../src/Erp/Norvia.Erp.Domain/BillableLines/) | The `BillableLine` aggregate, keyed by its Transport Order number, and the billing rules: the charge lines are there and add up to the total, and what counts as the same content on a repeat. The Waiting status is the only one built |
| Record a Billable line | [src/Erp/Norvia.Erp.Application/BillableLines](../../../src/Erp/Norvia.Erp.Application/BillableLines/Record/) | `RecordBillableLineCommand` and its handler: idempotent on the Transport Order number, `ShipperNotFound` for an unknown Shipper, `BillableLineConflict` for different content. It publishes nothing. The queries behind the Waiting lines list are beside it |
| ERP billing API | [src/Erp/Norvia.Erp.Api](../../../src/Erp/Norvia.Erp.Api/) | `POST /v1/billable-lines` takes only `TransportOrderBillable` (the Canonical Model) and needs the `BillableLines.Record` app role. 201 for a new line, 200 with `repeat: true` for the same content again. Refusals are `ProblemDetails` with the stable `code`: `ShipperNotFound` (422), `BillableLineConflict` (409), `FreightChargeWithoutChargeLines` and `FreightChargeTotalMismatch` (400). A body outside the Canonical Model is a 400. The OpenAPI document is published for API Management |
| Billing Logic App | [src/Integration/BillingLogicApp](../../../src/Integration/BillingLogicApp/), [billing-logic-app.bicep](../../../infra/estate/billing-logic-app.bicep) | A stateful workflow, `billing`, on the `billing` subscription of `transport-order-events` (the filter is `(eventType = 'TransportOrderBillable' AND resubmitTo IS NULL) OR resubmitTo = 'billing'`). It posts the message body to `POST /v1/billable-lines` as it is, because the ERP accepts the Canonical Model and there is nothing to map, as the Billing Logic App's identity with the event's correlation ID in `X-Correlation-ID` and as the run's client tracking ID. A `2xx` completes the message, a repeat (`200`) included. A timeout, `408`, `429` or a server error is retried three times (exponential, 5 s up to 20 s); another client error and a conflict are not. When the line is refused or the retries are used up, the message is dead-lettered with the ERP's code as `DeadLetterReason` (`ShipperNotFound`, `BillableLineConflict`, `FreightChargeWithoutChargeLines`, `FreightChargeTotalMismatch`, or `ErpCallRefused` and `ErpUnavailable`) and its message as `DeadLetterErrorDescription`, and the run fails. A message that isn't a `TransportOrderBillable` is dead-lettered as `BillableLineUnreadable`. The run tracks `billing` with the Transport Order number, the Shipment number, the Shipper as `partyId` and the event ID as `sourceMessageId`. The identity holds `BillableLines.Record` and the receiver role on its subscription, and nothing else. It has no Key Vault and no local function. Locally the AppHost runs `Norvia.Integration.LocalBillingWorker` in its place ([ADR 0043](../../adr/0043-locally-a-hosted-worker-plays-the-e1-billing-logic-app.md)), so a delivery in the TMS reaches the Waiting lines list; CI tests the workflow's definition and the [sample messages](../../../contracts/canonical-model/samples/transport-order-billable/) without Azure |
| Invoice now | [src/Erp/Norvia.Erp.Application/ShipperInvoices/Issue](../../../src/Erp/Norvia.Erp.Application/ShipperInvoices/Issue/), [ShipperInvoices](../../../src/Erp/Norvia.Erp.Domain/ShipperInvoices/) | `IssueShipperInvoiceCommand` and its handler run the Invoice run for one Shipper. It loads the Shipper's Waiting lines as tracked entities, takes the next number from the year's `InvoiceNumberSequence`, has `ShipperInvoice.Issue` set the lines to Invoiced and build the invoice (lines sorted by delivery time, VAT 21% rounded half away from zero), adds `ShipperInvoiceIssued` to the outbox and saves all of it in one `SaveChanges`, so one transaction. A Shipper without Waiting lines gets no invoice (the result carries none). The invoice date and the number's year are the day of the run in Europe/Amsterdam, from the injected `TimeProvider`. A new correlation ID is created for the invoice and carried by the event |
| Monthly Invoice run | [MonthlyInvoiceRunWorker](../../../src/Erp/Norvia.Erp.Web/ShipperInvoices/MonthlyInvoiceRunWorker.cs), [MonthlyInvoiceRun](../../../src/Erp/Norvia.Erp.Web/ShipperInvoices/MonthlyInvoiceRun.cs), [MonthlyInvoiceRunSchedule](../../../src/Erp/Norvia.Erp.Domain/ShipperInvoices/MonthlyInvoiceRunSchedule.cs) | A hosted service in the ERP UI's process looks every minute, and when the ERP starts, whether the latest 1st at 06:00 Europe/Amsterdam has been run since it started; if not, `MonthlyInvoiceRun` sends `IssueShipperInvoiceCommand` for every Shipper, each in a scope and a transaction of its own through `IConflictRetrier`, with `DeliveredBefore` set to midnight at the start of that 1st. It is the same handler as Invoice now, so the date, numbering (gapless, restarting each year) and double-invoicing guard are the same: a monthly run and Invoice now at once conflict on the lines, and the loser finds nothing Waiting. A Shipper that fails is logged and the whole run is tried again at the next look; what is invoiced already is not Waiting, so a repeat changes nothing. The worker reads the clock from the injected `TimeProvider`, so tests control it |
| Shipper invoice endpoint and invoices inbox | [contracts/partner-api/shipper-invoice.v1.schema.json](../../../contracts/partner-api/shipper-invoice.v1.schema.json), [ShipperInvoiceEndpoint](../../../src/PartnerPortal/Norvia.PartnerPortal.Web/ShipperInvoices/ShipperInvoiceEndpoint.cs), [InvoicesInbox](../../../src/PartnerPortal/Norvia.PartnerPortal.Web/ShipperInvoices/InvoicesInbox.razor), [ShipperInvoice](../../../src/PartnerPortal/Norvia.PartnerPortal.Domain/ShipperInvoices/ShipperInvoice.cs) | The [Shipper invoice format](#shipper-invoice-format) is the schema `shipper-invoice.v1` in the Partner API contracts, with sample documents in `samples/shipper-invoices`: `INVOICE` or `CREDIT_NOTE`, number and date as `dd-MM-yyyy`, the credited invoice number, Transport Order number and reason on a Credit note only, lines with `bookingRef`, `deliveryDate`, charge lines (`FREIGHT`, `MINIMUM_CHARGE_ADJUSTMENT`) and line total, and the totals without VAT, VAT and with VAT in EUR. It has no field for the Carrier or the Shipment number (ADR 0011). Each Shipper Persona has `POST /shipper/v1/<Shipper ID>/invoices`, guarded by the same credential as its notification endpoint (`X-Api-Key`, `shipper-endpoint-key-<Shipper ID>`) and called with `X-Correlation-ID`. It checks the message against the schema's rules and answers `200` with `{ documentNumber, repeated }` once the document is in the inbox. The document number is the key (a unique index per Persona), so a repeated call, or two at once, is acknowledged and ignored. `401` is a wrong credential, `404` an unknown Shipper and `400` a message that breaks the format. There are no answer modes. The inbox at `/personas/<id>/invoices` lists the documents newest first, each with its lines, charge lines and totals, a Credit note with the invoice, line and reason it credits, and reads itself again every two seconds, so a document shows without a page refresh. The portal keeps the document whole as one JSON column of `ShipperInvoices`, and **Reset** clears it with the notifications. The [Shipper invoice Logic App](#what-is-built) sends to it |
| Report a Shipper invoice Sent | [ReportShipperInvoiceSentHandler](../../../src/Erp/Norvia.Erp.Application/ShipperInvoices/ReportSent/ReportShipperInvoiceSentHandler.cs), [ShipperInvoiceEndpoints](../../../src/Erp/Norvia.Erp.Api/ShipperInvoices/ShipperInvoiceEndpoints.cs) | `POST /v1/shipper-invoices/{invoiceNumber}/report-sent` needs the `ShipperInvoices.Report` app role and an `X-Correlation-ID` (the invoice's own, which `ShipperInvoiceIssued` carried). It moves an Issued invoice to Sent, records `SentAt` from the injected `TimeProvider`, and publishes nothing. A report for an invoice that is Sent already is **ignored and logged**: it answers `200` with `ignored: true`, changes nothing and keeps the first sent time. `404` with `ShipperInvoiceNotFound` for an unknown number, `400` without the correlation ID. The call is traced as flow `invoicing` with `correlationId` and `documentNumber`, `Succeeded` for a first report and a repeat alike. An invoice never reported stays Issued with no sent time, and the Shipper invoices list and the invoice's page show that (a dash for the sent time). The invoice's number, lines and totals are never changed by a report |
| Shipper invoice Logic App | [src/Integration/ShipperInvoiceLogicApp](../../../src/Integration/ShipperInvoiceLogicApp/), [ShipperInvoiceMap](../../../src/Integration/Norvia.Integration.CarrierBooking/ShipperInvoiceMap.cs), [MapShipperInvoiceIssued](../../../src/Integration/Norvia.Integration.ShipperInvoice.Functions/MapShipperInvoiceIssued.cs), [shipper-invoice-logic-app.bicep](../../../infra/estate/shipper-invoice-logic-app.bicep) | A stateful workflow, `shipper-invoice`, on the `shipper-invoice` subscription of `invoicing-events` (the filter is `(eventType = 'ShipperInvoiceIssued' AND resubmitTo IS NULL) OR resubmitTo = 'shipper-invoice'`). It maps the event with the local function `MapShipperInvoiceIssued` (the `INVOICE` document of the [Shipper invoice format](#shipper-invoice-format): `bookingRef` is the Shipper's reference, dates are `dd-MM-yyyy`, the amounts are the ERP's own, and neither the Carrier, the Shipment number, the Shipper's name nor the status goes over), completes the message, reads the Shipper's credential from Key Vault (`shipper-endpoint-key-<Shipper ID>`) and posts the invoice to `/shipper/v1/<Shipper ID>/invoices` with `X-Correlation-ID` and `X-Api-Key`, retrying a timeout, `408`, `429` and a server error three times (exponential, 5 s up to 20 s). When the endpoint acknowledges it, the Logic App calls `report-sent` on the ERP as its own identity, which holds `ShipperInvoices.Report` and nothing else on the ERP. A message that isn't a `ShipperInvoiceIssued` the map can read is dead-lettered as `ShipperInvoiceIssuedUnreadable` and the run fails. **Failures are the next slice:** when the endpoint doesn't acknowledge the invoice, or the ERP doesn't take the report, the run fails and the invoice stays Issued without a sent time. The run tracks `invoicing` with the Shipper as `partyId`, the invoice number as `documentNumber` and the event ID as `sourceMessageId`. The Bicep adds the subscription and its filter, the identity (`ShipperInvoices.Report`, the receiver role on its subscription, and Key Vault Secrets User on each Shipper's endpoint secret, not the vault), and the Logic App on the shared plan; azd's postdeploy hook (`infra/deploy-shipper-invoice-logic-app.sh`) deploys the workflow and sets the ERP's and the portal's addresses. Locally the AppHost runs `Norvia.Integration.LocalShipperInvoiceWorker` in its place (ADR 0033, [ADR 0043](../../adr/0043-locally-a-hosted-worker-plays-the-e1-billing-logic-app.md)). CI tests the map against the [sample messages](../../../contracts/canonical-model/samples/shipper-invoice-issued/) without Azure: the invoice validates against its schema, equals the Partner API's sample and carries no Carrier or Shipment number; and it tests the workflow's definition |
| Numbering and double invoicing | [InvoiceNumberSequence](../../../src/Erp/Norvia.Erp.Domain/ShipperInvoices/InvoiceNumberSequence.cs), [configurations](../../../src/Erp/Norvia.Erp.Infrastructure/Data/Configurations/) | The sequence is one row per year with a row version, so two transactions taking the next number conflict instead of both taking it, and a rolled-back run takes none. A year's first run starts its row in a save of its own, which takes no number. Billable lines carry a row version too: two overlapping runs, or a request sent twice, conflict on the lines. The loser is run again by `IConflictRetrier` in a scope of its own, finds nothing Waiting and issues nothing. A `ShipperInvoice` has no method that changes its number, lines or totals after `Issue`, its only change is `ReportSent`, and nothing deletes it |
| Shipper invoices queries | [src/Erp/Norvia.Erp.Application/ShipperInvoices](../../../src/Erp/Norvia.Erp.Application/ShipperInvoices/) | `ListShipperInvoicesQuery` (newest first) and `OpenShipperInvoiceQuery` (one invoice with its lines) |
| `ShipperInvoiceIssued` | [ShipperInvoiceIssuedV1](../../../src/Erp/Norvia.Erp.Application/ShipperInvoices/ShipperInvoiceIssuedV1.cs) | Goes to the `invoicing-events` topic through the ERP's outbox, which the ERP UI's process runs. The topic has the `monitoring` subscription and the Shipper invoice Logic App's `shipper-invoice`. The body is [shipper-invoice-issued.v1](../../../contracts/canonical-model/shipper-invoice-issued.v1.schema.json) with the [shipper-invoice.v1](../../../contracts/canonical-model/shipper-invoice.v1.schema.json) document in it. The tracking keys on the message are `flow` (`invoicing`), `partyId` (the Shipper), `documentNumber` (the invoice number) and `correlationId`. The invoice inside it is as issued, so `status` is Issued and `sentAt` is left out |
| ERP UI | [src/Erp/Norvia.Erp.Web](../../../src/Erp/Norvia.Erp.Web/) | The Waiting billable lines list, per Shipper with its count and total, and each Shipper's lines opening to everything a line holds (a release without Proof of delivery is marked), reading again every second so a line the API records appears without a refresh. **Invoice now** is a button per Shipper in that list and on the Shipper's page; it runs `InvoiceNowAction` in a scope and a span of its own (`Invoice now`) and says which invoice it issued, linking to it, or that nothing was waiting. The Shipper invoices list shows the issued invoices newest first with number, Shipper, date and the three totals, and opens an invoice with its lines and charge lines; it reads again every second like the other lists. Four master data lists, Shippers, Carriers, Rate Cards and Carrier Rate Cards, with no edit action. Built with Fluent UI components only |

The Canonical Model has the answer as [billable-line.v1](../../../contracts/canonical-model/billable-line.v1.schema.json) and [billable-line-recording.v1](../../../contracts/canonical-model/billable-line-recording.v1.schema.json). Locally the AppHost hosts the API as `erp-api` and trusts a token signed with the local key, the Billing Logic App's client ID standing in for its managed identity. In Azure the estate defines the API's app registration and the role, and grants the role to the Billing Logic App's identity.

The seed loader reads `shippers`, `carriers`, `rateCards` and `carrierRateCards` from `seed/norvia.seed.json` (`Seed:Path`), as the TMS does. A broken seed stops the ERP as it starts. Recording a Billable line publishes nothing by design; Invoice now publishes `ShipperInvoiceIssued`. The other events are the slices after this one's.

### Access

Only a user with the `Finance` app role reaches the UI. The billing API is the other door, for the Billing Logic App alone. In Azure Container Apps' Easy Auth signs Finance in to the ERP's app registration and Entra refuses a user without the role; the ERP checks the role as well. Locally the AppHost signs the developer in as a local user holding `Finance` (`LocalUser` settings, accepted only in Development). See [identity and access](../../architecture/identity-and-access.md).

### Hosting

Aspire hosts the ERP like the TMS ([ADR 0004](../../adr/0004-bicep-owns-the-estate-aspire-owns-the-apps.md)): the AppHost declares the `erp` database, the migrations job and `erp-web`. The estate Bicep owns the ERP's managed identity (`id-erp-…`), and its app registration with the `Finance` role. The ERP's identity has no role on another application; it only sends to Service Bus.

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
