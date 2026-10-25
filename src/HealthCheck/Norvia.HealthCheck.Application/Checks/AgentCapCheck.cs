using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.Checks;

/// <summary>Every agent has a cap on tool calls per question and a request-rate limit in API Management.</summary>
public sealed class AgentCapCheck : Check
{
    public const string CheckId = "AGENT-CAP";

    public override string Id => CheckId;

    public override string Rule => "Every agent has a cap on tool calls per question and a request-rate limit in API Management.";

    public override string Title => "An agent has no cap or rate limit";

    public override Severity Severity => Severity.Medium;

    public override string AsFoundToggle => "noAgentCapOrRateLimit";

    public override string Fix =>
        "Set noAgentCapOrRateLimit to false and provision the estate: the agent's MCP server then gets its rate limit and the app its cap on tool calls.";

    public override IReadOnlyList<Evidence> Examine(EstateConfiguration estate)
    {
        ArgumentNullException.ThrowIfNull(estate);

        return
        [
            .. (estate.Agents?.Identities ?? [])
                .Where(agent => !agent.HasCallCap || !agent.HasRateLimit)
                .OrderBy(agent => agent.Name, StringComparer.Ordinal)
                .Select(agent => new Evidence(agent.Name, Missing(agent))),
        ];
    }

    private static string Missing(AgentIdentity agent) =>
        (agent.HasCallCap, agent.HasRateLimit) switch
        {
            (false, false) => "It has no cap on tool calls per question and no request-rate limit in API Management.",
            (false, true) => "It has no cap on tool calls per question.",
            _ => "It has no request-rate limit in API Management.",
        };
}
