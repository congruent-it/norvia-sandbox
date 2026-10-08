# Architecture

How Norvia's estate is put together, for engineers and coding agents working on it and for the technical audience of a demo. Each page covers one topic. What a process does, step by step, is in its [process doc](../processes/tms/README.md); how it is built goes in that doc's Implementation section, which the processes are getting one by one. Terms are defined in [CONTEXT.md](../../CONTEXT.md), **Flow** included.

| Page | Covers |
|---|---|
| [c4.md](c4.md) | L1 system context: Norvia's systems, the parties outside Norvia and the people who use them. L2 containers: the applications, API Management, the Logic Apps, Service Bus, SQL, storage, Key Vault, Application Insights and Entra, and how they talk. L3 components of the integration layer: API Management's policies, the Logic Apps and their local functions, the subscriptions and queues, and the TMS APIs they call |
| [local-vs-azure.md](local-vs-azure.md) | What plays each Azure piece under Aspire, the ADR that decided it, and where the two can differ |

## Conventions

- Diagrams are Mermaid flowcharts drawn in a C4 style, not Mermaid's experimental `C4Context`, so they render on GitHub with no build step.
- Planned systems and pieces, designed in a process doc but not built yet, are drawn dashed and grey. Each diagram page has a legend.
- These pages are kept current in the same PR as the change they describe: see [coding guidelines §11](../coding-guidelines.md#11-workflow) for which changes.
