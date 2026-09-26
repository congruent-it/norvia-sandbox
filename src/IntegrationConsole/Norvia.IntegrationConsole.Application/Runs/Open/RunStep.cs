namespace Norvia.IntegrationConsole.Application.Runs.Open;

/// <summary>One step of a run on the run detail page, without its bodies.</summary>
/// <param name="Duration">How long it took, or null while it's still running or when it never ran.</param>
/// <param name="InputsHidden">Whether its input is behind secure inputs: it exists but isn't shown.</param>
/// <param name="OutputsHidden">Whether its output is behind secure outputs: it exists but isn't shown.</param>
public sealed record RunStep(
    string Name,
    string Status,
    DateTimeOffset? Start,
    TimeSpan? Duration,
    StepError? Error,
    bool InputsHidden,
    bool OutputsHidden)
{
    /// <summary>Whether the step ended without doing what it was for: failed, timed out, aborted or cancelled.</summary>
    public bool HasFailure => RunStatuses.IsFailure(Status);
}
