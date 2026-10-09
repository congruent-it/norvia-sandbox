using Mediator;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet.ListVehicles;

public sealed class ListVehiclesHandler(IVehicleDirectory vehicles) : IQueryHandler<ListVehiclesQuery, IReadOnlyList<VehicleView>>
{
    public ValueTask<IReadOnlyList<VehicleView>> Handle(ListVehiclesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        IReadOnlyList<VehicleView> active =
        [
            .. vehicles.ListActive().Select(vehicle => new VehicleView(
                vehicle.Id.Value, vehicle.Registration, vehicle.Type, vehicle.PayloadKg, vehicle.PalletPlaces, LicenceRules.Needed(vehicle.Type))),
        ];
        return ValueTask.FromResult(active);
    }
}
