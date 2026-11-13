using Ardalis.Result;
using Mediator;

namespace Norvia.HealthCheck.Application.Cost;

/// <summary>Read the estate's spend per resource over the last whole days, next to Transport Order volume, and how each changed against the period before.</summary>
public sealed record GetCostViewQuery : IQuery<Result<CostView>>;

/// <summary>What the spend page shows. Every number is the cost reader's, summed and compared by code; no model is involved.</summary>
/// <param name="PeriodDays">How many days each of the two compared periods spans.</param>
/// <param name="Days">The whole days read, oldest first, each marked provisional while Cost Management may still change it.</param>
/// <param name="Resources">Each resource's average daily spend in the two periods, the biggest riser first.</param>
/// <param name="Total">All resources together.</param>
/// <param name="Volume">Transport Orders per day, to read the spend against.</param>
public sealed record CostView(
    int PeriodDays,
    IReadOnlyList<CostDay> Days,
    IReadOnlyList<PeriodChange> Resources,
    PeriodChange Total,
    PeriodChange Volume)
{
    /// <summary>True while any day is provisional: the page says the latest days may still change.</summary>
    public bool HasProvisionalDays => Days.Any(day => day.Provisional);
}

/// <summary>A figure's daily average in the previous period and the current one.</summary>
/// <param name="Name">The resource, or "Total" / "Transport Orders".</param>
/// <param name="Previous">Average per day over the previous period's settled days, null when it had none.</param>
/// <param name="Current">Average per day over the current period's settled days, null when it had none.</param>
public sealed record PeriodChange(string Name, decimal? Previous, decimal? Current)
{
    /// <summary>The change relative to the previous period (0.25 is 25% more). Null when either period has no figure or the previous is zero.</summary>
    public decimal? Relative => Previous is > 0 && Current is { } current ? (current - Previous.Value) / Previous.Value : null;
}
