# P6 Proof of delivery

Proof of delivery records the Consignee's confirmation that it received a Shipment's goods, and keeps it on the Shipment. Carriers, and later Drivers, send a Proof of delivery with its files. The TMS stores it, makes it the Shipment's current one, and flags Delivered Shipments that are still missing one. A Carrier's Proof of delivery travels as a claim check and is accepted asynchronously, so when one fails inside the integration layer, only Norvia can notice. Terms are defined in [CONTEXT.md](../../../CONTEXT.md).

| | |
|---|---|
| **Trigger** | A Carrier or Driver sends a Proof of delivery for a Shipment that is Booked or later |
| **Outcome** | The Proof of delivery and its files stored by the TMS as the Shipment's current one, any earlier one superseded, and `ProofOfDeliveryReceived` published |
| **Owning system** | TMS. It owns Shipments and their Proofs of delivery, files included (ADR 0002) |
| **Applies to** | Both. This design covers the Carrier source end to end. Own fleet feeds the same model through P12 |
| **Integrations touched** | Partner portal (Carrier Personas), API Management, inbound blob container, Service Bus (Proof of delivery queue, Shipment events), Carrier Proof of delivery Logic App, TMS Proof of delivery API |

## Scope

P6 defines the Proof of delivery model (content, current and superseded, the missing flag) and the Carrier path into it.

