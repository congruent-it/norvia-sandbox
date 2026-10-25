namespace Norvia.Agents;

/// <summary>
/// What an Agent says about a list of facts its tools returned, as the host application shows it (ADR 0040): a summary and, for each
/// fact it chose to explain, the plain-language impact. Used by the Estate health-check agent, whose facts are Findings. Like a
/// <see cref="Suggestion"/> it is shown for the session and stored nowhere, and it can only point at facts a tool result holds.
/// </summary>
/// <param name="Summary">One or two sentences: what the run found overall.</param>
/// <param name="Items">The facts explained, in the order the Agent put them. Empty when there was nothing to explain.</param>
/// <param name="Run">The Agent run that produced it, set by the client that ran it.</param>
public sealed record Narration(string Summary, IReadOnlyList<NarratedItem> Items, AgentRun? Run = null);

/// <summary>One fact, explained.</summary>
/// <param name="Id">The ID of the fact in the tool result (a Finding's ID). It is never an ID the run did not read.</param>
/// <param name="Impact">What it means for the business, in plain text.</param>
/// <param name="ResultId">The run's ID for the result the fact came from, such as <c>r1</c>.</param>
/// <param name="ToolName">The tool that returned it.</param>
public sealed record NarratedItem(string Id, string Impact, string ResultId, string ToolName);
