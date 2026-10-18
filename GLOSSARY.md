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

**Customer service**:
The Norvia employees who answer Shippers about their bookings and Transport Orders, for example about a Rejected booking, from the TMS UI.
_Avoid_: support, helpdesk, account management

**Role switcher**:
The control in the TMS UI that moves a Norvia user between Planner and Customer service without a login, so a demo moves quickly. Each role opens on its own page: the Planner on its home page, which counts the work to act on, and Customer service on its first view.
_Avoid_: login, impersonation, persona (that is for parties outside Norvia)

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

**Booking**:
What a Shipper sends Norvia through the Norvia Partner API to ask for a transport, in the partner format. Norvia takes it in as a Transport Order, or keeps it as a Rejected booking. The Partner portal keeps every booking a Persona sent, with Norvia's answer, as the outside party's own records.
_Avoid_: order, Transport Order (that is Norvia's, once accepted), Carrier booking (that is Norvia's request to a Carrier)

**Shipper reference**:
The Shipper's own reference for a booking. It is unique per Shipper: an Accepted Transport Order holds it, so the same booking sent again is a repeat that gets the original Transport Order back, and the reference sent with different content is rejected. A Rejected booking holds no reference.
_Avoid_: order reference, customer reference, idempotency key

**Rejected booking**:
A submitted Transport Order that broke one or more Transport Order rules. The TMS keeps it with every rejection reason (a stable code and a plain-language message) and what the Shipper submitted, but gives it no Transport Order number, and nothing downstream acts on it.
_Avoid_: failed order, invalid order

**Intake channel**:
The way a Transport Order reached the TMS: the Shipper API for Transport-only bookings, and later the WMS handoff for Fulfilment. Every channel uses the same intake API. The TMS knows the channel from the caller's identity, and each Transport Order records the one it came from.
_Avoid_: source, origin (origin is where goods are collected)

**Waiting for planning**:
Said of an Accepted Transport Order that no Shipment has been made from yet. The Planner's list in the TMS UI shows them, and a Transport Order appears there as soon as it is accepted.
_Avoid_: open orders, backlog, unplanned orders

**Shipment**:
The physical movement that carries out one Transport Order, including its tracking status. Each Transport Order has exactly one Shipment, and a Shipment never carries more than one Transport Order. Consolidation happens on a Trip, not inside a Shipment. The TMS owns it.
_Avoid_: delivery, consignment, trip

**Shipment number**:
The number the TMS gives a Shipment when a Planner assigns its Transport Order, such as SH-0000042. It stays the Shipment's through reassignment and release, and the Transport Order shows it once Planned.
_Avoid_: tracking number, consignment number

**Assignment rule**:
A rule a Planner's assignment of a Transport Order must meet before the TMS plans it: the Transport Order is Accepted and not yet Planned, and a chosen Carrier is known to the TMS and active. A reassignment of a Shipment meets the same Carrier rules, and the Shipment is Planned and goes to someone other than who carries it. A release needs the Shipment Planned, so a Released Shipment is neither reassigned nor released again. The TMS refuses an assignment that breaks one with a refusal (a stable code and a plain-language message), and plans nothing. There are no coverage rules, and the rating result never blocks an assignment.
_Avoid_: validation, planning check

**Fulfilment Order**:
An instruction to pick and pack a Shipper's stock for a Consignee. The WMS owns it.
_Avoid_: order, pick order, sales order

**Trip**:
One run on one day by a single Vehicle and Driver that visits an ordered sequence of Stops for the Shipments it carries. A Vehicle or Driver has at most one Trip a day. The TMS owns it.
_Avoid_: route, tour, run

**Draft**:
A Trip the Planner is still building. It can be changed or discarded, and it holds its Vehicle and Driver, so neither has another Trip that day. Dispatching it makes it a Dispatched Trip, which is never deleted. Nothing is published while a Trip is a Draft.
_Avoid_: unsaved trip, planned trip

**Planned start**:
The time on a Trip's day at which the Driver is to begin it, entered by the Planner. It must be inside the Driver's Shift that day, from its start up to but not including its end.
_Avoid_: departure time, start time

**Planned driving duration**:
How long the Planner expects the Driver to drive on a Trip, entered by the Planner, of more than nothing and at most 24 hours. It uses up the Driver's Driving-hour budget, and a Draft's counts too, so discarding the Draft gives the hours back.
_Avoid_: driving time, trip duration

**Discard**:
The Planner deleting a Draft, which gives its Vehicle and Driver back. Only a Draft can be discarded.
_Avoid_: delete trip, cancel trip

