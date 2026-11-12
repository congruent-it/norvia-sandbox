# Docs

Where to look. Terms are defined in [GLOSSARY.md](../GLOSSARY.md); the company behind the sandbox is in [SCENARIO.md](../SCENARIO.md).

| Looking for | Go to |
|---|---|
| How to write .NET code and tests, what CI gates | [coding-guidelines.md](coding-guidelines.md), by section |
| Why something is the way it is | [adr/](adr/), numbered, one decision each. Search the folder by keyword (`grep -il <word> docs/adr/*`) before reading one |
| How the estate fits together: systems, events, identities, failures, local against Azure | [architecture/](architecture/README.md) |
| What an application's APIs and database are | [applications/](applications/README.md), one page per application. The database sections are generated |
| What a business process does, step by step | [processes/tms/](processes/tms/README.md) (P1–P12), [processes/erp/](processes/erp/README.md) (E1–E2) |
| How to run, operate or read a part of the estate | [operations/](operations/) (below) |
| Adding or changing an Agent | [agents/README.md](../agents/README.md), "Wiring a new Agent" |
| Agent tool lists, evaluation cases | [contracts/agent-mcp/](../contracts/agent-mcp/README.md), [evaluation/](../evaluation/README.md) |
| Issue, label and glossary conventions for coding agents | [agents/](agents/): [issue-tracker.md](agents/issue-tracker.md), [triage-labels.md](agents/triage-labels.md), [domain.md](agents/domain.md) |
| Scripts and Azure deployment | `infra/`, with [architecture/deployment.md](architecture/deployment.md) and [coding-guidelines.md §12](coding-guidelines.md#12-scripts) |
| Colours, fonts, logo | [branding.md](branding.md) |

## Operations pages

| Page | Read it when |
|---|---|
| [agent-evaluation.md](operations/agent-evaluation.md) | Adding evaluation cases for an Agent, or reading a run's result |
| [agent-mcp-servers.md](operations/agent-mcp-servers.md) | Putting an application's agent view behind an API Management MCP server |
| [foundry.md](operations/foundry.md) | Working on the Foundry account, projects or published Agents |
| [as-found.md](operations/as-found.md) | Using or adding a toggle that makes the estate as it was found |
| [estate-health-check.md](operations/estate-health-check.md) | Running or reading an Estate health check |
| [integration-console.md](operations/integration-console.md) | Working on Message trails, failed runs, dead letters, resubmit or discard |
| [simulator.md](operations/simulator.md) | Working on the Simulator, its runs, faults or text pools |
| [partner-portal.md](operations/partner-portal.md) | Working on the Partner portal's booking forms, Fill from an email or the tracking assistant |

## Where the code is

`src/<Application>/` holds an application's projects (`Domain`, `Application`, `Infrastructure`, `Api` or `Web`), `src/Norvia.Agents` the Agent runner and scripted fakes, `src/Integration/` the local stand-ins for Logic Apps and workers, and `src/Norvia.AppHost` the Aspire AppHost, which wires the whole local estate. `tests/<Application>/` mirrors it: see [coding-guidelines.md §10](coding-guidelines.md#where-tests-go).
