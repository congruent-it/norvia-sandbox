# Architecture

How Norvia's estate is put together, for engineers and coding agents working on it and for the technical audience of a demo. Each page covers one topic. What a process does, step by step, is in its [process doc](../processes/tms/README.md); how a process is built is in that doc's Implementation section. Terms are defined in [CONTEXT.md](../../CONTEXT.md), **Flow** included.

| Page | Covers |
|---|---|
| [c4.md](c4.md) | L1 system context: Norvia's systems, the parties outside Norvia and the people who use them. L2 containers: the applications, API Management, the Logic Apps, Service Bus, SQL, storage, Key Vault, Application Insights and Entra, and how they talk |

## Conventions

- Diagrams are Mermaid flowcharts drawn in a C4 style, not Mermaid's experimental `C4Context`, so they render on GitHub with no build step.
- Planned systems and pieces, designed in a process doc but not built yet, are drawn dashed and grey. Each diagram page has a legend.
- A pull request that adds or changes an event, a subscription, a Flow, a Logic App, an app role or a local stand-in updates the matching page here in the same PR ([coding guidelines §11](../coding-guidelines.md#11-workflow)).
