# One Shipment per Transport Order

Each Transport Order has exactly one Shipment, and a Shipment never carries more than one Transport Order. Consolidation happens one level up: several Shipments on one Trip for Own fleet, or several Shipments in one Carrier booking. A Trip is an ordered sequence of Stops, each one a Shipment's collection or its delivery, derived from the Shipment and not a record of its own. If one Shipment could hold Transport Orders with different collection or delivery addresses, it would need several collection or delivery Stops, and the Trip model would break. Keeping it 1:1 also gives each Transport Order a single tracking status, Proof of delivery and Shipment number that the Shipper can follow.

_Corrected while designing P11: the first wording made a Stop a Shipment's position in the Trip, which can't express a separate collection and delivery._

## Considered Options

- **Groupage of the same A→B:** several Transport Orders share one Shipment when their collection address, delivery address and windows match. It saves little that a Trip or Carrier booking doesn't already save, and every Shipment-level fact (status, POD, exceptions) would need splitting back out per Transport Order.
- **Free consolidation:** any Transport Orders in one Shipment. This needs a Stop concept inside the Shipment, which duplicates the Trip.

## Consequences

P3 doesn't consolidate. It creates one Shipment per Transport Order and assigns it. A Transport Order can't be split over several Shipments either. Partial deliveries and split loads would need this decision reopened.

Consolidating several Shipments into one Carrier booking is deferred. P4 makes one Carrier booking per Shipment, so for now consolidation happens only on a Trip.
