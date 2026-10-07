using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Billing.Release;

public sealed class ReleaseWithoutProofOfDeliveryHandler(
    IRepository<TransportOrder> transportOrders,
    BillingCheck billing,
    TimeProvider time)
    : ICommandHandler<ReleaseWithoutProofOfDeliveryCommand, Result<ReleaseWithoutProofOfDeliveryResult>>
{
    public async ValueTask<Result<ReleaseWithoutProofOfDeliveryResult>> Handle(
        ReleaseWithoutProofOfDeliveryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!TransportOrderNumber.TryParse(command.TransportOrderNumber, out var number)
            || await transportOrders.SingleOrDefaultAsync(new TransportOrderToBillSpec(number), cancellationToken) is not { } order)
        {
            return BillingRefused.Because<ReleaseWithoutProofOfDeliveryResult>(BillingRefusal.NotFound(command.TransportOrderNumber));
        }

        var correlationId = new CorrelationId(command.CorrelationId);
        var now = time.GetUtcNow();
        var shipment = await billing.FindShipmentAsync(order, cancellationToken);
        var deliveredAt = shipment is null ? null : await billing.DeliveredAtAsync(shipment, cancellationToken);
        if (order.ReleaseWithoutProofOfDelivery(deliveredAt, command.Remark, command.ReleasedBy, now) is { } refusal)
        {
            return BillingRefused.Because<ReleaseWithoutProofOfDeliveryResult>(refusal);
        }

        // The release lifts only NoProofOfDelivery: the check still wants the order Rated and its Shipment free of open Exceptions.
        await billing.CheckAsync(order, shipment, new BillingChange(correlationId, now, DeliveredAt: deliveredAt), cancellationToken);

        // Saving the tracked order stores TransportOrderBillable's outbox message, if the release completed it, with the release, in one transaction.
        await transportOrders.SaveChangesAsync(cancellationToken);
        return new ReleaseWithoutProofOfDeliveryResult(order.Number!.Value, order.ShipmentNumber!.Value, order.BillingState!.Value, command.CorrelationId);
    }
}
