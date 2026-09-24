# A Confirmed Carrier booking can be cancelled before collection

A Shipment's status normally only moves forward (P5), and a Confirmed Carrier booking used to stand for good (P4). Exception handling (P7) needs a way out when a Carrier won't make its collection. So the Planner can cancel a Confirmed Carrier booking, but only from an open Exception and only while the Shipment is Booked and not yet Collected. In one transaction, the Carrier booking becomes Cancelled (final) and the Shipment steps back from Booked to Planned. The Planner then reassigns or releases it again as after a rejection. The Shipment and its Shipment number stay the same (ADR 0007). The cancellation takes effect in the TMS straight away. The Carrier is told afterwards by a message it only acknowledges, and it can't refuse it. This is the one place where a Shipment's status goes backwards. The line is drawn at collection because until then the goods are still with the Shipper, and after it only the Carrier holding them can deliver them.

## Considered Options

- **A new Shipment for the re-plan:** keeps status strictly forward, but breaks one Shipment per Transport Order and splits the Transport Order's tracking over two Shipment numbers.
- **The Carrier confirms the cancellation first:** the TMS can't be out of step with the Carrier, but it needs a Cancelling state, and the Planner can't re-plan until the Carrier answers, which is exactly when the Carrier has already let Norvia down.
- **Re-planning after collection too:** handing goods from one Carrier to another is a transshipment, which is a new leg, not a re-plan. It is deferred with return legs.

## Consequences

Tracking events and Proofs of delivery for a Cancelled Carrier booking are refused, like those for Rejected and Withdrawn bookings, even when their event time is before the cancellation. The Planner deals with such races with the Carrier directly. When the cancellation message can't be delivered, the Carrier booking is flagged "Carrier not told", and the Planner phones the Carrier. Withdraw (P4) now sends the same message.
