# The TMS rates Carrier bookings, the ERP owns Carrier Rate Cards

The Carrier cost of a Carrier booking is calculated by the TMS when the Planner releases the Shipment. It uses a replicated read copy of the Carrier's Carrier Rate Card and the same method as Rating (P2): Zones, Lanes, Weight Breaks and a minimum charge, with the version picked by collection date. This is ADR 0006 applied to the Carrier side. The ERP owns Carrier Rate Cards and is the only place they are changed. Until the ERP sync exists, both systems load them from the seed file. (The master-data sync of E3, [ADR 0050](0050-the-erp-replicates-each-master-data-change-to-the-tms-as-one-whole-record-event-with-a-revision.md), has since made the seed file only the first load.) When no Carrier Rate Card prices the Shipment, the Planner enters a Spot price with a remark instead. Either way the Carrier cost is frozen on the Carrier booking and sent to the Carrier in the booking request. When the Carrier confirms the booking, it has accepted that price. It can reject a booking with `PriceNotAccepted`, and there is no counter-offer.

## Considered Options

- **The Carrier quotes a price in its confirmation:** no Carrier Rate Cards are needed, but Norvia only learns its cost after committing to the Carrier, and Matching compares the Carrier's invoice against the Carrier's own number.
- **The Planner types a price at every release:** simple, but it doesn't scale, and there is no agreed baseline to match against.
- **The ERP calculates the Carrier cost:** purest ownership, but every release waits on an asynchronous round trip before the booking request can go out.

## Consequences

Matching (in the ERP) has a baseline that Norvia set before the Carrier accepted. The TMS never edits a Carrier Rate Card. The booking request now carries a price, which is the Carrier's own price and not the Shipper's Freight Charge, so P4's rule of keeping the Freight Charge away from the Carrier still holds. A Carrier cost can't be changed after release. A release again with a new Spot price creates a new Carrier booking. Choosing a Carrier on cost in P3 becomes possible, but it is still deferred.
