using Norvia.IntegrationConsole.Application.Trails;

namespace Norvia.IntegrationConsole.Infrastructure.Trails;

/// <summary>What every <see cref="ITrailSource"/> adapter does alike with the outcomes a hop recorded.</summary>
internal static class HopOutcomes
{
    /// <summary>
    /// The outcome a hop ended with, with the failure code recorded alongside it: a call that was retried and then got
    /// through succeeded. Outcomes outside the tracking contract are left out.
    /// </summary>
    public static (Outcome? Outcome, string? FailureCode) Last(IEnumerable<(DateTimeOffset? Time, string? Outcome, string? FailureCode)> recorded) =>
        recorded.Select(record => (record.Time, Outcome: record.Outcome is { } outcome ? FoundTrails.OutcomeOf(outcome) : null, record.FailureCode))
            .Where(record => record.Outcome is not null)
            .OrderByDescending(record => record.Time)
            .Select(record => (record.Outcome, string.IsNullOrEmpty(record.FailureCode) ? null : record.FailureCode))
            .FirstOrDefault();
}
