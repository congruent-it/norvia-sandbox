using Norvia.Agents;

namespace Norvia.Tms.Api.Agents;

/// <summary>What the calls a person makes from an Agent's Suggestion share: the Agent and its run, which come together and only reach the trace (ADR 0040).</summary>
internal static class ActingOnCalls
{
    /// <summary>
    /// Adds an error under <paramref name="key"/> when the Agent's name and run ID don't come together, or are too long, or when the name is not
    /// one of <paramref name="knownAgents"/> (given by a call that only one Agent's Suggestion leads to), so a caller cannot claim another Agent for the trace.
    /// </summary>
    public static void Check(Dictionary<string, string[]> errors, string key, string? agentName, string? agentRunId, IReadOnlyCollection<string>? knownAgents = null)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (string.IsNullOrWhiteSpace(agentName) != string.IsNullOrWhiteSpace(agentRunId))
        {
            errors[key] = ["The Agent and its run ID come together."];
        }
        else if (agentName?.Length > 100 || agentRunId?.Length > 200)
        {
            errors[key] = ["The Agent's name is at most 100 characters and its run ID 200."];
        }
        else if (knownAgents is not null && !string.IsNullOrWhiteSpace(agentName) && !knownAgents.Contains(agentName.Trim()))
        {
            errors[key] = ["The Agent is not one that drafts for this action."];
        }
    }

    /// <summary>The Agent run the person acted on, or null when they acted on their own.</summary>
    public static AgentRun? Of(string? agentName, string? agentRunId) =>
        string.IsNullOrWhiteSpace(agentName) ? null : new AgentRun(agentName.Trim(), agentRunId!.Trim());
}
