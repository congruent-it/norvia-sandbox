using Mediator;

namespace Norvia.Tms.Application.Fleet.ListDrivers;

/// <summary>The Own fleet's active Drivers, by name, for the Planner's Drivers tab.</summary>
public sealed record ListDriversQuery : IQuery<IReadOnlyList<DriverView>>;
