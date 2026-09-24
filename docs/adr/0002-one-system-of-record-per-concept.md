# One system of record per concept

Each core concept has exactly one owning application: the TMS owns Transport Orders, Shipments and Carrier bookings; the WMS owns Fulfilment Orders and Inventory; the ERP owns Shipper and Carrier master data, contracts and invoicing. Other applications hold only references or replicated read copies that they receive through AIS. This keeps every integration flow's direction unambiguous: the owner publishes and everyone else consumes.

## Consequences

Marketplace orders are Fulfilment Orders, so in the target design they go to the WMS, not the TMS. The WMS requests a Transport Order when goods are ready to ship. The scenario's inherited routing (marketplace orders sent straight to the TMS) is kept only as an estate setting for demos and findings. It is never built into the applications' domain logic.
