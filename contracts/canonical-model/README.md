# Canonical Model

Norvia's own message format ([GLOSSARY.md](../../GLOSSARY.md#integration)): one JSON Schema per business document or event, named with the glossary's terms and versioned in the file name. Partner formats are mapped to and from these at the edges. Changes to a published version are additive; a breaking change is a new version.

| Schema | What it is |
|---|---|
| [transport-order.v1](transport-order.v1.schema.json) | A Transport Order, as submitted to the TMS intake API and as carried on Transport Order events |
| [transport-order-accepted.v1](transport-order-accepted.v1.schema.json) | Event: the TMS accepted a Transport Order |
| [transport-order-rejected.v1](transport-order-rejected.v1.schema.json) | Event: the TMS rejected a booking, with every reason |
| [transport-order-rated.v1](transport-order-rated.v1.schema.json) | Event: the TMS rated an accepted Transport Order, with its Freight Charge |
| [transport-order-rating-failed.v1](transport-order-rating-failed.v1.schema.json) | Event: the TMS couldn't rate an accepted Transport Order, with the failure code and message |
| [transport-order-billable.v1](transport-order-billable.v1.schema.json) | Event: the TMS decided a Transport-only order is Billable (Delivered, Rated, with a current Proof of delivery), with its frozen Freight Charge, the Shipper reference, the Shipment number, the delivery time, whether it was released without a Proof of delivery, and the correlation IDs of its intake and of the transaction that made it Billable |
| [shipment-planned.v1](shipment-planned.v1.schema.json) | Event: a Planner assigned a Transport Order to Own fleet or a Carrier, and the TMS made its Planned Shipment |
| [shipment-reassigned.v1](shipment-reassigned.v1.schema.json) | Event: a Planner reassigned a Planned Shipment to Own fleet or another Carrier, with who carried it before |
| [shipment-released.v1](shipment-released.v1.schema.json) | Event: a Planner released a Planned Shipment, handing it over with its Transport Order's addresses, windows and goods lines, who carries it, and its Carrier booking number and Carrier cost for a Carrier Shipment |
| [carrier-cost.v1](carrier-cost.v1.schema.json) | Document: what Norvia pays a Carrier for one Carrier booking, calculated at release from the Carrier Rate Card, or entered as a Spot price, and frozen, with its charge lines, total, source and the time it was calculated; carried by `ShipmentReleased` |
| [carrier-booking-answer.v1](carrier-booking-answer.v1.schema.json) | Command: a Carrier's confirmation or rejection of a Requested Carrier booking, as the Carrier-answer Logic App sends it to the TMS Carrier booking API's answer operation |
| [carrier-booking-confirmed.v1](carrier-booking-confirmed.v1.schema.json) | Event: a Carrier confirmed a Requested Carrier booking with its own booking reference and the total of the Carrier cost it accepted, making the Shipment Booked |
| [carrier-booking-rejected.v1](carrier-booking-rejected.v1.schema.json) | Event: a Carrier rejected a Requested Carrier booking with a reason, sending the Shipment back to Planned |
| [carrier-booking-not-delivered.v1](carrier-booking-not-delivered.v1.schema.json) | Event: a Carrier booking request never reached the Carrier, with the technical cause, sending the Shipment back to Planned |
| [carrier-booking-withdrawn.v1](carrier-booking-withdrawn.v1.schema.json) | Event: the Planner withdrew a Requested Carrier booking before the Carrier answered, sending the Shipment back to Planned |
| [tracking-event.v1](tracking-event.v1.schema.json) | Command: a Tracking event a Carrier (or later Own fleet) reported for a Shipment, as the Carrier-tracking Logic App sends it to the TMS Tracking event API's record operation, naming its source |
| [proof-of-delivery.v1](proof-of-delivery.v1.schema.json) | Command: a Proof of delivery's metadata, as the Carrier Proof of delivery Logic App sends it in the `metadata` part of its multipart call to the TMS Proof of delivery API, naming its source and describing its files; the files travel in the `files` parts |
| [shipment-collected.v1](shipment-collected.v1.schema.json) | Event: a Tracking event moved a Booked Shipment to Collected |
| [shipment-in-transit.v1](shipment-in-transit.v1.schema.json) | Event: a Tracking event moved a Shipment to In transit |
| [shipment-delivered.v1](shipment-delivered.v1.schema.json) | Event: a Tracking event moved a Shipment to Delivered |
| [shipment-exception-reported.v1](shipment-exception-reported.v1.schema.json) | Event: an exception Tracking event was recorded for a Shipment, with the exception type and remark |
| [proof-of-delivery-received.v1](proof-of-delivery-received.v1.schema.json) | Event: a Proof of delivery was recorded as a Shipment's current one, with who received the goods, when, in which condition and how many files; never the files themselves |
| [carrier-cost-incurred.v1](carrier-cost-incurred.v1.schema.json) | Event: a Carrier Shipment moved to Delivered, so the Carrier earned the Carrier cost of its Confirmed Carrier booking, with the booking number and reference, the Shipment and Transport Order numbers, the Carrier, the Carrier cost with its charge lines and source, and the delivery time |
| [trip-dispatched.v1](trip-dispatched.v1.schema.json) | Event: a Planner dispatched a Trip, with its Vehicle, Driver, planned start and driving duration and every Stop in order, each with its Shipment and Transport Order numbers, place, window and goods lines; sample messages are in [samples/trip-dispatched](samples/trip-dispatched) |
| [exception-opened.v1](exception-opened.v1.schema.json) | Event: the TMS opened an Exception for a Shipment, with its Exception number, type, cause and who carries the Shipment |
| [exception-resolved.v1](exception-resolved.v1.schema.json) | Event: the Planner resolved an Exception, with its outcome and remark |
| [carrier-booking-cancelled.v1](carrier-booking-cancelled.v1.schema.json) | Event: the Planner cancelled a Confirmed Carrier booking before collection, with a reason, sending the Shipment back to Planned |
| [carrier-cancellation-not-delivered.v1](carrier-cancellation-not-delivered.v1.schema.json) | Event: the Carrier cancellation Logic App's message for a Cancelled or Withdrawn Carrier booking never reached the Carrier, with the technical cause; the booking is flagged Carrier not told |
| [shipper-notification-requested.v1](shipper-notification-requested.v1.schema.json) | Event: the Planner notified the Shipper from an open Exception, with the message, the Shipment's ETA or agreed delivery window and the notification ID |
| [shipper-notification-not-delivered.v1](shipper-notification-not-delivered.v1.schema.json) | Event: the Shipper notification Logic App's message never reached the Shipper, with the technical cause |
| [shipment-delivery-window-agreed.v1](shipment-delivery-window-agreed.v1.schema.json) | Event: the Planner agreed a delivery window with the Carrier for a Collected or In transit Shipment |
| [shipper-transport-order-list-item.v1](shipper-transport-order-list-item.v1.schema.json) | Response: the Shipper's view of one of its Transport Orders in a list, with its Tracking status, ETA and last update, as the TMS Shipper tracking API's list operation returns it; never a Carrier, a Shipment number, an Exception or Stale |
| [shipper-transport-order-detail.v1](shipper-transport-order-detail.v1.schema.json) | Response: the Shipper's view of one Transport Order, got by number or found by its Shipper reference, with its places, windows, Tracking status, ETA, last update, progress and current Proof of delivery with its files; never a Carrier, a Shipment number, an Exception, Stale or a remark |

Conventions: property names in camelCase, timestamps as UTC with an offset, weights in kilograms as numbers, money in EUR without VAT as numbers rounded to cents, countries as ISO 3166-1 alpha-2 codes.
