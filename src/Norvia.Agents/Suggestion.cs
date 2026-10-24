namespace Norvia.Agents;

/// <summary>
/// What an Agent proposes a person do, as the host application shows it (ADR 0040): a cause, the evidence that backs it, a
/// suggested action and a confidence. The one result type every Agent uses. It is shown for the session and stored nowhere.
/// </summary>
/// <param name="SuggestedAction">What to do, in plain text. Acting on it is the person's own command, under their own role.</param>
/// <param name="Action">The kind of action it suggests. Resubmit and Discard let the host pre-select the message; nothing executes.</param>
/// <param name="Run">The Agent run that produced it, set by the client that ran it, for the audit when the person acts on it.</param>
public sealed record Suggestion(
    string Cause,
    IReadOnlyList<Evidence> Evidence,
    string SuggestedAction,
    Confidence Confidence,
    SuggestedActionKind Action = SuggestedActionKind.NeedsAPerson,
    AgentRun? Run = null);

/// <summary>The Agent and the run ID that produced a <see cref="Suggestion"/>.</summary>
public sealed record AgentRun(string AgentName, string RunId);

/// <summary>What a <see cref="Suggestion"/> suggests. Its wire names are <c>resubmit</c>, <c>discard</c>, <c>fix-the-cause-first</c> and <c>needs-a-person</c>.</summary>
public enum SuggestedActionKind
{
    Resubmit,
    Discard,
    FixTheCauseFirst,
    NeedsAPerson,
}

/// <summary>One claim and the tool result it came from.</summary>
/// <param name="ResultId">The run's ID for the result, such as <c>r1</c>.</param>
/// <param name="ToolName">The tool that returned it.</param>
public sealed record Evidence(string Claim, string ResultId, string ToolName);

public enum Confidence
{
    Low,
    Medium,
    High,
}
