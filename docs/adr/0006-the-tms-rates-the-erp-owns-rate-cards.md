# The TMS rates, the ERP owns Rate Cards

Rating runs in the TMS, against a replicated read copy of each Shipper's Rate Card. The ERP stays the owner of Contracts and Rate Cards (ADR 0002) and is the only place they are changed. Rating needs the Transport Order's lanes, weights and goods lines, which the TMS owns. Keeping the calculation next to that data means a Transport Order can be rated without an asynchronous round trip, and without the ERP being available. Until the ERP is built, the seed file plays the ERP as the source of Rate Cards, the same way it does for Shipper data.

## Considered Options

- **The ERP rates:** purest ownership, but every Transport Order needs a round trip through AIS before it has a Freight Charge, and P2 can't be built until a real ERP exists (ADR 0001 rules out a stub).
- **The TMS owns Rate Cards:** simplest, but it breaks ADR 0002, and the Contract would be split across two systems.

## Consequences

The TMS never edits a Rate Card. A change to a Rate Card reaches the TMS only as a replicated copy published by the ERP (for now, a seed reload).
