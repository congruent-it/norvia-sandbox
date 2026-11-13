# Presenter demo

The presenter smoke run across the estate, in the order to play it after a deployment (`azd up`, see [deploying-to-azure.md](deploying-to-azure.md)). Each step links to the `Presenter smoke run` section that holds its steps; this page only gives the order and the one line of context. Later runs build on the Shipments and Transport Orders of earlier ones, so keep the order.

Open the TMS UI, the Partner portal and the Integration console side by side, as the presenter who ran `azd up`.

1. **Partner API.** Book through API Management as Brouwer Dranken and as Van Dijk Transport, and follow the bookings by correlation ID: [infra/smoke-test-partner-api.ps1](../../infra/smoke-test-partner-api.ps1).
2. **Integration console.** Follow a booking's Message trail: [Presenter smoke run](integration-console.md#presenter-smoke-run), and its [dead letters](integration-console.md#presenter-smoke-run-dead-letters) part.
3. **P2 Rating.** Book to Antwerp and to Düsseldorf and check the Freight Charge and `NoMatchingLane`: [Presenter smoke run](../processes/tms/p2-rating.md#presenter-smoke-run).
4. **P3 Transport planning.** Assign, reassign and release the Düsseldorf booking: [Presenter smoke run](../processes/tms/p3-transport-planning.md#presenter-smoke-run).
5. **P4 Carrier booking.** Reject at Kessler Spedition, re-release, confirm as Van Dijk Transport: [Presenter smoke run](../processes/tms/p4-carrier-booking.md#presenter-smoke-run).
6. **P5 Shipment tracking.** Post `PU`, `DEP` and `POD` as Van Dijk Transport, including the dead-letter and resubmit: [Presenter smoke run](../processes/tms/p5-shipment-tracking.md#presenter-smoke-run).
7. **P6 Proof of delivery.** Deliver a second Shipment and send its Proof of delivery: [Presenter smoke run](../processes/tms/p6-proof-of-delivery.md#presenter-smoke-run).
8. **P7 Exception handling.** Needs P4, P5 and P6 first: [Presenter smoke run](../processes/tms/p7-exception-handling.md#presenter-smoke-run).
9. **P8 Shipper visibility.** Needs P4, P5 and P6 first: [Presenter smoke run](../processes/tms/p8-shipper-visibility.md#presenter-smoke-run).
10. **P9 Freight settlement.** Needs P4, P6 and P7 first: [Presenter smoke run](../processes/tms/p9-freight-settlement.md#presenter-smoke-run).
11. **E1 Invoicing.** The faults and tracing of Invoicing; needs P9 first: [Presenter smoke run](../processes/erp/e1-invoicing.md#presenter-smoke-run).
12. **E2 Carrier invoice matching.** Needs P9 first: [Presenter smoke run](../processes/erp/e2-carrier-invoice-matching.md#presenter-smoke-run).
13. **P10 Fleet capacity planning.** The Over capacity day; needs P3 first: [Presenter smoke run](../processes/tms/p10-fleet-capacity-planning.md#presenter-smoke-run).
14. **P11 Trip dispatch.** Build a Trip, dispatch it, and the Driver app unavailable case; needs P10 first: [Presenter smoke run](../processes/tms/p11-trip-dispatch.md#presenter-smoke-run).
15. **P12 Trip execution.** The Driver runs the seeded Trip; needs P11 first: [Presenter smoke run](../processes/tms/p12-trip-execution.md#presenter-smoke-run).
16. **Simulator.** A small simulation, *Quiet Monday* with seed 349: [Presenter smoke run in Azure](simulator.md#presenter-smoke-run-in-azure).

P7 to P9, E1 and E2 sit after P6 and P9 because their own sections name the runs they follow.
