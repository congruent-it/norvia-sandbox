namespace Norvia.Agents.Definitions;

/// <summary>
/// An Agent as its versioned definition file describes it: the instructions, the tool list and the model settings
/// (ADR 0040). The in-process runner reads it locally and <c>infra/deploy-agents.sh</c> publishes the same file as a Foundry
/// prompt agent, so the file stays plain YAML a shell script can read with <c>yq</c>.
/// </summary>
/// <param name="Name">The Agent's name, lower-case letters, digits and hyphens. It is also the file's name before <c>.agent.yaml</c>.</param>
/// <param name="Version">Raised whenever the instructions, the tools or the model settings change.</param>
/// <param name="Instructions">What the model is told. Says that tool results are delimited data and that every claim cites its result.</param>
public sealed record AgentDefinition(
    string Name,
    int Version,
    string Description,
    string Instructions,
    ModelSettings Model,
    ToolSelection Tools,
    RunInputs Inputs);

/// <summary>The model settings of an Agent.</summary>
/// <param name="Deployment">The name of the model deployment in the Foundry project.</param>
/// <param name="MaxToolCalls">The cap on tool calls per question (ADR 0040).</param>
public sealed record ModelSettings(string Deployment, double Temperature, int MaxOutputTokens, int MaxToolCalls);

/// <summary>The tools an Agent may call: some of the tools of one MCP server's <see cref="Tools.ToolListContract"/>.</summary>
/// <param name="Server">The MCP server, which is also the name of its contract file <c>contracts/agent-mcp/&lt;server&gt;.tools.json</c>.</param>
public sealed record ToolSelection(string Server, IReadOnlyList<string> Names);

/// <summary>The structured input that links an Agent run to its tool calls: Foundry sends it as a header on each tool call.</summary>
/// <param name="CorrelationIdHeader">The header the correlation ID travels in.</param>
public sealed record RunInputs(string CorrelationIdHeader);
