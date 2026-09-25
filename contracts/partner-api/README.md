# Norvia Partner API: bookings

The format in which a Shipper books a Transport-only transport through the Norvia Partner API (API Management), and Norvia's answers. It is a partner format: it deliberately differs from the [Canonical Model](../canonical-model/README.md), and the intake Logic App maps it to a Canonical Model `TransportOrder` before calling the TMS ([P1](../../docs/processes/tms/p1-transport-order-intake.md), [ADR 0019](../../docs/adr/0019-the-intake-map-is-dotnet-code-run-as-a-logic-apps-local-function.md)).

| Schema | What it is |
|---|---|
| [booking.v1](booking.v1.schema.json) | A booking, as the Shipper sends it |
| [booking-response.v1](booking-response.v1.schema.json) | Norvia's answer: Accepted, Already booked, Rejected or Not received |

The booking never names the Shipper. API Management takes it from the token and passes it to the Logic App ([ADR 0005](../../docs/adr/0005-partner-identity-comes-from-the-token.md)).

## How it differs from the Canonical Model

| Partner booking | Canonical Model `TransportOrder` | What the map does |
|---|---|---|
| `bookingRef` | `shipperReference` | Copies it |
| `pickupStreet`, `pickupPostalCode`, `pickupCity`, `pickupCountry` | `collection.address` | Nests the flat fields |
| `pickupFrom`, `pickupUntil`: `dd-MM-yyyy HH:mm`, local to the pickup address | `collection.window.earliest`, `latest`: UTC with offset | Converts from the time zone of the pickup country, daylight saving time included |
| `receiverName`, `receiverContact`, `receiverPhone`, `receiverEmail` | `delivery.consignee` | Nests the flat fields |
| `receiverStreet`, `receiverPostalCode`, `receiverCity`, `receiverCountry` | `delivery.address` | Nests the flat fields |
| `deliverFrom`, `deliverUntil`: local to the receiver's address | `delivery.window` | Converts from the time zone of the receiver's country |
| Countries by English name (`Netherlands`, `United Kingdom`), their own name (`Nederland`, `Österreich`), a common variant (`Holland`, `UK`) or code | ISO 3166-1 alpha-2 (`NL`, `GB`) | Translates the name, for every country in Europe. It doesn't decide where Norvia carries goods: that is for the TMS, and for Rating once it exists |
| `items[].packaging`: UN/ECE Rec. 21 code `PX`, `PK`, `CR`, `NE` | `goodsLines[].packageType`: `pallet`, `parcel`, `crate`, `loose` | Translates the code. An unknown code is passed on as given, so the TMS's rejection names it |
| `items[].count` | `goodsLines[].quantity` | Copies it |
| `items[].weight`: text with a unit, `kg`, `t`, `lb` or `lbs`, decimal point or comma (`2,5 t`) | `goodsLines[].weightKg`: a number | Converts to kilograms, rounded to the gram |
| `items[].dimensions`: `L x W x H` text with a unit, `cm`, `mm` or `m` | `goodsLines[].dimensionsCm`: numbers | Converts to centimetres |
| `remarks` | `instructions` | Copies it |

The map never rejects a booking; the TMS decides. Any other value the map can't read (a country name it doesn't know, a date in another format, a weight without a unit) is left out, and the TMS's [validation rules](../../docs/processes/tms/p1-transport-order-intake.md#validation-rules) report what is then missing, with their stable codes. A local time in the hour the clocks skip in spring reads as standard time. One in the hour they repeat in autumn reads as its later, standard-time occurrence.

Dimensions are optional, so dimensions the map can't read are left out without the TMS noticing. The booking schema's pattern is what keeps them readable.

## Responses

The Logic App relays the TMS's outcome in the partner format, unchanged in meaning. `messageId` is the correlation ID of the attempt, by which Norvia finds every trace of it.

| `result` | HTTP status | When | Carries |
|---|---|---|---|
| `ACCEPTED` | 201 | The TMS accepted the booking | `norviaOrderNo`, the Transport Order number |
| `ALREADY_BOOKED` | 200 | The same booking (same Shipper, reference and content) was accepted before. Nothing new is booked | `norviaOrderNo` of the original Transport Order |
| `REJECTED` | 422 | The booking breaks Transport Order rules, or reuses a reference for different content | `errors`: every reason, with the TMS's code and message as `code` and `text` |
| `NOT_RECEIVED` | 502 | The TMS couldn't be reached, even after the Logic App's retries. Nothing is booked, so the Shipper sends it again | `text` saying so |

Any other answer from the TMS (400, 401 or 403) means the integration layer is at fault, not the Shipper: the Logic App sent something the TMS won't take, or isn't allowed to call it. The Shipper gets `NOT_RECEIVED` too, and the Logic App run fails, for the Integration operator to find by its `messageId`.

## Samples

[samples/bookings](samples/bookings) holds sample bookings, each (`name.booking.json`) with the Canonical Model Transport Order the map must give for it (`name.transport-order.json`), for the Shipper `SHP-0001`:

| Sample | Shows |
|---|---|
| `minimal` | Only what the TMS needs: a reference, both addresses, the Consignee's name, a collection window and one line |
| `full` | Everything: a delivery window, the Consignee's contact details, several lines of every package type, dimensions and remarks |
| `units` | Weights in tonnes and pounds, decimal commas, dimensions in metres and millimetres |
| `local-times` | A collection window across the night the clocks go back, and a delivery in another time zone (London) |
| `clocks-forward` | A collection time in the hour the clocks skip in spring |
| `clocks-back` | A collection time in the hour the clocks repeat in autumn |
| `unreadable` | Values the map can't read, left out for the TMS to reject. It breaks the booking schema on purpose |

The dates are in 2030, so the samples stay bookable for years. [samples/responses](samples/responses) holds a sample of each response. `Norvia.Integration.TransportOrderIntake.UnitTests` runs the map on every sample booking, compares the result with its expected Transport Order and validates it against the Canonical Model schema. It also checks that every sample keeps to the Partner API's schemas.
