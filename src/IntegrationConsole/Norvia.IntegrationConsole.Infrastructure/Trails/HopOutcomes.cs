using Norvia.IntegrationConsole.Application.Trails;

namespace Norvia.IntegrationConsole.Infrastructure.Trails;

/// <summary>What every <see cref="ITrailSource"/> adapter does alike with the outcomes a hop recorded.</summary>
internal static class HopOutcomes
{
    /// <summary>
    /// The worst outcome a hop recorded, with the failure code recorded alongside it: a hop that failed after it was
    /// refused failed, whatever it logged last. Outcomes outside the tracking contract are left out.
    /// </summary>
    public static (Outcome? Outcome, string? FailureCode) Worst(IEnumerable<(string? Outcome, string? FailureCode)> recorded) =>
        recorded.Select(record => (Outcome: record.Outcome is { } outcome ? FoundTrails.OutcomeOf(outcome) : null, record.FailureCode))
            .Where(record => record.Outcome is not null)
            .OrderByDescending(record => record.Outcome)
            .ThenByDescending(record => !string.IsNullOrEmpty(record.FailureCode))
            .Select(record => (record.Outcome, string.IsNullOrEmpty(record.FailureCode) ? null : record.FailureCode))
            .FirstOrDefault();
}
