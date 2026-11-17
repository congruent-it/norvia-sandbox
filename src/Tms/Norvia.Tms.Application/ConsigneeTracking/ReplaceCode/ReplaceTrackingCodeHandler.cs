using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.ConsigneeTracking.ReplaceCode;

public sealed class ReplaceTrackingCodeHandler(IRepository<TransportOrder> transportOrders)
    : ICommandHandler<ReplaceTrackingCodeCommand, Result<TransportOrderTrackingCodeView>>
{
    public async ValueTask<Result<TransportOrderTrackingCodeView>> Handle(ReplaceTrackingCodeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!TransportOrderNumber.TryParse(command.TransportOrderNumber, out var number)
            || await transportOrders.FirstOrDefaultAsync(new TransportOrderToReplaceTrackingCodeSpec(number), cancellationToken) is not { } order
            || order.ReplaceTrackingCode() is not { } code)
        {
            return Result.NotFound();
        }

        await transportOrders.SaveChangesAsync(cancellationToken);
        return new TransportOrderTrackingCodeView(order.Number!.Value, code.Value);
    }
}
