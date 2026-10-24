# Scenario: Norvia Logistics B.V.

The fictional company behind everything in this sandbox. Every application, flow, demo and seed record belongs to this one coherent world. Terms in **bold** are defined in [GLOSSARY.md](GLOSSARY.md).

This is the sandbox's own copy of the scenario. The health-check repo keeps a separate copy for its audit samples; the two may drift, and where they differ, this file is the reference for the sandbox. The main differences are listed at the end.

## The company

Norvia Logistics B.V. is a road freight and warehousing company with roughly 3,800 employees and a mid-eight-figure annual revenue. It is headquartered in Rotterdam and runs a distribution hub in Antwerp. Margins in freight and warehousing are thin, so IT has never been resourced as a strategic function. It is the department that keeps the lights on.

## What Norvia sells

Norvia's customers are **Shippers**: organizations that pay Norvia to store, move or deliver their goods. The goods end up with **Consignees**, which may be businesses or end consumers. Norvia offers two services:

- **Fulfilment**: Norvia holds a Shipper's **Inventory** in the Antwerp hub. When an order arrives (often from a marketplace), the warehouse picks and packs it as a **Fulfilment Order** and it is then delivered to the Consignee.
- **Transport-only**: a Shipper books Norvia to move goods from A to B as a **Transport Order**, with no warehousing involved.

Norvia runs a **mixed fleet**. Its own **Vehicles** and **Drivers** handle regional and Benelux transport, grouped into **Trips**. Subcontracted **Carriers** handle long-haul, parcel and peak volume.

## Systems

| System | Owns | Notes |
|---|---|---|
| **TMS** | Transport Orders, Shipments, Trips, Carrier bookings | Hosted in Azure. The first application built. |
| **WMS** | Fulfilment Orders, Inventory | Receives marketplace orders; requests a Transport Order when goods are ready to ship. |
| **ERP** | Shipper and Carrier master data, contracts, invoicing | Its first slice is invoicing ([E1](docs/processes/erp/e1-invoicing.md), [E2](docs/processes/erp/e2-carrier-invoice-matching.md)). Master data still comes from the sandbox's seed file, loaded by both the ERP and the TMS. |
| **Driver app** | Nothing | Used by Norvia's **Drivers** on the road to receive a dispatched **Trip** and report each **Stop**. Reaches the TMS only through Azure Integration Services. |
| **Partner portal** | Nothing at Norvia | Plays every outside party (Shippers and Carriers) as **Personas**. It reaches Norvia only through API Management. |
| **Integration console** | Nothing | Used by Norvia's **Integration operator** to find Message trails and resolve failed runs and dead-lettered messages. It reads the estate's telemetry and keeps nothing of its own ([design](docs/operations/integration-console.md)). |

The applications never call each other directly. Everything between them goes through Azure Integration Services: API Management at the edge, Logic Apps for mapping and routing, and Service Bus for events, all speaking Norvia's **Canonical Model**.

## The cast

The seed file is the one source of truth for the fictional world. These parties anchor the first demos:

- **Brouwer Dranken**: a Shipper that books transport-only Transport Orders.
- **Van Dijk Transport**: a Carrier that Norvia subcontracts for long-haul transport.

## How the estate got here

Norvia ran BizTalk Server 2016 for a decade to connect its TMS, WMS, ERP and a growing number of Carrier and Shipper EDI/API integrations. In 2024, a "modernize to Azure" initiative hired a contractor to migrate flows to Azure Integration Services. About 60% of the estate was migrated, the most valuable and fragile orchestrations first. Then the contractor's engagement ended and the initiative quietly stalled. BizTalk still runs the remaining 40% (mostly EDI/AS2 trading partner agreements), nobody is sure exactly which 40%, and the person who understood the whole picture is gone.

BizTalk is part of Norvia's history, not of the sandbox. Nothing here runs or simulates it: every flow in the sandbox is one that has already been migrated to Azure.

## The as-found estate

The sandbox can run in an *as-found* state, which reproduces the problems a health check would find, and each problem can be fixed one at a time:

- **Configuration problems** (for example no alert on a dead-letter queue, or a secret inlined in a workflow) are toggles in the estate's Bicep.
- **Runtime faults** (for example a Carrier sending a status the mapping can't handle, or the TMS API failing for a few minutes) are switched on from a clearly marked presenter panel in the Partner portal.
- **Inherited design smells** are also settings, never application logic. The main one: marketplace orders routed straight to the TMS instead of to the WMS.

## The headline incident

Two events in the same quarter made Norvia's integration estate a management problem:

1. A Carrier integration silently dropped shipment status updates for four days. Nobody noticed until a Consignee called to ask why their tracking hadn't changed.
2. Finance flagged a 30% quarter-over-quarter increase in the Azure bill with no matching change in shipment volume, and nobody in IT could explain which resource was driving it.

The first slice of the sandbox can re-enact the first incident end to end: the Carrier posts "delivered" in the Partner portal, the update fails inside the integration layer without raising an alert, and the Shipper's tracking screen stays on "in transit". The way to re-enact it is the [silent Carrier scenario](docs/operations/as-found.md#incident-1-the-silent-carrier).

## Differences from the health-check copy

- The TMS is hosted in Azure. There is no on-premises data gateway in the sandbox.
- Marketplace orders belong to the WMS; the old routing to the TMS is only an as-found setting.
- Norvia runs a mixed fleet of its own Vehicles plus subcontracted Carriers.
- Parties are named Shipper and Consignee rather than "customer".
- Outside parties are played by the Partner portal.
- BizTalk is not part of the sandbox.