**Dispatched Shipment**:
An Own fleet Shipment whose Trip the Planner has dispatched: the Own fleet counterpart of a Booked Carrier Shipment. It moves Planned, Released, Dispatched, Collected. Dispatching a Trip sets it, and no event is published per Shipment, because the Trip's `TripDispatched` lists them.
_Avoid_: sent, assigned to a Trip

**Stop**:
One Shipment's collection or its delivery, at its place in a Trip's sequence. A Shipment on a Trip has exactly two Stops, and its collection comes before its delivery, or only its delivery Stop on a Re-delivery. A Stop is derived from its Shipment and has no life of its own. The Driver completes a Stop by giving it an outcome on that Trip: collected or collection failed, delivered or delivery failed.
_Avoid_: drop, visit, leg

**Dispatch**:
The Planner sending a finished Trip to its Driver through the Driver app.
_Avoid_: release (that is for a Shipment), send, publish

**Amend**:
The Planner changing a Dispatched Trip, which sends its Driver a new version of the whole Trip. Once the Trip is under way, the only change allowed is taking off a Shipment that hasn't been collected.
_Avoid_: re-dispatch, update, recall

**Take off (a Shipment from a Trip)**:
The Planner removing an uncollected Shipment's two Stops from its Trip, which puts the Shipment back to Planned so it can be re-planned in P3, and publishes `ShipmentTakenOffTrip`. On a Dispatched Trip it is also an Amend.
_Avoid_: unassign, remove, unplan

**Driver report**:
One thing a Driver did from the road and told the Driver app: Start Trip, or a report on a Stop (Collected, Collection failed, Delivery failed, Report damage). The Driver app keeps it with its state until the Driver reports Logic App says what became of it. Published as a Driver app event.
_Avoid_: update, message

