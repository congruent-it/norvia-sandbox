using Norvia.IntegrationConsole.Application.Trails;

namespace Norvia.IntegrationConsole.Application.Runs.Open;

/// <summary>A Logic App run on the run detail page, as run history has it now.</summary>
/// <param name="IsKept">
/// Whether run history still keeps the run. Once it doesn't, the run has no status or steps, but its hop stays on the
/// Message trail, because Application Insights has its own retention.
/// </param>
/// <param name="Steps">Its trigger and actions, in the order they started.</param>
/// <param name="RunHistoryDays">How long run history keeps a run.</param>
public sealed record RunDetail(
    WorkflowRun Run,
    bool IsKept,
    string? Status,
    DateTimeOffset? Start,
    TimeSpan? Duration,
    IReadOnlyList<RunStep> Steps,
    int RunHistoryDays)
{
    /// <summary>Whether the run ended without doing what it was for: failed, timed out, aborted or cancelled.</summary>
    public bool HasFailure => RunStatuses.IsFailure(Status);
}
