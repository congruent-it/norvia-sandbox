# The TMS decides when to bill, the ERP invoices

The TMS decides when a Transport Order is Billable, and the ERP does everything after that: Invoice runs, Shipper invoices, Credit notes and Matching Carrier invoices. A Transport Order is Billable once it is Delivered, Rated, has a current Proof of delivery and has no open Exception ("no POD, no invoice"). Customer service can release a Delivered order without a Proof of delivery, with a remark. Every one of those facts is TMS data, so the TMS checks them in the same transaction as the event that completes them and publishes `TransportOrderBillable` once, with the frozen Freight Charge. It publishes `CarrierCostIncurred` in the same way when a Carrier Shipment is Delivered. The ERP owns invoicing (ADR 0002), so it receives both through a Logic App and never asks the TMS anything. Nothing flows back: the TMS never learns an invoice or Credit note number. P9 is also where the ERP is first built (ADR 0001), as an invoicing slice that loads its master data from the seed file.

## Considered Options

- **The ERP decides when to bill:** the ERP would consume Delivered, Rated, Proof of delivery and Exception events and work out billability itself. That needs a replicated copy of four TMS concepts in the ERP and an ordering problem across several topics, only to reach a decision the TMS can make in one transaction.
- **Invoice on Delivered alone:** simpler, but a Shipper would be invoiced for a delivery with no signed Proof of delivery, or with open damage or reservations that may end in a Credit note.
- **The ERP reports each invoice back to the TMS:** it would let the TMS show "Invoiced", but nothing in the TMS needs that, and it adds a return path whose failure would have no business effect.

## Consequences

Billable is final. A Proof of delivery that replaces the current one, or an Exception opened after Billable, doesn't reopen it. Any correction after that is a Credit note, issued by Finance in the ERP. A Transport Order that never gets a Proof of delivery stays not yet billable until Customer service releases it, so the TMS shows Customer service what is blocking each Delivered order. If the Billing Logic App's subscription dead-letters silently, orders are delivered but never invoiced, which is the headline incident replayed for revenue.
