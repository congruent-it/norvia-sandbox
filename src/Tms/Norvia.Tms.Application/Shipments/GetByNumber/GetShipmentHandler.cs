using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.Shipments.GetByNumber;

public sealed class GetShipmentHandler(
    IReadRepository<Shipment> repository, IReadRepository<CarrierBooking> carrierBookings, IShipperDirectory shippers, ICarrierDirectory carriers)
    : IQueryHandler<GetShipmentQuery, Result<ShipmentView>>
{
    public async ValueTask<Result<ShipmentView>> Handle(GetShipmentQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!ShipmentNumber.TryParse(query.ShipmentNumber, out var number))
        {
            return Result.NotFound();
        }

        var shipment = await repository.FirstOrDefaultAsync(new ShipmentByNumberSpec(number), cancellationToken);
        if (shipment is null)
        {
            return Result.NotFound();
        }

        var bookings = await carrierBookings.ListAsync(new CarrierBookingsByShipmentSpec(shipment.Id), cancellationToken);
        return ShipmentView.Of(shipment, bookings, shippers, carriers);
    }
}
