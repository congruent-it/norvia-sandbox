namespace Norvia.Agents.Runner;

/// <summary>The stable failure codes of an Agent run (ADR 0040, docs/architecture/failure-behaviour.md).</summary>
public static class AgentFailureCodes
{
    /// <summary>Foundry or the model is down or too slow, or its reply isn't the expected result.</summary>
    public const string AgentUnavailable = "AgentUnavailable";

    /// <summary>A tool call was refused: no token, the wrong role, a rate limit, a tool the definition doesn't list, or the cap on tool calls.</summary>
    public const string AgentToolRefused = "AgentToolRefused";
}
