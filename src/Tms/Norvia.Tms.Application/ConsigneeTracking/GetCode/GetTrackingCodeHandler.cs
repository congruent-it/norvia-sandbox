using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.ConsigneeTracking.GetCode;

public sealed class GetTrackingCodeHandler(IReadRepository<TransportOrder> transportOrders)
    : IQueryHandler<GetTrackingCodeQuery, Result<TransportOrderTrackingCodeView>>
{
    public async ValueTask<Result<TransportOrderTrackingCodeView>> Handle(GetTrackingCodeQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!TransportOrderNumber.TryParse(query.TransportOrderNumber, out var number)
            || await transportOrders.FirstOrDefaultAsync(new TransportOrderByNumberSpec(number), cancellationToken) is not { } order)
        {
            return Result.NotFound();
        }

        return new TransportOrderTrackingCodeView(order.Number!.Value, order.TrackingCode?.Value);
    }
}
