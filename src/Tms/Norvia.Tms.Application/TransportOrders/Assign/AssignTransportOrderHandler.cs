using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.Assign;

public sealed class AssignTransportOrderHandler(
    IRepository<TransportOrder> transportOrders,
    IRepository<Shipment> shipments,
    IShipmentNumbers shipmentNumbers,
    ICarrierDirectory carriers,
    TimeProvider time)
    : ICommandHandler<AssignTransportOrderCommand, Result<AssignTransportOrderResult>>
{
    public async ValueTask<Result<AssignTransportOrderResult>> Handle(AssignTransportOrderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!TransportOrderNumber.TryParse(command.TransportOrderNumber, out var number)
            || await transportOrders.SingleOrDefaultAsync(new TransportOrderToPlanSpec(number), cancellationToken) is not { } order)
        {
            return Result.NotFound();
        }

        var key = new IdempotencyKey(command.AssignmentKey);
        if (order.WasPlannedUnder(key)
            && await shipments.SingleOrDefaultAsync(new ShipmentByNumberSpec(order.ShipmentNumber!), cancellationToken) is { } planned)
        {
            return ResultOf(planned, repeat: true);
        }

        var carriedBy = command.CarrierId is { } carrierId ? CarriedBy.Carrier(new CarrierId(carrierId)) : CarriedBy.OwnFleet;
        var shipmentNumber = await shipmentNumbers.NextAsync(cancellationToken);
        if (order.Plan(shipmentNumber, carriedBy, key, new CorrelationId(command.CorrelationId), time.GetUtcNow()) is not { } shipment)
        {
            return Result.Conflict($"Transport Order {number} is {order.Status}; only an Accepted Transport Order can be planned.");
        }

        // Adding the Shipment saves the tracked Transport Order's new status with it, and ShipmentPlanned's outbox
        // message, in one transaction.
        await shipments.AddAsync(shipment, cancellationToken);
        return ResultOf(shipment, repeat: false);
    }

    private AssignTransportOrderResult ResultOf(Shipment shipment, bool repeat) => new(
        shipment.Number.Value,
        shipment.TransportOrderNumber.Value,
        shipment.Status,
        CarriedByView.Of(shipment.CarriedBy, carriers),
        shipment.ShipperId.Value,
        shipment.CorrelationId.Value,
        repeat);
}
