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

**Planner**:
A Norvia employee who turns accepted Transport Orders into Shipments, assigns each one to Norvia's own fleet or to a Carrier, and builds and dispatches the Own fleet's Trips.
_Avoid_: scheduler, traffic controller

**Integration operator**:
A Norvia IT employee who watches the integration layer and resolves the messages that failed in it, using the Integration console.
_Avoid_: admin, support engineer, ops

**Vehicle**:
A van, rigid truck or tractor with trailer in Norvia's own fleet, with a payload in kg and a number of pallet places. Each type needs a Driver with a matching licence. The ERP owns its master data, and the TMS owns its availability.
_Avoid_: truck, asset, unit

**Own fleet**:
Norvia's Vehicles and Drivers taken together, as the alternative to a Carrier for carrying a Shipment.
_Avoid_: own transport, in-house fleet

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
The physical movement that carries out one Transport Order, including its tracking status. Each Transport Order has exactly one Shipment, and a Shipment never carries more than one Transport Order. Consolidation happens on a Trip, not inside a Shipment. The TMS owns it.
_Avoid_: delivery, consignment, trip

**Fulfilment Order**:
An instruction to pick and pack a Shipper's stock for a Consignee. The WMS owns it.
_Avoid_: order, pick order, sales order

**Trip**:
One run on one day by a single Vehicle and Driver that visits an ordered sequence of Stops for the Shipments it carries. A Vehicle or Driver has at most one Trip a day. The TMS owns it.
_Avoid_: route, tour, run

**Stop**:
One Shipment's collection or its delivery, at its place in a Trip's sequence. A Shipment on a Trip has exactly two Stops, and its collection comes before its delivery, or only its delivery Stop on a Re-delivery. A Stop is derived from its Shipment and has no life of its own. The Driver completes a Stop by giving it an outcome on that Trip: collected or collection failed, delivered or delivery failed.
_Avoid_: drop, visit, leg

**Dispatch**:
The Planner sending a finished Trip to its Driver through the Driver app.
_Avoid_: release (that is for a Shipment), send, publish

**Amend**:
The Planner changing a Dispatched Trip, which sends its Driver a new version of the whole Trip. Once the Trip is under way, the only change allowed is taking off a Shipment that hasn't been collected.
_Avoid_: re-dispatch, update, recall

**Start Trip**:
The Driver beginning a Dispatched Trip in the Driver app on its day, which makes it In progress. From then on the Planner can only take off a Shipment that isn't collected.
_Avoid_: begin, check in

**End Trip**:
The Driver finishing an In progress Trip once every Stop has an outcome, which makes it Completed. Shipments not collected go back to planning, and Shipments not delivered become Awaiting re-delivery.
_Avoid_: close, finish, check out

**Re-delivery**:
Carrying a Shipment that was collected but not delivered on a later Trip, with only its delivery Stop. Between the Trips the Shipment is Awaiting re-delivery: a flag, not a status, since Norvia still holds the goods.
_Avoid_: redelivery attempt, return, second run

**Carrier booking**:
A request to one Carrier to carry one Shipment, together with that Carrier's answer. A Shipment can have several Carrier bookings over its life, one after each that ends without being carried out (rejected, withdrawn or cancelled), but only one open at a time. The TMS owns it.
_Avoid_: tender, order, job

**Withdraw**:
The Planner taking back a Carrier booking before the Carrier has answered, so the Shipment can be reassigned or released again. It applies only to a Carrier booking that is still open.
_Avoid_: cancel (that is for a Confirmed Carrier booking), revoke, recall

**Cancel**:
Norvia ending a Confirmed Carrier booking before the Carrier has collected the goods, so the Shipment can be re-planned. The Planner does it to resolve an Exception, and the Carrier is told. After collection a Carrier booking can't be cancelled.
_Avoid_: withdraw (that is for an unanswered Carrier booking), revoke, annul

