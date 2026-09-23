# Norvia Logistics

The fictional reference company whose integration landscape this sandbox builds. Norvia Logistics B.V. is a Rotterdam-based road freight and warehousing company offering both warehouse fulfilment and transport-only services.

## Parties

**Norvia**:
The fictional company itself, Norvia Logistics B.V., and the reference scenario for all work in this sandbox.
_Avoid_: the client, the company

**Shipper**:
An organization that pays Norvia to store, move or deliver its goods. Norvia's customer.
_Avoid_: customer, client, brand

**Consignee**:
The party that receives the goods at the end of a transport. It may be a business or an end consumer.
_Avoid_: customer, recipient, end user

**Carrier**:
A transport company that Norvia subcontracts to perform a transport it doesn't run itself.
_Avoid_: haulier, partner, subcontractor

**Driver**:
A Norvia employee who carries out Trips with Norvia's own fleet.
_Avoid_: chauffeur, operator

**Vehicle**:
A truck or van in Norvia's own fleet.
_Avoid_: truck, asset, unit

## Services

**Fulfilment**:
The service in which Norvia holds a Shipper's stock and picks, packs and delivers it to Consignees on the Shipper's behalf.
_Avoid_: 3PL, e-commerce logistics

**Transport-only**:
The service in which a Shipper books Norvia to move goods from A to B, with no warehousing involved.
_Avoid_: freight, forwarding

## Orders and movements

**Transport Order**:
A Shipper's instruction for Norvia to move goods from an origin to a destination. The TMS owns it.
_Avoid_: order, booking, job

**Shipment**:
The physical movement that carries out a Transport Order, including its tracking status. The TMS owns it.
_Avoid_: delivery, consignment, trip

**Fulfilment Order**:
An instruction to pick and pack a Shipper's stock for a Consignee. The WMS owns it.
_Avoid_: order, pick order, sales order

**Trip**:
One run by a single Vehicle and Driver that serves an ordered list of Shipments. A Stop is only a Shipment's position in that list, not a separate concept.
_Avoid_: route, tour, run

**Inventory**:
A Shipper's stock held by Norvia, per location. The WMS owns it.
_Avoid_: stock levels

## Systems

**TMS**:
Norvia's transport management system, the system of record for Transport Orders, Shipments, Trips and Carrier bookings.

**WMS**:
Norvia's warehouse management system, the system of record for Fulfilment Orders and Inventory.

**ERP**:
Norvia's system of record for Shipper master data, contracts and invoicing.

**Partner portal**:
The single application that plays every party outside Norvia (Shippers and Carriers) in demos, with one persona per party. It reaches Norvia only through API Management.
_Avoid_: shipper portal, carrier portal, external app

**Persona**:
One fictional party (a specific Shipper or Carrier) as played in the Partner portal, with its own identity towards Norvia.
_Avoid_: user, role, tenant

## Integration

**Canonical Model**:
Norvia's own message format: one schema per business event or document, named with the terms in this glossary. Partner formats are mapped to and from it at the edges.
_Avoid_: common data model, standard format
