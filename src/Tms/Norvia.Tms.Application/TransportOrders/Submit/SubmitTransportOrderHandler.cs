using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.Submit;

public sealed class SubmitTransportOrderHandler(
    IRepository<TransportOrder> repository,
    ITransportOrderNumbers numbers,
    IShipperDirectory shippers,
    TransportOrderRules rules,
    TimeProvider time)
    : ICommandHandler<SubmitTransportOrderCommand, Result<SubmitTransportOrderResult>>
{
    public async ValueTask<Result<SubmitTransportOrderResult>> Handle(SubmitTransportOrderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var shipperId = new ShipperId(command.TransportOrder.ShipperId);
        var details = command.TransportOrder.ToDetails();
        var correlationId = new CorrelationId(command.CorrelationId);
        var receivedAt = time.GetUtcNow();

        // A double click or a Logic App retry sends the same booking again: answer with what it got the first time.
        var referenceHolder = await FindReferenceHolderAsync(shipperId, details.ShipperReference, cancellationToken);
        if (referenceHolder is not null && referenceHolder.IsRepeatOf(details))
        {
            return SubmitTransportOrderResult.From(referenceHolder, repeat: true);
        }

        var reasons = rules.Check(shippers.Find(shipperId), details, receivedAt, referenceHolder);
        var order = reasons.Count > 0
            ? TransportOrder.Reject(shipperId, details, reasons, command.Channel, correlationId, receivedAt)
            : TransportOrder.Accept(shipperId, details, await numbers.NextAsync(cancellationToken), command.Channel, correlationId, receivedAt);

        await repository.AddAsync(order, cancellationToken);

        return SubmitTransportOrderResult.From(order);
    }

    private async Task<TransportOrder?> FindReferenceHolderAsync(ShipperId shipperId, string? shipperReference, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(shipperReference)
            ? null
            : await repository.SingleOrDefaultAsync(new TransportOrderByShipperReferenceSpec(shipperId, shipperReference), cancellationToken);
}
