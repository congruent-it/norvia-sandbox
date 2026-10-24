namespace Norvia.Agents.Runner;

/// <summary>One question put to an Agent.</summary>
/// <param name="CorrelationId">The Message trail the question is about; it travels down to every tool call.</param>
/// <param name="RunId">The Agent run's ID, which the host's span records.</param>
public sealed record AgentQuestion(string Text, string CorrelationId, string RunId);
