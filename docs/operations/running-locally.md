# Running it locally

How to run the estate on a developer's machine under Aspire, and how to run its tests. What plays each Azure piece locally is in [architecture/local-vs-azure.md](../architecture/local-vs-azure.md). To deploy to Azure instead, see [deploying-to-azure.md](deploying-to-azure.md).

## Prerequisites

.NET 10 SDK, Docker, and Node.js 20 or later (the TMS UI's, the Partner portal's, the Integration console's and the Simulator's stylesheets are built with Tailwind CSS, [ADR 0018](../adr/0018-the-tms-ui-is-styled-with-tailwind-in-flatlys-look.md), [ADR 0022](../adr/0022-the-partner-portal-is-styled-with-tailwind-in-luxs-look.md), [ADR 0025](../adr/0025-the-integration-console-is-styled-with-tailwind-in-darklys-look.md)).

## Commands

```bash
dotnet run --project src/Norvia.AppHost      # TMS API and UI, Partner portal, Integration console, the local Partner API, migrations, SQL Server and the Service Bus emulator, with the Aspire dashboard
for p in $(find tests -name '*.UnitTests.csproj' -o -name '*.ArchitectureTests.csproj' | sort); do dotnet test "$p"; done   # unit and architecture tests
for p in $(find tests -name '*.IntegrationTests*.csproj' -o -name '*.UiTests*.csproj' | sort); do dotnet test "$p"; done        # integration and UI tests, one project at a time
```

Run the integration and UI tests one project at a time, as CI does, and not with `dotnet test Norvia.slnx`: each of them starts AppHosts of its own (SQL Server and the Service Bus emulator in Docker, about 6 GB each), and the solution run starts those of several projects at once. On a 64 GB machine that ran out of memory, so containers failed to start, Chromium crashed and the desktop's out-of-memory daemon killed applications. Run one such project at a time, and one test session at a time.

## Layout

| Where | What |
|---|---|
| [src/](../../src/) | The applications: the Aspire AppHost and ServiceDefaults, the TMS in Clean Architecture layers, with its Blazor UI in `Norvia.Tms.Web`, the ERP, the Partner portal, the Integration console and the Simulator |
| [tests/](../../tests/) | Tests per project, mirroring `src/` |
| [contracts/canonical-model/](../../contracts/canonical-model/) | The Canonical Model as JSON Schemas |
| [contracts/partner-api/](../../contracts/partner-api/README.md) | The Norvia Partner API's booking format and responses, with sample bookings and the Canonical Model Transport Order each maps to |
| [src/Integration/](../../src/Integration/) | What the integration layer runs: the intake Logic App ([IntakeLogicApp](../../src/Integration/IntakeLogicApp/)), and the intake map and relay it calls as .NET local functions ([ADR 0019](../adr/0019-the-intake-map-is-dotnet-code-run-as-a-logic-apps-local-function.md)) |
| [seed/norvia.seed.json](../../seed/norvia.seed.json) | The seed file: the one source of Shippers, Rate Cards, Carriers and Personas |
| [infra/](../../infra/) | The estate Bicep and the azd hooks that deploy it ([ADR 0004](../adr/0004-bicep-owns-the-estate-aspire-owns-the-apps.md)) |

## Tokens and logs

There is no Entra locally. The TMS trusts JWTs signed with the AppHost's secret parameter `local-token-signing-key` (issuer and audience in [LocalTokens.cs](../../src/Norvia.AppHost/LocalTokens.cs)), so submitting a Transport Order needs a token signed with that key, with the `TransportOrders.Intake` role and the local intake Logic App's client ID as `azp`. The integration tests issue their own.

Set `NORVIA_TEST_LOG_FILE` to a path to keep the TMS's own logs from an integration test run.

## Applications

What each application does locally is in its page in [applications/](../applications/README.md) and in [local-vs-azure.md](../architecture/local-vs-azure.md). Only what those pages do not say is here.

- **TMS UI** (`tms-web` on the Aspire dashboard). It reads the TMS database through the TMS query side; it takes in nothing and publishes nothing, and changes things only through the TMS API, as its own identity. Its lists of Transport Orders waiting for planning, of rejected bookings and of rating exceptions read again every second (`LiveList:RefreshInterval`), so a new one appears without a refresh. The Role switcher in the navbar remembers the chosen role in a cookie, and `/` opens that role's view: the Planner's by default. In Azure its managed identity holds the TMS API's `TransportOrders.Manage` role, and locally it signs its own token with `local-token-signing-key`. Its UI tests drive it with Playwright in headless Chromium, which their fixture installs on the first run. They keep a trace and a video of each failed test under `TestResults/playwright`, or under `$NORVIA_UI_TEST_ARTIFACTS/playwright` when that is set.
- **Partner portal** (`partner-portal-web`). It calls the Norvia Partner API only, which locally is the `partner-api` stand-in ([ADR 0023](../adr/0023-locally-a-stand-in-plays-the-norvia-partner-api.md)). The AppHost gives each Persona in the seed file a client ID and a subscription key for the run, and the portal signs each Persona's token with `local-token-signing-key`, with the submit role for Shipper Personas only.
- **Integration console** (`integration-console-web`). It searches what the local apps traced, through the Aspire dashboard's telemetry API ([ADR 0027](../adr/0027-locally-the-integration-console-searches-the-aspire-dashboards-telemetry.md)); it holds only the current run, so a booking made in the local Partner portal is found by its Transport Order number or correlation ID a few seconds later. To read a deployed estate instead, as you with your Azure CLI or IDE sign-in, give the AppHost that estate's workspace ID and retention as user secrets (`dotnet user-secrets set Parameters:estate-log-analytics-workspace-id <AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE_ID>` and `Parameters:estate-integration-retention-days`, both in `azd env get-values`), and hold Log Analytics Reader on the workspace. Logic Apps exist only in Azure, so a run opens only once you also set `Parameters:estate-resource-group-id` (`AZURE_ESTATE_RESOURCE_GROUP_ID`) and hold Logic Apps Standard Reader on the Logic App, as the estate grants the console's identity. Its UI tests run it alone on a test host with every port faked (`ConsoleWebHost`), and sign in by sending the header Easy Auth sends.

## Migrations

The TMS database's migrations are a deployment step, not something the TMS does when it starts ([coding guidelines §5](../coding-guidelines.md#5-persistence-ef-core-with-ardalis-repositories-and-specifications)). `Norvia.Tms.Migrations` applies them and exits; the TMS API and UI wait for it.

## CI

CI ([.github/workflows/ci.yml](../../.github/workflows/ci.yml)) runs the pull request gates of the coding guidelines on every PR and on `main`: a warning-free build, `dotnet format`, the vulnerable-package check, unit and architecture tests with the coverage gate, and the integration tests.
