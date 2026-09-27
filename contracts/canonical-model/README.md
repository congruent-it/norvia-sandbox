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

Conventions: property names in camelCase, timestamps as UTC with an offset, weights in kilograms as numbers, money in EUR without VAT as numbers rounded to cents, countries as ISO 3166-1 alpha-2 codes.
