using Norvia.HealthCheck.Application.Cost;

namespace Norvia.HealthCheck.Application.Estate;

/// <summary>
/// What the estate's bill and volume were over the latest whole days: the fact the cost Check reads. The adapter holds only settled days (a
/// <see cref="CostDay.Provisional"/> day may still change), so a Check never compares a number that is not final.
/// </summary>
/// <param name="Days">Settled whole days with their spend per resource and their Transport Order count, in any order.</param>
public sealed record CostFacts(IReadOnlyList<CostDay> Days)
{
    /// <summary>The days of each of the two periods the cost Check compares, and so half the days an adapter reads.</summary>
    public const int PeriodDays = 7;
}
