# Locally, an in-process runner and an MCP stand-in play Foundry and API Management's MCP server

**Status: Accepted** with [ADR 0040](0040-agents-read-the-estate-through-api-management-have-no-write-tools-and-their-own-identity.md), now that the first two slices of the Integration operations agent work end to end.

Under Aspire there is no API Management and no Foundry, and Foundry is a cloud service: it can't call an MCP server on a developer's machine. The stand-ins of [ADR 0023](0023-locally-a-stand-in-plays-the-norvia-partner-api.md) replace Azure pieces that the code calls. Here the piece that calls the tools, the Foundry agent loop, is itself in the cloud. So the agent loop has to run in the application locally.

- **One definition, two runners.** An Agent's definition (instructions, tool list, model settings) is a versioned file in the repo. In Azure it is deployed as a Foundry prompt agent that calls API Management's MCP server with its agent identity. Locally an **in-process runner** (`Microsoft.Extensions.AI` with an MCP client), in `Norvia.Agents`, runs the same definition against a local MCP stand-in. The application's `IAgentClient` port hides which one runs.
- **The MCP stand-in** is a small app or route group beside `Norvia.Integration.LocalPartnerApi`, started by the AppHost. It serves `/mcp` and makes the same checks in the same order as the API Management policy: the Agent's token (signed with `local-token-signing-key`, as the other local tokens are), its read role, the rate limit, and then it calls the application's agent view as the Agent's identity and carries the correlation ID. It has one tool server per Agent, as API Management has one MCP server per Agent.
- **A drift test.** The stand-in's tool list must equal the tool list in the API Management Bicep. It does what the policy tests do for the Partner API's policies, and it is the one thing that keeps the second implementation honest.
- **The model is a scripted fake by default.** A fake `IChatClient` in `Norvia.Agents` replays fixed tool calls and a fixed answer for the demo scenarios, so a local run and CI are deterministic and need no Azure. A setting can point the runner at a developer's own Foundry model deployment for a real local demo. `Agents:Enabled` is off locally unless the AppHost turns it on.
- **What local cannot show.** The Agent's Entra agent identity, the Foundry traces and the real answers of a real model exist only in Azure. Locally the Message trail shows the tool calls, and the answer is scripted unless a model is configured. The Estate health check reads snapshots locally, as its own docs say.
- **Same code in the application.** Only the `IAgentClient` implementation and the endpoints differ, as only endpoints and credentials differ for the Simulator ([ADR 0039](0039-the-simulator-books-only-through-the-estates-real-doors.md)).

## Considered Options

- **Run the agent loop in the application in Azure too (ephemeral agents):** one runner and no drift, but the application's own identity makes the tool calls, so there is no per-Agent identity, no Foundry publishing and no registry. That drops the main reason for Foundry and the identity decision of ADR 0040.
- **Foundry agents everywhere, with a dev tunnel to the developer's machine:** the closest to Azure, but every local run and every CI run would need Azure and a tunnel, which ADR 0023 rejected for the same reason.
- **Skip the local story and run Agents in Azure only:** least code, but no side-by-side local demo, no Playwright journey in CI and no way to test the wiring before a deployment.

## Consequences

- The runner is a second implementation of the Foundry agent loop, and the stand-in is a second implementation of the API Management MCP policy. A change to an Agent's tools, an operation's checks or a role has to be made in both. The drift test and the stand-in's own tests check the same list, but they cannot run the Azure side: the post-deploy smoke run of each Agent does.
- A local demo of an Agent shows the wiring and the scripted answer. A real answer needs a model, either a developer's own deployment or Azure.
- The stand-in runs only locally. `azd` never deploys it; in Azure the application's agent client is given the Foundry project's address instead.
- The scripted fake has to be kept in step with each definition's tools, or its replays will call tools the definition no longer has. The runner refuses a tool call the definition doesn't list, which makes that a failing test.
