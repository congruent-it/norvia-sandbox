# Shippers see a Tracking status, not the Shipment's workings

When a Shipper queries its Transport Orders (P8), the TMS answers with a Tracking status (Received, Scheduled, Collected, In transit, Delivered) and the progress Tracking events, not with the Shipment status and its full log. Scheduled covers Planned, Released and Booked, so the Shipper can't tell Own fleet from a Carrier, and a Shipment that goes back to planning before collection (P4, ADR 0010) doesn't step backwards in the Shipper's view. The Carrier, exception Tracking events, Exceptions, remarks and Stale are never shown. What the Shipper hears about a problem comes only from a Shipper notification the Planner chooses to send (P7). The TMS makes this projection. The API Management policy only changes the format.

## Considered Options

- **Show the Shipment status and every Tracking event:** simplest, and the most transparent. But it names the Carrier, which contradicts Norvia carrying the goods towards the Shipper. It shows the Shipper a failed delivery or damage before the Planner has decided what to do, which works around P7. And every re-plan shows as a step backwards.
- **Show Exceptions and their outcomes as well:** it would tell the Shipper more, but it would expose Norvia's internal handling and make Exception outcomes a promise to the Shipper.
- **Project in the API Management policy:** it would keep the TMS API generic, but business rules would live in a policy that can't be unit-tested, and the TMS would hand internal data to the edge.

## Consequences

Shippers who integrate with the tracking API rely on the five Tracking statuses, so adding one is a change to a partner contract. A Shipper can see less than Norvia knows: after a late Delivery failed it stays on Delivered, and after `ReturnedToShipper` (P7) it stays on its last Tracking status until return legs exist. The last update time is what shows the Shipper that a Shipment has gone quiet.
