# Agent MCP tool lists

The tool list of each API Management MCP server ([ADR 0040](../../docs/adr/0040-agents-read-the-estate-through-api-management-have-no-write-tools-and-their-own-identity.md)), one file per server: `<server>.tools.json`. The Bicep module [agent-mcp-server.bicep](../../infra/estate/agent-mcp-server.bicep) reads it to build the server, and the local MCP stand-in in `Norvia.Agents` ([ADR 0041](../../docs/adr/0041-locally-an-in-process-runner-and-an-mcp-stand-in-play-foundry-and-api-managements-mcp-server.md)) reads the same file, so the two cannot drift. A drift test fails when a server's tools in the Bicep differ from its file.

| Field | Meaning |
|---|---|
| `server` | The server's name, which is also the file's name. Lower-case letters, digits and hyphens |
| `role` | The one read role (an app role of the agent view's app registration) that an Agent's token must carry |
| `tools[].name` | The tool's name as the model sees it: `snake_case`, unique in the server |
| `tools[].description` | What the tool returns, for the model |
| `tools[].method` | `GET`. A tool is a read; any other method fails the contract test (ADR 0040: no write tools) |
| `tools[].path` | The operation's route on the application's agent view, starting with `/`. `{name}` segments become tool inputs |
| `tools[].mockResponse` | The example server only: a fixed JSON answer from API Management itself, for a server with no application behind it. Real servers leave it out |

The audience (the agent view's app registration) is not in the file: it differs per environment.
