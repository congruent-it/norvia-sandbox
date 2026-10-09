using Mediator;

namespace Norvia.Tms.Application.Fleet.ListVehicles;

/// <summary>The Own fleet's active Vehicles, by registration, for the Planner's Vehicles tab.</summary>
public sealed record ListVehiclesQuery : IQuery<IReadOnlyList<VehicleView>>;
