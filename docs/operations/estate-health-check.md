# Estate health check

How an Integration operator runs an **Estate health check** and reads what it found. The Estate health-check agent (#371) works end to end **locally**, with the full checklist, from a snapshot of the estate's configuration, and needs no Azure; **in Azure** (#392) the same app reads the real estate and its Agent is published in Foundry. Terms are defined in [GLOSSARY.md](../../GLOSSARY.md).

## What it is

The Estate health check is a small app, `src/HealthCheck` (Application, Infrastructure, Web; no database). It runs a checklist over the estate's configuration and lists a **Finding** for each **Check** that fails. The Estate health-check agent then narrates the Findings: it puts them in order and explains in plain language what each one means for the business. It changes nothing, stores nothing (a result lives in the page session) and never invents a Finding.

| Part | Is |
|---|---|
| The checklist engine | `Checklist` in `Norvia.HealthCheck.Application`. Runs every Check over the estate's facts and makes a Finding for each that finds something, most severe first. Evidence is masked on the way out |
| A Check | An ID, a rule, a title, a severity, the as-found toggle that breaks it and the fix. Deterministic code evaluates it. **Severity and fix are the Check's, never the model's** |
| `IEstateConfiguration` | The port to the estate's configuration. A Check reads only the facts it gives, never a resource |
| The snapshot adapter | The local adapter: reads a JSON snapshot embedded from the repo |
| The agent API | `GET /agent/v1/checks` (`run_checks`), `/agent/v1/failed-runs` (`list_failed_runs`) and `/agent/v1/dead-letter-queues` (`list_dead_letter_queues`), the Agent's tools. The two lists show what failed and whether an alert watches it, and carry no message body. `run_checks` answers with the report the page (or an earlier `run_checks`) made less than a minute ago instead of reading the estate again, which takes about 20 s; the page's own run always reads afresh. Behind a token and the role `HealthCheck.Agent.Read` |
| The Agent | [agents/estate-health-check.agent.yaml](../../agents/estate-health-check.agent.yaml), with the tool list [health-check.tools.json](../../contracts/agent-mcp/health-check.tools.json) |
| The page | `/`: **Run health check**, the steps of the run as they start (in Azure, one for each thing it reads from the estate, then the Checks; a tick for each step that is done), the report (Findings grouped by severity, the Agent's narration, the comparison with the previous run, **Export as Markdown**). With the assistant on, **Run ID** under the narration is the run's correlation ID, with a link to the Integration console's Message trail for it (`IntegrationConsole:Address`): the narration, the assistant's calls through API Management and the app's agent view are on it |

## The checklist

One Check for each as-found toggle ([as-found](as-found.md)): a Check finds the toggle's effect and no other, so every toggle has a Check that finds it and a known answer. A Check reads only the facts of `EstateConfiguration`, and its severity, rule and fix are its own.

The two `...-UNTOLD` Checks are the exception to one Check per toggle: they pair a failure with a missing alert, so a failure that nobody was told about is a Finding of its own, deterministic in the API, with the failures as evidence ("the carrier-tracking Logic App failed 14 times in the last 4 days and has no failed-run alert", "the dead-letter queue of carrier-tracking holds 3 messages and no alert watches it"). They share the toggle of the alert Check whose absence they pair with, and give no Finding for a failure that has an alert, for a flow or queue that never failed, or for a count the adapter could not read. The agent narrates them; it never decides them.

| Check | Severity | Rule | Maps to the toggle | Evidence names |
|---|---|---|---|---|
| <a id="check-failed-run-alert"></a>`FAILED-RUN-ALERT` | High | Every Logic App has an alert on its failed runs | `flowsWithoutFailedRunAlert` | The flows without one |
| <a id="check-dlq-alert"></a>`DLQ-ALERT` | High | Every Service Bus queue and subscription with a dead-letter queue has an alert on it | `entitiesWithoutDeadLetterAlert` | The entities without one |
| <a id="check-failed-runs-untold"></a>`FAILED-RUNS-UNTOLD` | Critical | A Logic App that failed has an alert on its failed runs, so somebody is told | `flowsWithoutFailedRunAlert` (shared with `FAILED-RUN-ALERT`) | The flows that failed in the last four days with no alert, and how many runs failed |
| <a id="check-dlq-untold"></a>`DLQ-UNTOLD` | Critical | A queue or subscription that holds dead-lettered messages has an alert on its dead-letter queue, so somebody is told | `entitiesWithoutDeadLetterAlert` (shared with `DLQ-ALERT`) | The entities that hold dead-lettered messages with no alert, and how many |
| <a id="check-alert-receiver"></a>`ALERT-RECEIVER` | High | The action group the alerts use has a receiver | `noAlertReceiver` | The action group |
| <a id="check-secret-inlined"></a>`SECRET-INLINED` | Critical | A Carrier endpoint secret is read from Key Vault, never kept in a Logic App | `carrierSecretInlined` | Where the secret sits (the Logic App and the setting's name), never its value |
| <a id="check-appinsights-retention"></a>`APPINSIGHTS-RETENTION` | Medium | Application Insights keeps what it traces at least as long as the run history is kept | `appInsightsRetentionShort` | Application Insights, with both periods |
| <a id="check-tracked-properties"></a>`TRACKED-PROPERTIES` | Medium | Every Logic App workflow records its tracked properties | `logicAppsWithoutTrackedProperties` | The flows without them |
| <a id="check-lifecycle-inbound"></a>`LIFECYCLE-INBOUND` | Medium | The inbound Proof of delivery container deletes a blob after the estate's retention | `noInboundLifecycleRule` | The container |
| <a id="check-lifecycle-transit"></a>`LIFECYCLE-TRANSIT` | Medium | The Driver app's transit container deletes a file after the estate's retention | `noTransitLifecycleRule` | The container |
| <a id="check-proof-hot-tier"></a>`PROOF-HOT-TIER` | Low | The TMS's Proof of delivery files move to the cool tier after 30 days | `proofFilesStayHot` | The container |
| <a id="check-malware-scanning"></a>`MALWARE-SCANNING` | High | Defender for Storage malware scanning is on for the inbound container | `noMalwareScanning` | The storage account |
| <a id="check-stale-thresholds"></a>`STALE-THRESHOLDS` | High | The TMS has the thresholds that flag a quiet Shipment Stale | `staleThresholdsUnset` | The TMS |
| <a id="check-proof-missing-threshold"></a>`PROOF-MISSING-THRESHOLD` | Medium | The TMS has the threshold after which a Delivered Shipment's Proof of delivery is flagged missing | `proofMissingThresholdUnset` | The TMS |
| <a id="check-late-delivery-check"></a>`LATE-DELIVERY-CHECK` | High | The TMS's time-based check opens a Late delivery Exception once the delivery window has passed | `lateDeliveryCheckOff` | The TMS |
| <a id="check-apim-cache"></a>`APIM-CACHE` | High | API Management does not cache the Shipper tracking answers for longer than five minutes | `shipperTrackingCached` | The Shipper tracking API, with the lifetime |
| <a id="check-apim-rate-limit"></a>`APIM-RATE-LIMIT` | Medium | The Shipper tracking operations have a rate limit | `noShipperRateLimit` | The Shipper tracking API |
| <a id="check-driver-report-back"></a>`DRIVER-REPORT-BACK` | Medium | The Driver reports Logic App tells the Driver app what became of each report | `noReportBackToDriverApp` | The Logic App |
| <a id="check-conversation-sweeper"></a>`CONVERSATION-SWEEPER` | Medium | The nightly conversation sweeper runs | `noConversationSweeper` | The Logic App |
| <a id="check-agent-content-filter"></a>`AGENT-CONTENT-FILTER` | High | The agents' model deployment has a content filter and a prompt shield | `noContentFilterOrPromptShield` | The model deployment |
| <a id="check-agent-role"></a>`AGENT-ROLE` | High | Every agent identity holds only a role that reads | `agentRoleWiderThanRead` | The agents whose role is wider, with the role |
| <a id="check-agent-cap"></a>`AGENT-CAP` | Medium | Every agent has a cap on tool calls per question and a request-rate limit in API Management | `noAgentCapOrRateLimit` | The agents without, with which guard is missing |
| <a id="check-agent-cost-tag"></a>`AGENT-COST-TAG` | Low | Everything an agent costs is tagged | `noAgentCostTag` | The agents without a cost tag |
| <a id="check-agents-public"></a>`AGENTS-PUBLIC` | High | `Agents:Enabled` is off in a Demo UI the public can reach | `agentsEnabledInPublicDemoUi` | The Demo UI |
| <a id="check-assistant-limits"></a>`ASSISTANT-LIMITS` | High | The Shipper tracking assistant is limited per Persona in rate, size and daily tokens (ADR 0049) | `assistantLimitsOff` | The assistant |
| <a id="check-assistant-output-check"></a>`ASSISTANT-OUTPUT-CHECK` | High | The Partner portal checks every answer of the tracking assistant against the data before the Shipper sees it | `assistantOutputCheckOff` | The assistant |
| <a id="check-assistant-access"></a>`ASSISTANT-ACCESS` | High | The tracking assistant lists no tool and its Agent identity holds no role | `assistantIdentityHoldsRoleOrTool` | The assistant, with each role and tool it holds |
| <a id="check-model-quota"></a>`MODEL-QUOTA` | Medium | The agents' model deployment has a quota that stops a runaway bill | `unlimitedModelQuota` | The model deployment |

- **A Finding's ID** is `F-` and its Check's ID (`F-DLQ-ALERT`). A Check gives one Finding, whose evidence names every resource that breaks the rule, in name order. Findings are listed most severe first, then by ID.
- **The fix** of a Finding is the toggle put back, named by the Bicep parameter or setting that does it ("Remove the entity from `asFound.entitiesWithoutDeadLetterAlert` and provision the estate"). It is the Check's, not the model's.
- **The toggles of the first seventeen rows** are built in the estate ([as-found](as-found.md#toggles)); the presets in [infra/presets](../../infra/presets) set them. A test checks that every key of the presets has exactly one Check.
- **The nine agent toggles** (the last nine rows, the three `ASSISTANT-` ones of [ADR 0049](../adr/0049-the-shipper-tracking-assistant-has-no-tools-and-only-restates-what-the-portal-fetched-with-the-personas-token.md) among them, the ones [ADR 0040](../adr/0040-agents-read-the-estate-through-api-management-have-no-write-tools-and-their-own-identity.md) and #369 name) are not in the estate's Bicep yet: the names above are the ones the Checks look for, and the Checks find them in a snapshot that sets the fact (unit tests do). When the estate builds the toggles, they take these names, or the Check's `AsFoundToggle` follows theirs.
- **A secret is never in a Finding.** `SECRET-INLINED` names the Logic App and the setting (`booking Logic App setting CARRIER_ENDPOINT_KEY`), and the value is not a fact the app reads at all ([Evidence is masked](#evidence-is-masked)).
- **What an adapter cannot read gives no Finding.** A fact is `null` when the adapter cannot see it, and a Check gives nothing for it, rather than treat unknown as healthy or as broken ([In Azure](#in-azure) says which facts those are).

## Running it locally

Run the AppHost. The app is `health-check-web` on the Aspire dashboard (**Estate health check**), and the local operator holds `HealthCheck.Run` and `HealthCheck.Read`.

- **The snapshot.** The estate as found by default. Set `HealthCheck:Snapshot` to `fixed`, `contrast`, `look-alike` or `silent-carrier` in the AppHost's user secrets (or its launch profile) to read another: the fixed estate or the preset of that name. With the as-found snapshot, **Run health check** shows a Finding for each toggle the preset turns on (thirteen), `F-DLQ-ALERT` among them, naming the seven queues and subscriptions that have no dead-letter alert, `carrier-tracking` among them, and the two that pair the tracking failures the snapshot holds (14 failed runs and 3 dead-lettered messages on `carrier-tracking`, the P5 unmappable-code fault) with their missing alerts: `F-FAILED-RUNS-UNTOLD` and `F-DLQ-UNTOLD`. The `silent-carrier` snapshot holds the same failures. The snapshots stand in for the failure facts, as for the rest. With the fixed snapshot it shows no Finding and says all the Checks passed.
- **The narration.** The Agents are off unless `Agents:Enabled` is true in the AppHost's user secrets, as for the Integration console ([Explain and the agent API](integration-console.md#explain-and-the-agent-api)). Then the app runs the Agent in process (`Norvia.Agents`) against the MCP stand-in, `Norvia.Integration.LocalAgentMcp`, which serves this Agent's server at `/mcp/health-check` and calls the app's agent API as the Agent's identity. The model is a scripted fake: it calls `run_checks`, orders the Findings by severity, and explains each from the Finding's own fields (its title and the rule it breaks), linking `F-DLQ-ALERT` to the Carrier-tracking incident when its evidence names `carrier-tracking`. Set `Agents:Model:Endpoint` (and `Agents:Model:Deployment`) to use your own Foundry deployment instead.
- **On any failure of the Agent** the page says "Assistant unavailable", logs the failure, and the Findings stay. The run has a short timeout (20 seconds) and is never retried ([failure behaviour](../architecture/failure-behaviour.md#agents)).

## The report

What the page shows after **Run health check**, for an operator to read, act on and compare. It is built from the Findings and stores nothing.

- **Grouped by severity**, the most severe first (`SeverityGroup`). Each Finding shows its Check ID, evidence, the as-found toggle and the **fix** (the Bicep parameter or setting, the Check's own), and, with the Agents on, **why it matters**: the Agent's narration of that Finding, linked to the Carrier-tracking incident where its evidence names `carrier-tracking`.
- **The Agent's ordering** is explained in one paragraph (its summary). It orders the Findings *within* a severity and never moves one to another group: the severity is the Check's, and the Agent can neither change one nor add a Finding.
- **Re-run and compare.** A second **Run health check** compares with the run before it (by Finding ID) and shows how many Findings are **new**, **fixed** and **unchanged**, lists the fixed ones and marks each Finding New or Unchanged. The previous result is held in the page session (the circuit) and nowhere else; closing the page forgets it. A run that fails keeps the last good one to compare with.
- **Export as Markdown** downloads the current report (`estate-health-check-<date>-<time>.md`): every Finding with its Check ID, severity, evidence and fix, the assistant's words marked as its, and the comparison where there is one. The browser builds no file of its own; the page hands it the text.
- **Follow-up questions.** With the Agents on, each Finding has two buttons, **Why is this a problem?** and **What changes if I fix it?**: fixed questions, no free text, the Finding's ID the only thing that varies ([HealthCheckQuestions](../../src/Norvia.Agents/Runner/HealthCheckQuestions.cs)). The Agent answers about that Finding alone (anything else it says is refused), and the fix it describes is the Check's. If the assistant fails the Finding says "Assistant unavailable" and nothing else changes.

## Local Findings come from a snapshot

Locally nothing reads a running estate: there are no alerts to look at under Aspire. The Findings are the snapshot's.

- [fixed.snapshot.json](../../src/HealthCheck/Norvia.HealthCheck.Infrastructure/Snapshots/fixed.snapshot.json) is the fixed estate: every alert, protection and setting a healthy estate has, and nothing for any Check to find.
- There is one snapshot for each preset in [infra/presets](../../infra/presets): [as-found](../../src/HealthCheck/Norvia.HealthCheck.Infrastructure/Snapshots/as-found.snapshot.json), [contrast](../../src/HealthCheck/Norvia.HealthCheck.Infrastructure/Snapshots/contrast.snapshot.json), [look-alike](../../src/HealthCheck/Norvia.HealthCheck.Infrastructure/Snapshots/look-alike.snapshot.json) and [silent-carrier](../../src/HealthCheck/Norvia.HealthCheck.Infrastructure/Snapshots/silent-carrier.snapshot.json). A snapshot holds the same estate as its preset leaves it: the flows and the queues and subscriptions, each with the alert its preset gives it, and the storage, secret, retention and API Management facts the toggles set.
- Tests keep them true: **each snapshot gives exactly the Findings for the toggles its preset turns on** (one Check for each, none for the fixed estate), names the flows and entities its preset lists, holds exactly the flows and entities [estate.bicep](../../infra/estate.bicep) alerts, and gives no evidence a secret value. A preset that changes without its snapshot fails the build.
- In Azure an adapter over Azure Resource Graph and Azure Monitor produces the same shape ([In Azure](#in-azure)). The snapshot adapter stays for local runs.

## In Azure

The same app, `health-check-web`, deployed as a Container App by the AppHost (`azd up`). Its Azure parts are the estate's and the app's, as for the Integration console:

To deploy only the app, skip azd's postdeploy hook (the database migrations, the Logic Apps, the sign-in callbacks and the Agents, none of which a change to this app touches): `SKIP_POSTDEPLOY=true azd deploy health-check-web`. The agent's definition is published by that hook, so after a change to `agents/estate-health-check.agent.yaml` run `./infra/deploy-agents.ps1 estate-health-check` as well.

| Part | Is |
|---|---|
| The app's identity | A user-assigned managed identity, `id-health-check-<token>`, made by [health-check.bicep](../../infra/estate/health-check.bicep). It holds **Reader on the estate's resource group** and no other Azure role, and **Foundry User** on the `norvia` project to ask its Agent. The privileged role belongs to the app, never to the Agent (#371, question 1) |
| The operators' sign-in | The app registration `Norvia Estate health check (<env>)`, with the roles `HealthCheck.Run` and `HealthCheck.Read`, for users only. Easy Auth sits in front of everything but the health probes and `/agent/v1/*`. [register-sign-in-callbacks.ps1](../../infra/register-sign-in-callbacks.ps1) adds its callback after the deploy |
| The estate adapter | `AzureEstateConfiguration`. **Azure Resource Graph** answers which Azure Monitor metric alert rules exist and what each watches (`microsoft.insights/metricalerts` of the resource group), which Logic Apps, action groups, Application Insights components, workspaces, storage accounts and API Management services there are, and the environment the TMS API's Container App runs with. The namespace's **queues and subscriptions** come from Service Bus's management plane, because Resource Graph does not hold them, with the dead-lettered messages each holds (`countDetails.deadLetterMessageCount`); a Logic App's **failed runs** of the last four days come from Azure Monitor's `WorkflowRunsCompleted` metric filtered to `status eq Failed` (left unknown where the metric is not there), and a storage account's **lifecycle rules**, its **Defender for Storage setting** and API Management's **policies** on the three Shipper tracking operations come from the resource itself. A queue or subscription has a dead-letter alert when an enabled rule scoped to the namespace watches `DeadletteredMessages` for it by an `EntityName` dimension that includes it (or by none, which watches them all); a Logic App has a failed-run alert when an enabled rule scoped to it watches `WorkflowRunsCompleted`. The subscriptions [alert-exemptions.json](../../infra/estate/alert-exemptions.json) lists are left out, as [alerts.bicep](../../infra/estate/alerts.bicep) leaves them out. It only reads. Set by `Estate:ResourceGroupId` and `Estate:ServiceBusNamespace`; where they are unset the snapshot is read |
| The agent API's token | Entra's, for the app registration `Norvia Estate health check agent API (<env>)` (audience), which holds one role, `HealthCheck.Agent.Read`, assigned to API Management's identity alone. The app takes it on `/agent/v1` and nothing else. API Management's token names the gateway, so the Agent's identity is read from the `X-Agent-ID` header the policy sets |
| The MCP server | `agents/health-check` in API Management, made by [health-check-mcp.bicep](../../infra/estate/health-check-mcp.bicep) from [health-check.tools.json](../../contracts/agent-mcp/health-check.tools.json) with [agent-mcp-server.bicep](../../infra/estate/agent-mcp-server.bicep): the Agent's token (audience `Norvia health-check agent view (<env>)`, role `HealthCheck.Agent.Read`), the tool list, 10 calls a minute per identity, and the correlation ID, the Agent's identity and the tool name on each call. Its backend is `health-check-agent-api`; [deploy-agents.ps1](../../infra/deploy-agents.ps1) points it at the app and stores `ESTATE_HEALTH_CHECK_BASE_URL` |
| The Agent | Published into the `norvia` project from [the definition](../../agents/estate-health-check.agent.yaml) by [deploy-agents.ps1](../../infra/deploy-agents.ps1), with its own identity, which [assign-agent-roles.ps1](../../infra/assign-agent-roles.ps1) gives `HealthCheck.Agent.Read` on the agent view. It holds no Azure role. The app asks it through the Responses API as its own identity (`FoundryAgentClient`), with `store: false`, no conversation, the run's response deleted afterwards (best effort) and the correlation ID as a structured input that becomes the tool call's `X-Correlation-ID` |

Turn the Agents on with `ESTATE_AGENTS_ENABLED_SETTING=true` (`azd env set`) and `azd up`; they are off by default, and the page then says "Assistant unavailable" and still lists the Findings.

**The narration is checked against the tool's own answer.** Foundry returns the output of each tool call, and the client refuses a narration that explains a Finding that output does not hold, so the Agent cannot invent one in Azure either. If Foundry hands back no output the narration is refused (`AgentUnavailable`) and the Findings stay.

### What Reader cannot see

The app's identity holds Reader and no more, so some facts cannot be read, and are left unknown (`null`): a Check gives no Finding for an unknown fact, and a page that lists none for it does not say the estate is healthy there.

| Fact | Check | Why |
|---|---|---|
| A Carrier endpoint secret kept in a Logic App's settings | `SECRET-INLINED` | A Logic App's app settings are read by an action Reader does not hold (`Microsoft.Web/sites/config/list/action`), and giving the app that right would put a secret-reading role on a reader. In Azure only the snapshots find this Check's toggle, and the smoke run of [as-found](as-found.md#storage-secrets-and-retention) checks the setting by name |
| A workflow's tracked properties and its calls to the Driver app | `TRACKED-PROPERTIES`, `DRIVER-REPORT-BACK` | They are in the workflow's definition, which the Logic App's runtime serves, not Azure Resource Manager. A Logic App's name is the evidence of the failed-run alert, not the flow's |
| What guards the agents | The `AGENT-`, `MODEL-` and `ASSISTANT-` Checks | The estate does not build the agent toggles yet (see [the checklist](#the-checklist)), so there is nothing to read |
| The TMS storage account's lifecycle rule | `PROOF-HOT-TIER` | Found by the account that holds no `stinbound` or `stdriver` prefix: read when the AppHost has provisioned one |

Where the resource a fact is read from is not there (no action group, no Application Insights, no TMS Container App), the fact is unknown too.

### Smoke run

[infra/smoke-test-estate-health-check.ps1](../../infra/smoke-test-estate-health-check.ps1), after `azd up` with the Agents on, signed in to the Azure CLI as the presenter. It checks that the Agent holds only `HealthCheck.Agent.Read` and no Azure role, that the app's agent API has that one role held by API Management alone, that the app's identity holds Reader on the resource group and no other role there, and that the project's shared identity holds none; then applies the **as-found preset** (the dead-letter alerts of the seven entities are deleted) and asks the Agent until the checks return `F-DLQ-ALERT`, naming `carrier-tracking`, and the real model explains it citing `r1`; then applies the **fixed preset**, which deploys the estate again and rebuilds the alerts, and asks until the checks return no Finding and the narration invents none; and finally that the Agent's tool calls are hops of the run's Message trail (`agent-mcp-health-check`, `agent-view-health-check`) tagged with the Agent's identity and `run_checks`. Resource Graph takes a short while to see an alert come and go, so each check asks again for up to ten minutes. The estate ends in the fixed state. The script prints names, IDs and counts only.

## What the Agent can and cannot do

- It reads through three tools, `run_checks`, `list_failed_runs` and `list_dead_letter_queues`, each a GET on the app's agent API. No tool writes, and none returns a secret value.
- Its identity holds one role, `HealthCheck.Agent.Read`, and **no Azure role**. Reading Azure is the app's own identity's job, never the Agent's.
- It cites a Finding by its ID, and the runner refuses a narration that explains a Finding the tool did not return, cites a result the run never had, or is not the expected object: `AgentUnavailable`. It cannot change a severity or a fix, because the page shows those from the Finding.
- Everything a tool returns is data. The instructions say so, the runner delimits each result, and the page shows the narration and the evidence as text, never as markup.

## Evidence is masked

A Finding's evidence names the resources concerned. A secret is reported by where it is (a setting's name, a resource), never by its value: `EvidenceMasker` hides whatever follows a secret-looking name (`password`, `secret`, `key`, `token`, `credential`, `connectionstring`) and any bearer token, in every Finding the engine makes. The agent view is an allowlist of fields with no free-form value.

## Roles

| Role | Holder | Opens |
|---|---|---|
| `HealthCheck.Run` | The Integration operator | The page: running a health check and seeing what it found |
| `HealthCheck.Read` | The Integration operator | Nothing yet. Reserved for showing a report to someone who may not run a check |
| `HealthCheck.Agent.Read` | The Estate health-check agent's identity alone | `/agent/v1/checks`, and nothing else in the app. No operator holds it |

Locally the AppHost signs in a local operator holding both operator roles, and refuses to do it outside Development. In Azure Container Apps' Easy Auth signs the operator in to the app's own app registration, `Norvia Estate health check (<env>)`, and the app reads their app roles from it ([ADR 0026](../adr/0026-the-integration-console-signs-operators-in-with-easy-auth-and-checks-their-app-role.md), as for the Integration console); Entra signs in only the users assigned a role, and the presenter holds both. See [identity and access](../architecture/identity-and-access.md#agents).

## Evaluation

The Agent's narration is checked against known answers, on demand and before a release; it never runs in CI and never gates a merge. There is a case for every toggle (the estate with that toggle on, the known answer being its Check's Finding), for the fixed estate (no Finding, said plainly), for a question about a problem no Check found (the Agent says it found nothing) and for injection through a tag, a resource name and a description. How to run it and how to read a result: [agent-evaluation.md](agent-evaluation.md#reading-the-estate-health-check-agents-results). The cases: [evaluation/cases/estate-health-check/](../../evaluation/cases/estate-health-check/).

## Tests

- **Unit tests** (80% line coverage): the report (grouping, the comparison, the Markdown export, a severity the Agent's answer cannot change, the follow-up question), the engine, every Check against the facts of its toggle (it fires for its toggle and only its toggle, and the healthy estate gives no Finding), every snapshot (exactly the Findings of the toggles its preset turns on, the fixed one none), the masking, the use cases, the role checks, the agent API (token, role, audience, GET only, the tracking hop), the page (bUnit), the local agent client against a fake MCP server, and the stand-in's second server.
- **Architecture tests**: dependencies point inwards, no write tool, one role for the Agent, and only Infrastructure references the Foundry SDK.
- **Integration tests**: #398 (locally) and #400 (in Azure: [AzureHealthCheckTests](../../tests/HealthCheck/Norvia.HealthCheck.Agents.IntegrationTests/AzureHealthCheckTests.cs), the app built as in Azure over faked Resource Graph, Service Bus and Foundry endpoints, the Finding coming and going as the alert does) and #401 (the whole checklist: [ChecklistAgainstSnapshotsTests](../../tests/HealthCheck/Norvia.HealthCheck.Agents.IntegrationTests/ChecklistAgainstSnapshotsTests.cs), every snapshot giving exactly the Findings for the toggles its preset turns on, the fixed one none, every toggle with a Check, and no secret value in evidence) and #402 (the report: [ReportAgainstSnapshotsTests](../../tests/HealthCheck/Norvia.HealthCheck.Agents.IntegrationTests/ReportAgainstSnapshotsTests.cs), the comparison of two runs as new, fixed and unchanged, severity staying the Check's after the Agent's answer, and the Markdown export holding every Finding with its Check ID, evidence and fix). **UI tests**: #399.
- **Policy and drift tests** ([HealthCheckAgentAzureTests](../../tests/Tms/Norvia.Tms.ArchitectureTests/HealthCheckAgentAzureTests.cs), `bicep snapshot`, nothing deployed): the MCP server has the contract's tools and no others, GET only; the policy gets the contract's role, the agent view as audience and a rate limit; both app registrations hold the one role and Entra issues no token without it; only API Management's identity is given a role in the Bicep; the app's identity holds Reader on the resource group and nothing else, and nothing on the Agent's side holds an Azure role. The stand-in's unit tests serve the same contract file, so the stand-in's tool list is the Bicep's.
- **Unit tests of the Azure adapter** ([AzureEstateConfigurationTests](../../tests/HealthCheck/Norvia.HealthCheck.Infrastructure.UnitTests/Estate/AzureEstateConfigurationTests.cs)): faked Resource Graph, Service Bus, storage and API Management answers mapped to the snapshots' shape, and the Foundry client against a faked Responses endpoint.
- **Smoke run** (not in CI): [infra/smoke-test-estate-health-check.ps1](../../infra/smoke-test-estate-health-check.ps1), below.
