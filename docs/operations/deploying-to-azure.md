# Deploying to Azure

How to deploy the estate and the applications with `azd up`, check the deployment and tear it down. What differs from a local run is in [architecture/local-vs-azure.md](../architecture/local-vs-azure.md); to run locally, see [running-locally.md](running-locally.md).

## Prerequisites

You need the [Azure Developer CLI](https://aka.ms/azd) and the Azure CLI, both signed in to the same tenant as a user who may create resources in the subscription and app registrations in Entra ID, plus [PowerShell 7.4 or later](https://aka.ms/powershell) (`pwsh`) for the scripts that have been converted ([ADR 0048](../adr/0048-scripts-are-powershell-7-not-bash.md)), and `jq` and `zip` for those that have not, and [`yq`](https://github.com/mikefarah/yq) to publish the Agents.

## Deploy

```bash
azd env new <environment> --subscription <subscription-id> --location <region>
AZD_UP_CONCURRENCY=1 azd up                  # one image at a time (see below)
infra/smoke-test-intake.ps1                  # after a deployment: checks intake end to end on the deployed TMS
infra/smoke-test-partner-api.ps1             # and the demo smoke run: books through API Management as each Persona
```

azd asks for the AppHost's parameters, which are the estate's outputs, before it runs the preprovision hook. So `azd up` also runs the estate hook as a preup hook, before anything else, and the preprovision run then sees the estate is already deployed. The hook needs the environment's subscription and region, so create the environment with both, as above. If azd still asks for an `estate_*` parameter, don't type in a value: they are the names and IDs the estate creates. Stop, run `azd hooks run preprovision`, and start `azd up` again.

azd (1.33 and 1.34 at least) builds the AppHost's container images in parallel, and the builds share the .NET SDK's local cache of base-image layers. Two of them can then collide on the same cached layer (`CONTAINER1013 … being used by another process`), and azd cancels the rest. `AZD_UP_CONCURRENCY=1`, or `AZD_DEPLOY_CONCURRENCY=1` for `azd deploy`, builds one image at a time. azd reads both from the shell, not from the azd environment.

## The three steps

`azd up` runs in three steps ([ADR 0004](../adr/0004-bicep-owns-the-estate-aspire-owns-the-apps.md)):

1. The estate hook ([infra/provision-estate.ps1](../../infra/provision-estate.ps1)), run before `azd up` and before `azd provision`, deploys the estate, [infra/estate.bicep](../../infra/estate.bicep), into `rg-<environment>`. That covers the Service Bus namespace with the `transport-order-events` topic, Log Analytics and Application Insights, the TMS API's app registration with its intake role, the intake Logic App: a Logic Apps Standard app on a Workflow Standard plan, with its storage account and a Key Vault, and API Management with the Norvia Partner API ([infra/estate/partner-api.bicep](../../infra/estate/partner-api.bicep)). It also registers the demo UIs, the TMS UI and the Partner portal, in Entra, with only the presenter assigned, and the Integration console with its app roles `IntegrationConsole.Read` and `IntegrationConsole.Resubmit`, both assigned to the presenter, and its managed identity. The Logic App calls the TMS as the intake Logic App's managed identity, the one that holds that role, and reaches its own storage and Key Vault with a second identity that holds nothing else. Its trigger accepts only API Management's identity (Easy Auth). In Entra the estate registers the Norvia Partner API with its `TransportOrders.Submit` role, and one client app per Persona in the seed file, each trusting the Partner portal's managed identity instead of holding a secret; Shipper Personas get the role. Each Persona's subscription key is kept in a Key Vault that only the Partner portal's identity may read. API Management is on the Developer tier, whose first deployment takes about 45 minutes. The hook stores the estate's outputs in the azd environment as `AZURE_ESTATE_*`.
   
2. azd provisions and deploys the apps from the AppHost into the same resource group: the Container Apps environment, the TMS API (reachable from outside the environment), the TMS UI and the Partner portal (both reachable from the presenter's browser, behind an Entra sign-in; see [Opening the UIs](#opening-the-uis)), the Integration console (reachable from the operators' browsers, behind its own Entra sign-in and running as the estate's console identity), the migrations as Container Apps jobs, and Azure SQL serverless on the free offer, with a database each for the TMS and the Partner portal. The Partner portal runs as the estate's Partner portal identity, gets each Persona's token from Entra through the Persona's federated credential, and books through API Management. In Azure the AppHost references the estate's Service Bus, workspace and Application Insights instead of creating its own. The TMS trusts only Entra tokens for its app registration, and only the intake Logic App's identity may submit. Its queries also need such a token until API Management's Shipper tracking role arrives (P8); Entra issues one only to a principal holding one of the TMS's roles.
   
3. The postdeploy hook runs the TMS's and the Partner portal's migrations jobs ([infra/migrate.ps1](../../infra/migrate.ps1)) and fails the deployment if either fails. In Azure nothing makes the TMS wait for it, so on a first deployment the TMS logs failed outbox cycles until the schema exists. It then deploys the intake Logic App's workflow and local functions ([infra/deploy-intake-logic-app.ps1](../../infra/deploy-intake-logic-app.ps1)) and gives it the TMS's address, which exists only once Aspire has deployed the TMS. The address is kept in the azd environment as `ESTATE_TMS_BASE_URL`, so deploying the estate again keeps it. Last, it gives the demo UIs' and the Integration console's app registrations their sign-in callbacks ([infra/register-sign-in-callbacks.ps1](../../infra/register-sign-in-callbacks.ps1)) and prints the address of each UI.

## Opening the UIs

The presenter, the user who ran `azd up`, opens the TMS UI and the Partner portal side by side from their own browser at the addresses the postdeploy hook prints, or at their Container Apps URLs in the portal (`tms-web` and `partner-portal-web`). Container Apps' built-in authentication asks them to sign in with Entra once per browser session; after that the Role switcher and the Persona switcher work as they do locally. Entra admits only users assigned to the estate's *Norvia demo UIs* app registration, which is the presenter alone; assign anyone else who presents the `DemoUis.Use` role in Entra ID. The gate holds no client secret ([ADR 0024](../adr/0024-the-demo-uis-are-public-behind-an-entra-gate-for-the-presenter.md)). Locally there is no sign-in.

The presenter also opens the Integration console (`integration-console-web`), where they play the Integration operator. Easy Auth signs them in to the console's own app registration, *Norvia Integration console*, and the console checks that they hold `IntegrationConsole.Read`; anyone without it is refused ([ADR 0026](../adr/0026-the-integration-console-signs-operators-in-with-easy-auth-and-checks-their-app-role.md)). The console reads the estate as its own managed identity, never as the operator.

## Smoke tests

The demo smoke run ([infra/smoke-test-partner-api.ps1](../../infra/smoke-test-partner-api.ps1)) is what a presenter repeats after a deployment. It books through API Management as Brouwer Dranken and as Van Dijk Transport, tries a missing or wrong key and token, and follows the accepted and the refused bookings by correlation ID to the Logic App run, the Transport Order, the event and Application Insights. A Persona's token can be had only as the Partner portal's identity, so the Personas' calls run from a throwaway Container Apps job as it. It prints `PASS` or `FAIL` per check.

The intake smoke test runs the intake identity's calls from a throwaway Container Apps job, and sees the event through a Service Bus subscription of its own that it removes afterwards. It needs the rights of whoever ran `azd up`: the estate grants that user Azure Service Bus Data Receiver on the namespace.

## Secrets and identities

The applications hold no secrets. The TMS reaches SQL and Service Bus with its managed identity, and Service Bus accepts no keys. The intake Logic App reaches its storage and the TMS with managed identities too. Only its content share, which Logic Apps Standard still reaches with a storage key, needs one: the estate puts that connection string in Key Vault, and the app reads it through a Key Vault reference. The Application Insights connection string only says where telemetry goes. Retention of Application Insights, Log Analytics and the Logic App's run history is the estate parameter `integrationRetentionDays`, 90 days by default (run history keeps 365 days at most). To change it, set it in [infra/estate.bicepparam](../../infra/estate.bicepparam).

## Tear down

`azd down --purge` removes the resource group. The app registrations are not in it; delete the ones ending in *(<environment>)* in Entra ID yourself: *Norvia TMS API*, *Norvia Partner API*, *Norvia intake Logic App trigger*, *Norvia demo UIs*, *Norvia Integration console* and each *Norvia Persona*.
