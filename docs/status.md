# Status

What the sandbox does today, area by area, and what is not built. Checked against the issue list on 2026-11-13. The process catalogues ([TMS](processes/tms/README.md), [ERP](processes/erp/README.md)) say what each process is designed to do; this page says what exists. Terms are defined in [GLOSSARY.md](../GLOSSARY.md).

## Built

| Area | What it does today | Read |
|---|---|---|
| TMS | Takes in Transport Orders, rates them, plans them into Shipments and Trips, books Carriers, tracks Shipments, stores Proof of delivery, handles exceptions, answers Shipper tracking queries, feeds settlement, plans own-fleet capacity and dispatches and runs Trips (P1–P12). Its UI serves the Planner and Customer service | [applications/tms.md](applications/tms.md), [processes/tms/](processes/tms/README.md) |
| ERP | Loads Shippers, Carriers, Rate Cards and Carrier Rate Cards from the seed file and shows them read-only. Invoices Shippers, issues Credit notes and matches Carrier invoices against Carrier costs (E1–E2). Finance works in it | [applications/erp.md](applications/erp.md), [processes/erp/](processes/erp/README.md) |
| Driver app | Drivers receive a dispatched Trip and report each Stop, through Azure Integration Services | [applications/driver-app.md](applications/driver-app.md) |
| Partner portal | A presenter plays a Shipper or Carrier Persona: books transport, tracks Shipments, answers Carrier bookings, uploads Proof of delivery, fills a booking from an email and asks the tracking assistant. Reaches Norvia only through API Management | [applications/partner-portal.md](applications/partner-portal.md), [operations/partner-portal.md](operations/partner-portal.md) |
| Integration console | Finds Message trails, shows failed Logic App runs and dead-lettered messages, and resubmits or discards them. Reads the estate's telemetry and keeps nothing of its own | [applications/integration-console.md](applications/integration-console.md), [operations/integration-console.md](operations/integration-console.md) |
| Simulator | Feeds the estate with representative orders at a chosen volume, plans them as the Planner and has Carriers answer, track and deliver them, through the estate's real doors | [applications/simulator.md](applications/simulator.md), [operations/simulator.md](operations/simulator.md) |
| As-found estate | Toggles and presets that break the estate's alerts, storage, secrets and retention and fix them again, with presenter-panel faults and re-enactment scenarios | [operations/as-found.md](operations/as-found.md) |
| Estate health check | Audits the estate's configuration and cost and reports Findings. Runs locally from a snapshot, and in Azure against the real estate | [applications/estate-health-check.md](applications/estate-health-check.md), [operations/estate-health-check.md](operations/estate-health-check.md) |
| Agents | Integration operations, Estate health-check, Planner, Customer service, Intake, Carrier invoice matching, FinOps, Shipper tracking assistant and the Simulator personas. They read the estate through API Management MCP servers and have no write tools | [agents/README.md](../agents/README.md), [operations/foundry.md](operations/foundry.md) |

The estate (API Management, Logic Apps, Service Bus, monitoring, Entra) deploys to Azure with `azd up`; locally the AppHost runs the applications and stand-ins for the Azure services.

## Not built

| Area | State | Issue |
|---|---|---|
| WMS | No application. Fulfilment Orders and Inventory exist only in the glossary and the scenario. Handoff from the WMS is deferred | [#911](https://github.com/congruent-it/norvia-sandbox/issues/911) |
| E3 master-data sync, other entities | The Shipper, the Carrier, the Rate Card and the Carrier Rate Card are edited in the ERP API and replicated to the TMS. Vehicles and Drivers, and the ERP UI's forms, are not built, so those still come from the seed file | [#907](https://github.com/congruent-it/norvia-sandbox/issues/907) |
| Fuller Rate Card, re-rating | Deferred | [#908](https://github.com/congruent-it/norvia-sandbox/issues/908) |
| Push Shipment progress to the Shipper | Deferred | [#909](https://github.com/congruent-it/norvia-sandbox/issues/909) |
| Consignee tracking | Deferred | [#910](https://github.com/congruent-it/norvia-sandbox/issues/910) |
| Amend and cancel a Transport Order | Deferred | [#912](https://github.com/congruent-it/norvia-sandbox/issues/912) |
| Carrier backing out, booking amendments | Deferred | [#913](https://github.com/congruent-it/norvia-sandbox/issues/913) |
| Several Shipments per Transport Order | Deferred | [#914](https://github.com/congruent-it/norvia-sandbox/issues/914) |
| Automatic planning, route optimisation | Deferred | [#915](https://github.com/congruent-it/norvia-sandbox/issues/915) |
| Fleet realism (tachograph hours, breaks, depots) | Deferred | [#916](https://github.com/congruent-it/norvia-sandbox/issues/916) |
| Driver app v2 (offline, location) | Deferred | [#917](https://github.com/congruent-it/norvia-sandbox/issues/917) |
| Carrier channel variants (polling, EDI/AS2) | Deferred | [#918](https://github.com/congruent-it/norvia-sandbox/issues/918) |
| Dispatcher, Customer service and pricing desk roles | Deferred | [#919](https://github.com/congruent-it/norvia-sandbox/issues/919) |
| Finance depth (payments, VAT, invoice PDFs) | Deferred | [#920](https://github.com/congruent-it/norvia-sandbox/issues/920) |
| Retention and malware scanning for Proof of delivery | Deferred | [#921](https://github.com/congruent-it/norvia-sandbox/issues/921) |
| Integration console panel for failures that never reach a Logic App | Deferred | [#922](https://github.com/congruent-it/norvia-sandbox/issues/922) |

Also open, and optional: running the sandbox on Kubernetes ([#923](https://github.com/congruent-it/norvia-sandbox/issues/923)) and Dapr for messaging, resiliency and scheduled work ([#937](https://github.com/congruent-it/norvia-sandbox/issues/937), [#939](https://github.com/congruent-it/norvia-sandbox/issues/939), [#940](https://github.com/congruent-it/norvia-sandbox/issues/940)).
