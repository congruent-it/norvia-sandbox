# Estate health check

How an Integration operator runs an **Estate health check** and reads what it found. The first slice of the Estate health-check agent (#371): it works end to end **locally**, with one **Check**, from a snapshot of the estate's configuration, and needs no Azure. Terms are defined in [GLOSSARY.md](../../GLOSSARY.md).

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
- In Azure an adapter over Azure Resource Graph and Azure Monitor produces the same shape, and the app's managed identity holds Reader on the estate's resource group (#392). The snapshot adapter stays for local runs.

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

Locally the AppHost signs in a local operator holding both operator roles, and refuses to do it outside Development; where no local operator is configured nothing signs anyone in, so every page refuses. Easy Auth in Azure comes with the app's deployment (#392). See [identity and access](../architecture/identity-and-access.md#agents).

## Tests

- **Unit tests** (80% line coverage): the engine and the Check against both snapshots, the masking, the use cases, the role checks, the agent API (token, role, audience, GET only, the tracking hop), the page (bUnit), the local agent client against a fake MCP server, and the stand-in's second server.
- **Architecture tests**: dependencies point inwards, no write tool, one role for the Agent, and only Infrastructure references the Foundry SDK.
- **Integration tests**: #398. **UI tests**: #399.
