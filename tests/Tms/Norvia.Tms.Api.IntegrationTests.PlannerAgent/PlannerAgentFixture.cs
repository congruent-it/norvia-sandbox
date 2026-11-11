using Norvia.Agents.Runner;
using Norvia.Agents.Scripts;
using Norvia.AppHost;

namespace Norvia.Tms.Api.IntegrationTests.PlannerAgent;

/// <summary>The TMS with the Agents switched on (<see cref="AgentMcpFixture"/>), with the Planner agent's tool served at <c>/mcp/planner</c>.</summary>
public sealed class PlannerAgentFixture : AgentMcpFixture
{
    public const string McpPath = "/mcp/planner";
    public const string NotificationMcpPath = "/mcp/planner-notification";

    /// <summary>A token as Entra issues it to the Planner agent's identity for the MCP server; change any part to show what is refused.</summary>
    public string AgentToken(string? agentId = null, string[]? roles = null, string? signingKey = null) =>
        IssueToken(roles ?? [LocalTokens.PlannerAgentReadRole], LocalTokens.PlannerAgentAudience, agentId ?? NewAgentId(), signingKey);

    /// <summary>A token as Entra issues it to the Customer service agent's identity, which is not for the Planner agent's server.</summary>
    public string CustomerServiceAgentToken() =>
        IssueToken([LocalTokens.CustomerServiceAgentReadRole], LocalTokens.CustomerServiceAgentAudience, NewAgentId());

    /// <summary>A token as Entra issues it to the Planner notification agent's identity for its MCP server.</summary>
    public string NotificationAgentToken(string? agentId = null, string[]? roles = null) =>
        IssueToken(roles ?? [LocalTokens.PlannerNotificationAgentReadRole], LocalTokens.PlannerNotificationAgentAudience, agentId ?? NewAgentId());

    /// <summary>The MCP endpoint of the Planner notification agent's server.</summary>
    public Uri NotificationMcpAddress => McpEndpoint(NotificationMcpPath);

    /// <summary>The MCP endpoint a runner connects to.</summary>
    public Uri McpAddress => McpEndpoint(McpPath);

    /// <summary>The Agent's run, as the TMS UI runs it in process: its definition, the scripted model and the stand-in's tool, with the Agent's token.</summary>
    public Task<AgentRunResult> SuggestAsync(string agentId, string transportOrderNumber, string correlationId, CancellationToken cancellationToken) =>
        SuggestAsync(agentId, transportOrderNumber, [], correlationId, cancellationToken);

    /// <summary>The Agent's run for one order of a plan of the waiting list (#445): the same question with the orders the plan has already suggested for Own fleet.</summary>
    public Task<AgentRunResult> SuggestAsync(
        string agentId, string transportOrderNumber, IReadOnlyList<string> alreadyAssumed, string correlationId, CancellationToken cancellationToken) =>
        PlannerAgentCalls.RunAsync(
            new AgentServer(McpAddress, AgentToken(agentId)),
            "planner.agent.yaml",
            PlannerScript.Model(),
            PlannerQuestions.SuggestAssignment(transportOrderNumber, alreadyAssumed),
            correlationId,
            cancellationToken);

    /// <summary>The Agent's draft of a note on the Exception, run as the TMS UI runs it: the note definition and its scripted model.</summary>
    public Task<AgentRunResult> DraftNoteAsync(string agentId, string exceptionNumber, string correlationId, CancellationToken cancellationToken) =>
        PlannerAgentCalls.RunAsync(
            new AgentServer(McpAddress, AgentToken(agentId)), "planner-exception-note.agent.yaml", PlannerExceptionNoteScript.Model(), PlannerQuestions.DraftExceptionNote(exceptionNumber), correlationId, cancellationToken);

    /// <summary>The Agent's draft of a Notify Shipper message, run as the TMS UI runs it: the notification definition and its scripted model, with the notification agent's token.</summary>
    public Task<AgentRunResult> DraftShipperNotificationAsync(
        string agentId, string exceptionNumber, string transportOrderNumber, string correlationId, CancellationToken cancellationToken) =>
        PlannerAgentCalls.RunAsync(
            new AgentServer(NotificationMcpAddress, NotificationAgentToken(agentId)),
            "planner-notification.agent.yaml",
            PlannerNotificationScript.Model(),
            PlannerQuestions.DraftShipperNotification(exceptionNumber, transportOrderNumber),
            correlationId,
            cancellationToken);
}
