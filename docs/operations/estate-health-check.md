# Estate health check

How an Integration operator runs an **Estate health check** and reads what it found. The Estate health-check agent (#371) works end to end **locally**, with one **Check**, from a snapshot of the estate's configuration, and needs no Azure; **in Azure** (#392) the same app reads the real estate and its Agent is published in Foundry. Terms are defined in [GLOSSARY.md](../../GLOSSARY.md).

## What it is

The Estate health check is a small app, `src/HealthCheck` (Application, Infrastructure, Web; no database). It runs a checklist over the estate's configuration and lists a **Finding** for each **Check** that fails. The Estate health-check agent then narrates the Findings: it puts them in order and explains in plain language what each one means for the business. It changes nothing, stores nothing (a result lives in the page session) and never invents a Finding.

| Part | Is |
|---|---|
| The checklist engine | `Checklist` in `Norvia.HealthCheck.Application`. Runs every Check over the estate's facts and makes a Finding for each that finds something, most severe first. Evidence is masked on the way out |
| A Check | An ID, a rule, a title, a severity, the as-found toggle that breaks it and the fix. Deterministic code evaluates it. **Severity and fix are the Check's, never the model's** |
| `IEstateConfiguration` | The port to the estate's configuration. A Check reads only the facts it gives, never a resource |
| The snapshot adapter | The local adapter: reads a JSON snapshot embedded from the repo |
| The agent API | `GET /agent/v1/checks`, the Agent's one tool, `run_checks`. Behind a token and the role `HealthCheck.Agent.Read` |
| The Agent | [agents/estate-health-check.agent.yaml](../../agents/estate-health-check.agent.yaml), with the tool list [health-check.tools.json](../../contracts/agent-mcp/health-check.tools.json) |
| The page | `/`: **Run health check**, the Findings list and the Agent's narration |

## The checklist

| Check | Severity | Rule | Maps to the toggle | Fix |
|---|---|---|---|---|
| `DLQ-ALERT` | High | Every Service Bus queue and subscription with a dead-letter queue has an alert on it | `entitiesWithoutDeadLetterAlert` ([as-found](as-found.md#toggles)) | Remove the entity from `asFound.entitiesWithoutDeadLetterAlert` and provision the estate: [alerts.bicep](../../infra/estate/alerts.bicep) then builds `alert-dead-letter-<entity>` |

A Finding's ID is `F-` and its Check's ID (`F-DLQ-ALERT`). A Check gives one Finding, whose evidence names every resource that breaks the rule, in name order. The full checklist, one Check per as-found toggle, is #393.

## Running it locally

Run the AppHost. The app is `health-check-web` on the Aspire dashboard (**Estate health check**), and the local operator holds `HealthCheck.Run` and `HealthCheck.Read`.

- **The snapshot.** The estate as found by default. Set `HealthCheck:Snapshot` to `fixed` in the AppHost's user secrets (or its launch profile) to read the fixed estate. With the as-found snapshot, **Run health check** shows one Finding, `F-DLQ-ALERT`, naming the seven queues and subscriptions that have no dead-letter alert, `carrier-tracking` among them. With the fixed snapshot it shows no Finding and says all the Checks passed.
- **The narration.** The Agents are off unless `Agents:Enabled` is true in the AppHost's user secrets, as for the Integration console ([Explain and the agent API](integration-console.md#explain-and-the-agent-api)). Then the app runs the Agent in process (`Norvia.Agents`) against the MCP stand-in, `Norvia.Integration.LocalAgentMcp`, which serves this Agent's server at `/mcp/health-check` and calls the app's agent API as the Agent's identity. The model is a scripted fake: it calls `run_checks`, orders the Findings by severity, and explains each from the Finding's own fields, linking `F-DLQ-ALERT` to the Carrier-tracking incident when its evidence names `carrier-tracking`. Set `Agents:Model:Endpoint` (and `Agents:Model:Deployment`) to use your own Foundry deployment instead.
- **On any failure of the Agent** the page says "Assistant unavailable", logs the failure, and the Findings stay. The run has a short timeout (20 seconds) and is never retried ([failure behaviour](../architecture/failure-behaviour.md#agents)).

## Local Findings come from a snapshot

Locally nothing reads a running estate: there are no alerts to look at under Aspire. The Findings are the snapshot's.

- [fixed.snapshot.json](../../src/HealthCheck/Norvia.HealthCheck.Infrastructure/Snapshots/fixed.snapshot.json) is the fixed estate: every queue and subscription a Logic App reads, each with a dead-letter alert.
- [as-found.snapshot.json](../../src/HealthCheck/Norvia.HealthCheck.Infrastructure/Snapshots/as-found.snapshot.json) is the same estate as the as-found preset leaves it ([infra/presets/as-found.json](../../infra/presets/as-found.json)).
- Tests keep them true: each snapshot gives the Findings it must (none, and the one), lists as missing exactly the entities its preset lists in `entitiesWithoutDeadLetterAlert`, and holds exactly the entities [estate.bicep](../../infra/estate.bicep) alerts. A preset that changes without its snapshot fails the build.
- In Azure an adapter over Azure Resource Graph and Azure Monitor produces the same shape ([In Azure](#in-azure)). The snapshot adapter stays for local runs.

## In Azure

The same app, `health-check-web`, deployed as a Container App by the AppHost (`azd up`). Its Azure parts are the estate's and the app's, as for the Integration console:

| Part | Is |
|---|---|
| The app's identity | A user-assigned managed identity, `id-health-check-<token>`, made by [health-check.bicep](../../infra/estate/health-check.bicep). It holds **Reader on the estate's resource group** and no other Azure role, and **Foundry User** on the `norvia` project to ask its Agent. The privileged role belongs to the app, never to the Agent (#371, question 1) |
| The operators' sign-in | The app registration `Norvia Estate health check (<env>)`, with the roles `HealthCheck.Run` and `HealthCheck.Read`, for users only. Easy Auth sits in front of everything but the health probes and `/agent/v1/*`. [register-sign-in-callbacks.sh](../../infra/register-sign-in-callbacks.sh) adds its callback after the deploy |
| The estate adapter | `AzureEstateConfiguration`. **Azure Resource Graph** answers which Azure Monitor metric alert rules exist and what each watches (`microsoft.insights/metricalerts` of the resource group); the namespace's **queues and subscriptions** come from Service Bus's management plane, because Resource Graph does not hold them. A queue or subscription has a dead-letter alert when an enabled rule scoped to the namespace watches `DeadletteredMessages` for it by an `EntityName` dimension that includes it (or by none, which watches them all). The subscriptions [alert-exemptions.json](../../infra/estate/alert-exemptions.json) lists are left out, as [alerts.bicep](../../infra/estate/alerts.bicep) leaves them out. It only reads. Set by `Estate:ResourceGroupId` and `Estate:ServiceBusNamespace`; where they are unset the snapshot is read |
| The agent API's token | Entra's, for the app registration `Norvia Estate health check agent API (<env>)` (audience), which holds one role, `HealthCheck.Agent.Read`, assigned to API Management's identity alone. The app takes it on `/agent/v1` and nothing else. API Management's token names the gateway, so the Agent's identity is read from the `X-Agent-ID` header the policy sets |
| The MCP server | `agents/health-check` in API Management, made by [health-check-mcp.bicep](../../infra/estate/health-check-mcp.bicep) from [health-check.tools.json](../../contracts/agent-mcp/health-check.tools.json) with [agent-mcp-server.bicep](../../infra/estate/agent-mcp-server.bicep): the Agent's token (audience `Norvia health-check agent view (<env>)`, role `HealthCheck.Agent.Read`), the tool list, 10 calls a minute per identity, and the correlation ID, the Agent's identity and the tool name on each call. Its backend is `health-check-agent-api`; [deploy-agents.sh](../../infra/deploy-agents.sh) points it at the app and stores `ESTATE_HEALTH_CHECK_BASE_URL` |
| The Agent | Published into the `norvia` project from [the definition](../../agents/estate-health-check.agent.yaml) by [deploy-agents.sh](../../infra/deploy-agents.sh), with its own identity, which [assign-agent-roles.sh](../../infra/assign-agent-roles.sh) gives `HealthCheck.Agent.Read` on the agent view. It holds no Azure role. The app asks it through the Responses API as its own identity (`FoundryAgentClient`), with `store: false`, no conversation, the run's response deleted afterwards (best effort) and the correlation ID as a structured input that becomes the tool call's `X-Correlation-ID` |

Turn the Agents on with `ESTATE_AGENTS_ENABLED_SETTING=true` (`azd env set`) and `azd up`; they are off by default, and the page then says "Assistant unavailable" and still lists the Findings.

**The narration is checked against the tool's own answer.** Foundry returns the output of each tool call, and the client refuses a narration that explains a Finding that output does not hold, so the Agent cannot invent one in Azure either. If Foundry hands back no output the narration is refused (`AgentUnavailable`) and the Findings stay.

### Smoke run

[infra/smoke-test-estate-health-check.sh](../../infra/smoke-test-estate-health-check.sh), after `azd up` with the Agents on, signed in to the Azure CLI as the presenter. It checks that the Agent holds only `HealthCheck.Agent.Read` and no Azure role, that the app's agent API has that one role held by API Management alone, that the app's identity holds Reader on the resource group and no other role there, and that the project's shared identity holds none; then applies the **as-found preset** (the dead-letter alerts of the seven entities are deleted) and asks the Agent until the checks return `F-DLQ-ALERT`, naming `carrier-tracking`, and the real model explains it citing `r1`; then applies the **fixed preset**, which deploys the estate again and rebuilds the alerts, and asks until the checks return no Finding and the narration invents none; and finally that the Agent's tool calls are hops of the run's Message trail (`agent-mcp-health-check`, `agent-view-health-check`) tagged with the Agent's identity and `run_checks`. Resource Graph takes a short while to see an alert come and go, so each check asks again for up to ten minutes. The estate ends in the fixed state. The script prints names, IDs and counts only.

## What the Agent can and cannot do

- It reads through one tool, `run_checks`, a GET on the app's agent API. No tool writes, and none returns a secret value.
- Its identity holds one role, `HealthCheck.Agent.Read`, and **no Azure role**. Reading Azure is the app's own identity's job, never the Agent's.
- It cites a Finding by its ID, and the runner refuses a narration that explains a Finding the tool did not return, cites a result the run never had, or is not the expected object: `AgentUnavailable`. It cannot change a severity or a fix, because the page shows those from the Finding.
- Everything a tool returns is data. The instructions say so, the runner delimits each result, and the page shows the narration and the evidence as text, never as markup.

## Evidence is masked

A Finding's evidence names the resources concerned. A secret is reported by where it is (a setting's name, a resource), never by its value: `EvidenceMasker` hides whatever follows a secret-looking name (`password`, `secret`, `key`, `token`, `credential`, `connectionstring`) and any bearer token, in every Finding the engine makes. The agent view is an allowlist of fields with no free-form value.

## Roles

| Role | Holder | Opens |
|---|---|---|
| `HealthCheck.Run` | The Integration operator | The page: running a health check and seeing what it found |
| `HealthCheck.Read` | The Integration operator | Nothing yet. Reserved for the report (#394), which shows a result to someone who may not run one |
| `HealthCheck.Agent.Read` | The Estate health-check agent's identity alone | `/agent/v1/checks`, and nothing else in the app. No operator holds it |

Locally the AppHost signs in a local operator holding both operator roles, and refuses to do it outside Development. In Azure Container Apps' Easy Auth signs the operator in to the app's own app registration, `Norvia Estate health check (<env>)`, and the app reads their app roles from it ([ADR 0026](../adr/0026-the-integration-console-signs-operators-in-with-easy-auth-and-checks-their-app-role.md), as for the Integration console); Entra signs in only the users assigned a role, and the presenter holds both. See [identity and access](../architecture/identity-and-access.md#agents).

## Tests

- **Unit tests** (80% line coverage): the engine and the Check against both snapshots, the masking, the use cases, the role checks, the agent API (token, role, audience, GET only, the tracking hop), the page (bUnit), the local agent client against a fake MCP server, and the stand-in's second server.
- **Architecture tests**: dependencies point inwards, no write tool, one role for the Agent, and only Infrastructure references the Foundry SDK.
- **Integration tests**: #398 (locally) and #400 (in Azure: [AzureHealthCheckTests](../../tests/HealthCheck/Norvia.HealthCheck.Agents.IntegrationTests/AzureHealthCheckTests.cs), the app built as in Azure over faked Resource Graph, Service Bus and Foundry endpoints, the Finding coming and going as the alert does). **UI tests**: #399.
- **Policy and drift tests** ([HealthCheckAgentAzureTests](../../tests/Tms/Norvia.Tms.ArchitectureTests/HealthCheckAgentAzureTests.cs), `bicep snapshot`, nothing deployed): the MCP server has the contract's tools and no others, GET only; the policy gets the contract's role, the agent view as audience and a rate limit; both app registrations hold the one role and Entra issues no token without it; only API Management's identity is given a role in the Bicep; the app's identity holds Reader on the resource group and nothing else, and nothing on the Agent's side holds an Azure role. The stand-in's unit tests serve the same contract file, so the stand-in's tool list is the Bicep's.
- **Unit tests of the Azure adapter** ([AzureEstateConfigurationTests](../../tests/HealthCheck/Norvia.HealthCheck.Infrastructure.UnitTests/Estate/AzureEstateConfigurationTests.cs)): faked Resource Graph and Service Bus answers mapped to the snapshots' shape, and the Foundry client against a faked Responses endpoint.
- **Smoke run** (not in CI): [infra/smoke-test-estate-health-check.sh](../../infra/smoke-test-estate-health-check.sh), below.
