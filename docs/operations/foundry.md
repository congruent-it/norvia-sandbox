# Foundry

The Foundry account and projects the Agents run on ([ADR 0040](../adr/0040-agents-read-the-estate-through-api-management-have-no-write-tools-and-their-own-identity.md)), what the estate deploys of it, and the answers to the two spikes of #497. For engineers and coding agents working on the estate. Terms are defined in [GLOSSARY.md](../../GLOSSARY.md).

No Agent is defined or published yet: the deploy scripts that publish them are #499, and the model and tracing here are what they will use.

## What the estate deploys

[infra/estate/foundry.bicep](../../infra/estate/foundry.bicep), called from [infra/estate.bicep](../../infra/estate.bicep):

| Resource | Notes |
|---|---|
| Account `ais-norvia-<token>` | Kind `AIServices`, project management on, a public endpoint with Entra only (local keys off), no VNet. In `AZURE_LOCATION` when the model has a `DataZoneStandard` deployment there, else Sweden Central |
| Project `norvia` | The project of every Agent except Intake, with the Application Insights connection. Its endpoint is `https://<account>.services.ai.azure.com/api/projects/norvia` |
| Project `norvia-intake` | The **Intake agent's** project, with **no Application Insights connection** (spike 2). Endpoint `https://<account>.services.ai.azure.com/api/projects/norvia-intake` |
| Model deployment | One, shared by both projects (a deployment belongs to the account), type **`DataZoneStandard`**: prompts and responses are processed inside the EU Data Boundary. `GlobalStandard` is not used. Capped at `foundryModelCapacity` (30 thousand tokens a minute by default, at most 100) |
| Connection `application-insights` | On the `norvia` project, not the account: an account connection is shared with every project, so it would trace the Intake project too. Connects the estate's Application Insights, which turns on Foundry's server-side tracing for every agent in that project. The traces land in the same workspace as the Message trails, so the console can find an agent run |
| Role assignments | The presenter, and any object ID in `ESTATE_FOUNDRY_USER_PRINCIPAL_IDS` (the pipeline that deploys the Agents), hold **Foundry User** on both projects: run agents and call models, no management |

Every Foundry resource carries the cost tags `workload=foundry-agents` and `cost-category=ai-model`, and the project carries `agents-enabled`.

## Which project each Agent lives in

| Agent | Project | Traced to Application Insights |
|---|---|---|
| Intake | `norvia-intake` | No |
| Every other Agent | `norvia` | Yes |

An Agent that reads a Shipper's text goes in `norvia-intake`; one that reads only the estate's own data through narrow views goes in `norvia`. The Intake agent's slice publishes into `norvia-intake` (`FOUNDRY_INTAKE_PROJECT_ENDPOINT`).

## Settings

Set in the azd environment, then `azd provision`:

| Setting | Default | Does |
|---|---|---|
| `ESTATE_AGENTS_ENABLED_SETTING` | `false` | `Agents:Enabled`: whether the Agents do anything. The estate decides, as for the Simulator. The account, project and model exist either way. The estate is at the 64 outputs a template may have, so [provision-estate.sh](../../infra/provision-estate.sh) hands it to the apps as `AZURE_ESTATE_AGENTS_ENABLED`, as it does the reset setting |
| `ESTATE_FOUNDRY_MODEL_DEPLOYED` | `true` | `false` removes only the model deployment, so every Agent run fails. This is the switch #377's agent toggles use to break the Agents |
| `ESTATE_FOUNDRY_MODEL_CAPACITY` | `30` | The cap, in thousands of tokens a minute |
| `ESTATE_FOUNDRY_LOCATION` | empty | Overrides the region |
| `ESTATE_FOUNDRY_USER_PRINCIPAL_IDS` | empty | Comma-separated object IDs that also get Foundry User |