**Release**:
The Planner handing a planned Shipment over to be carried out: booking it with its Carrier, or making it available for a Trip. Until release, the Planner can still change who carries it.
_Avoid_: tender, dispatch, send

**Tracking event**:
One report from a Carrier or Driver that something happened to a Shipment (collected, in transit, out for delivery, delivered, or an exception such as a failed collection, a failed delivery or damage), at a given time and place. A Shipment's status follows from its Tracking events, but not every Tracking event changes it. The TMS owns it.
_Avoid_: status event, status update, scan, milestone

**Tracking status**:
What a Shipper is shown of where its Transport Order stands: Received, Scheduled, Collected, In transit or Delivered. It is derived from the Transport Order and its Shipment, and it hides how Norvia carries the goods, so a Shipment that goes back to planning before collection stays Scheduled. It never moves backwards.
_Avoid_: Shipment status (that is Norvia's own, finer view), tracking state, milestone

**Stale**:
Said of a Shipment on its way whose next expected Tracking event hasn't arrived in time: not collected after its collection window, silent for too long in transit, or not delivered after its delivery window. It is only a flag for the Planner.
_Avoid_: overdue (that is for Carrier bookings), late, delayed

**Proof of delivery**:
The Consignee's confirmation that it received a Shipment's goods: who signed, when and where, and whether the goods were taken clean or with reservations, backed by one or more files such as a signed CMR, a signature or photos. A Shipment has at most one current Proof of delivery: a later one replaces it, and the earlier one is kept as superseded. The TMS owns it.
_Avoid_: POD (the Carrier status code for Delivered), delivery receipt, signed POD

**Exception**:
Something that went wrong with a Shipment on its way and needs a decision: a failed collection, a failed delivery, damage, a Proof of delivery with reservations, or a Late delivery. The TMS opens it for one Shipment, and the Planner resolves it, re-planning the Shipment or notifying the Shipper as needed. A Shipment can have several Exceptions over its life. The TMS owns it.
_Avoid_: incident, issue, problem, alert

**Late delivery**:
An Exception for a Shipment that won't be, or wasn't, delivered within its delivery window: a Tracking event's ETA falls after the window, or the window passes without Delivered. Unlike Stale, it means Norvia knows the Shipment is late, not that it has gone quiet.
_Avoid_: delay, overdue, Stale

**Inventory**:
A Shipper's stock held by Norvia, per location. The WMS owns it.
_Avoid_: stock levels

## Fleet capacity

**Planning horizon**:
The days ahead, today and the next 14 by default, whose Own fleet availability is open for planning. A daily job opens each new day from the Drivers' Rosters.
_Avoid_: planning window, schedule

**Roster**:
A Driver's standard weekly working pattern: a Shift start and end for each weekday they work. It is copied onto each day as the day opens.
_Avoid_: schedule, rota

**Shift**:
A Driver's working time on one day, taken from their Roster unless the Planner edits it.
_Avoid_: duty, roster (that is the weekly pattern)

**Absence**:
A whole day on which a Driver can't work, for leave, sickness or training.
_Avoid_: leave (that is only one reason), unavailability

**Off the road**:
Said of a Vehicle that can't be used on a day, for maintenance or repair.
_Avoid_: VOR, downtime, out of service

**Driving-hour budget**:
The planned driving hours a Driver has left under the daily, weekly and fortnightly limits, a simplified form of EU Regulation 561/2006. Trips use it up.
_Avoid_: tacho hours, working time

**Over capacity**:
Said of a day whose Own fleet demand, in weight or pallet places, is more than its available Vehicles and Drivers can carry. It is only a warning for the Planner.
_Avoid_: overbooked, full

## Commercial

**Contract**:
Norvia's commercial agreement with a Shipper. The ERP owns it.
_Avoid_: agreement, deal

**Rate Card**:
The part of a Contract that sets what Norvia charges a Shipper for transport, by lane and weight.
_Avoid_: tariff, price list, rate sheet

**Zone**:
A named area in a Rate Card: a country plus a set of postcode prefixes.
_Avoid_: region, area

**Lane**:
A pair of Zones, origin to destination, that a Rate Card prices.
_Avoid_: route, corridor, relation

**Weight Break**:
A weight band within a Lane that sets the price per 100 kg for Transport Orders whose total weight falls in it.
_Avoid_: weight bracket, tier

**Rating**:
Calculating the Freight Charge for a Transport Order from the Shipper's Rate Card.
_Avoid_: pricing, costing

**Freight Charge**:
The price Norvia charges a Shipper for one Transport Order, as calculated by Rating and broken into charge lines.
_Avoid_: price, rate, cost, quote

**Carrier Rate Card**:
The part of Norvia's agreement with a Carrier that sets what Norvia pays the Carrier for transport, by Lane and weight. It has the same shape as a Rate Card. The ERP owns it.
_Avoid_: buy rate, purchase tariff

**Carrier cost**:
The price Norvia pays a Carrier for one Carrier booking. The TMS calculates it from the Carrier Rate Card at release, or takes a Spot price, and freezes it on the Carrier booking.
_Avoid_: buy price, cost price, Freight Charge (that is what the Shipper pays)

**Spot price**:
A Carrier cost that the Planner enters at release, with a remark, because no Carrier Rate Card can price the Shipment.
_Avoid_: ad-hoc rate, quote

**Billable**:
Said of a Transport Order that is ready to invoice: Delivered, Rated, with a current Proof of delivery and no open Exception, or released by Customer service without a Proof of delivery. Once Billable, it stays Billable.
_Avoid_: invoiceable, ready to bill

**Invoice run**:
The ERP turning each Shipper's Billable Transport Orders that aren't yet invoiced into one Shipper invoice per Shipper.
_Avoid_: billing run

**Shipper invoice**:
The ERP's document that charges a Shipper, with one line per Transport Order. The ERP owns it.
_Avoid_: bill, statement, invoice (on its own, when a Carrier invoice could be meant)

**Credit note**:
The ERP's document that reverses all or part of a Shipper invoice line, with a reason. The ERP owns it.
_Avoid_: refund, credit memo

**Carrier invoice**:
A Carrier's claim for payment from Norvia, with one line per Carrier booking, identified by the Carrier booking reference. The ERP owns it once received.
_Avoid_: purchase invoice, bill

**Matching**:
Checking each Carrier invoice line against the Carrier cost of its Carrier booking.
_Avoid_: reconciliation, 3-way match

## Systems

**TMS**:
Norvia's transport management system, the system of record for Transport Orders, Shipments, Trips and Carrier bookings, and for Own fleet availability.

**WMS**:
Norvia's warehouse management system, the system of record for Fulfilment Orders and Inventory.

**ERP**:
Norvia's system of record for Shipper, Carrier, Vehicle and Driver master data, Contracts, Rate Cards, Carrier Rate Cards and invoicing: Shipper invoices, Credit notes and Carrier invoices.

**Driver app**:
The application a Driver uses on the road to receive their Trip, start and end it, and complete each Stop, capturing a Proof of delivery at each delivery. It is Norvia's own, separate from the TMS, and reaches it only through Azure Integration Services.
_Avoid_: mobile app, handheld, scanner

**Integration console**:
Norvia's own application for the Integration operator to find Message trails, see failed runs and dead-lettered messages, and resubmit or discard them. It reads what the integration layer already records and keeps nothing of its own.
_Avoid_: admin portal, monitoring portal, dashboard

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

**Message trail**:
Everything the integration layer did under one correlation ID: the call or event it started from, and every queue, subscription, Logic App run and application call that followed from it, fan-out included. An action by a Norvia employee in an application starts a new trail.
_Avoid_: integration message, message flow, transaction
