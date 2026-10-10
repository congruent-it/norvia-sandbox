using Mediator;

namespace Norvia.Tms.Application.Trips.List;

/// <summary>The Trips of today and later, by day, for the Planner's Trips view (P11).</summary>
public sealed record ListTripsQuery : IQuery<IReadOnlyList<TripView>>;
