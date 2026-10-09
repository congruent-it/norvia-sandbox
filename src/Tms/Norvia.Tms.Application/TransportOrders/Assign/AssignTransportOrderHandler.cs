using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.Fleet.Planning;
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
    LoadCheckWarnings loadWarnings,
    TimeProvider time)
    : ICommandHandler<AssignTransportOrderCommand, Result<AssignTransportOrderResult>>
{
    public async ValueTask<Result<AssignTransportOrderResult>> Handle(AssignTransportOrderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!TransportOrderNumber.TryParse(command.TransportOrderNumber, out var number)
            || await transportOrders.SingleOrDefaultAsync(new TransportOrderToPlanSpec(number), cancellationToken) is not { } order)
        {
            return AssignmentRefused.Because<AssignTransportOrderResult>(AssignmentRules.TransportOrderNotFound(command.TransportOrderNumber));
        }

        var key = new IdempotencyKey(command.AssignmentKey);
        if (order.WasPlannedUnder(key)
            && await shipments.SingleOrDefaultAsync(new ShipmentByNumberSpec(order.ShipmentNumber!), cancellationToken) is { } planned)
        {
            return await ResultOfAsync(planned, repeat: true, cancellationToken);
        }

        var carrierId = command.CarrierId is { } id ? new CarrierId(id) : null;
        if (AssignmentRules.Check(order, carrierId, carrierId is null ? null : carriers.Find(carrierId)) is { } refusal)
        {
            return AssignmentRefused.Because<AssignTransportOrderResult>(refusal);
        }

        var carriedBy = carrierId is null ? CarriedBy.OwnFleet : CarriedBy.Carrier(carrierId);
        var shipmentNumber = await shipmentNumbers.NextAsync(cancellationToken);
        var shipment = order.Plan(shipmentNumber, carriedBy, key, new CorrelationId(command.CorrelationId), time.GetUtcNow())
            ?? throw new InvalidOperationException($"Transport Order {number} passed the assignment rules but wasn't planned.");

        // Adding the Shipment saves the tracked Transport Order's new status with it, and ShipmentPlanned's outbox
        // message, in one transaction.
        await shipments.AddAsync(shipment, cancellationToken);
        return await ResultOfAsync(shipment, repeat: false, cancellationToken);
    }

    private async Task<AssignTransportOrderResult> ResultOfAsync(Shipment shipment, bool repeat, CancellationToken cancellationToken) => new(
        shipment.Number.Value,
        shipment.TransportOrderNumber.Value,
        shipment.Status,
        CarriedByView.Of(shipment.CarriedBy, carriers),
        shipment.ShipperId.Value,
        shipment.CorrelationId.Value,
        repeat,
        await loadWarnings.ForAsync(shipment, cancellationToken));
}