- **Carriers** are designed here in full.
- **Own fleet** is designed in [P12](p12-trip-execution.md). The Driver reports Logic App sends a Driver's Proof of delivery to the same TMS operation, with Own fleet as the source (see [Own fleet](#own-fleet)).
- **Shipper access** to a Proof of delivery belongs to P8. Invoicing waits for one: a Transport Order is Billable only with a current Proof of delivery ([P9](p9-freight-settlement.md#conditions), ADR 0012).

## Proof of delivery

A Proof of delivery holds:

| Part | Required |
|---|---|
| Received by: the name of the person who signed | Yes |
| Delivery time and place | Yes |
| Condition: clean, or with reservations | Yes |
| Remark. Required when the condition is with reservations | No |
| Files: a signed CMR, a signature, photos. PDF, JPEG or PNG, 1 to 5 files, each 10 MB or less | Yes |
| Source: a Carrier ID, or Own fleet | Yes |

### Current and superseded

A Shipment has at most one **current** Proof of delivery. A later one, with a different document ID, becomes current, and the earlier one is kept as **superseded**, files and all. A Carrier can therefore send a clearer scan, or a photo first and the signed CMR later. Nothing is merged: each Proof of delivery stands on its own.

### Relationship to the Shipment status

A Proof of delivery **never changes the Shipment status**. Only Tracking events do (P5). Tracking events and Proofs of delivery arrive on separate paths, so a Proof of delivery can arrive before the Delivered Tracking event, or for a Shipment that is still In transit. It is accepted and attached for any Shipment that is Booked or later, and the Planner sees it flagged (see [Planner's lists](#planners-lists)). Refusing it would lose a commercially important document over a race.

A Proof of delivery with reservations doesn't create a Damaged Tracking event either. The TMS doesn't invent Tracking events on a reporter's behalf. It records the reservations, and in the same transaction [P7](p7-exception-handling.md) opens a Reservations Exception, or adds it to an open one.

The delivery time on the Proof of delivery is what the Consignee signed. It isn't checked against the Delivered Tracking event's time.

## Steps

1. **Send.** The Carrier sends a Proof of delivery from its Persona in the Partner portal, by hand or through its tracking mode (see [Partner portal](#partner-portal)). The portal calls API Management with the Persona's token and subscription key. The body is one JSON document with the files base64-encoded.
2. **Accept.** API Management checks the token (audience "Norvia Partner API", role `ProofsOfDelivery.Submit`) and the body size, maps the `azp` claim to a Carrier ID (ADR 0005) and creates a correlation ID. It stores the body unparsed as a blob in the inbound container, then puts a message with the blob reference, the Carrier ID and the correlation ID on the Service Bus queue for inbound Proofs of delivery, and returns `202 Accepted` (ADR 0009). The Carrier learns nothing more about this Proof of delivery.
3. **Map.** The Carrier Proof of delivery Logic App reads the queue and fetches the blob. It maps the [Carrier Proof of delivery format](#carrier-proof-of-delivery-format) to the Canonical Model and decodes the files.
4. **Record.** The Logic App calls the TMS Proof of delivery API in one multipart call: the canonical metadata plus the files. The TMS checks the [Proof of delivery rules](#proof-of-delivery-rules), stores the files in its own storage, records the Proof of delivery as current, supersedes any earlier one, and publishes `ProofOfDeliveryReceived`, all in one transaction.
5. **Clean up.** Once the TMS has accepted it, the Logic App deletes the blob from the inbound container.
6. **Follow up.** The Planner sees Delivered Shipments without a Proof of delivery in their own list, and each Proof of delivery on the Shipment's timeline (see [Planner's lists](#planners-lists)).

### When something fails

| Failure | What happens |
|---|---|
| The body is over 25 MB | API Management refuses it with `413`. The only failure the Carrier sees |
| API Management stored the blob but couldn't put the message on the queue | The Carrier gets a server error and can send again. The orphaned blob is removed by the inbound container's retention rule |
| The content is invalid (a missing field, a bad file, the blob is gone) | The Logic App run fails. The message goes to the Proof of delivery queue's dead-letter queue |
| The TMS refuses the Proof of delivery (client error) | The Logic App dead-letters the message with the TMS's failure code and message. No retry |
| The TMS is unavailable (server error or timeout) | The message is retried from the queue. After the queue's maximum delivery count it goes to the dead-letter queue |

In every failure after `202`, the blob stays in the inbound container, so a dead-lettered message can be investigated and replayed. A lifecycle rule deletes anything left there after 90 days, the integration layer's retention (`integrationRetentionDays`).

None of these reaches the Carrier. Nothing is lost, but it's only seen if someone watches: alerts on failed Carrier Proof of delivery Logic App runs and on the dead-letter queue on the integration side, and the missing flag on the business side.

## Carrier Proof of delivery format

The Carrier Proof of delivery format follows the conventions of the [Carrier booking format](p4-carrier-booking.md#carrier-booking-format): JSON with its own field names, flat address fields, local date/times without an offset, and ISO 3166 alpha-3 country codes. Files are carried base64-encoded inside the JSON, so API Management can store the body without parsing it. The Carrier Proof of delivery Logic App maps Carrier → Canonical.

A Proof of delivery carries:

| Field | Required |
|---|---|
| Carrier booking reference | Yes |
| Carrier booking number | No. Used only when the reference doesn't match |
| Carrier's document ID | Yes |
| Received by | Yes |
| Delivery time, local to the location | Yes |
| Location: place, postcode, country | Yes |
| Condition: `CLEAN` or `RESERVATIONS` | Yes |
| Remark | Only with `RESERVATIONS` |
| Files: name, content type, content (base64) | 1 to 5 |

## Proof of delivery rules

| Rule | Failure code |
|---|---|
| The Carrier booking reference (or number) is known for the Carrier from the token | `ShipmentNotFound` |
| That Carrier is the Carrier on the Shipment's Confirmed Carrier booking | `ShipmentNotFound` |
| The Shipment is Booked or later | `ShipmentNotInTransport` |
| The delivery time is not in the future, beyond a small clock-skew allowance | Client error |
| The required fields are present, and a remark comes with reservations | Client error |
| 1 to 5 files, each 10 MB or less, each a PDF, JPEG or PNG judged by its content, not its declared name or type | `FileNotAccepted` |

A Carrier's Proof of delivery for one of its old bookings, one that was Rejected, Withdrawn or Cancelled (P7) before the Shipment went to someone else, is refused, not stored.

A Proof of delivery that repeats a Carrier's document ID already recorded for that Carrier succeeds, returns the original result flagged as a repeat, and publishes no event.

Each failure has a stable code and a readable message.

## Missing Proof of delivery

A Delivered Shipment is flagged **Proof of delivery missing** when it has no current Proof of delivery this long after the event time of its Delivered Tracking event. The threshold is a TMS setting, 48 hours by default. The rule is the same for Own fleet.

Like Stale, it is only a flag. Nothing happens automatically, it publishes nothing, and it clears when a Proof of delivery arrives. Chasing the Carrier happens outside the TMS.

## Planner's lists

P6 adds a fifth list to the Planner's four from P3 to P5, and extends P5's.

| List | Shows | Actions |
|---|---|---|
| Shipments in progress (P5, extended) | As in P5, plus a marker when a Proof of delivery has arrived before the Shipment is Delivered | Open the timeline |
| Delivered without Proof of delivery (P6) | Delivered Shipments with no current Proof of delivery: Shipment, Carrier or Own fleet, delivered time and the missing flag. Flagged Shipments first, then oldest first | Open the timeline |

A Shipment leaves the new list when a Proof of delivery arrives. The list is read-only and updates without a page refresh, as the other lists do.

The timeline shows Proofs of delivery alongside the Tracking events, in time order: received by, delivery time, condition and remark, and the files to view or download. Superseded Proofs of delivery are shown struck through.

## Partner portal

Each Shipment in a Carrier Persona's list, Delivered ones included, offers **Send Proof of delivery** once it is Booked. The form asks for received by, condition, remark (required with reservations) and files, 1 to 5: uploaded, or a sample CMR bundled with the portal. Nothing is sent until the presenter sends it.

The tracking modes from P5 decide whether one is sent automatically:

| Tracking mode | Proof of delivery |
|---|---|
| Manual | Only by hand |
| Auto-progress | After `POD` (Delivered), the Persona also sends a clean Proof of delivery with the sample CMR, after the same delay |

Automatic Proofs of delivery go through exactly the same path as manual ones. The portal gives each one a unique document ID and a location from the Shipment's delivery address.

## TMS API

The Proof of delivery API accepts only the Canonical Model (ADR 0003). It takes the files in the same call, as multipart. The TMS never reads from the integration layer's storage.

| Operation | Caller | Role |
|---|---|---|
| Record a Proof of delivery (canonical metadata plus files) | Carrier Proof of delivery Logic App. Driver reports Logic App ([P12](p12-trip-execution.md)) | `ProofsOfDelivery.Record` |
| Read a Proof of delivery file | TMS UI. Shippers use P8's own operation | Planner permissions from P3 |

The canonical Proof of delivery names its source: a Carrier ID, or Own fleet. For a Carrier, the Carrier comes from API Management through the Logic App, never from the payload.

## Storage

The TMS keeps Proof of delivery files in its own blob container. They stay in the hot tier for 30 days, then move to cool. They aren't deleted: a legal retention period is deferred.

The inbound container belongs to the integration layer and is only transit. The Logic App deletes each blob once the TMS has accepted it, and a lifecycle rule deletes anything left after 90 days, the integration layer's retention (`integrationRetentionDays`).

## Own fleet

Own fleet Shipments use the same model: the same content, current and superseded, and missing flag. [P12](p12-trip-execution.md) adds a second caller of "Record a Proof of delivery", the Driver reports Logic App, with Own fleet as the source.

- **Capture.** The Driver app requires a Proof of delivery to complete a delivery Stop as Delivered: received by, condition, a signature drawn on screen and up to four photos ([Capturing a Proof of delivery](p12-trip-execution.md#capturing-a-proof-of-delivery)). So the missing flag should almost never be raised for Own fleet, and when it is, a report went wrong in the integration layer.
- **Files.** They travel by claim check through the Driver app's own transit container, and the Driver reports Logic App makes the same multipart call as the Carrier Logic App (ADR 0016).
- **Rules.** P12's [Driver report rules](p12-trip-execution.md#driver-report-rules) replace the Carrier booking rules above. The content and file rules apply unchanged.

`ProofOfDeliveryReceived` carries the Trip number where it carries a Carrier booking reference.

## Correlation ID

API Management creates a new correlation ID for each Carrier Proof of delivery, as it does for each Carrier Tracking event in P5. It travels on the queue message. The TMS stores it on the Proof of delivery, and `ProofOfDeliveryReceived` carries it.

Tracing a Proof of delivery follows the Shipment number and the Carrier's document ID. Tracing one that never reached the TMS starts at the dead-letter queue or the failed Logic App run, which keep the correlation ID and the blob reference. The blob holds the Carrier's document ID.

## Events

All events go to the topic for Shipment events, through the TMS's transactional outbox (ADR 0003). A repeat publishes nothing, and neither does the missing flag.

| Event | Published when | Carries | Consumers |
|---|---|---|---|
| `ProofOfDeliveryReceived` | A Proof of delivery is recorded, including one that replaces an earlier one | Shipment number, Transport Order number, Carrier ID or Own fleet, Carrier booking reference, received by, delivery time, condition, remark, number of files, whether it replaces an earlier one, correlation ID | Monitoring. P7 opens its Exception, and P9 checks whether the order is Billable, inside the TMS, not from this event (ADR 0003) |

The event carries no file content and no link. Consumers read the files through the TMS.

## Presenter smoke run

After a deployment, and not in CI, the presenter checks Proof of delivery end to end on the deployed estate, before the headline incident breaks the claim-check path. Do it after the presenter smoke run of Shipment tracking ([docs/processes/tms/p5-shipment-tracking.md](p5-shipment-tracking.md#presenter-smoke-run)), whose steps get a Shipment Booked. Open the TMS UI and the Integration console side by side.

1. **Get another Booked Shipment.** Follow P4's run to step 7 again: Rotterdam to Düsseldorf as Brouwer Dranken, confirmed by Van Dijk Transport. Note the Transport Order number, and set Kessler Spedition back to *Auto-confirm* afterwards. Keep the Van Dijk Transport Persona's tracking mode on *Manual*, as the script delivers the Shipment itself, and an Auto-progress Persona would send a Proof of delivery of its own.
2. **Run the check.** Run `infra/smoke-test-proof-of-delivery.sh <Transport Order number>`. It posts as Van Dijk Transport, through API Management, from a throwaway Container Apps job running as the Partner portal, and reads the TMS as the intake identity. It reads the inbound container with the presenter's own Storage Blob Data Reader right and Application Insights, which is usually 1 to 5 minutes behind. It checks that:
   - `PU`, `DEP` and `POD` each return `202 Accepted` and the Shipment is Delivered, in *Delivered without Proof of delivery*;
   - a Proof of delivery with the sample CMR returns `202 Accepted`, the Shipment leaves that list, and its timeline holds the Proof of delivery as the current one, with its file;
   - the Proof of delivery's blob is gone from the inbound container;
   - a Proof of delivery for a reference no Carrier booking has is accepted and lands on the `carrier-proof-of-delivery` queue's dead-letter queue with `ShipmentNotFound`, and its blob is still in the inbound container;
   - a body over 25 MB is refused with `413`;
   - each call is in Application Insights by its correlation ID: from API Management, the Carrier Proof of delivery Logic App and the TMS, and for the refused body from API Management.
3. **Follow it.** The script prints the TMS UI's page for the Shipment, where the timeline shows the Proof of delivery with its file, and the Integration console's links by Shipment number and correlation ID.

The script prints `PASS` or `FAIL` per check, never a body, a token or a key. Dead-lettered messages are read by locking and releasing them, so the unknown reference's message and blob stay for the console and the inbound container. Discard the message in the console afterwards, and leave the blob to the retention rule.

If a check fails, file the gap as its own issue against P6, rather than working around it in the run book.

## Deferred

These were left out on purpose while designing P6, each with its reason.

- **Shipper download of a Proof of delivery.** That is [P8](p8-shipper-visibility.md). Notifying the Shipper about one is deferred there.
- **Acting on reservations.** P6 records them. Notifying the Shipper and resolving them is [P7](p7-exception-handling.md). Claims are settled outside the TMS.
- **The Planner uploading a Proof of delivery by hand**, for example one a Carrier emailed. It would add a third source with its own permissions, and it would cover up exactly the silent failure the demo exists to expose, as manual Tracking events would in P5.
- **Malware scanning of uploaded files.** It costs money and has nothing to do with the incidents. It is an as-found candidate below.
- **A legal retention period, and deleting Proof of delivery files after it.**
- **Polling Carrier APIs for Proofs of delivery.** Push through the Partner portal, as in P5.
- **How the Driver app captures a Proof of delivery.** Designed in [P12](p12-trip-execution.md#capturing-a-proof-of-delivery).
- **As-found toggles and presenter-panel faults.** They are added after the fixed state is built, as for P1 to P5. The candidates:
  - Toggle: no alert on failed Carrier Proof of delivery Logic App runs, or on the dead-letter queue of the Proof of delivery queue.
  - Presenter-panel fault: a Carrier sends a `.heic` photo. The TMS refuses it with `FileNotAccepted` and the message is dead-lettered, while the Carrier believes it succeeded.
  - Toggle: no lifecycle rule on the inbound container, and the Logic App doesn't delete processed blobs, so storage grows silently. This ties to the headline incident's unexplained Azure bill.
  - Toggle: Proof of delivery files in the TMS stay in the hot tier.
  - Toggle: the missing threshold unset, so a Proof of delivery that never arrived goes unnoticed.
  - Toggle: no malware scanning, so files from outside reach the TMS unscanned.
