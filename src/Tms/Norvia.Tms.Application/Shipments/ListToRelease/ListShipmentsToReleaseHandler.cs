using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.Shipments.ListToRelease;

public sealed class ListShipmentsToReleaseHandler(IReadRepository<Shipment> repository, IShipperDirectory shippers, ICarrierDirectory carriers)
    : IQueryHandler<ListShipmentsToReleaseQuery, IReadOnlyList<ShipmentToReleaseSummary>>
{
    public async ValueTask<IReadOnlyList<ShipmentToReleaseSummary>> Handle(ListShipmentsToReleaseQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var shipments = await repository.ListAsync(new ShipmentsToReleaseSpec(), cancellationToken);

        return [.. shipments.Select(shipment => new ShipmentToReleaseSummary(
            shipment.Number.Value,
            shipment.TransportOrderNumber.Value,
            ShipperView.Of(shipment.ShipperId, shippers),
            CarriedByView.Of(shipment.CarriedBy, carriers),
            shipment.PlannedAt))];
    }
}
