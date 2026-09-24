# Norvia sandbox

A working integration estate for a fictional company, **Norvia Logistics B.V.**, a road freight and warehousing company based in Rotterdam. The sandbox builds Norvia's applications (TMS, WMS, ERP, Driver app, Partner portal, Integration console) as realistic .NET applications. They talk to each other only through Azure Integration Services: API Management, Logic Apps and Service Bus. The point is to demo the integration layer. Someone clicks through a UI in one application, watches the result appear in another, and sees exactly where it failed when it doesn't.

The estate can also run *as found*, reproducing the problems a health check would find (a missing dead-letter alert, a mapping that can't handle a Carrier status, marketplace orders routed to the wrong system), and fixing them one at a time.

> **Status:** design phase. The domain model, decisions and business processes are documented. No application code or infrastructure has been committed yet. The first build is one thin end-to-end slice through the TMS.

## Start here

| Read | For |
|---|---|
| [SCENARIO.md](SCENARIO.md) | The company, its systems, the cast, and the headline incident the first slice re-enacts |
| [CONTEXT.md](CONTEXT.md) | The glossary. Every domain term used in code, docs and issues, and the synonyms to avoid |
| [docs/adr/](docs/adr/) | Architectural decisions, numbered 0001 onwards |
| [docs/processes/tms/](docs/processes/tms/README.md) | The TMS process catalogue, P1 Transport Order intake to P12 Trip execution, each with a design doc and a BPMN diagram |
| [docs/processes/erp/](docs/processes/erp/README.md) | The ERP process catalogue: E1 Invoicing, E2 Carrier invoice matching |
| [docs/operations/integration-console.md](docs/operations/integration-console.md) | How the Integration operator finds Message trails and resolves failed messages |
| [docs/coding-guidelines.md](docs/coding-guidelines.md) | How code is written here, including the tracking contract every flow must follow |

## Systems

| System | Owns |
|---|---|
| **TMS** | Transport Orders, Shipments, Trips, Carrier bookings, Own fleet availability |
| **WMS** | Fulfilment Orders, Inventory |
| **ERP** | Shipper, Carrier, Vehicle and Driver master data, Contracts, Rate Cards, invoicing |
| **Driver app** | Nothing. Drivers receive Trips and report Stops through it |
| **Partner portal** | Nothing at Norvia. It plays every outside Shipper and Carrier as a Persona |
| **Integration console** | Nothing. It reads the estate's telemetry |

Every concept has one system of record ([ADR 0002](docs/adr/0002-one-system-of-record-per-concept.md)). Until the ERP's master-data sync exists, a shared seed file supplies master data to both the ERP and the TMS.

## Architecture in brief

- **Integrated only through AIS.** Applications never call each other directly ([ADR 0001](docs/adr/0001-realistic-applications-integrated-only-through-ais.md)). All messages use Norvia's Canonical Model.
- **HTTP in, events out.** An application receives commands as HTTP calls from a Logic App and publishes what happened to Service Bus. It never consumes from Service Bus. Queries go from API Management straight to the application's API, with no Logic App on the path ([ADR 0003](docs/adr/0003-http-in-events-out.md)).
- **Identity from the token.** A partner's identity comes from its Entra token, never from the payload ([ADR 0005](docs/adr/0005-partner-identity-comes-from-the-token.md)).
- **Bicep owns the estate, Aspire owns the apps.** Hand-written Bicep in `infra/` provisions the AIS estate and every broken/fixed toggle. .NET Aspire runs the applications. azd deploys both ([ADR 0004](docs/adr/0004-bicep-owns-the-estate-aspire-owns-the-apps.md)).
- **Every flow is traceable.** Each flow records the same tracking keys (correlation ID, flow name, business keys, outcome), so the Integration console can follow any message end to end ([ADR 0017](docs/adr/0017-the-integration-console-reads-the-estates-telemetry-and-stores-nothing.md)).

## Tech stack

.NET 10 with Clean Architecture, EF Core with Ardalis repositories and specifications, Minimal APIs, and Blazor for web UIs. Tests use xUnit, NSubstitute, bUnit, Testcontainers and Playwright, with an 80% line coverage gate on unit tests. The Azure side is API Management, Logic Apps, Service Bus, Key Vault, Entra ID, Application Insights and Container Apps. See [docs/coding-guidelines.md](docs/coding-guidelines.md) for the full rules.

## Contributing

- Issues and specs live in GitHub Issues for `congruent-it/norvia-sandbox`, triaged with the labels in [docs/agents/triage-labels.md](docs/agents/triage-labels.md).
- Use the terms in [CONTEXT.md](CONTEXT.md). If a term is missing, add it there before you use it in code.
- A decision that is hard to reverse, surprising, or the result of a real trade-off gets an ADR in [docs/adr/](docs/adr/).
- Every change goes through a pull request with at least one review, and keeps to one issue.

Guidance for coding agents is in [CLAUDE.md](CLAUDE.md) and [docs/agents/](docs/agents/).
