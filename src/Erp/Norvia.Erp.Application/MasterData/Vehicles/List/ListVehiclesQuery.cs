using Mediator;

namespace Norvia.Erp.Application.MasterData.Vehicles.List;

/// <summary>The Own fleet's Vehicles in the ERP's master data.</summary>
public sealed record ListVehiclesQuery : IQuery<IReadOnlyList<VehicleView>>;