**Driver app event**:
An event the Driver app publishes for a Driver report, such as `TripStartReported` or `StopCompletedReported`, with its own Driver app event ID. The Driver app reports and the TMS decides, so the name says what the Driver did, not what the TMS made of it.
_Avoid_: Trip event (those are the TMS's)

**Sending / Recorded / Refused / Not recorded**:
The state of a Driver report as the Driver sees it. Sending: published, no answer yet. Recorded: the TMS accepted it. Refused: the TMS refused it, with its failure code and message. Not recorded: the Driver reports Logic App gave up after its retries.
_Avoid_: pending, failed, error

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

**Shipper notification**:
A message the Planner sends the Shipper about an open Exception, as often as needed, starting from a template for the Exception's type. It has a notification ID and is Requested until the Shipper notification Logic App reports it Sent or Not delivered, with the technical cause. It is one-way: the Shipper can't reply through the TMS.
_Avoid_: alert, email, update

**Carrier not told**:
A flag on a Withdrawn or Cancelled Carrier booking whose message never reached the Carrier, with the technical cause. The Planner phones the Carrier; nothing is retried. A booking the Carrier acknowledged has a Carrier told time instead.
_Avoid_: undelivered cancellation, failed cancellation

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

**Cause cleared**:
A marker on an open Exception whose cause no longer holds, for example a Collection failed Exception once the Shipment is Collected. The TMS never closes an Exception: the Planner still resolves it. Damaged is never marked, and a new cause of the same type removes the marker. Nothing is published for it.

**Exception number**:
The number the TMS gives an Exception when it opens it, such as `EX-0000042`. It stays the Exception's for life and is how the Planner, the Message trail and monitoring refer to it.
_Avoid_: incident number, ticket number

**Late delivery**:
An Exception for a Shipment that won't be, or wasn't, delivered within its delivery window: a Tracking event's ETA falls after the window, or the window passes without Delivered. Unlike Stale, it means Norvia knows the Shipment is late, not that it has gone quiet.
The **grace** is a TMS setting, 0 by default, added to the delivery window's latest time before the time-based check that also sets Stale opens a Late delivery without a Tracking event.
_Avoid_: delay, overdue, Stale

**Agreed delivery window**:
The delivery window the Planner sets on a Collected or In transit Shipment, from an open Exception, after agreeing a redelivery with the Carrier outside the TMS. It replaces the Transport Order's delivery window for Late delivery, Stale and the fallback ETA, while the Transport Order keeps the Shipper's original window. The last one set counts, and earlier ones are kept. It isn't sent to the Carrier.
_Avoid_: new window, revised window, redelivery slot

**Inventory**:
A Shipper's stock held by Norvia, per location. The WMS owns it.
_Avoid_: stock levels

## Fleet capacity

**Planning horizon**:
The days ahead, today and the next 14 by default, whose Own fleet availability is open for planning. A daily job opens each new day from the Drivers' Rosters.
_Avoid_: planning window, schedule

**Open day**:
A day of the Planning horizon, today or later, that the Planner can still change. Days before today are closed and read-only.
_Avoid_: active day

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

**Extension**:
A day on which a Driver's planned driving goes over the daily limit, up to the extended daily limit (10 h by default). The Driving-hour budget allows at most two a fixed week and uses one automatically when a day goes over.
_Avoid_: overtime, exception

**Load check**:
A rough comparison, for a day, of what the Own fleet can carry against the Own fleet work planned for it. It is a figure for the Planner, not a plan. When a Shipment is assigned to Own fleet in P3, the load check for its collection day, with that Shipment counted, comes back as a warning, or says the day is outside the Planning horizon.
_Avoid_: capacity check, capacity warning

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
A weight band within a Lane that sets the price per 100 kg for Transport Orders whose total weight falls in it. It runs up to and including its upper limit, so a weight on a boundary falls in the lighter band.
_Avoid_: weight bracket, tier

**Rating**:
Calculating the Freight Charge for a Transport Order from the Shipper's Rate Card.
_Avoid_: pricing, costing

**Freight Charge**:
The price Norvia charges a Shipper for one Transport Order, as calculated by Rating and broken into charge lines.
_Avoid_: price, rate, cost, quote

**Charge line**:
One part of a Freight Charge, in EUR without VAT and rounded to cents: always a Freight line, plus a Minimum charge adjustment line when the Lane's minimum charge applies. The Freight Charge's total is the sum of its charge lines.
_Avoid_: price line, surcharge (on its own)

**Rating result**:
Where a Transport Order stands commercially, separate from its status: Not yet rated, Rated, Rating failed or Not rated separately. Rated and Not rated separately are final. A Rejected booking has none.
_Avoid_: rating status, price status

**Rating failure**:
Why a Transport Order is Rating failed: a stable code (`NoRateCard`, `NoValidRateCardVersion`, `NoMatchingLane` or `WeightAboveHighestBreak`) and a plain-language message. The order stays Accepted, so a Shipper's pricing gap never rejects its booking.
_Avoid_: rating error, pricing error, rejection (that is for a booking)

**Rating exception**:
A Transport Order whose Rating result is Rating failed, as Customer service follows it up in the TMS UI and re-rates it, typically after its Rate Card was corrected. It leaves the rating exceptions once a re-rate Rates it.
_Avoid_: Exception (that is a Shipment going wrong, which the Planner resolves), pricing exception

**Not rated separately**:
The Rating result of a Fulfilment Transport Order (WMS channel). It is billed through the Fulfilment Contract, so it gets no Freight Charge of its own.
_Avoid_: unrated, exempt

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

**Simulator**:
Norvia's own application that feeds the estate with representative orders, shipments and trips at a chosen volume, so demos and the other applications' lists have something real to show. It acts only through the estate's real doors (the Norvia Partner API as each Shipper's and each Carrier's Persona, the TMS API as the Planner, and later the Driver app API), never a database or Service Bus, so everything it makes has a Message trail. It keeps its own runs and logs.
_Avoid_: seeder, data generator, load tester, fixture

**Simulation run**:
One go of the Simulator: a Simulation plan sent to the estate, either as fast as the estate accepts (burst) or spread over a window (paced). Only one run is active at a time, it can be cancelled but not resumed after a restart, and its log of what each actor did and how Norvia answered stays after a refresh. Its Shipper references are `SIM-<run>-<n>`.
_Avoid_: job, batch, test run, scenario

**Reset estate**:
What the Simulator's button of that name does: it returns the whole estate to a clean sheet through each application's own reset endpoint (the TMS API, the Partner portal and the Driver app API), then clears the Simulator's own runs, logs and run counter. Every transactional record goes, whoever created it, and reference data stays; the next Simulation run is number 1. It is refused while a run is active, while an application has its reset switched off, or while a Service Bus subscription holds unprocessed messages. Unlike Clear history, which removes only the Simulator's own finished runs and their logs and leaves what they sent.
_Avoid_: wipe, purge, reset the Simulator

**Simulation plan**:
What a Simulation run will send, made before anything is sent from the run's settings and a seed: the same ones always give the same plan. It holds the bookings and what the Planner means to do with the Transport Orders they become. What the Carriers then do with the Shipments that went to them is worked out when they have, from the same seed and the settings' Carrier shares. The Plan preview shows it per Shipper and booking.
_Avoid_: script, schedule, dataset

**Preset**:
A named set of numbers that fills a Simulation run's settings, such as "Quiet Monday" or "Peak week": the order count, the share of bad bookings, the share collected today, the Planner's shares (assigned, to Own fleet, reassigned), and burst or paced.
_Avoid_: profile, template, scenario

**Partner portal**:
The single application that plays every party outside Norvia (Shippers and Carriers) in demos, with one persona per party. It reaches Norvia only through API Management.
_Avoid_: shipper portal, carrier portal, external app

**Norvia Partner API**:
The API that Shippers and Carriers call Norvia through, in API Management. A partner calls it with a subscription key and an Entra token for its own client app; the token, never the payload, says which party it is. Its booking operation is the TMS's Shipper API intake channel.
_Avoid_: Shipper API (that is the intake channel), gateway, public API

**Persona**:
One fictional party (a specific Shipper or Carrier) as played in the Partner portal, with its own identity towards Norvia.
_Avoid_: user, role, tenant

**Answer mode**:
How a Carrier Persona answers the booking requests that reach its inbox: Manual, Auto-confirm, or Auto-reject with a chosen reason code, after a delay. The presenter sets it on the Persona's own page in the Partner portal, and a request keeps the mode it arrived with. An automatic answer goes through API Management exactly as a Confirm or Reject does.
_Avoid_: auto-reply, bot mode

**Persona switcher**:
The control in the Partner portal that chooses which Persona the presenter plays. Everything done after choosing one is done as that party, towards Norvia.
_Avoid_: login, impersonation, role switcher (that is for Norvia's own users)

**Presenter**:
The person who runs a demo of the sandbox: they play Norvia's users through the Role switcher and its partners through the Persona switcher. Not a party of Norvia's; in Azure, the one person the demo UIs let in.
_Avoid_: user, admin, operator (that is the Integration operator)

**Presenter panel**:
The page of the Partner portal (`/presenter`) where the presenter switches the demo's faults, such as *Driver app unavailable*, which makes the Driver app refuse every Trip version so the Planner sees Not delivered. It is for faults only: a Carrier Persona's modes are set on its own page.
_Avoid_: admin page, control panel

**Demo UIs**:
The TMS UI and the Partner portal, the two UIs a presenter shows side by side. They have no login; in Azure an Entra sign-in in front of them, the gate, admits only the presenter (ADR 0024). The gate is not a login in the domain's sense: the apps don't know who passed it.
_Avoid_: frontends, portals, login (for the gate)

## Integration

**Canonical Model**:
Norvia's own message format: one schema per business event or document, named with the terms in this glossary. Partner formats are mapped to and from it at the edges.
_Avoid_: common data model, standard format

**Message trail**:
Everything the integration layer did under one correlation ID: the call or event it started from, and every queue, subscription, Logic App run and application call that followed from it, fan-out included. An action by a Norvia employee in an application starts a new trail, except a re-rate: it continues the trail of its Transport Order's intake, so the order's whole rating is found by one correlation ID.
_Avoid_: integration message, message flow, transaction

**Flow**:
One named path through the integration layer, from what starts it to the application call or published event it ends in, such as transport-order-intake or carrier-answer. A business process uses one or more flows. Whatever reacts to a published event starts a new flow on the same Message trail. Every Hop belongs to exactly one flow.
_Avoid_: integration, interface, pipeline, workflow (that is one Logic App's definition)

**Hop**:
One thing the integration layer did on a Message trail: an API Management call, a Logic App run, an application call or a published event, with its flow, time, duration and outcome. A hop that started others caused them; where it started more than one, the trail fans out.
_Avoid_: step (that is an action inside a Logic App run), span, event

## Agents

**Agent**:
A Microsoft Foundry agent that reads the estate through API Management's MCP door and answers a person. It has its own Entra agent identity holding only read roles and no write tool, so it never changes anything: the person decides and acts (ADR 0040).
_Avoid_: bot, copilot, AI, assistant

**Agent run**:
One question put to an Agent, with its tool calls and its answer, identified by a run ID. It is a span in the host application, and its tool calls are hops on a Message trail tagged with the Agent's identity and the tool name.
_Avoid_: chat, session, conversation (that is Foundry's own record of a run)

**Suggestion**:
What an Agent proposes a person do, shown in the host application's UI: a cause or reasoning, the evidence it cites, a suggested action and a confidence. It is not stored, and acting on it is the person's own command, whose trace names the Agent and the run ID.
_Avoid_: recommendation, proposal, agent action
