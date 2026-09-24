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

        var reasons = rules.Check(shippers.Find(shipperId), details, receivedAt);
        var order = reasons.Count > 0
            ? TransportOrder.Reject(shipperId, details, reasons, correlationId, receivedAt)
            : TransportOrder.Accept(shipperId, details, await numbers.NextAsync(cancellationToken), correlationId, receivedAt);

        await repository.AddAsync(order, cancellationToken);

        return new SubmitTransportOrderResult(order.Id.Value, order.Status, order.Number?.Value, RejectionReasonView.From(order.RejectionReasons));
    }
}