The `norvia` project endpoint is stored by the same script as `AZURE_ESTATE_FOUNDRY_PROJECT_ENDPOINT` (the AppHost's `estate-foundry-project-endpoint`) and `FOUNDRY_PROJECT_ENDPOINT` (what scripts and the Foundry SDKs read). The Intake project's is `FOUNDRY_INTAKE_PROJECT_ENDPOINT`, set the same way (the estate is at its 64 outputs, so it is not a template output).

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

**Consequence, as decided in #378:** the Intake agent, whose prompts are a Shipper's booking text, moves to **its own Foundry project with no Application Insights connection**. That project is built (#721, `norvia-intake`). The other Agents read the estate's own data through narrow views and stay in the `norvia` project, where the connection stays on for the agent-run-in-the-console story ([#369](https://github.com/congruent-it/norvia-sandbox/issues/369) decision 8). The connection is on the `norvia` project and not the account, because an account connection is shared with every project.

Two steps lower the risk of what does stay in the project, and are not yet done: register `protectGenAISensitiveData` on the subscription and set `AppGenAIContent` to *Protected* (both are per subscription or workspace, outside this Bicep). The smoke run checks the part that matters here: the prompt text is in none of the tables the Message trails use.

This answer comes from the documents. If a setting turns up, the Intake agent can stay in the one project and the follow-up closes.

## Smoke run

[infra/smoke-test-foundry.sh](../../infra/smoke-test-foundry.sh), after `azd up`, as the presenter. It checks that the deployment is `DataZoneStandard` in an EU region, that it is capped and tagged, that the model answers one request through the project endpoint, and that Foundry's span reaches Application Insights without the prompt text (a sentinel the script made up) in `AppDependencies`, `AppTraces`, `AppEvents` or `AppRequests`. It prints names and counts only.

## Agent roles: which credential

The spike of #499. Foundry makes an Agent's identity when it is published, so a script gives that identity its read role ([infra/assign-agent-roles.sh](../../infra/assign-agent-roles.sh)). The question is whose token makes the call.

What the documents say:

- The call is `POST /servicePrincipals/{agent view's service principal}/appRoleAssignedTo` with the agent identity as `principalId`. It needs `AppRoleAssignment.ReadWrite.All` and `Application.Read.All`, delegated (with a directory role such as Application Administrator) or application. It is the ordinary service principal API, not an Agent ID API.
- The Entra Agent ID APIs (agent identity blueprints, creating agent identities) reject Azure CLI tokens with a 403, because those carry `Directory.AccessAsUser.All`. Nothing here calls them: Foundry creates the blueprint and the identity.
- Agent identities are not in the way as the *principal* of an app role assignment: the Entra documentation does this for agent identities with the same call. Foundry does not assign the role itself, and does not give the identity any permission.

So the presenter's `az` token is expected to work, and **the script uses it by default**. If Graph answers 403 (`Directory.AccessAsUser.All` rejected), the fallback is a deployment app registration: set `AGENT_ROLES_CLIENT_ID` and a secret (`AGENT_ROLES_CLIENT_SECRET`) or, in CI, a federated credential's assertion (`AGENT_ROLES_CLIENT_ASSERTION`), and the script signs in with client credentials. That registration needs only the application permissions `AppRoleAssignment.ReadWrite.All` and `Application.Read.All` (neither is on the list of permissions blocked for agents, which concerns permissions *granted to* agents), admin-consented, and it would go in [identities.bicep](../../infra/estate/identities.bicep) with just those.

**Not yet verified in a tenant.** The answer above comes from the documents, as spikes 1 and 2 above did. It is settled by one run: after the first publish, `infra/assign-agent-roles.sh` as the presenter. If it passes, nothing more is needed and the registration is never added. If Graph answers 403, add the registration and record that here.

## When a publish gives a new identity

Every publish gives the Agent a new `agentIdentityId`, and the new identity holds nothing: its tool calls are refused (403) until the role is assigned. So `infra/assign-agent-roles.sh` runs after every publish, with the publish's result (one entry per Agent: name, server, `agentIdentityId`). If a tool call fails after a publish, run it again; it skips an identity that already holds its role. The project's shared identity, which an unpublished Agent uses, holds no role on any agent view, and the script fails if it finds one. The superseded identity keeps its role until Foundry deletes it with the version it belonged to.

## Agent connection: Bicep or script

The project's connection to an API Management MCP server uses the agent-identity authentication type (`agentic-identity`: Foundry calls the server with the token of the Agent that makes the call). The Bicep connection resource, up to API version `2026-07-01`, lists no such `authType`, so [infra/create-agent-connection.sh](../../infra/create-agent-connection.sh) makes it with `azd ai connection create`. When the schema gains the type, make the connection in Bicep and delete the script.

## Conversation cleanup

The Agent Service is stateful: conversations and responses persist until they are deleted ([Foundry Agent Service FAQ](https://learn.microsoft.com/azure/foundry/agents/faq#do-you-store-any-data-used-in-the-foundry-agent-service-api)), and the Intake agent's conversation holds a Shipper's email. Three things keep what Foundry keeps short ([#500](https://github.com/congruent-it/norvia-sandbox/issues/500), ADR 0040's personal-data decision):

| Layer | What | Where |
|---|---|---|
| **Store nothing** | Every response is created with `store: false`, so a stateless call leaves no response behind (spike 3 below) | The Foundry client of `Norvia.Agents` (#379) |
| **Delete after each run** | `IConversationCleanup` deletes the run's conversation and its responses as soon as the host application has the answer, best effort. A failure is logged as `ConversationNotDeleted` on the run's span and never reaches the user | `Norvia.Agents`, shared by every host (#379) |
| **Delete before returning** | The Intake agent's operation (`booking-drafts`, #458) deletes first and only then returns the draft. A failed delete is `ConversationNotDeleted`, the draft is still returned (the Shipper is not punished for the cleanup) and the sweeper retries it | The Intake operation |
| **The nightly sweeper** | Deletes every conversation, and every response, older than a day. A safety net, not the plan | [conversation-sweeper-logic-app.bicep](../../infra/estate/conversation-sweeper-logic-app.bicep) |

Locally there is no Foundry conversation (the in-process runner keeps its history in memory for the run), so there is nothing to clean up and no sweeper.

### The sweeper

A Logic Apps Standard app with one workflow, `conversation-sweeper` ([src/Integration/ConversationSweeperLogicApp](../../src/Integration/ConversationSweeperLogicApp)), on the intake Logic App's plan, as the other flows are. A Container Apps job would have worked as well, but the estate has no job yet and every Logic App already has its failed-run alert and its place in the Integration console.

- **Runs** every night at 02:00 UTC. `CONVERSATION_MAX_AGE_MINUTES` (1440, a day) is the age; a day is far shorter than `integrationRetentionDays`.
- **Does** `GET /openai/v1/conversations?order=asc&limit=100` on the project endpoint, deletes each conversation whose `created_at` is older than the cutoff (`DELETE /openai/v1/conversations/{id}`), and asks for the first page again until a page deletes nothing or `has_more` is false. It then does the same for `/openai/v1/responses`, which catches a response that no conversation owned. It always asks for the *first* page, because deleting the item a cursor points at would break the cursor.
- **Fails** the run with the code `ConversationNotDeleted` when any delete failed after its retries, so the failed-run alert (`alert-failed-runs-conversation-sweeper`) fires. Tracked properties on the run: `flow` (`conversation-sweeper`), `deleted` and `notDeleted`. It reads and deletes ids and timestamps; it never opens a conversation's items.
- **Runs as** its own managed identity, `id-conversation-sweeper-<token>`, which holds **Foundry User on the project** and nothing else (no Azure role, no estate data). A second identity, the Logic App's host identity, holds its own storage and the Key Vault reference, as for the other Logic Apps.
- **Is deployed** like the other Logic Apps: Bicep makes the app, `infra/deploy-conversation-sweeper-logic-app.sh` (an azd postdeploy hook) publishes the workflow. It needs no address from the apps Aspire deploys.

**The as-found toggle `noConversationSweeper`** builds the app but stopped (`enabled: false`), so nothing is ever swept. Conversation data of the Intake agent piles up silently, which is a finding for the health check (#377): the conversations are in the project, the Logic App exists and has no runs. The failed-run alert never fires, because there are no runs.

**Not yet verified in a tenant:** that Foundry User may list and delete conversations and responses, and the shape of the list response (`data`, `has_more`, `created_at` in seconds), which come from the REST reference. The first run is `infra/smoke-test-conversation-sweeper.sh`; if the Foundry role is too narrow, the run fails with a 403 on the list call and the role is the thing to change here and in the identity matrix.

### Smoke run

[infra/smoke-test-conversation-sweeper.sh](../../infra/smoke-test-conversation-sweeper.sh), after `azd up`, as the presenter. It leaves a conversation in the project, sets the sweeper's age to a minute, runs the trigger once through the Logic Apps management API, checks the run succeeded and the conversation is gone (404), and puts the age back to a day. It prints ids and counts only.

### Spike 3: a store-nothing option

**Question:** does the Responses API have a store-nothing option for prompt agents?

**Answer: yes, `store: false`, for the responses.** Read in October 2026: [Build with agents, conversations, and responses](https://learn.microsoft.com/azure/foundry/agents/concepts/runtime-components#generate-responses) ("Generate a response without storing") shows a prompt agent's response created with `store=False`: "the service doesn't persist the response", and the caller carries the context itself. It is the answer for the Intake agent and for every Agent run that has one turn.

What it does not do: it does not turn the stateful Agent Service into a stateless one. A conversation made with `POST /openai/v1/conversations` is stored whatever the response says, so the clients **do not create a conversation** unless an Agent needs history (none does: Agents keep no memory between runs, ADR 0040), and they pass `store: false` on every call. The FAQ still says the Agent Service "retains data" and the documents give no setting for a project that stores nothing, so the delete after each run and the sweeper stay: the sweeper is the safety net for a client that forgot, a failed delete and a run that died. **Not verified in a tenant**: whether `store: false` is honoured when the agent calls an MCP tool; the smoke run of the first Agent (#379) lists the project's responses afterwards and settles it.

## Open points of #500

The estate half of #500 is built (the sweeper, its toggle, the smoke run, the spike). The host-side code is not, because it lives in `Norvia.Agents`, which #379 creates. Whoever builds #379 or #458 picks these up; #500 stays open until they are done.

| # | Open point | Where | Needs |
|---|---|---|---|
| 1 | `IConversationCleanup` in `Norvia.Agents`: deletes the run's conversation and its responses as soon as the host has the answer. Best effort: a failure is logged as `ConversationNotDeleted` on the run's span and never thrown. Unit tests with the Foundry client faked (the call, the code on failure, no exception to the caller); at least 80% line coverage | `Norvia.Agents`, shared by every host | #379 |
| 2 | Every Foundry call sets `store: false` and creates no conversation unless an Agent needs history (none does). This is the answer of spike 3 | The Foundry client of `Norvia.Agents`; an architecture test can check no `CreateResponse` omits it | #379 |
| 3 | The Intake operation (`booking-drafts`) deletes first and only then returns the draft. A failed delete is `ConversationNotDeleted`, the draft is still returned (the Shipper is not punished), and the sweeper retries it. Unit test: delete fails, draft returned, code on the span | The Intake operation of #458 | #379, #458 |
| 4 | Only Infrastructure projects reference the Foundry SDK, so the cleanup port is in the host's Application layer or in `Norvia.Agents` and the call is in Infrastructure (the architecture test of #379 covers it) | `Norvia.Agents` and each host | #379 |
| 5 | Verify in a tenant: run `infra/smoke-test-conversation-sweeper.sh` (Foundry User may list and delete, the list response shape), and after the first Agent run list the project's responses to see whether `store: false` is honoured when an Agent calls an MCP tool. Record both here, in "The sweeper" and spike 3 | A deployed estate | #379 deployed |
| 6 | Docs that say "planned" for the host side: [failure-behaviour.md](../architecture/failure-behaviour.md) (the row for `IConversationCleanup`), [local-vs-azure.md](../architecture/local-vs-azure.md) (locally there is no Foundry conversation), the Intake agent's P1 doc. Drop "planned" when 1 to 3 are built | Docs | 1 to 3 |

Acceptance criteria of #500 still open: *a normal Agent run leaves no conversation behind* (1, 2) and *the Intake operation deletes before it returns, and a failed delete is a coded span, not an error to the Shipper* (3). *A conversation the host could not delete is removed by the sweeper* is built and waits for point 5.

## Open point of #721

The conversation sweeper (#500) lists and deletes conversations in the `norvia` project only, and the Intake agent's conversations are in `norvia-intake`. Before the Intake agent publishes, the sweeper needs the Intake endpoint as well and its identity needs Foundry User on the Intake project.

## Not here

Publishing the Agents from their definition files (`infra/deploy-agents.sh`, the agent registry and the smoke run wait for the definition format of #379), `IConversationCleanup` itself ([open points](#open-points-of-500)), and the evaluation harness, which is [agent-evaluation.md](agent-evaluation.md) (#501).
