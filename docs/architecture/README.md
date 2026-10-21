# Architecture

How Norvia's estate is put together, for engineers and coding agents working on it and for the technical audience of a demo. Each page covers one topic. What a process does, step by step, is in its [process doc](../processes/tms/README.md); how it is built goes in that doc's Implementation section, which the processes are getting one by one. Terms are defined in [GLOSSARY.md](../../GLOSSARY.md), **Flow** included.

| Page | Covers |
|---|---|
| [c4.md](c4.md) | L1 system context: Norvia's systems, the parties outside Norvia and the people who use them. L2 containers: the applications, API Management, the Logic Apps, Service Bus, SQL, storage, Key Vault, Application Insights and Entra, and how they talk. L3 components of the integration layer: API Management's policies, the Logic Apps and their local functions, the subscriptions and queues, and the TMS APIs they call |
| [context-map.md](context-map.md) | The bounded contexts: what each application owns, what it holds only a copy of, and how they relate (customer and supplier, partnership, open host service), with the ADR behind each relationship |
| [tms-architecture.md](tms-architecture.md) | The TMS as a hexagon: Domain, Application with its use cases and ports, the adapters in Infrastructure, and what the architecture tests hold. The template for the other applications |
| [tms-lifecycles.md](tms-lifecycles.md) | The state diagrams of the TMS's aggregates (Transport Order, Shipment, Carrier booking, Trip, Exception, Shipper notification, planning day), each move with its domain method and guard |
| [erp-architecture.md](erp-architecture.md), [erp-lifecycles.md](erp-lifecycles.md) | The ERP's hexagon and its state diagrams: Billable line, Shipper invoice, Credit note, Carrier cost, Carrier invoice and its lines |
| [driver-app-architecture.md](driver-app-architecture.md) | The Driver app's hexagon: no HTTP call at all, reports published through its own outbox, and the state of a Driver report |
| [partner-portal-architecture.md](partner-portal-architecture.md) | The Partner portal's hexagon: Persona endpoints in the Web project, the Norvia Partner API as its only way in, and a Carrier booking request's states |
| [simulator-architecture.md](simulator-architecture.md) | The Simulator's hexagon: ports that lead to the estate's real doors, and a run's states |
| [integration-console-architecture.md](integration-console-architecture.md) | The Integration console's hexagon: no Domain and no database, with an Azure and a local adapter for each source |
| [events.md](events.md) | The event catalogue: every event with its topic, publisher, schema, subscriptions with their filters, subscribers and process. A test fails when it disagrees with the Canonical Model, the Bicep or the AppHost |
| [identity-and-access.md](identity-and-access.md) | Each managed identity and app registration: who it is in Entra, the app roles it holds, the APIs it may call, and the subscriptions, containers and secrets it may read |
| [failure-behaviour.md](failure-behaviour.md) | What happens when each hop of each Flow fails: its retry policy, dead-letter destination and `failureCode`s, the alert (or its absence), what the Integration console can resubmit or discard, and the as-found toggle that breaks it. Also the as-found backlog, since what is not built is marked *planned* |
| [business-automation-model.md](business-automation-model.md) | Which application performs each TMS and ERP process step, and whether a person, the application or an integration flow does it: processes as columns, applications as rows. The diagram is an [Excalidraw file](business-automation-model.excalidraw) in the [matrix style](diagrams/README.md) |
| [local-vs-azure.md](local-vs-azure.md) | What plays each Azure piece under Aspire, the ADR that decided it, and where the two can differ |

## Conventions

- Diagrams are Excalidraw files, generated from a spec and embedded as PNGs so they render on GitHub with no build step: the C4 diagrams in the graph style, and matrices of process steps against applications in the matrix style. Their styles are described in [diagrams/README.md](diagrams/README.md). Edit the spec and re-run it, never the file.
- Planned systems and pieces, designed in a process doc but not built yet, are drawn dashed and grey. Each diagram page has a legend.
- These pages are kept current in the same PR as the change they describe: see [coding guidelines §11](../coding-guidelines.md#11-workflow) for which changes.
