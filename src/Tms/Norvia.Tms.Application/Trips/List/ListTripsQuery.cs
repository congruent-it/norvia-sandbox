using Mediator;

namespace Norvia.Tms.Application.Trips.List;

/// <summary>Every Trip, by day, for the Planner's Trips view (P11).</summary>
public sealed record ListTripsQuery : IQuery<IReadOnlyList<TripView>>;
