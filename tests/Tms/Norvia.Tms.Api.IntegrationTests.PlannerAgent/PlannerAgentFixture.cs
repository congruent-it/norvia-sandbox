using Norvia.Agents.Runner;
using Norvia.Agents.Scripts;
using Norvia.AppHost;

namespace Norvia.Tms.Api.IntegrationTests.PlannerAgent;

/// <summary>The TMS with the Agents switched on (<see cref="AgentMcpFixture"/>), with the Planner agent's tool served at <c>/mcp/planner</c>.</summary>
public sealed class PlannerAgentFixture : AgentMcpFixture
{
    public const string McpPath = "/mcp/planner";

    /// <summary>A token as Entra issues it to the Planner agent's identity for the MCP server; change any part to show what is refused.</summary>
    public string AgentToken(string? agentId = null, string[]? roles = null, string? signingKey = null) =>
        IssueToken(roles ?? [LocalTokens.PlannerAgentReadRole], LocalTokens.PlannerAgentAudience, agentId ?? NewAgentId(), signingKey);

    /// <summary>A token as Entra issues it to the Customer service agent's identity, which is not for the Planner agent's server.</summary>
    public string CustomerServiceAgentToken() =>
        IssueToken([LocalTokens.CustomerServiceAgentReadRole], LocalTokens.CustomerServiceAgentAudience, NewAgentId());

    /// <summary>The MCP endpoint a runner connects to.</summary>
    public Uri McpAddress => McpEndpoint(McpPath);

    /// <summary>The Agent's run, as the TMS UI runs it in process: its definition, the scripted model and the stand-in's tool, with the Agent's token.</summary>
    public Task<AgentRunResult> SuggestAsync(string agentId, string transportOrderNumber, string correlationId, CancellationToken cancellationToken) =>
        PlannerAgentCalls.RunAsync(
            this, agentId, "planner.agent.yaml", PlannerScript.Model(), PlannerQuestions.SuggestAssignment(transportOrderNumber), correlationId, cancellationToken);

    /// <summary>The Agent's draft of a note on the Exception, run as the TMS UI runs it: the note definition and its scripted model.</summary>
    public Task<AgentRunResult> DraftNoteAsync(string agentId, string exceptionNumber, string correlationId, CancellationToken cancellationToken) =>
        PlannerAgentCalls.RunAsync(
            this, agentId, "planner-exception-note.agent.yaml", PlannerExceptionNoteScript.Model(), PlannerQuestions.DraftExceptionNote(exceptionNumber), correlationId, cancellationToken);
}
