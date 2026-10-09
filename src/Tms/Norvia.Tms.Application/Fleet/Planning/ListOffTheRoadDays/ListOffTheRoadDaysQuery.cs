using Mediator;

namespace Norvia.Tms.Application.Fleet.Planning.ListOffTheRoadDays;

/// <summary>The Off the road days of today and later, earliest first, for the Planner's Vehicles tab.</summary>
public sealed record ListOffTheRoadDaysQuery : IQuery<IReadOnlyList<OffTheRoadDayView>>;
