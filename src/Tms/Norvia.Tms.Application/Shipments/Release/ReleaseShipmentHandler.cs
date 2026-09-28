using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.CarrierBookings;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Shipments.Release;

public sealed class ReleaseShipmentHandler(
    IRepository<Shipment> shipments,
    IRepository<TransportOrder> transportOrders,
    IRepository<CarrierBooking> carrierBookings,
    ICarrierBookingNumbers carrierBookingNumbers,
    ICarrierDirectory carriers,
    TimeProvider time)
    : ICommandHandler<ReleaseShipmentCommand, Result<ReleaseShipmentResult>>
{
    public async ValueTask<Result<ReleaseShipmentResult>> Handle(ReleaseShipmentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!ShipmentNumber.TryParse(command.ShipmentNumber, out var number)
            || await shipments.SingleOrDefaultAsync(new ShipmentToChangeSpec(number), cancellationToken) is not { } shipment)
        {
            return AssignmentRefused.Because<ReleaseShipmentResult>(AssignmentRules.ShipmentNotFound(command.ShipmentNumber));
        }

        var key = new IdempotencyKey(command.ReleaseKey);
        if (shipment.ReleaseUnder(key) is { } earlier)
        {
            return ResultOf(shipment, earlier, repeat: true);
        }

        if (AssignmentRules.CheckRelease(shipment) is { } refusal)
        {
            return AssignmentRefused.Because<ReleaseShipmentResult>(refusal);
        }

        // The release hands the Transport Order's details over with the Shipment, so ShipmentReleased carries them.
        var order = await transportOrders.SingleOrDefaultAsync(new TransportOrderByNumberSpec(shipment.TransportOrderNumber), cancellationToken)
            ?? throw new InvalidOperationException($"Shipment {number} carries out Transport Order {shipment.TransportOrderNumber}, which the TMS doesn't have.");

        // A Carrier Shipment gets its Carrier booking number before the release, so ShipmentReleased carries it (P4).
        var carrierBookingNumber = shipment.CarriedBy.IsOwnFleet ? null : await carrierBookingNumbers.NextAsync(cancellationToken);
        var release = shipment.Release(order, key, new CorrelationId(command.CorrelationId), time.GetUtcNow(), carrierBookingNumber)
            ?? throw new InvalidOperationException($"Shipment {number} passed the assignment rules but wasn't released.");

        if (carrierBookingNumber is not null)
        {
            var booking = CarrierBooking.Request(
                carrierBookingNumber, shipment.Id, shipment.Number, shipment.CarriedBy.CarrierId!, release.CorrelationId, release.ReleasedAt);

            // Adding the Carrier booking saves the tracked Shipment's release and ShipmentReleased's outbox message with it,
            // in one transaction (P4).
            await carrierBookings.AddAsync(booking, cancellationToken);
        }
        else
        {
            await shipments.SaveChangesAsync(cancellationToken);
        }

        return ResultOf(shipment, release, repeat: false);
    }

    private ReleaseShipmentResult ResultOf(Shipment shipment, ShipmentRelease release, bool repeat) => new(
        shipment.Number.Value,
        shipment.TransportOrderNumber.Value,
        shipment.Status,
        CarriedByView.Of(shipment.CarriedBy, carriers),
        shipment.ShipperId.Value,
        release.CorrelationId.Value,
        release.CarrierBookingNumber?.Value,
        repeat);
}
