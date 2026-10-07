# Norvia Partner API: bookings

The format in which a Shipper books a Transport-only transport through the Norvia Partner API (API Management), and Norvia's answers. It is a partner format: it deliberately differs from the [Canonical Model](../canonical-model/README.md), and the intake Logic App maps it to a Canonical Model `TransportOrder` before calling the TMS ([P1](../../docs/processes/tms/p1-transport-order-intake.md), [ADR 0019](../../docs/adr/0019-the-intake-map-is-dotnet-code-run-as-a-logic-apps-local-function.md)).

| Schema | What it is |
|---|---|
| [booking.v1](booking.v1.schema.json) | A booking, as the Shipper sends it |
| [booking-response.v1](booking-response.v1.schema.json) | Norvia's answer: Accepted, Already booked, Rejected or Not received |
| [carrier-booking.v1](carrier-booking.v1.schema.json) | A booking request, as Norvia's P4 booking Logic App sends it to a Carrier's booking endpoint (the other direction: see [Carrier booking](#carrier-booking)) |
| [carrier-answer.v1](carrier-answer.v1.schema.json) | A Carrier's confirmation or rejection of a booking request, as the Carrier sends it to the Norvia Partner API (see [Carrier answer](#carrier-answer)) |
| [carrier-answer-response.v1](carrier-answer-response.v1.schema.json) | Norvia's answer to a Carrier's answer: Recorded, Booking not open, Booking not found, Not allowed, Invalid or Not received |
| [carrier-tracking.v1](carrier-tracking.v1.schema.json) | A Carrier's tracking message, as the Carrier sends it to the Norvia Partner API (see [Carrier tracking](#carrier-tracking)) |
| [carrier-proof-of-delivery.v1](carrier-proof-of-delivery.v1.schema.json) | A Carrier's Proof of delivery with its files base64-encoded, as the Carrier sends it to the Norvia Partner API (see [Carrier Proof of delivery](#carrier-proof-of-delivery)) |
| [shipper-notification.v1](shipper-notification.v1.schema.json) | A notification, as Norvia's P7 Shipper notification Logic App sends it to a Shipper's notification endpoint (see [Shipper notification](#shipper-notification)) |

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

Any other client error from the TMS (400, 401 or 403, for example) means the integration layer is at fault, not the Shipper: the Logic App sent something the TMS won't take, or isn't allowed to call it. The Shipper gets `NOT_RECEIVED` too, and the Logic App run fails, for the Integration operator to find by its `messageId`.

An answer the Logic App can't pass on in full, such as an acceptance without a Transport Order number or a rejection without its reasons, is also `NOT_RECEIVED`, because relaying part of it would change its meaning.

## Calling the Norvia Partner API

A Shipper submits a booking with `POST {partner API URL}/transport-orders`, where the URL is the estate's output `ESTATE_PARTNER_API_URL` (`https://<API Management>.azure-api.net/partner/v1`). Every call carries:

| Header | Holds |
|---|---|
| `Ocp-Apim-Subscription-Key` | The partner's subscription key for the *Norvia partners* product. There is one subscription per Persona, named after it |
| `Authorization` | `Bearer` and an Entra v2.0 token for the Norvia Partner API, requested as the Persona's client app with the scope `<Norvia Partner API client ID>/.default`. Only a token holding the app role `TransportOrders.Submit` may submit |

API Management answers these calls itself, before any Logic App runs. Each answer carries the correlation ID in `X-Correlation-ID`, by which Norvia finds the refused call:

| HTTP status | When | Body |
|---|---|---|
| 401 | The subscription key or the token is missing, or isn't valid for the Norvia Partner API | API Management's own error |
| 403 | The token is valid but lacks the role `TransportOrders.Submit`, as a Carrier's does, or names a client that is no Shipper | `messageId` (the correlation ID) and `text` |

## How API Management calls the intake Logic App

API Management passes the booking to the intake Logic App's HTTP trigger as the Shipper sent it (`POST`, the booking as the body), with two headers of its own. It calls the trigger without a SAS signature, with a token of its own managed identity, and replaces any of these headers the Shipper sent:

| Header | Holds |
|---|---|
| `X-Shipper-ID` | The Shipper ID, which API Management took from the token (ADR 0005) |
| `X-Correlation-ID` | The correlation ID API Management created at the edge. It becomes the run's client tracking ID and the answer's `messageId` |

The Logic App answers with the response above, and returns the correlation ID in the `X-Correlation-ID` header too.

The trigger accepts only API Management. Easy Auth lets through only a token of API Management's identity, and the workflow's first step answers 403 to any call Easy Auth didn't authenticate as that identity, such as one through a SAS-signed callback URL. That run fails with the code `CallerNotApiManagement`, before anything is mapped or sent to the TMS.

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

## Carrier booking

The other way round, [P4](../../docs/processes/tms/p4-carrier-booking.md#carrier-booking-format): once a Planner releases a Shipment to a Carrier, the P4 booking Logic App maps the Canonical Model `ShipmentReleased` to the [carrier-booking.v1](carrier-booking.v1.schema.json) format and sends it to the Carrier Persona's booking endpoint. The map is `CarrierBookingMap`, run as a local function of the Logic App like the intake map ([ADR 0019](../../docs/adr/0019-the-intake-map-is-dotnet-code-run-as-a-logic-apps-local-function.md)). It is tested against the sample pairs in `samples/carrier-bookings`.

| Canonical Model `ShipmentReleased` | Carrier booking | What the map does |
|---|---|---|
| `carrierBookingNumber`, `shipmentNumber` | The same | Copies them |
| `transportOrder.collection.address`, `transportOrder.delivery.address` | `collectionStreet`, `collectionPostcode`, `collectionCity`, `collectionCountry`; the same for `delivery` | Flattens the address |
| `country`: ISO 3166-1 alpha-2 | ISO 3166-1 alpha-3 | Translates the code |
| `window.earliest`, `latest`: UTC with offset | `collectFrom`, `collectUntil`, `deliverFrom`, `deliverUntil`: local date/time without an offset | Converts to the time zone of the address's country, daylight saving time included |
| `transportOrder.delivery.consignee.name` | `consigneeName` | Copies the name only: no contact, phone or email |
| `transportOrder.goodsLines[]` | `goods[]` | Flattens `dimensionsCm` into `lengthCm`, `widthCm` and `heightCm` |
| `shipperId`, `transportOrder.shipperReference`, `transportOrder.instructions`, anything of the Freight Charge | Left out | Towards the Carrier, Norvia is the customer |

A value the Carrier format can't hold is left out, as in the intake map. The Carrier answers a request it can't use.

The request carries the release's correlation ID in `X-Correlation-ID` and the Carrier's credential in `X-Api-Key`. The Carrier booking number is the key: a request sent again is acknowledged and ignored.

## Carrier answer

A Carrier answers the booking request in its inbox with `POST {partner API URL}/carrier-answers`, through API Management like a Shipper's booking ([P4](../../docs/processes/tms/p4-carrier-booking.md)). The Carrier-answer Logic App maps the answer to the Canonical Model [carrier-booking-answer.v1](../canonical-model/carrier-booking-answer.v1.schema.json), records it with the TMS, and relays the TMS's response in the format below. The map is `CarrierAnswerMap` and the relay `CarrierAnswerResponses`, run as local functions of the Logic App like the intake map ([ADR 0019](../../docs/adr/0019-the-intake-map-is-dotnet-code-run-as-a-logic-apps-local-function.md)). They are tested against the sample pairs in `samples/carrier-answers` and `samples/carrier-answer-responses`.

The answer never names the Carrier. API Management takes it from the token's `azp` claim ([ADR 0005](../../docs/adr/0005-partner-identity-comes-from-the-token.md)), so a Carrier can only answer its own bookings; the TMS refuses the answer of any other Carrier. Every call carries the headers of [Calling the Norvia Partner API](#calling-the-norvia-partner-api), but the token must hold the app role `CarrierBookings.Respond`, which only Carrier Personas hold. API Management creates a **new correlation ID for each answer**, which the TMS stores on the Carrier booking beside the release's and the answer events carry.

| Carrier answer | Canonical Model `CarrierBookingAnswer` | What the map does |
|---|---|---|
| `carrierBookingNumber` | The path of the TMS call | Checks it reads `CB-` and seven digits |
| `decision`: `CONFIRMED` or `DECLINED` | `outcome`: `Confirmed` or `Rejected` | Translates it. Any other decision is `INVALID`: left out, it would reach the TMS as the default outcome |
| `bookingRef` | `carrierBookingReference` | Copies it. A confirmation without one is `INVALID` |
| `plannedCollection`: a date and time with its UTC offset | `plannedCollectionTime`: UTC with an offset | Converts it. Without an offset it is `INVALID`: it names no instant |
| `declineReason`: `NO_CAPACITY`, `LANE_NOT_SERVED`, `WINDOW_NOT_FEASIBLE`, `GOODS_NOT_ACCEPTED`, `OTHER` | `reasonCode`: `NoCapacity`, `LaneNotServed`, `WindowNotFeasible`, `GoodsNotAccepted`, `Other` | Translates it. An unknown code is `INVALID` |
| `declineText` | `reasonText` | Copies it. A rejection for `OTHER` without one is `INVALID` |
| Anything else, a `carrierId` included | Left out | The Carrier comes from the token alone |

An `INVALID` answer never reaches the TMS.

### Carrier answer responses

`messageId` is the correlation ID of the answer, by which Norvia finds every trace of it.

| `result` | HTTP status | When | Carries |
|---|---|---|---|
| `RECORDED` | 200 | The TMS recorded the answer: the booking is Confirmed or Rejected. `repeated` is true when the same answer was recorded before, and nothing was recorded again | `carrierBookingNumber`, `decision`, `repeated` |
| `BOOKING_NOT_OPEN` | 409 | The booking isn't Requested any more: Norvia withdrew it while the Carrier was answering, or it already has a different answer (`BookingNotOpen` at the TMS) | `carrierBookingNumber`, `text` |
| `BOOKING_NOT_FOUND` | 404 | Norvia has no such booking | `carrierBookingNumber`, `text` |
| `NOT_ALLOWED` | 403 | The booking is another Carrier's | `carrierBookingNumber`, `text` |
| `INVALID` | 400 | The answer breaks the rules above. Nothing was sent to the TMS | `carrierBookingNumber` when it named one, `text` saying why |
| `NOT_RECEIVED` | 502 | The TMS couldn't be reached, even after the Logic App's retries, or answered something the integration layer can't pass on. Nothing was recorded, so the Carrier answers again | `carrierBookingNumber`, `text` |

An answer the Logic App can't relay in full, such as a `200` without the booking's decision, is `NOT_RECEIVED` too, because relaying part of it would change its meaning. The run of a `NOT_RECEIVED` or `INVALID` answer fails, with the failure code `TmsUnavailable`, `TmsCallRefused` or `CarrierAnswerUnreadable`, for the Integration operator to find by its `messageId`. A refusal the TMS gave (`BOOKING_NOT_OPEN`, `BOOKING_NOT_FOUND`, `NOT_ALLOWED`) is a business outcome the Carrier has been told, so its run succeeds.

API Management answers a call it refuses itself, as for a booking: `401` for a missing or invalid subscription key or token, and `403` with `messageId` and `text` for a token without the role `CarrierBookings.Respond`, or whose client is no Carrier.

### How API Management calls the Carrier-answer Logic App

API Management passes the answer to the Logic App's HTTP trigger as the Carrier sent it, with two headers of its own that replace any the Carrier sent: `X-Carrier-ID` (the Carrier ID it took from the token) and `X-Correlation-ID` (the new correlation ID, which becomes the run's client tracking ID and the response's `messageId`). The trigger accepts only API Management, as the intake's does ([ADR 0020](../../docs/adr/0020-only-api-management-may-call-the-intake-logic-app.md)). The Logic App passes the answer to the map base64-encoded, so no field of it can stand in for the `X-Carrier-ID` it passes alongside.

## Carrier tracking

A Carrier reports what happened to a Shipment with a tracking message in the [carrier-tracking.v1](carrier-tracking.v1.schema.json) format ([P5](../../docs/processes/tms/p5-shipment-tracking.md#carrier-tracking-format)). The Carrier-tracking Logic App maps it to the Canonical Model [Tracking event](../canonical-model/tracking-event.v1.schema.json) before calling the TMS. The map is `CarrierTrackingMap`, run as a local function like the others ([ADR 0019](../../docs/adr/0019-the-intake-map-is-dotnet-code-run-as-a-logic-apps-local-function.md)). It is tested against the sample pairs in `samples/carrier-tracking`, with no Azure.

### How a Carrier submits it

`POST /partner/v1/carrier-tracking` with the subscription key and a token holding the app role `Tracking.Submit`. API Management checks both, takes the Carrier from the token's `azp`, creates the correlation ID and puts the message, as sent, on the tracking queue. It then answers `202 Accepted` with `{ "messageId": "<correlation ID>" }` and the same `X-Correlation-ID` header. The Carrier learns nothing more ([ADR 0008](../../docs/adr/0008-carrier-tracking-events-are-accepted-asynchronously.md)). A token without the role, or for a client that is no Carrier, gets `403`; if the message can't be put on the queue the answer is `503` and the Carrier sends it again. No Logic App is called: the Carrier-tracking Logic App reads the queue. Locally the Partner API stand-in answers the same ([ADR 0031](../../docs/adr/0031-locally-a-stand-in-and-a-hosted-worker-play-the-carrier-tracking-path.md)).

The message carries the application properties `correlationId`, `partyId` (the Carrier ID), `sourceMessageId` (the Carrier's `eventId`, when it has one) and `flow` = `carrier-tracking`, so a message Service Bus dead-letters itself can still be found by the Integration console.

The message never names the Carrier: API Management takes it from the token ([ADR 0005](../../docs/adr/0005-partner-identity-comes-from-the-token.md)).

| Carrier tracking | Canonical Model `TrackingEvent` | What the map does |
|---|---|---|
| `bookingRef` | `carrierBookingReference` | Copies it |
| `carrierBookingNumber` | `carrierBookingNumber` | Copies it |
| `eventId` | `eventId` | Copies it |
| `statusCode`: `PU`, `DEP`, `ARR`, `OFD`, `POD`, `NPU`, `NDL`, `DMG` | `type`: `Collected`, `Departed`, `Arrived`, `OutForDelivery`, `Delivered`, `CollectionFailed`, `DeliveryFailed`, `Damaged` | Translates it. Any other code fails the run, so the message is dead-lettered |
| `eventTime`: local date/time without an offset | `eventTime`: with the offset | Reads it in the time zone of the location's country, daylight saving time included |
| `place`, `postcode`, `country` | `location.place`, `location.postcode`, `location.countryCode` | Nests the flat fields |
| `country`: ISO 3166-1 alpha-3 | ISO 3166-1 alpha-2 | Translates the code |
| `eta`: a date and time with its UTC offset, UTC being fine | `eta`: UTC with an offset | Converts it. Unlike the event time it isn't local: the message holds no delivery address to read a local time in. Without an offset it is left out |
| `remark` | `remark` | Copies it |
| Anything else, a `carrierId` included | Left out | The Carrier comes from the token alone |

A value the map can't read, other than the status code, is left out, and the TMS's Tracking event rules report what is missing.

## Carrier Proof of delivery

A Carrier proves a delivery with a message in the [carrier-proof-of-delivery.v1](carrier-proof-of-delivery.v1.schema.json) format ([P6](../../docs/processes/tms/p6-proof-of-delivery.md#carrier-proof-of-delivery-format)). The Carrier Proof of delivery Logic App maps it to the Canonical Model [Proof of delivery](../canonical-model/proof-of-delivery.v1.schema.json) metadata and decodes its files before calling the TMS. The map is `CarrierProofOfDeliveryMap`, run as a local function like the others ([ADR 0019](../../docs/adr/0019-the-intake-map-is-dotnet-code-run-as-a-logic-apps-local-function.md)). It is tested against the sample pairs in `samples/carrier-proofs-of-delivery`, with no Azure.

The message never names the Carrier: API Management takes it from the token ([ADR 0005](../../docs/adr/0005-partner-identity-comes-from-the-token.md)).

| Carrier Proof of delivery | Canonical Model `ProofOfDelivery` | What the map does |
|---|---|---|
| `bookingRef` | `carrierBookingReference` | Copies it |
| `carrierBookingNumber` | `carrierBookingNumber` | Copies it |
| `documentId` | `documentId` | Copies it |
| `receivedBy` | `receivedBy` | Copies it |
| `deliveryTime`: local date/time without an offset | `deliveryTime`: with the offset | Reads it in the time zone of the location's country, daylight saving time included |
| `place`, `postcode`, `country` | `location.place`, `location.postcode`, `location.countryCode` | Nests the flat fields |
| `country`: ISO 3166-1 alpha-3 | ISO 3166-1 alpha-2 | Translates the code |
| `condition`: `CLEAN`, `RESERVATIONS` | `condition`: `Clean`, `Reservations` | Translates it |
| `remark` | `remark` | Copies it. Required with `RESERVATIONS` |
| `files[]`: `name`, `contentType`, `content` (base64) | `files[]`: `name`, `contentType`, and the decoded bytes for the call's `files` parts | Decodes the content; the metadata carries no content |
| Anything else, a `carrierId` included | Left out | The Carrier comes from the token alone |

Content the map can't read fails the run, so the message is dead-lettered: not a JSON object, a missing or unreadable required field, an unknown condition or country, reservations without a remark, no files or more than five, or a file that isn't valid base64. A file's type and size are the TMS's to judge (`FileNotAccepted`).

### Submitting

`POST /partner/v1/carrier-proofs-of-delivery` with the subscription key and a token holding the app role `ProofsOfDelivery.Submit`. API Management checks both, refuses a body over 25 MB with `413`, takes the Carrier from the token's `azp` and creates the correlation ID. It stores the body, as sent, as a blob in the inbound container, and only then puts the blob's reference, with the Carrier ID and the correlation ID, on the Proof of delivery queue. It answers `202 Accepted` with `{ "messageId": "<correlation ID>" }` and the same `X-Correlation-ID` header. The Carrier learns nothing more ([ADR 0009](../../docs/adr/0009-carrier-proofs-of-delivery-use-a-claim-check.md)). A token without the role, or for a client that is no Carrier, gets `403`; if the body can't be stored or its reference can't be queued the answer is a server error (`503`) and the Carrier sends the Proof of delivery again. No Logic App is called: the Carrier Proof of delivery Logic App reads the queue, fetches the blob, maps it, calls the TMS in one multipart call and deletes the blob once the TMS accepted it. Locally the Partner API stand-in answers the same ([ADR 0032](../../docs/adr/0032-locally-a-stand-in-and-a-hosted-worker-play-the-carrier-proof-of-delivery-path.md)).

The message on the queue is `{ "container": "...", "blobName": "<correlation ID>" }` with the application properties `correlationId`, `partyId` (the Carrier ID), `sourceMessageId` (the Carrier's `documentId`, when it has one) and `flow` = `carrier-proof-of-delivery`, so a message Service Bus dead-letters itself can still be found by the Integration console. A message the TMS refuses is dead-lettered with the failure code as `DeadLetterReason` and the TMS's message as `DeadLetterErrorDescription`, and its blob is kept; one the TMS couldn't be reached for is delivered again up to the queue's maximum delivery count.

## Shipper notification

[P7](../../docs/processes/tms/p7-exception-handling.md#shipper-notification-format): once the Planner notifies the Shipper of an Exception, the Shipper notification Logic App maps the Canonical Model `ShipperNotificationRequested` to the [shipper-notification.v1](shipper-notification.v1.schema.json) format and sends it to the Shipper Persona's notification endpoint, `POST /shipper/v1/<Shipper ID>/notifications` in the Partner portal. The map is `ShipperNotificationMap`, run as a local function of the Logic App ([ADR 0019](../../docs/adr/0019-the-intake-map-is-dotnet-code-run-as-a-logic-apps-local-function.md)) and tested against the sample pairs in `samples/shipper-notifications`.

The request carries the action's correlation ID in `X-Correlation-ID` and the Shipper's credential, which Key Vault holds as `shipper-endpoint-key-<Shipper ID>`, in `X-Api-Key`. The notification ID is the key: a notification sent again is acknowledged and ignored. The endpoint answers `200` when the notification is in the Shipper's inbox, or already was.

The Logic App retries a server error, a timeout, `408` and `429` up to three times, waiting 5, 10 and 20 seconds, and no other client error. It then reports the notification to the TMS as Sent (`POST /v1/shipper-notifications/<notification ID>/report-sent`), or as Not delivered (`report-not-delivered`) with the endpoint's status code, none for a timeout, and a message of at most 500 characters. It reports as the one identity holding `ShipperNotifications.Report`. Locally a hosted worker plays the Logic App ([ADR 0033](../../docs/adr/0033-locally-a-hosted-worker-plays-the-p7-shipper-notification-logic-app.md)).

## Carrier cancellation

[P7](../../docs/processes/tms/p7-exception-handling.md#carrier-cancellation-format): when Norvia withdraws a Carrier booking the Carrier hasn't answered, or cancels one it confirmed, the Carrier Persona's cancellation endpoint takes the [carrier-cancellation.v1](carrier-cancellation.v1.schema.json) message, `POST /carrier/v1/<Carrier ID>/cancellations` in the Partner portal, with sample pairs in `samples/carrier-cancellations`.

The request carries the action's correlation ID in `X-Correlation-ID` and the Carrier's own credential in `X-Api-Key`: the one Key Vault already holds as `carrier-endpoint-key-<Carrier ID>` for the booking endpoint. The Carrier booking number is the key: a cancellation or withdrawal sent again is acknowledged and changes nothing. The endpoint only acknowledges, so the Carrier can't refuse. It answers `200` with `{ "carrierBookingNumber": "...", "applied": true|false, "repeated": true|false }`, also for a booking its inbox never had, or a withdrawal of a request the Carrier had confirmed already (`applied` and `repeated` both false). It answers `401` for a missing or wrong credential, `404` for an unknown Carrier and `400` for a message that breaks the format (a `CANCELLED` without a reference or reason text, a `WITHDRAWN` with either, a missing correlation ID).

The Carrier's booking inbox shows the booking as "Withdrawn by Norvia", or "Cancelled by Norvia" with the reason, and a cancelled Shipment leaves the Carrier's list of Shipments to track.
