# Norvia sandbox

A working integration estate for a fictional company, **Norvia Logistics B.V.**, a road freight and warehousing company based in Rotterdam. The sandbox builds Norvia's applications (TMS, WMS, ERP, Driver app, Partner portal, Integration console) as realistic .NET applications. They talk to each other only through Azure Integration Services: API Management, Logic Apps and Service Bus. The point is to demo the integration layer. Someone clicks through a UI in one application, watches the result appear in another, and sees exactly where it failed when it doesn't.

The estate can also run *as found*, reproducing the problems a health check would find (a missing dead-letter alert, a mapping that can't handle a Carrier status, marketplace orders routed to the wrong system), and fixing them one at a time.

> **Status:** first build. The TMS takes in a Transport Order through its intake API and publishes `TransportOrderAccepted`, running locally under Aspire. The TMS UI lists the Transport Orders waiting for planning for the Planner and the rejected bookings for Customer service, both live, and shows each one in full; a Role switcher moves between the two without a login. The partner booking format and the intake map to the Canonical Model exist and are tested with sample messages. `azd up` deploys the estate (Service Bus, monitoring, Entra, the intake Logic App, API Management) and the TMS to Azure. A Shipper books through the Norvia Partner API in API Management with a subscription key and an Entra token; API Management takes the Shipper from the token and passes the booking to the intake Logic App, which maps it, submits it to the TMS as the one identity holding the intake role, and answers in the partner format. In the Partner portal a presenter plays a Persona: as Brouwer Dranken they book a transport and see the Transport Order number, or every reason Norvia rejected it, while the order appears in the TMS UI beside it. As Van Dijk Transport, a Carrier, the booking is refused. In Azure the presenter opens both UIs from their own browser behind an Entra sign-in that admits only them ([ADR 0024](docs/adr/0024-the-demo-uis-are-public-behind-an-entra-gate-for-the-presenter.md)). Locally a stand-in plays API Management and the intake Logic App ([ADR 0023](docs/adr/0023-locally-a-stand-in-plays-the-norvia-partner-api.md)), so the whole path runs under Aspire.

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

.NET 10 with Clean Architecture, EF Core with Ardalis repositories and specifications, Minimal APIs, and Blazor with Fluent UI components for web UIs, styled with Tailwind CSS in the TMS UI and the Partner portal. Tests use xUnit, NSubstitute, bUnit, Testcontainers and Playwright, with an 80% line coverage gate on unit tests. The Azure side is API Management, Logic Apps, Service Bus, Key Vault, Entra ID, Application Insights and Container Apps. See [docs/coding-guidelines.md](docs/coding-guidelines.md) for the full rules.

## Running it locally

Needs the .NET 10 SDK, Docker, and Node.js 20 or later (the TMS UI's and the Partner portal's stylesheets are built with Tailwind CSS, [ADR 0018](docs/adr/0018-the-tms-ui-is-styled-with-tailwind-in-flatlys-look.md), [ADR 0022](docs/adr/0022-the-partner-portal-is-styled-with-tailwind-in-luxs-look.md)).

```bash
dotnet run --project src/Norvia.AppHost      # TMS API and UI, Partner portal, the local Partner API, migrations, SQL Server and the Service Bus emulator, with the Aspire dashboard
dotnet test Norvia.slnx                      # unit, architecture, integration and UI tests (the last two start the AppHost)
```

| Where | What |
|---|---|
| [src/](src/) | The applications: the Aspire AppHost and ServiceDefaults, and the TMS in Clean Architecture layers, with its Blazor UI in `Norvia.Tms.Web` |
| [tests/](tests/) | Tests per project, mirroring `src/` |
| [contracts/canonical-model/](contracts/canonical-model/) | The Canonical Model as JSON Schemas |
| [contracts/partner-api/](contracts/partner-api/README.md) | The Norvia Partner API's booking format and responses, with sample bookings and the Canonical Model Transport Order each maps to |
| [src/Integration/](src/Integration/) | What the integration layer runs: the intake Logic App ([IntakeLogicApp](src/Integration/IntakeLogicApp/)), and the intake map and relay it calls as .NET local functions ([ADR 0019](docs/adr/0019-the-intake-map-is-dotnet-code-run-as-a-logic-apps-local-function.md)) |
| [seed/norvia.seed.json](seed/norvia.seed.json) | The seed file: the one source of Shippers, Carriers and Personas |
| [infra/](infra/) | The estate Bicep and the azd hooks that deploy it ([ADR 0004](docs/adr/0004-bicep-owns-the-estate-aspire-owns-the-apps.md)) |

There is no Entra locally. The TMS trusts JWTs signed with the AppHost's secret parameter `local-token-signing-key` (issuer and audience in [LocalTokens.cs](src/Norvia.AppHost/LocalTokens.cs)), so submitting a Transport Order needs a token signed with that key, with the `TransportOrders.Intake` role and the local intake Logic App's client ID as `azp`. The integration tests issue their own.

Set `NORVIA_TEST_LOG_FILE` to a path to keep the TMS's own logs from an integration test run.

The TMS UI (`tms-web` on the Aspire dashboard) reads the TMS database through the TMS query side; it takes in nothing and publishes nothing. Its lists of Transport Orders waiting for planning and of rejected bookings read again every second (`LiveList:RefreshInterval`), so a new one appears without a refresh. The Role switcher in the navbar remembers the chosen role in a cookie, and `/` opens that role's view: the Planner's by default. The UI tests drive it with Playwright in headless Chromium, which their fixture installs on the first run. They keep a trace and a video of each failed test under `TestResults/playwright`, or under `$NORVIA_UI_TEST_ARTIFACTS/playwright` when that is set.

The Partner portal (`partner-portal-web` on the Aspire dashboard) plays every Shipper and Carrier as a Persona; the Persona switcher in its navbar chooses one, and `/` opens the booking form of the one last chosen. It calls the Norvia Partner API only. Locally that is `partner-api`, a stand-in for API Management and the intake Logic App that checks the same subscription key, token and role, and then maps the booking and calls the TMS with the Logic App's own code ([ADR 0023](docs/adr/0023-locally-a-stand-in-plays-the-norvia-partner-api.md)). The AppHost gives each Persona in the seed file a client ID and a subscription key for the run, and the portal signs each Persona's token with `local-token-signing-key`, as Entra would, with the submit role for Shipper Personas only. The portal keeps each Persona's bookings and Norvia's answers in its own database, as the outside party's records.

The TMS database's migrations are a deployment step, not something the TMS does when it starts ([coding guidelines §5](docs/coding-guidelines.md#5-persistence-ef-core-with-ardalis-repositories-and-specifications)). `Norvia.Tms.Migrations` applies them and exits; the TMS API and UI wait for it.

CI ([.github/workflows/ci.yml](.github/workflows/ci.yml)) runs the pull request gates of the coding guidelines on every PR and on `main`: a warning-free build, `dotnet format`, the vulnerable-package check, unit and architecture tests with the coverage gate, and the integration tests.

## Deploying to Azure

Needs the [Azure Developer CLI](https://aka.ms/azd) and the Azure CLI, both signed in to the same tenant as a user who may create resources in the subscription and app registrations in Entra ID, plus `jq` and `zip`.

```bash
azd env new <environment> --subscription <subscription-id> --location <region>
AZD_UP_CONCURRENCY=1 azd up                  # one image at a time (see below)
infra/smoke-test-intake.sh                   # after a deployment: checks intake end to end on the deployed TMS
infra/smoke-test-partner-api.sh              # and the demo smoke run: books through API Management as each Persona
```

azd asks for the AppHost's parameters, which are the estate's outputs, before it runs the preprovision hook. So `azd up` also runs the estate hook as a preup hook, before anything else, and the preprovision run then sees the estate is already deployed. The hook needs the environment's subscription and region, so create the environment with both, as above. If azd still asks for an `estate_*` parameter, don't type in a value: they are the names and IDs the estate creates. Stop, run `azd hooks run preprovision`, and start `azd up` again.

azd (1.33 and 1.34 at least) builds the AppHost's container images in parallel, and the builds share the .NET SDK's local cache of base-image layers. Two of them can then collide on the same cached layer (`CONTAINER1013 … being used by another process`), and azd cancels the rest. `AZD_UP_CONCURRENCY=1`, or `AZD_DEPLOY_CONCURRENCY=1` for `azd deploy`, builds one image at a time. azd reads both from the shell, not from the azd environment.

`azd up` runs in three steps ([ADR 0004](docs/adr/0004-bicep-owns-the-estate-aspire-owns-the-apps.md)):

1. The estate hook ([infra/provision-estate.sh](infra/provision-estate.sh)), run before `azd up` and before `azd provision`, deploys the estate, [infra/estate.bicep](infra/estate.bicep), into `rg-<environment>`. That covers the Service Bus namespace with the `transport-order-events` topic, Log Analytics and Application Insights, the TMS API's app registration with its intake role, the intake Logic App: a Logic Apps Standard app on a Workflow Standard plan, with its storage account and a Key Vault, and API Management with the Norvia Partner API ([infra/estate/partner-api.bicep](infra/estate/partner-api.bicep)). It also registers the demo UIs, the TMS UI and the Partner portal, in Entra, with only the presenter assigned. The Logic App calls the TMS as the intake Logic App's managed identity, the one that holds that role, and reaches its own storage and Key Vault with a second identity that holds nothing else. Its trigger accepts only API Management's identity (Easy Auth). In Entra the estate registers the Norvia Partner API with its `TransportOrders.Submit` role, and one client app per Persona in the seed file, each trusting the Partner portal's managed identity instead of holding a secret; Shipper Personas get the role. Each Persona's subscription key is kept in a Key Vault that only the Partner portal's identity may read. API Management is on the Developer tier, whose first deployment takes about 45 minutes. The hook stores the estate's outputs in the azd environment as `AZURE_ESTATE_*`.
2. azd provisions and deploys the apps from the AppHost into the same resource group: the Container Apps environment, the TMS API (reachable from outside the environment), the TMS UI and the Partner portal (both reachable from the presenter's browser, behind an Entra sign-in; see below), the migrations as Container Apps jobs, and Azure SQL serverless on the free offer, with a database each for the TMS and the Partner portal. The Partner portal runs as the estate's Partner portal identity, gets each Persona's token from Entra through the Persona's federated credential, and books through API Management. In Azure the AppHost references the estate's Service Bus, workspace and Application Insights instead of creating its own. The TMS trusts only Entra tokens for its app registration, and only the intake Logic App's identity may submit. Its queries also need such a token until API Management's Shipper tracking role arrives (P8); Entra issues one only to a principal holding one of the TMS's roles.
3. The postdeploy hook runs the TMS's and the Partner portal's migrations jobs ([infra/migrate.sh](infra/migrate.sh)) and fails the deployment if either fails. In Azure nothing makes the TMS wait for it, so on a first deployment the TMS logs failed outbox cycles until the schema exists. It then deploys the intake Logic App's workflow and local functions ([infra/deploy-intake-logic-app.sh](infra/deploy-intake-logic-app.sh)) and gives it the TMS's address, which exists only once Aspire has deployed the TMS. The address is kept in the azd environment as `ESTATE_TMS_BASE_URL`, so deploying the estate again keeps it. Last, it gives the demo UIs' app registration their sign-in callbacks ([infra/open-demo-uis.sh](infra/open-demo-uis.sh)) and prints the address of each UI.

The presenter, the user who ran `azd up`, opens the TMS UI and the Partner portal side by side from their own browser at the addresses the postdeploy hook prints, or at their Container Apps URLs in the portal (`tms-web` and `partner-portal-web`). Container Apps' built-in authentication asks them to sign in with Entra once per browser session; after that the Role switcher and the Persona switcher work as they do locally. Entra admits only users assigned to the estate's *Norvia demo UIs* app registration, which is the presenter alone; assign anyone else who presents the `DemoUis.Use` role in Entra ID. The gate holds no client secret ([ADR 0024](docs/adr/0024-the-demo-uis-are-public-behind-an-entra-gate-for-the-presenter.md)). Locally there is no sign-in.

The demo smoke run ([infra/smoke-test-partner-api.sh](infra/smoke-test-partner-api.sh)) is what a presenter repeats after a deployment. It books through API Management as Brouwer Dranken and as Van Dijk Transport, tries a missing or wrong key and token, and follows the accepted and the refused bookings by correlation ID to the Logic App run, the Transport Order, the event and Application Insights. A Persona's token can be had only as the Partner portal's identity, so the Personas' calls run from a throwaway Container Apps job as it. It prints `PASS` or `FAIL` per check.

The intake smoke test runs the intake identity's calls from a throwaway Container Apps job, and sees the event through a Service Bus subscription of its own that it removes afterwards. It needs the rights of whoever ran `azd up`: the estate grants that user Azure Service Bus Data Receiver on the namespace.

The applications hold no secrets. The TMS reaches SQL and Service Bus with its managed identity, and Service Bus accepts no keys. The intake Logic App reaches its storage and the TMS with managed identities too. Only its content share, which Logic Apps Standard still reaches with a storage key, needs one: the estate puts that connection string in Key Vault, and the app reads it through a Key Vault reference. The Application Insights connection string only says where telemetry goes. Retention of Application Insights, Log Analytics and the Logic App's run history is the estate parameter `integrationRetentionDays`, 90 days by default (run history keeps 365 days at most). To change it, set it in [infra/estate.bicepparam](infra/estate.bicepparam).

`azd down --purge` removes the resource group. The app registrations are not in it; delete the ones ending in *(&lt;environment&gt;)* in Entra ID yourself: *Norvia TMS API*, *Norvia Partner API*, *Norvia intake Logic App trigger*, *Norvia demo UIs* and each *Norvia Persona*.

## Contributing

- Issues and specs live in GitHub Issues for `congruent-it/norvia-sandbox`, triaged with the labels in [docs/agents/triage-labels.md](docs/agents/triage-labels.md).
- Use the terms in [CONTEXT.md](CONTEXT.md). If a term is missing, add it there before you use it in code.
- A decision that is hard to reverse, surprising, or the result of a real trade-off gets an ADR in [docs/adr/](docs/adr/).
- Every change goes through a pull request with at least one review, and keeps to one issue.

Guidance for coding agents is in [CLAUDE.md](CLAUDE.md) and [docs/agents/](docs/agents/).
