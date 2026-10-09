using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.Fleet.Planning;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Shipments.Reassign;

public sealed class ReassignShipmentHandler(IRepository<Shipment> shipments, ICarrierDirectory carriers, LoadCheckWarnings loadWarnings, TimeProvider time)
    : ICommandHandler<ReassignShipmentCommand, Result<ReassignShipmentResult>>
{
    public async ValueTask<Result<ReassignShipmentResult>> Handle(ReassignShipmentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!ShipmentNumber.TryParse(command.ShipmentNumber, out var number)
            || await shipments.SingleOrDefaultAsync(new ShipmentToChangeSpec(number), cancellationToken) is not { } shipment)
        {
            return AssignmentRefused.Because<ReassignShipmentResult>(AssignmentRules.ShipmentNotFound(command.ShipmentNumber));
        }

        var key = new IdempotencyKey(command.ReassignmentKey);
        if (shipment.ReassignmentUnder(key) is var (earlierPrevious, earlier))
        {
            return await ResultOfAsync(shipment, earlierPrevious, earlier, repeat: true, cancellationToken);
        }

        var carrierId = command.CarrierId is { } id ? new CarrierId(id) : null;
        if (AssignmentRules.CheckReassignment(shipment, carrierId, carrierId is null ? null : carriers.Find(carrierId)) is { } refusal)
        {
            return AssignmentRefused.Because<ReassignShipmentResult>(refusal);
        }

        var previous = shipment.Assignments[^1];
        var carriedBy = carrierId is null ? CarriedBy.OwnFleet : CarriedBy.Carrier(carrierId);
        if (!shipment.Reassign(carriedBy, key, new CorrelationId(command.CorrelationId), time.GetUtcNow()))
        {
            throw new InvalidOperationException($"Shipment {number} passed the assignment rules but wasn't reassigned.");
        }

        // Saving the tracked Shipment stores ShipmentReassigned's outbox message with it, in one transaction.
        await shipments.SaveChangesAsync(cancellationToken);
        return await ResultOfAsync(shipment, previous, shipment.Assignments[^1], repeat: false, cancellationToken);
    }

    private async Task<ReassignShipmentResult> ResultOfAsync(Shipment shipment, ShipmentAssignment previous, ShipmentAssignment reassignment, bool repeat, CancellationToken cancellationToken) => new(
        shipment.Number.Value,
        shipment.TransportOrderNumber.Value,
        shipment.Status,
        CarriedByView.Of(reassignment.CarriedBy, carriers),
        CarriedByView.Of(previous.CarriedBy, carriers),
        shipment.ShipperId.Value,
        reassignment.CorrelationId.Value,
        repeat,
        await loadWarnings.ForAsync(shipment, cancellationToken));
}
