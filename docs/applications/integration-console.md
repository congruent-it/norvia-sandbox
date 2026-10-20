# Integration console

Part of the [applications](README.md).

## APIs

The Integration console has **no API of its own** and publishes no OpenAPI document. It is a Blazor UI the Integration operator signs in to (Easy Auth with the `IntegrationConsole.Read` role in Azure, a local operator under Aspire; see [identity-and-access.md](../architecture/identity-and-access.md) and [ADR 0026](../adr/0026-the-integration-console-signs-operators-in-with-easy-auth-and-checks-their-app-role.md)). Its only HTTP endpoint besides the UI's own assets is the `/health` check that Aspire and Container Apps call. Nothing in the estate calls the console, so there are no consumers to list.

- **Planned.** An agent API for the Integration operations agent, reached through API Management's MCP server with the role `IntegrationConsole.Agent.Read`, is drawn in the identity design but not built, so no endpoint is listed ([ADR 0040](../adr/0040-agents-read-the-estate-through-api-management-have-no-write-tools-and-their-own-identity.md)).
- **What it calls.** The console reads the estate: it searches Message trails in the Log Analytics workspace and opens Logic App runs through the management API, as its own managed identity and never with the operator's token. The one thing it changes is Service Bus: an operator can resubmit a dead-lettered message through its topic, aimed at the subscription it came from, or discard it ([ADR 0017](../adr/0017-the-integration-console-reads-the-estates-telemetry-and-stores-nothing.md)). Locally it searches the Aspire dashboard's telemetry instead ([ADR 0027](../adr/0027-locally-the-integration-console-searches-the-aspire-dashboards-telemetry.md)). It calls none of the applications' APIs (it reads telemetry and Service Bus, not them), so it is a consumer of none of the other pages.

## Database

The Integration console stores nothing: it reads the estate's telemetry ([ADR 0017](../adr/0017-the-integration-console-reads-the-estates-telemetry-and-stores-nothing.md)), so it has no database.
