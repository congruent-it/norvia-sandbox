# Estate health check

Part of the [applications](README.md). What it does and how to run it is in [operations/estate-health-check.md](../operations/estate-health-check.md).

## APIs

The Estate health check has **no API for its callers** and publishes no OpenAPI document. It is a Blazor UI the Integration operator signs in to (the roles `HealthCheck.Run` and `HealthCheck.Read`; a local operator under Aspire, Easy Auth in Azure from #392; see [identity-and-access.md](../architecture/identity-and-access.md)). Its only HTTP endpoints besides the UI's own assets are the `/health` check that Aspire calls and, while the Agents are on, the agent API below. Nothing else in the estate calls the app, so there are no other consumers to list.

- **The agent API, `/agent/v1`.** Mapped only while `Agents:Enabled` is on (off by default). A read-only view of the health check for the Estate health-check agent, behind a bearer token for the role `HealthCheck.Agent.Read` that no operator holds: `GET /agent/v1/checks` runs the checks and answers the Findings, each with its ID, the Check's ID, title, rule and severity, the evidence (subject and detail, masked), the as-found toggle and the fix. It answers an empty list when every Check passed. It has no write route, and it takes no input. Locally the MCP stand-in, `Norvia.Integration.LocalAgentMcp`, is its one caller, as the Agent's identity. It publishes no OpenAPI document of its own: its tool is listed in [health-check.tools.json](../../contracts/agent-mcp/health-check.tools.json) ([ADR 0040](../adr/0040-agents-read-the-estate-through-api-management-have-no-write-tools-and-their-own-identity.md)).
- **What it calls.** Locally it reads a snapshot of the estate's configuration embedded in the app, and calls no other application. In Azure it will read Azure Resource Graph and Azure Monitor as its own managed identity, which holds Reader on the estate's resource group (#392). It is a consumer of none of the other pages.

## Database

The Estate health check stores nothing: a result lives in the page session, so it has no database.
