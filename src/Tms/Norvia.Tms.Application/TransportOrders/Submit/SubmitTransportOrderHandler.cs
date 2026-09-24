using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.Submit;

public sealed class SubmitTransportOrderHandler(
    IRepository<TransportOrder> repository,
    ITransportOrderNumbers numbers,
    TimeProvider time)
    : ICommandHandler<SubmitTransportOrderCommand, Result<SubmitTransportOrderResult>>
{
    public async ValueTask<Result<SubmitTransportOrderResult>> Handle(SubmitTransportOrderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var order = TransportOrder.Accept(
            new ShipperId(command.TransportOrder.ShipperId),
            command.TransportOrder.ToDetails(),
            await numbers.NextAsync(cancellationToken),
            new CorrelationId(command.CorrelationId),
            time.GetUtcNow());

        await repository.AddAsync(order, cancellationToken);

        return new SubmitTransportOrderResult(order.Number.Value, order.Status);
    }
}
