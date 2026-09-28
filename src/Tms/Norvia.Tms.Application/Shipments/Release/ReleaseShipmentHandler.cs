using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Shipments.Release;

public sealed class ReleaseShipmentHandler(
    IRepository<Shipment> shipments,
    IReadRepository<TransportOrder> transportOrders,
    ICarrierDirectory carriers,
    TimeProvider time)
    : ICommandHandler<ReleaseShipmentCommand, Result<ReleaseShipmentResult>>
{
    public async ValueTask<Result<ReleaseShipmentResult>> Handle(ReleaseShipmentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!ShipmentNumber.TryParse(command.ShipmentNumber, out var number)
            || await shipments.SingleOrDefaultAsync(new ShipmentToReleaseSpec(number), cancellationToken) is not { } shipment)
        {
            return AssignmentRefused.Because<ReleaseShipmentResult>(AssignmentRules.ShipmentNotFound(command.ShipmentNumber));
        }

        var key = new IdempotencyKey(command.ReleaseKey);
        if (shipment.WasReleasedUnder(key))
        {
            return ResultOf(shipment, repeat: true);
        }

        if (AssignmentRules.CheckRelease(shipment) is { } refusal)
        {
            return AssignmentRefused.Because<ReleaseShipmentResult>(refusal);
        }

        // The release hands the Transport Order's details over with the Shipment, so ShipmentReleased carries them.
        var order = await transportOrders.SingleOrDefaultAsync(new TransportOrderByNumberSpec(shipment.TransportOrderNumber), cancellationToken)
            ?? throw new InvalidOperationException($"Shipment {number} carries out Transport Order {shipment.TransportOrderNumber}, which the TMS doesn't have.");
        if (!shipment.Release(order, key, new CorrelationId(command.CorrelationId), time.GetUtcNow()))
        {
            throw new InvalidOperationException($"Shipment {number} passed the assignment rules but wasn't released.");
        }

        // Saving the tracked Shipment stores ShipmentReleased's outbox message with it, in one transaction.
        await shipments.SaveChangesAsync(cancellationToken);
        return ResultOf(shipment, repeat: false);
    }

    private ReleaseShipmentResult ResultOf(Shipment shipment, bool repeat) => new(
        shipment.Number.Value,
        shipment.TransportOrderNumber.Value,
        shipment.Status,
        CarriedByView.Of(shipment.CarriedBy, carriers),
        shipment.ShipperId.Value,
        shipment.LastRelease!.CorrelationId.Value,
        repeat);
}
