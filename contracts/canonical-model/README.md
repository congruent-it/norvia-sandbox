# Canonical Model

Norvia's own message format ([CONTEXT.md](../../CONTEXT.md#integration)): one JSON Schema per business document or event, named with the glossary's terms and versioned in the file name. Partner formats are mapped to and from these at the edges. Changes to a published version are additive; a breaking change is a new version.

| Schema | What it is |
|---|---|
| [transport-order.v1](transport-order.v1.schema.json) | A Transport Order, as submitted to the TMS intake API and as carried on Transport Order events |
| [transport-order-accepted.v1](transport-order-accepted.v1.schema.json) | Event: the TMS accepted a Transport Order |

Conventions: property names in camelCase, timestamps as UTC with an offset, weights in kilograms as numbers, countries as ISO 3166-1 alpha-2 codes.
