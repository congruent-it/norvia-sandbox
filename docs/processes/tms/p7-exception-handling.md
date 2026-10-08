# P7 Exception handling

Exception handling makes sure someone acts when a Shipment on its way goes wrong. The TMS opens an Exception when a Tracking event reports a failed collection, a failed delivery or damage, when a Proof of delivery comes with reservations, or when a Shipment will be late. The Planner then decides what to do. They can notify the Shipper, cancel the Carrier booking and re-plan, agree a new delivery window, or conclude that nothing needs doing. Finally they resolve the Exception with an outcome. Terms are defined in [CONTEXT.md](../../../CONTEXT.md).

| | |
|---|---|
| **Trigger** | A Collection failed, Delivery failed or Damaged Tracking event (P5), a Proof of delivery with reservations (P6), or a Late delivery |
| **Outcome** | A Resolved Exception with its outcome, after any Shipper notifications, Carrier booking cancellation or agreed delivery window the Planner chose |
| **Owning system** | TMS. It owns Exceptions, Shipments and Carrier bookings (ADR 0002) |
| **Applies to** | Both. Re-planning by cancellation is for Carriers only. Taking an Own fleet Shipment off its Trip belongs to [P11](p11-trip-dispatch.md), and Re-delivery to [P12](p12-trip-execution.md#re-delivery) |
| **Integrations touched** | Service Bus (Shipment events), Shipper notification Logic App, Carrier cancellation Logic App, Key Vault, Partner portal (Shipper notification endpoint and inbox, Carrier cancellation endpoint), TMS Exception API |

## Scope

P7 defines the Exception model, the Planner's actions on it and the two outbound messages those actions send: a notification to the Shipper and a cancellation to the Carrier.

- **What opens an Exception**: exception Tracking events from P5, Proofs of delivery with reservations from P6, and the Late delivery check.
- **Re-planning** is designed for Carrier Shipments. For Own fleet, the Exception opens and the Shipper can be notified. The Planner can take the Shipment off its Trip before collection ([P11](p11-trip-dispatch.md#taking-a-shipment-off-a-trip)), and a Shipment not delivered is re-delivered on a later Trip ([P12](p12-trip-execution.md#re-delivery)).
- **Stale** stays a flag (P5). It doesn't open an Exception. Silence is a sign that the integration isn't healthy, not news for the Shipper.

## Exceptions

An Exception belongs to one Shipment. It has an Exception number given by the TMS, a type, one or more causes, the actions taken on it and, once resolved, an outcome.

### Types

| Type | Opened by |
|---|---|
| Collection failed | A Collection failed Tracking event (Carrier code `NPU`) |
| Delivery failed | A Delivery failed Tracking event (Carrier code `NDL`) |
| Damaged | A Damaged Tracking event (Carrier code `DMG`) |
| Reservations | A Proof of delivery recorded with the condition "with reservations" |
| Late delivery | A Tracking event whose ETA is after the latest time of the Shipment's delivery window. Or that latest time passing, plus a TMS setting (0 by default), without the Shipment being Delivered |

The delivery window is the agreed delivery window when the Planner has set one (see [Agreeing a delivery window](#agreeing-a-delivery-window)), and otherwise the Transport Order's.

Damaged and Reservations are kept apart because reservations are often a shortage, not damage. A Proof of delivery with reservations doesn't create a Damaged Tracking event (P6), so the two are never confused.

### One open Exception per type

A Shipment has at most one open Exception of each type. A cause of a type that already has an open Exception is **added** to it as a new cause. For example, a second Delivery failed, or a later ETA that is still after the window. It doesn't open a new Exception. A Shipment can have several Exceptions of different types open at once, for example Damaged and Late delivery.

Once an Exception is resolved, a new cause of the same type opens a new Exception.

### Cause cleared

The TMS never closes an Exception by itself. When its cause no longer holds, the Exception is only marked **cause cleared**, and the Planner still resolves it, because the Shipper may still need to be told.

| Type | Cause cleared when |
|---|---|
| Collection failed | The Shipment becomes Collected |
| Delivery failed | The Shipment becomes Delivered |
| Damaged | Never. Only the Planner can judge it |
| Reservations | A later Proof of delivery with the condition clean becomes current |
| Late delivery | The Shipment becomes Delivered. Or the latest ETA falls inside the delivery window again, for example after the Planner agrees a new one |

A new cause of the same type removes the marker again.

### After Delivered

P5 accepts Tracking events after Delivered and doesn't change the status. A Collection failed, Delivery failed or Damaged Tracking event that arrives then still opens an Exception, marked **after Delivered**. The Shipment stays Delivered. P7 never moves a Shipment's status forward, and it moves it back only when the Planner cancels a Carrier booking (see [Cancelling a Carrier booking](#cancelling-a-carrier-booking)). A Proof of delivery usually arrives after Delivered, so the marker applies only to Exceptions opened by Tracking events.

Late delivery isn't checked for a Shipment that is already Delivered.

## Status

### Exception

| Status | Meaning |
|---|---|
| Open | Opened by the TMS. It waits for the Planner. It can gain causes and actions |
| Resolved | The Planner resolved it with an outcome and a remark. Final |

| Outcome | Meaning |
|---|---|
| `NoActionNeeded` | Nothing needed doing, for example a Late delivery whose cause cleared |
| `Redelivered` | The Carrier delivered on another attempt, or the Own fleet on a Re-delivery, whether or not a delivery window was agreed |
| `Replanned` | The Carrier booking was cancelled, or the Own fleet Shipment was taken off its Trip, and the Shipment went back to planning |
| `ReturnedToShipper` | The goods go back to the Shipper. The return itself isn't planned in the TMS (see [Deferred](#deferred)) |
| `ClaimHandledOutsideTms` | Damage or a shortage is being settled as a claim outside the TMS |

The remark is required for every outcome. Any outcome can be chosen for any type. The TMS doesn't check that the outcome fits the actions taken.

### Carrier booking

P7 adds one Carrier booking status to P4's.

| Status | Meaning |
|---|---|
| Cancelled | Norvia ended the Carrier booking after the Carrier had confirmed it and before collection, with a reason. Final |

A Withdrawn or Cancelled Carrier booking also has a **Carrier told** time once the Carrier has acknowledged the cancellation message. If the message can't be delivered, it is flagged **Carrier not told**, with the technical cause.

### Shipment

P7 adds no Shipment status. Cancelling a Carrier booking moves the Shipment from Booked back to **Planned** in the same transaction, as a rejection does in P4 (ADR 0010).

## Steps

1. **Open.** The TMS opens the Exception in the same transaction as the Tracking event or Proof of delivery that causes it (P5, P6), and publishes `ExceptionOpened`. A Late delivery caused by the window passing is opened by the same time-based check that sets Stale. The TMS doesn't consume Service Bus events to do this (ADR 0003).
2. **See it.** The Exception appears at the top of the Planner's list of open Exceptions, and on the Shipment's timeline (see [Planner's lists](#planners-lists)).
3. **Act.** While it is open, the Planner takes any of the [actions](#actions), as often as needed and in any order.
4. **Resolve.** The Planner resolves the Exception with an outcome and a remark. The TMS publishes `ExceptionResolved`. The Exception leaves the list and stays on the timeline.

## Actions

| Action | Allowed when | Effect |
|---|---|---|
| Notify the Shipper | Always, while the Exception is open | A Shipper notification goes out (see [Notifying the Shipper](#notifying-the-shipper)) |
| Cancel the Carrier booking | A Carrier Shipment that is Booked, not yet Collected | The Carrier booking is Cancelled, the Shipment goes back to Planned and the Carrier is told (see [Cancelling a Carrier booking](#cancelling-a-carrier-booking)) |
| Agree a delivery window | A Shipment that is Collected or In transit | The Shipment gets an agreed delivery window (see [Agreeing a delivery window](#agreeing-a-delivery-window)) |
| Add a note | Always, while the Exception is open | A free-text note on the Exception, for example "phoned Kessler, redelivery Thursday" |

Every action is recorded on the Exception with the Planner and the time.

### Notifying the Shipper

1. **Write.** The Planner opens **Notify Shipper**. The message is prefilled from a template for the Exception's type, and the Planner can edit it.
2. **Record.** The TMS creates the Shipper notification with a notification ID and the status Requested, and publishes `ShipperNotificationRequested`, in one transaction.
3. **Map.** The Shipper notification Logic App picks up the event and maps the Canonical Model to the [Shipper notification format](#shipper-notification-format).
4. **Send.** The Logic App calls the Shipper Persona's notification endpoint in the Partner portal, with credentials it reads from Key Vault. The endpoint acknowledges receipt. The Partner portal treats the notification ID as the key, so a repeated call is ignored.
5. **Report.** The Logic App reports the notification to the TMS as Sent, and the TMS records the time.

The notification is one-way. The Shipper can't reply to it through the TMS.

On a server error or timeout, the Logic App retries with a bounded exponential policy, as in P4. It doesn't retry a client error. When the retries are used up, or on a client error, it reports the notification as **Not delivered** with the technical cause. The TMS publishes `ShipperNotificationNotDelivered`, and the Exception shows that the Shipper wasn't told. The Planner can notify again, or phone the Shipper and add a note. If the Logic App can't reach the TMS either, its run fails and the notification stays Requested, which the list shows.

| Shipper notification status | Meaning |
|---|---|
| Requested | Created by the Planner, not yet acknowledged by the Shipper |
| Sent | The Shipper's endpoint acknowledged it. Final |
| Not delivered | It never reached the Shipper, with the technical cause. Final |

### Cancelling a Carrier booking

The Planner cancels when a Carrier won't make its collection, for example after a Late delivery that opened because the ETA is far beyond the window. It is the only way P7 re-plans, and it is allowed only before collection (ADR 0010).

1. **Cancel.** The Planner chooses **Cancel Carrier booking** on the Exception and gives a reason text. In one transaction, the TMS moves the Carrier booking from Confirmed to Cancelled and the Shipment from Booked back to Planned, records the action on the Exception, and publishes `CarrierBookingCancelled`.
2. **Re-plan.** The Shipment appears in the Planner's list of Shipments to release, showing "Cancelled by Norvia" and the reason. The Planner reassigns it or releases it again, as for any Planned Shipment (P3). A new release creates a new Carrier booking (P4).
3. **Tell the Carrier.** The Carrier cancellation Logic App picks up the event and maps it to the [Carrier cancellation format](#carrier-cancellation-format). It calls the Carrier Persona's cancellation endpoint in the Partner portal with credentials from Key Vault, and reports the cancellation to the TMS as told. The Carrier can't refuse a cancellation. The endpoint only acknowledges receipt, and the Partner portal treats the Carrier booking number as the key.
4. **Resolve.** The Planner usually resolves the Exception as `Replanned`, now or once the Shipment is booked again.

The cancellation takes effect in the TMS straight away, whether or not the Carrier has been told. On a server error or timeout the Logic App retries as above. When the retries are used up, or on a client error, it reports the cancellation as not delivered. The TMS flags the Carrier booking **Carrier not told** with the technical cause and publishes `CarrierCancellationNotDelivered`. The Planner sees the flag in the list of Shipments to release and on the Exception, and phones the Carrier. Nothing is retried after that.

The same message and Logic App now also tell the Carrier about a **Withdrawn** Carrier booking (P4), with the same Carrier told and Carrier not told handling.

Once a Carrier booking is Cancelled, P5 and P6 refuse its Carrier's Tracking events and Proofs of delivery for it, as they refuse them for Rejected and Withdrawn bookings. That holds even when the event time is before the cancellation, for example a Collected that the Carrier sends late. The refused message is dead-lettered, and the Planner sorts it out with the Carrier.

### Agreeing a delivery window

After collection the Carrier holds the goods, so the Shipment can't be reassigned. What the Planner can re-plan is when it arrives. After agreeing a redelivery with the Carrier outside the TMS, for example by phone, the Planner sets an **agreed delivery window** on the Shipment from the Exception.

- The agreed delivery window replaces the Transport Order's for Late delivery, the Stale rules (P5) and the ETA when no Tracking event has given one (P5). The Transport Order keeps the Shipper's original window.
- Its latest time may not be in the past, and its earliest time may not be after its latest time.
- The Planner can set it again later. The last one set counts, and earlier ones are kept on the timeline.
- It isn't sent to the Carrier. The Carrier agreed it already.
- The TMS publishes `ShipmentDeliveryWindowAgreed`.

Setting it re-checks Late delivery at once. An open Late delivery whose latest ETA now falls inside the window is marked cause cleared.

## Shipper notification format

The Shipper notification format follows the conventions of the Shipper booking format in P1: JSON with its own field names, and local date/times without an offset, in the time zone of the delivery address. The Shipper notification Logic App maps Canonical → Shipper.

| Field | Required |
|---|---|
| Notification ID | Yes |
| Shipper reference (from the Transport Order) | Yes |
| Transport Order number | Yes |
| Exception type | Yes |
| Message, as written by the Planner | Yes |
| Current ETA, or the agreed delivery window if one is set | No |
| Norvia contact: the Planner's name | Yes |

The notification doesn't name the Carrier or the Shipment number. Towards the Shipper, Norvia carries the goods, just as towards the Carrier, Norvia is the customer (P4). The Shipper deals in Transport Orders (ADR 0011).

The schema is [shipper-notification.v1.schema.json](../../../contracts/partner-api/shipper-notification.v1.schema.json), with sample messages in `contracts/partner-api/samples/shipper-notifications`. The map reads the delivery address's country and postcode from the event, so it needs no lookup to show times in that time zone. An agreed delivery window replaces the ETA.

The Exception types map to the Shipper codes `COLLECTION_FAILED`, `DELIVERY_FAILED`, `DAMAGED`, `RESERVATIONS` and `LATE`.

## Carrier cancellation format

The Carrier cancellation format follows the conventions of the [Carrier booking format](p4-carrier-booking.md#carrier-booking-format). It carries:

| Field | Required |
|---|---|
| Carrier booking number | Yes |
| Carrier booking reference | Only for a Cancelled booking. A Withdrawn one never had one |
| Shipment number | Yes |
| Kind: `WITHDRAWN` or `CANCELLED` | Yes |
| Reason text | Yes for `CANCELLED`, no for `WITHDRAWN` |

The schema is [carrier-cancellation.v1.schema.json](../../../contracts/partner-api/carrier-cancellation.v1.schema.json), with sample messages in `contracts/partner-api/samples/carrier-cancellations`. The map reads the `CarrierBookingCancelled` or `CarrierBookingWithdrawn` event, so it needs no lookup. A `WITHDRAWN` message never carries a reference or a reason, and the Exception number stays inside Norvia.

## Action rules

| Rule | Failure code |
|---|---|
| The Exception exists | `ExceptionNotFound` |
| The Exception is Open (for every action and for resolving) | `ExceptionNotOpen` |
| For a cancellation: the Shipment has a Carrier, and its Carrier booking is Confirmed | `NoConfirmedCarrierBooking` |
| For a cancellation: the Shipment is Booked, not Collected or later | `ShipmentAlreadyCollected` |
| For a cancellation: a reason text is given | Client error |
| For an agreed delivery window: the Shipment is Collected or In transit | `ShipmentNotInTransport` |
| For an agreed delivery window: the window is consistent and doesn't end in the past | Client error |
| For resolving: the outcome is from the list, and a remark is given | Client error |

Each failure has a stable code and a readable message.

## Report rules

A sent or not-delivered report for a Shipper notification that is no longer Requested is ignored and logged. So is a told or not-told report for a Carrier booking that has already been told. Neither publishes an event.

## Planner's lists

P7 adds a sixth list to the Planner's five from P3 to P6, and extends two of them.

| List | Shows | Actions |
|---|---|---|
| Open Exceptions (P7) | Open Exceptions: Exception number, Shipment, Shipper, Carrier or Own fleet, type, opened at, number of causes, the cause cleared and after Delivered markers, and the last Shipper notification's status. Oldest first | Notify the Shipper, cancel the Carrier booking, agree a delivery window, add a note, resolve. Only the actions that are allowed are offered. Open the timeline |
| Shipments to release (P3, extended) | As in P4. The last Carrier booking's outcome can now also be "Cancelled by Norvia", with the reason. A Withdrawn or Cancelled booking shows "Carrier not told" when flagged | Reassign, release |
| Shipments in progress (P5, extended) | As in P6, plus a marker for a Shipment with an open Exception | Open the timeline |

The timeline shows each Exception with its causes, actions, Shipper notifications and outcome, and each agreed delivery window, alongside the Tracking events and Proofs of delivery. All lists update without a page refresh, as the other lists do.

## Partner portal

- **Shipper Personas** get a notification endpoint and a **notification inbox** that lists the notifications received, newest first, with their Transport Order number and Shipper reference. Every active Shipper has a Persona, as every active Carrier does.
- **Carrier Personas** get a cancellation endpoint. Their booking inbox from P4 shows a request as "withdrawn by Norvia" or "cancelled by Norvia", with the reason. A cancelled Shipment leaves the Persona's list of Shipments to track.

The endpoints have no answer modes. They acknowledge every valid message. A Shipper Persona's notification endpoint is `/shipper/v1/<Shipper ID>/notifications`. It asks its caller for the Shipper's own credential, which Key Vault holds as `shipper-endpoint-key-<Shipper ID>`, and stores a notification once per notification ID. A Carrier Persona's cancellation endpoint is `/carrier/v1/<Carrier ID>/cancellations`. It asks for the Carrier's own credential, the one Key Vault already holds as `carrier-endpoint-key-<Carrier ID>` for the booking endpoint, and applies a cancellation or withdrawal once per Carrier booking number. It acknowledges a message it can't apply, such as a cancellation for a booking its inbox never had, or a withdrawal of a request the Carrier already confirmed, and leaves the inbox as it was.

## TMS API

The Exception API accepts only the Canonical Model (ADR 0003). Each Logic App's managed identity holds only the role it needs.

| Operation | Caller | Role |
|---|---|---|
| Report a Shipper notification as sent or not delivered | Shipper notification Logic App | `ShipperNotifications.Report` |
| Report a cancellation as told or not delivered | Carrier cancellation Logic App | `CarrierBookings.Report` (from P4) |

The Planner takes every action and resolves Exceptions in the TMS UI, with the Planner permissions from P3.

## Own fleet

Own fleet Shipments get Exceptions in exactly the same way, from [P12](p12-trip-execution.md)'s Tracking events and Proofs of delivery, and from the Late delivery check. The Planner can notify the Shipper, agree a delivery window, add notes and resolve. Cancelling doesn't apply: there is no Carrier booking. Re-planning works in two ways:

- **Before collection**, the Planner takes the Shipment off its Trip, which sends it back to Planned ([P11](p11-trip-dispatch.md#taking-a-shipment-off-a-trip)). A Shipment whose collection failed goes back to Planned by itself when the Driver ends the Trip.
- **After collection**, a Shipment whose delivery failed is Awaiting re-delivery once the Driver ends the Trip, and the Planner puts it on a later Trip ([Re-delivery](p12-trip-execution.md#re-delivery)). The Planner can agree a delivery window with the Consignee for it, since the agreed delivery window isn't sent to anyone.

## Correlation ID

An Exception opened by a Tracking event or Proof of delivery carries that event's correlation ID. One opened by the time-based check gets a new one from the TMS. Each Planner action and each resolution gets a new correlation ID from the TMS, as in P3. The Shipper notification and cancellation Logic Apps carry the action's correlation ID on to the Partner portal and on their reports to the TMS.

Tracing an Exception follows the Shipment number and the Exception number. Tracing a notification follows its notification ID, and a cancellation its Carrier booking number.

## Events

All events go to the topic for Shipment events, through the TMS's transactional outbox (ADR 0003). A cause added to an open Exception, the cause cleared marker, a note, a sent report and a repeat publish nothing.

| Event | Published when | Carries | Consumers |
|---|---|---|---|
| `ExceptionOpened` | An Exception opens | Exception number, Shipment number, Transport Order number, Shipper, Carrier ID or Own fleet, type, the cause (Tracking event, Proof of delivery or window passed), the after Delivered marker, correlation ID | [Subscribers](../../architecture/events.md#catalogue) |
| `ExceptionResolved` | The Planner resolves an Exception | Exception number, Shipment number, type, outcome, remark, correlation ID | [Subscribers](../../architecture/events.md#catalogue). [P9](p9-freight-settlement.md) checks whether the order is Billable inside the TMS, not from this event (ADR 0003). |
| `ShipperNotificationRequested` | The Planner notifies the Shipper | Notification ID, Exception number, Shipment number, Transport Order number, Shipper, Shipper reference, delivery country and postcode, type, message, ETA or agreed delivery window, Planner's name, correlation ID | [Subscribers](../../architecture/events.md#catalogue) |
| `ShipperNotificationNotDelivered` | A notification is reported not delivered | Notification ID, Exception number, Shipment number, Shipper, technical cause, correlation ID | [Subscribers](../../architecture/events.md#catalogue) |
| `CarrierBookingCancelled` | The Planner cancels a Carrier booking | Carrier booking number, Carrier booking reference, Shipment number, Carrier ID, Exception number, reason text, correlation ID | [Subscribers](../../architecture/events.md#catalogue) |
| `CarrierCancellationNotDelivered` | A cancellation or withdrawal is reported not delivered | Carrier booking number, Shipment number, Carrier ID, kind, technical cause, correlation ID | [Subscribers](../../architecture/events.md#catalogue) |
| `ShipmentDeliveryWindowAgreed` | The Planner agrees a delivery window | Shipment number, Transport Order number, the window, Exception number, correlation ID | [Subscribers](../../architecture/events.md#catalogue) |
| `CarrierBookingWithdrawn` (P4, new consumer) | As in P4 | As in P4 | [Subscribers](../../architecture/events.md#catalogue) |

The Shipment going back to Planned publishes no event of its own, because `CarrierBookingCancelled` already says it, as in P4.

## Presenter smoke run

After a deployment, and not in CI, the presenter checks Exception handling end to end on the deployed estate, before the headline incident relies on it. Do it after the presenter smoke runs of Carrier booking ([P4](p4-carrier-booking.md#presenter-smoke-run)), Shipment tracking ([P5](p5-shipment-tracking.md#presenter-smoke-run)) and Proof of delivery ([P6](p6-proof-of-delivery.md#presenter-smoke-run)). The script makes the Carriers' calls. The Planner's actions are the presenter's, in the TMS UI, and the script stops and says what to do. Run it in a terminal, with the TMS UI, the Partner portal and the Integration console side by side.

1. **Get two Booked Shipments.** Follow P4's run twice: Rotterdam to Düsseldorf as Brouwer Dranken. The first is confirmed by Van Dijk Transport (story 25), the second by Kessler Spedition, set to *Auto-confirm* for it (story 26). Note both Transport Order numbers. Keep both Personas' tracking mode on *Manual*, as the script sends the Tracking events itself.
2. **Run the check.** Run `infra/smoke-test-exception-handling.sh <story 25 Transport Order number> <story 26 Transport Order number>`. It posts as the Carriers, through API Management, from a throwaway Container Apps job running as the Partner portal, and reads the TMS as the intake identity, and Application Insights, which is usually 1 to 5 minutes behind.
3. **Story 25.** The script sends `PU`, `DEP` and `NDL` as Van Dijk Transport and checks that a Delivery failed Exception opens. In the TMS UI, notify Brouwer Dranken and agree a new delivery window. The script checks that the notification ended Sent and that `ShipmentDeliveryWindowAgreed` was published, sends `POD` and checks that the Exception is marked cause cleared. Resolve it as `Redelivered`. The script checks that it left the list and that `ExceptionResolved` was published. Open Brouwer Dranken's notification inbox in the Partner portal and see the notification.
4. **Story 26.** The script sends an `NPU` with an ETA two weeks away as Kessler Spedition, so that the Shipment stays Booked, and checks that a Late delivery Exception opens. In the TMS UI, cancel the Carrier booking with a reason and wait. The script checks that the Carrier booking is Cancelled, that Shipments to release shows it as cancelled by Norvia with the reason and that the Carrier isn't flagged as not told. Then reassign the Shipment to Van Dijk Transport and release it. Open Kessler Spedition's booking inbox and see the request as "cancelled by Norvia".
5. **Stop the endpoint.** The script points the Shipper notification Logic App away from the Partner portal. Notify the Shipper again from the story 26 Exception. The script checks that the notification ends Not delivered after the retries, and puts the Logic App back, even when the run fails.
6. **Follow it.** The script checks that the Tracking events, the notification and the cancellation are in Application Insights by correlation ID, in API Management, the Logic Apps and the TMS, and prints the TMS UI's and the Integration console's links.

The script prints `PASS` or `FAIL` per check, never a body, a token or a key. If a check fails, file the gap as its own issue against P7, rather than working around it in the run book.

## Deferred

These were left out on purpose while designing P7, each with its reason.

- **The Carrier backing out after confirmation.** It needs a new inbound Carrier message. For now the Carrier reports Delivery failed with a remark, or the Planner cancels.
- **Cancelling a Carrier booking without an Exception, and cancelling a Transport Order.** Changes after confirmation go through an Exception so they always have a recorded reason. Cancelling a Transport Order is deferred in P1.
- **Re-planning after collection with another Carrier (transshipment), and return legs.** Both are new legs of transport, which needs one Transport Order to have several Shipments (ADR 0007). `ReturnedToShipper` records the decision only.
- **Sending an agreed delivery window to the Carrier as a booking amendment.** The Planner agreed it with the Carrier already. It needs a new Carrier message.
- **Automatic Shipper notification, and automatic re-planning.** The Planner decides, as in P3 to P6. The TMS only opens Exceptions.
- **A Customer service role** that works Exceptions and talks to Shippers. The Planner does it for now. The role can be added without changing the model.
- **Shipper replies, email notifications and notifying the Consignee.** The notification is one-way to the Shipper's endpoint. Consignee tracking is deferred in [P8](p8-shipper-visibility.md).
- **Claims.** Damage and shortages are settled outside the TMS and resolved as `ClaimHandledOutsideTms`. They don't affect invoicing: an open Exception holds billing back until it is resolved, and Finance issues a Credit note by hand if a claim is settled against the invoice ([P9](p9-freight-settlement.md#billable-is-final)).
- **Stale opening an Exception.** Stale means the Shipment has gone quiet, which is about the integration's health, not news for the Shipper.
- **As-found toggles and presenter-panel faults.** They are added after the fixed state is built, as for P1 to P6. The candidates:
  - Presenter-panel fault: the Shipper notification endpoint is down, so notifications end Not delivered.
  - Toggle: no alert on failed Shipper notification or Carrier cancellation Logic App runs.
  - Toggle: the Late delivery check switched off. Together with the unset Stale thresholds from P5, only a Consignee's phone call reveals the problem, as in the headline incident.
  - Presenter-panel fault: a Carrier's cancellation endpoint is down, so the Carrier booking is flagged Carrier not told.
