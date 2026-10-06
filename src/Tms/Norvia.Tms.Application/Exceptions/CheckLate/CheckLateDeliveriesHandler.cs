using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Exceptions.CheckLate;

public sealed class CheckLateDeliveriesHandler(
    IReadRepository<Shipment> shipments,
    IReadRepository<TransportOrder> transportOrders,
    IRepository<ExceptionCase> exceptions,
    ExceptionCauses exceptionCauses,
    LateDeliveryRules rules,
    TimeProvider time)
    : ICommandHandler<CheckLateDeliveriesCommand>
{
    public async ValueTask<Unit> Handle(CheckLateDeliveriesCommand command, CancellationToken cancellationToken)
    {
        var inProgress = await shipments.ListAsync(new ShipmentsInProgressSpec(), cancellationToken);
        if (inProgress.Count == 0)
        {
            return Unit.Value;
        }

        var orderNumbers = inProgress.Select(shipment => shipment.TransportOrderNumber).Distinct().ToList();
        var detailsByOrder = (await transportOrders.ListAsync(new TransportOrdersByNumbersSpec(orderNumbers), cancellationToken))
            .ToDictionary(order => order.Number!, order => order.Details);

        var now = time.GetUtcNow();
        foreach (var shipment in inProgress.Where(shipment => rules.HasWindowPassed(shipment.DeliveryWindowOf(detailsByOrder.GetValueOrDefault(shipment.TransportOrderNumber)), now)))
        {
            // The check has no message to carry a trail, so each Exception it opens gets a new one.
            var cause = ExceptionCause.WindowPassed(shipment, new CorrelationId(Guid.NewGuid().ToString()), now);
            await exceptionCauses.OpenUnlessOpenAsync(shipment, cause, cancellationToken);
        }

        await exceptions.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
