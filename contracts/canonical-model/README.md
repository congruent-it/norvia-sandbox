# Canonical Model

Norvia's own message format ([CONTEXT.md](../../CONTEXT.md#integration)): one JSON Schema per business document or event, named with the glossary's terms and versioned in the file name. Partner formats are mapped to and from these at the edges. Changes to a published version are additive; a breaking change is a new version.

| Schema | What it is |
|---|---|
| [transport-order.v1](transport-order.v1.schema.json) | A Transport Order, as submitted to the TMS intake API and as carried on Transport Order events |
| [transport-order-accepted.v1](transport-order-accepted.v1.schema.json) | Event: the TMS accepted a Transport Order |
| [transport-order-rejected.v1](transport-order-rejected.v1.schema.json) | Event: the TMS rejected a booking, with every reason |
| [transport-order-rated.v1](transport-order-rated.v1.schema.json) | Event: the TMS rated an accepted Transport Order, with its Freight Charge |
| [transport-order-rating-failed.v1](transport-order-rating-failed.v1.schema.json) | Event: the TMS couldn't rate an accepted Transport Order, with the failure code and message |
| [shipment-planned.v1](shipment-planned.v1.schema.json) | Event: a Planner assigned a Transport Order to Own fleet or a Carrier, and the TMS made its Planned Shipment |
| [shipment-reassigned.v1](shipment-reassigned.v1.schema.json) | Event: a Planner reassigned a Planned Shipment to Own fleet or another Carrier, with who carried it before |
| [shipment-released.v1](shipment-released.v1.schema.json) | Event: a Planner released a Planned Shipment, handing it over with its Transport Order's addresses, windows and goods lines, who carries it, and its Carrier booking number for a Carrier Shipment |
| [carrier-booking-answer.v1](carrier-booking-answer.v1.schema.json) | Command: a Carrier's confirmation or rejection of a Requested Carrier booking, as the Carrier-answer Logic App sends it to the TMS Carrier booking API's answer operation |
| [carrier-booking-confirmed.v1](carrier-booking-confirmed.v1.schema.json) | Event: a Carrier confirmed a Requested Carrier booking with its own booking reference, making the Shipment Booked |
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

Conventions: property names in camelCase, timestamps as UTC with an offset, weights in kilograms as numbers, money in EUR without VAT as numbers rounded to cents, countries as ISO 3166-1 alpha-2 codes.
