using Mediator;

namespace Norvia.Tms.Application.Fleet.Planning.ListRosters;

/// <summary>The Rosters of the active Drivers, by name, for the Planner's Drivers tab.</summary>
public sealed record ListRostersQuery : IQuery<IReadOnlyList<RosterView>>;
