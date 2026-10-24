# Agent MCP servers

How an application's agent view is put behind an API Management MCP server for the Agents ([ADR 0040](../adr/0040-agents-read-the-estate-through-api-management-have-no-write-tools-and-their-own-identity.md), [ADR 0041](../adr/0041-locally-an-in-process-runner-and-an-mcp-stand-in-play-foundry-and-api-managements-mcp-server.md)). For engineers and coding agents adding an Agent. Terms are defined in [GLOSSARY.md](../../GLOSSARY.md).

## What the module makes

[infra/estate/agent-mcp-server.bicep](../../infra/estate/agent-mcp-server.bicep) takes a tool list and makes, in the estate's API Management:

| Resource | Notes |
|---|---|
| Agent view API `<server>-agent-view` | One GET operation per tool, at `/agent-views/<server>`. In no product and without a subscription key, so it checks the token itself on every operation (same audience, same role) |
| MCP server `<server>-mcp` | A REST API-backed MCP server over the agent view, one tool per operation, served at `<gateway>/agents/<server>/mcp`. The Agent's Entra token is its credential. Uses API version `2025-09-01-preview`, the first with MCP servers |
| The server's policy | [mcp-server.policy.xml](../../infra/estate/agent-mcp-server/mcp-server.policy.xml), filled in by the module |

| Parameter | Is |
|---|---|
| `apiManagementName`, `serverName`, `purpose` | The service, the server's name (lower-case letters, digits, hyphens) and what it is for |
| `tools` | The tool list, in the shape of the [contract file](../../contracts/agent-mcp/README.md) |
| `audience` | The agent view's app registration (client ID), the audience of the Agent's token |
| `requiredRole` | The one read role the token must carry |
| `rateLimitCalls`, `rateLimitPeriodSeconds` | Calls per Agent identity per window (60 a minute) |
| `backendId`, `backendAudience`, `apiManagementIdentityClientId` | The application's agent view as an API Management backend, the application's API app registration, and API Management's identity, which makes the tool calls. Empty only for the example, whose tools have a `mockResponse` |

## The policy

In this order, each refusal a trace on the Message trail (flow `agent-mcp-<server>`) and a JSON body with the correlation ID:

1. **Correlation ID.** API Management makes a new one for every call and ignores any the caller sends. It is returned as `X-Correlation-ID`, also on a refusal.
2. **Token.** `validate-azure-ad-token` for the audience. A missing token, another audience or an expired token is 401.
3. **Read role.** The `roles` claim must hold the role, else 403, failure code `AgentRoleMissing`. A token without `azp` is 403, `NoAgentIdentity`.
4. **Rate limit.** `rate-limit-by-key` on the token's `azp`, so each Agent identity has its own budget. Over it is 429.
5. **Tool list.** A `tools/call` whose tool is not in the list is 403, `ToolNotOnList`. The name is read from the MCP message, never from a header. The tool resources are the other half: the server serves no tool that is not in the list.
6. **Headers.** `X-Correlation-ID`, `X-Agent-ID` (the `azp`) and `X-Tool-Name` are set on the call to the agent view, overriding the caller's. The service diagnostics log them as headers on the backend request, as they log `X-Shipper-ID` for the Partner API, and the traces carry `agentId` and `toolName` ([tracking contract](../coding-guidelines.md#tracking-contract)).

The policy never reads a response body: MCP responses stream, and payload logging is off.

## Payload logging stays off

API Management MCP servers need response payload logging off at the service scope. The diagnostics in [partner-api.bicep](../../infra/estate/partner-api.bicep) log headers only, and `AgentMcpServerTests` fails if any API Management diagnostics resource in `infra/` gains a `body` setting. The module declares no diagnostics of its own.

## Add an agent API in four steps

1. **Write the tool list.** Add `contracts/agent-mcp/<server>.tools.json` (name, description, `GET`, path for each tool, and the read role). Tools are reads only; the contract test fails on any other method. The local MCP stand-in reads the same file.
2. **Register the agent view's identity.** In the application's Bicep, an app registration for the agent view with the role from the file, a service principal that requires assignment (see `agent-mcp-example.bicep`). Agents are given the role after each publish ([identity and access](../architecture/identity-and-access.md#agents-planned)).
3. **Call the module.** `tools: loadJsonContent('../../contracts/agent-mcp/<server>.tools.json').tools`, the `audience`, the `requiredRole`, and the `backendId`, `backendAudience` and identity of the application's agent view. Add the application's agent view as an API Management backend the way `tms-api` is in `partner-api.bicep`.
4. **Add the smoke run.** Copy `infra/smoke-test-agent-mcp-server.sh` for the server's first tool, and add the server to the C4 and the identity matrix.

## The example server

[agent-mcp-example.bicep](../../infra/estate/agent-mcp-example.bicep) deploys the server `example` ([example.tools.json](../../contracts/agent-mcp/example.tools.json)): one tool, `get_estate_info`, which API Management answers itself. It has three app registrations in the estate: the audience `Norvia example agent view` (role `AgentExample.Read`), the identity that holds the role, and one that does not. The module's tests build the tools of this file.

## Tests

- [AgentMcpServerTests](../../tests/Tms/Norvia.Tms.ArchitectureTests/AgentMcpServerTests.cs) evaluates the module with `bicep snapshot` and tests the policy a deployment would apply: valid token and audience, wrong role, rate limit, a tool not on the list, the headers and traces recorded, no response body use. It also checks the contract files' shape, that the Bicep reads them, and the logging precondition.
- The drift test, from the stand-in's list in `Norvia.Agents` to the contract file, comes with the stand-in.
- API Management runs only in Azure, so the policy is tested by reading it; the smoke run calls it.

## Smoke run

[infra/smoke-test-agent-mcp-server.sh](../../infra/smoke-test-agent-mcp-server.sh), after `azd up`, signed in as the presenter. It calls the example server's `tools/list` and its tool with a token for the example identity, and checks that a call without a token, a token for another API, an identity without the role and an unlisted tool are refused, and that the call is in Application Insights with `agentId` and `toolName`. The example identity has no secret, so the script adds one for an hour and removes it again.
