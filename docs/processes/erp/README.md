# ERP business processes

The catalogue of business processes the ERP supports. The ERP is Norvia's system of record for Shipper and Carrier master data, Contracts, Rate Cards, Carrier Rate Cards and invoicing (ADR 0002). It is built as a realistic application with its own domain model, persistence and UI (ADR 0001), and it talks to the TMS only through Azure Integration Services. Terms are defined in [GLOSSARY.md](../../../GLOSSARY.md).

The ERP's first slice is invoicing, which the TMS's [P9 Freight settlement](../tms/p9-freight-settlement.md) needs. In this slice the ERP loads Shippers, Carriers, Rate Cards and Carrier Rate Cards from the seed file, with the same IDs as the TMS, and shows them read-only. Editing them, and syncing them to the TMS, come later.

| # | Process | Trigger → outcome | Main integrations | Status |
|---|---|---|---|---|
| E1 | [Invoicing](e1-invoicing.md) | Billable Transport Order received from the TMS → Shipper invoice issued in an Invoice run and sent to the Shipper. Credit notes | TMS via Service Bus and the Billing Logic App. Partner portal (Shipper Personas) via the Shipper invoice Logic App | Adopted |
| E2 | [Carrier invoice matching](e2-carrier-invoice-matching.md) | Carrier invoice received → each line matched against its Carrier cost, decided by Finance where needed, and the Carrier told | TMS via Service Bus and the Carrier cost Logic App. Partner portal (Carrier Personas) via APIM and the Carrier invoice Logic Apps | Adopted |
| E3 | Master-data sync | Shipper, Carrier, Rate Card or Carrier Rate Card changed in the ERP → replicated to the TMS | TMS via Service Bus and a Logic App | Deferred |

## Roles

| Role | Does |
|---|---|
| Finance | Runs Invoice runs, issues Credit notes, and decides Carrier invoice lines that didn't match |

Finance is a Norvia employee, as the Planner and Customer service are. It exists only in the ERP.
