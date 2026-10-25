# Applications

What each application offers and keeps, for engineers and coding agents working on the estate and for the technical audience of a demo: its APIs with their consumers, and its database. One page per application. How the applications fit together is in [docs/architecture](../architecture/README.md); terms are defined in [GLOSSARY.md](../../GLOSSARY.md).

| Page | Database | OpenAPI |
|---|---|---|
| [tms.md](tms.md) | `tms` | [v1](openapi/tms.v1.json) |
| [erp.md](erp.md) | `erp` | [v1](openapi/erp.v1.json) |
| [partner-portal.md](partner-portal.md) | Partner portal | none published |
| [driver-app.md](driver-app.md) | `driver-app` | [v1](openapi/driver-app.v1.json) |
| [simulator.md](simulator.md) | `simulator` | none published |
| [integration-console.md](integration-console.md) | none: it stores nothing | none published |
| [estate-health-check.md](estate-health-check.md) | none: it stores nothing | none published |

## What a page holds

- **APIs**: for each endpoint that something other than the application's own browser calls: method and route, purpose, app role, request and response summary, and its **consumers**. A consumer is the calling component (Logic App, API Management policy, another application, the Simulator, an agent through MCP), the identity and app role it calls with, the process it serves and whether it goes through API Management or direct. Who holds which app role is in [identity-and-access.md](../architecture/identity-and-access.md); it is linked, not copied. The shapes come from the OpenAPI documents in [openapi/](openapi/).
- **Database**: every table with its columns (SQL type, nullability, default), primary key, foreign keys with their delete behaviour, unique constraints and indexes, and an ER diagram. A **Logical references** table lists the identifiers the application stores from another one, which the database doesn't enforce.

## What is generated

The database section of each page (between the `generated:database` markers), the model behind its ER diagram (`er-<slug>.json` in [docs/architecture/diagrams/](../architecture/diagrams/README.md#entity-relationships)) and every file in [openapi/](openapi/) are written by `Norvia.Docs.Generator` from the code. The ER picture itself is drawn from that JSON by `docs/architecture/diagrams/entities.py`, which you run after a schema change. The rest, including the table and column descriptions in [descriptions/](descriptions/), is written by hand.

```bash
dotnet run --project src/Docs/Norvia.Docs.Generator            # writes the generated parts
dotnet run --project src/Docs/Norvia.Docs.Generator -- --check # writes nothing, exits 1 when one is stale
```

Descriptions are `{ "tables": { "<Table>": { "description": "...", "columns": { "<Column>": "..." } } } }`.

## Conventions

- A PR that changes an endpoint or the schema regenerates these files in the same PR ([coding guidelines §11](../coding-guidelines.md#11-workflow)).
- Planned tables and endpoints are not listed: the pages describe what the code does.
