# Norvia sandbox

A working integration estate for a fictional company, **Norvia Logistics B.V.**, a road freight and warehousing company based in Rotterdam. The sandbox builds Norvia's applications (TMS, WMS, ERP, Driver app, Partner portal, Integration console) as realistic .NET applications. They talk to each other only through Azure Integration Services: API Management, Logic Apps and Service Bus. The point is to demo the integration layer. Someone clicks through a UI in one application, watches the result appear in another, and sees exactly where it failed when it doesn't.

The estate can also run *as found*, reproducing the problems a health check would find (a missing dead-letter alert, a mapping that can't handle a Carrier status, marketplace orders routed to the wrong system), and fixing them one at a time.

> **Status:** the TMS, the ERP, the Driver app, the Partner portal, the Integration console and the Simulator run under Aspire and deploy to Azure with `azd up`. What each area does today, and what is not built, is in [docs/status.md](docs/status.md).

## Systems

| System | Owns |
|---|---|
| **TMS** | Transport Orders, Shipments, Trips, Carrier bookings, Own fleet availability |
| **WMS** | Fulfilment Orders, Inventory |
| **ERP** | Shipper, Carrier, Vehicle and Driver master data, Contracts, Rate Cards, invoicing |
| **Driver app** | Nothing. Drivers receive Trips and report Stops through it |
| **Partner portal** | Nothing at Norvia. It plays every outside Shipper and Carrier as a Persona |
| **Integration console** | Nothing. It reads the estate's telemetry |
| **Simulator** | Its own runs and logs. It feeds the estate with representative orders through its real doors |

Every concept has one system of record ([ADR 0002](docs/adr/0002-one-system-of-record-per-concept.md)).

## Architecture in brief

- **Integrated only through AIS.** Applications never call each other directly ([ADR 0001](docs/adr/0001-realistic-applications-integrated-only-through-ais.md)). All messages use Norvia's Canonical Model.
- **HTTP in, events out.** An application receives commands as HTTP calls from a Logic App and publishes what happened to Service Bus. It never consumes from Service Bus ([ADR 0003](docs/adr/0003-http-in-events-out.md)).
- **Identity from the token.** A partner's identity comes from its Entra token, never from the payload ([ADR 0005](docs/adr/0005-partner-identity-comes-from-the-token.md)).
- **Bicep owns the estate, Aspire owns the apps.** Hand-written Bicep in `infra/` provisions the AIS estate and every broken/fixed toggle. .NET Aspire runs the applications. azd deploys both ([ADR 0004](docs/adr/0004-bicep-owns-the-estate-aspire-owns-the-apps.md)).
- **Every flow is traceable.** Each flow records the same tracking keys, so the Integration console can follow any message end to end ([ADR 0017](docs/adr/0017-the-integration-console-reads-the-estates-telemetry-and-stores-nothing.md)).

## Tech stack

.NET 10 with Clean Architecture, EF Core with Ardalis repositories and specifications, Minimal APIs, and Blazor with Fluent UI components for web UIs. Tests use xUnit, NSubstitute, bUnit, Testcontainers and Playwright, with an 80% line coverage gate on unit tests. The Azure side is API Management, Logic Apps, Service Bus, Key Vault, Entra ID, Application Insights and Container Apps. See [docs/coding-guidelines.md](docs/coding-guidelines.md).

## Run it

Needs the .NET 10 SDK, Docker and Node.js 20 or later. Prerequisites, local tokens and logs are in [running-locally.md](docs/operations/running-locally.md).

```bash
dotnet run --project src/Norvia.AppHost      # the applications, SQL Server and the Service Bus emulator, with the Aspire dashboard
for p in $(find tests -name '*.UnitTests.csproj' -o -name '*.ArchitectureTests.csproj' | sort); do dotnet test "$p"; done   # unit and architecture tests
for p in $(find tests -name '*.IntegrationTests*.csproj' -o -name '*.UiTests*.csproj' | sort); do dotnet test "$p"; done        # integration and UI tests, one project at a time
```

## Deploy it

```bash
azd env new <environment> --subscription <subscription-id> --location <region>
AZD_UP_CONCURRENCY=1 azd up
```

The prerequisites, what `azd up` does, the smoke tests and the teardown are in [deploying-to-azure.md](docs/operations/deploying-to-azure.md).

| Target | Page |
|---|---|
| Running under Aspire | [running-locally.md](docs/operations/running-locally.md) |
| Deploying to Azure | [deploying-to-azure.md](docs/operations/deploying-to-azure.md) |

What plays each Azure piece locally is in [local-vs-azure.md](docs/architecture/local-vs-azure.md).

## Read more

| Read | For |
|---|---|
| [docs/README.md](docs/README.md) | Where to look: the map of the docs and the code |
| [docs/status.md](docs/status.md) | What each area does today and what is not built |
| [SCENARIO.md](SCENARIO.md) | The company, its systems, the cast, and the headline incident |
| [GLOSSARY.md](GLOSSARY.md) | Every domain term used in code, docs and issues |
| [docs/architecture/](docs/architecture/README.md) | C4 context and containers, events, identity and access, failure behaviour, local against Azure |
| [docs/adr/](docs/adr/) | Architectural decisions, numbered 0001 onwards |
| [docs/applications/](docs/applications/README.md) | Each application's APIs and database |
| [docs/processes/tms/](docs/processes/tms/README.md), [docs/processes/erp/](docs/processes/erp/README.md) | The process catalogues, each process with a design doc and a BPMN diagram |
| [docs/operations/presenter-demo.md](docs/operations/presenter-demo.md) | The presenter smoke run across the processes |
| [docs/operations/](docs/operations/) | Running and operating the estate: Integration console, health check, Simulator, as-found toggles, Agents |
| [docs/coding-guidelines.md](docs/coding-guidelines.md) | How code is written here, including the tracking contract |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Issues, glossary, ADRs and pull requests |

Guidance for coding agents is in [CLAUDE.md](CLAUDE.md) and [docs/agents/](docs/agents/).
