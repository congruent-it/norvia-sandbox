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
    /// <summary>Every resource that has spend on any day, by name.</summary>
    public IReadOnlyList<string> ResourceNames { get; } = [.. Days.SelectMany(day => day.Spend).Select(spend => spend.Resource).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>True while any day is provisional: the page says the latest days may still change.</summary>
    public bool HasProvisionalDays => Days.Any(day => day.Provisional);
}

/// <summary>A figure's daily average in the previous period and the current one.</summary>
/// <param name="Name">The resource's name as the estate has it, or the label of the total or the volume.</param>
/// <param name="Previous">Average per day over the previous period's settled days, null when it had none.</param>
/// <param name="Current">Average per day over the current period's settled days, null when it had none.</param>
public sealed record PeriodChange(string Name, decimal? Previous, decimal? Current, ChangeKind Kind = ChangeKind.Resource)
{
    /// <summary>The change relative to the previous period (0.25 is 25% more). Null when either period has no figure or the previous is zero.</summary>
    public decimal? Relative => Previous is > 0 && Current is { } current ? (current - Previous.Value) / Previous.Value : null;
}

/// <summary>What a <see cref="PeriodChange"/> measures, so a resource that happens to be called "Total" is never taken for the total.</summary>
public enum ChangeKind
{
    /// <summary>One resource's spend in EUR.</summary>
    Resource,

    /// <summary>All resources' spend in EUR.</summary>
    Total,

    /// <summary>Transport Orders received, a count.</summary>
    Volume,
}
