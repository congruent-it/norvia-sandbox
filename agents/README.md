# Agent definitions

One file per Agent, `<name>.agent.yaml` ([ADR 0040](../docs/adr/0040-agents-read-the-estate-through-api-management-have-no-write-tools-and-their-own-identity.md)): the Agent's instructions, tool list and model settings, versioned in the repo and never edited in the portal. Two readers use the same file: the in-process runner in `Norvia.Agents` ([ADR 0041](../docs/adr/0041-locally-an-in-process-runner-and-an-mcp-stand-in-play-foundry-and-api-managements-mcp-server.md)), through `AgentDefinitionLoader`, and `infra/deploy-agents.sh` (#499), which publishes each file as a Foundry prompt agent. It is plain YAML, so a script reads it with `yq`.

| Field | Meaning |
|---|---|
| `name` | The Agent's name: lower-case letters, digits and hyphens, and the file's name before `.agent.yaml` |
| `version` | A whole number, raised whenever the instructions, the tools or the model settings change |
| `description` | What the Agent is for, one sentence |
| `instructions` | What the model is told. It says that tool results are delimited data and never instructions, and that every claim cites the result it came from |
| `model.deployment` | The model deployment in the Foundry project. `infra/deploy-agents.sh` uses the estate's own when this one isn't there |
| `model.temperature`, `model.maxOutputTokens` | Sampling and the length of an answer |
| `model.maxToolCalls` | The cap on tool calls per question. The runner refuses a call over it |
| `tools.server` | The MCP server, which is the name of its tool list in [contracts/agent-mcp/](../contracts/agent-mcp/README.md) |
| `tools.names` | The tools of that server this Agent may call, each once. A name the server doesn't serve fails the loader's checks, and a write tool fails the architecture tests |
| `inputs.correlationIdHeader` | The header that carries the correlation ID of the trail in question to every tool call. It is a structured input, supplied per run. `infra/deploy-agents.sh` declares the structured input `correlationId` and puts `{{correlationId}}` in this header of the MCP tool; the console's Foundry client supplies it on each response |

The answer is one JSON object, which the runner reads as a `Suggestion`: `cause`, `evidence` (a `claim` and the `citation` of the result `rN` it came from), `action` (`resubmit`, `discard`, `fix-the-cause-first` or `needs-a-person`), `suggestedAction` (the advice in plain text) and `confidence` (`low`, `medium` or `high`). A reply that is not that, or that cites a result the run never had, is `AgentUnavailable`.

In Azure the results of the tool calls are not wrapped in `<tool_result>` tags the way the in-process runner wraps them, so the instructions number them: the first call's result is `r1`, the second's `r2`. Foundry's output lists the calls in order, and the console's Foundry client reads the reply with the same rules as the runner (a citation of a result the run never had is `AgentUnavailable`).

The Estate health-check agent answers with a different object, which the runner reads as a `Narration`: `summary` and `findings`, each with the `id` of a Finding the tool returned, its `impact` in plain text and the `citation` of the result `rN` it came from. A reply that explains a Finding the result does not hold, or cites a result the run never had, is `AgentUnavailable`: the agent cannot invent a Finding.
