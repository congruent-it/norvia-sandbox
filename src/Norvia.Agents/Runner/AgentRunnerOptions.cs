namespace Norvia.Agents.Runner;

/// <summary>How long a run may take. A short timeout and no retry: an Agent is never on a critical path (ADR 0040).</summary>
public sealed record AgentRunnerOptions(TimeSpan Timeout);
