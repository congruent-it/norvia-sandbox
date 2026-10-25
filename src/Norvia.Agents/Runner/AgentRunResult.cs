namespace Norvia.Agents.Runner;

/// <summary>A finished Agent run.</summary>
/// <param name="ToolCalls">The tool calls the run made, in order, with the ID each result is cited by.</param>
public sealed record AgentRunResult(string RunId, Suggestion Suggestion, IReadOnlyList<ToolCallRecord> ToolCalls);

/// <summary>One tool call of a run.</summary>
public sealed record ToolCallRecord(string ResultId, string ToolName);

/// <summary>A finished Agent run that ended in a <see cref="Narration"/>.</summary>
public sealed record NarrationRunResult(string RunId, Narration Narration, IReadOnlyList<ToolCallRecord> ToolCalls);
