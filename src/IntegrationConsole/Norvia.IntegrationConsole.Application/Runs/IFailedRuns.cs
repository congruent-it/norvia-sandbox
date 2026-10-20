namespace Norvia.IntegrationConsole.Application.Runs;

/// <summary>
/// Where failed runs are listed as they are now. In Azure that is the estate's Logic Apps, read live as the console's own
/// identity (ADR 0017); locally, with no Logic Apps, the failures the Aspire dashboard holds (ADR 0027). It reads the
/// tracking contract's keys and a step's error only, never a message body, and it never resubmits anything.
/// </summary>
public interface IFailedRuns
{
    /// <summary>
    /// The runs that failed since <paramref name="since"/>, in no particular order. Each carries the flow and correlation
    /// ID it tracked, so the Message trail can be opened from it.
    /// </summary>
    Task<IReadOnlyList<RecordedFailedRun>> FailedRunsAsync(DateTimeOffset since, CancellationToken cancellationToken);
}
