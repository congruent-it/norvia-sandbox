namespace Norvia.Agents;

/// <summary>
/// What an Agent proposes a person do, as the host application shows it (ADR 0040): a cause, the evidence that backs it, a
/// suggested action and a confidence. The one result type every Agent uses. It is shown for the session and stored nowhere.
/// </summary>
/// <param name="SuggestedAction">Text only. Acting on it is the person's own command, under their own role.</param>
public sealed record Suggestion(string Cause, IReadOnlyList<Evidence> Evidence, string SuggestedAction, Confidence Confidence);

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
