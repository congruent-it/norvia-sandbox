namespace Norvia.Tms.Application.Trips.GetByNumber;

/// <param name="Failures">The rules the Trip breaks as it stands now, such as a Vehicle that has since gone Off the road or a load that no longer fits.</param>
/// <param name="Warnings">What the Planner should know, which doesn't stop the Trip.</param>
public sealed record TripDetailView(TripView Trip, IReadOnlyList<TripStopView> Stops, IReadOnlyList<TripRuleFailureView> Failures, IReadOnlyList<TripWarning> Warnings)
{
    /// <summary>Every version the Trip has been sent as, oldest first, each with whether the Driver app has it.</summary>
    public IReadOnlyList<TripVersionView> Versions { get; init; } = [];
}
