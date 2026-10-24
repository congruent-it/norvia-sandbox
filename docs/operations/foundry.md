# Foundry

The Foundry account and project the Agents run on ([ADR 0040](../adr/0040-agents-read-the-estate-through-api-management-have-no-write-tools-and-their-own-identity.md)), what the estate deploys of it, and the answers to the two spikes of #497. For engineers and coding agents working on the estate. Terms are defined in [GLOSSARY.md](../../GLOSSARY.md).

No Agent is defined or published yet: the deploy scripts that publish them are #499, and the model and tracing here are what they will use.

## What the estate deploys

[infra/estate/foundry.bicep](../../infra/estate/foundry.bicep), called from [infra/estate.bicep](../../infra/estate.bicep):

| Resource | Notes |
|---|---|
| Account `ais-norvia-<token>` | Kind `AIServices`, project management on, a public endpoint with Entra only (local keys off), no VNet. In `AZURE_LOCATION` when the model has a `DataZoneStandard` deployment there, else Sweden Central |
| Project `norvia` | The one project. Its endpoint is `https://<account>.services.ai.azure.com/api/projects/norvia` |
| Model deployment | One, shared, type **`DataZoneStandard`**: prompts and responses are processed inside the EU Data Boundary. `GlobalStandard` is not used. Capped at `foundryModelCapacity` (30 thousand tokens a minute by default, at most 100) |
| Connection `application-insights` | Connects the estate's Application Insights, which turns on Foundry's server-side tracing for every agent in the project. The traces land in the same workspace as the Message trails, so the console can find an agent run |
| Role assignments | The presenter, and any object ID in `ESTATE_FOUNDRY_USER_PRINCIPAL_IDS` (the pipeline that deploys the Agents), hold **Foundry User** on the project: run agents and call models, no management |

Every Foundry resource carries the cost tags `workload=foundry-agents` and `cost-category=ai-model`, and the project carries `agents-enabled`.

## Settings

Set in the azd environment, then `azd provision`:

| Setting | Default | Does |
|---|---|---|
| `ESTATE_AGENTS_ENABLED_SETTING` | `false` | `Agents:Enabled`: whether the Agents do anything. The estate decides, as for the Simulator. The account, project and model exist either way. The estate is at the 64 outputs a template may have, so [provision-estate.sh](../../infra/provision-estate.sh) hands it to the apps as `AZURE_ESTATE_AGENTS_ENABLED`, as it does the reset setting |
| `ESTATE_FOUNDRY_MODEL_DEPLOYED` | `true` | `false` removes only the model deployment, so every Agent run fails. This is the switch #377's agent toggles use to break the Agents |
| `ESTATE_FOUNDRY_MODEL_CAPACITY` | `30` | The cap, in thousands of tokens a minute |
| `ESTATE_FOUNDRY_LOCATION` | empty | Overrides the region |
| `ESTATE_FOUNDRY_USER_PRINCIPAL_IDS` | empty | Comma-separated object IDs that also get Foundry User |

The project endpoint is stored by the same script as `AZURE_ESTATE_FOUNDRY_PROJECT_ENDPOINT` (the AppHost's `estate-foundry-project-endpoint`) and `FOUNDRY_PROJECT_ENDPOINT` (what scripts and the Foundry SDKs read).

## Spike 1: model and region

Source: [Region availability for Foundry Models sold by Azure](https://learn.microsoft.com/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure-region-availability#data-zone-standard), Data Zone Standard, Europe, read in October 2026.

- **Model: `gpt-5.4-mini`, version `2026-03-17`.** The smallest current model with a wide EU spread. The older small models (`gpt-4.1-mini`, `gpt-4o-mini`) are being retired, and `gpt-5.4-nano` is in North Europe only.
- **Regions with it as `DataZoneStandard`:** France Central, Germany West Central, Italy North, Norway East, Poland Central, Spain Central, Sweden Central, Switzerland North and West Europe. North Europe has no `gpt-5.4-mini` at this type.
- **Region: `AZURE_LOCATION` if it is one of those, else Sweden Central**, which has every model of the list and so survives a change of model. The Bicep decides this itself (`foundryRegions` in `estate.bicep`).
- **Capacity: 30** thousand tokens a minute. The Agents are interactive and a person waits for each answer, so one run is a few thousand tokens and a handful at once fit in. It is a cap on spend, not a target. Raise it with `ESTATE_FOUNDRY_MODEL_CAPACITY` if a subscription's quota allows it.
- **Data at rest** (stored conversations) is in the account's region. **Processing** is anywhere in the EU.

The table moves; when a deploy fails with a quota or region error, check it again and change `foundryModelName`, `foundryModelVersion` or the region.

## Spike 2: trace content

**Question:** can Foundry's server-side tracing be set not to record prompt content?

**Answer: no documented setting does it.** Read in October 2026:

- [Set up tracing](https://learn.microsoft.com/azure/foundry/observability/how-to/trace-agent-setup): server-side tracing starts when a project is connected to Application Insights, "no code changes", and captures "prompt content". The only documented way to stop it is to disconnect Application Insights ([Tracing and data handling](https://learn.microsoft.com/azure/foundry/observability/concepts/trace-data)).
- The content-recording switches (`OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT`, `AZURE_EXPERIMENTAL_ENABLE_GENAI_TRACING`) are environment variables of client-side instrumentation. They do not reach a server-side trace, which is recorded by Foundry, not by our code.
- [Restrict access to sensitive content](https://learn.microsoft.com/azure/foundry/observability/how-to/traces-sensitive-content) (preview) keeps the content but moves it: with the `protectGenAISensitiveData` feature flag registered, prompts, responses, system instructions and tool arguments and results go only to the `AppGenAIContent` table, which can be protected so only **Privileged Monitoring Data Reader** reads it. From 30 September 2026 this is how Application Insights stores them regardless.

So the content is **recorded, but kept apart**. The estate's Application Insights is shared with the Message trails, whose rule is that no body is ever in a trail ([coding guidelines §7](../coding-guidelines.md#7-observability)). The `AppGenAIContent` table is not one the trails read, but a person who can read the workspace can read it unless the table is protected, and a shared workspace is the wrong place to rely on that.

**Consequence, as decided in #378:** the Intake agent, whose prompts are a Shipper's booking text, moves to **its own Foundry project with no Application Insights connection**. A follow-up issue opens that project (#721). The other Agents read the estate's own data through narrow views and stay in the `norvia` project, where the connection stays on for the agent-run-in-the-console story ([#369](https://github.com/congruent-it/norvia-sandbox/issues/369) decision 8). Until the follow-up lands, no Intake agent exists, so nothing sensitive is traced.

Two steps lower the risk of what does stay in the project, and are not yet done: register `protectGenAISensitiveData` on the subscription and set `AppGenAIContent` to *Protected* (both are per subscription or workspace, outside this Bicep). The smoke run checks the part that matters here: the prompt text is in none of the tables the Message trails use.

This answer comes from the documents. If a setting turns up, the Intake agent can stay in the one project and the follow-up closes.

## Smoke run

[infra/smoke-test-foundry.sh](../../infra/smoke-test-foundry.sh), after `azd up`, as the presenter. It checks that the deployment is `DataZoneStandard` in an EU region, that it is capped and tagged, that the model answers one request through the project endpoint, and that Foundry's span reaches Application Insights without the prompt text (a sentinel the script made up) in `AppDependencies`, `AppTraces`, `AppEvents` or `AppRequests`. It prints names and counts only.

## Not here

Publishing the Agents and giving each identity its read role (#499), the API Management MCP servers (#498), conversation cleanup (#500) and the evaluation harness (#501).
