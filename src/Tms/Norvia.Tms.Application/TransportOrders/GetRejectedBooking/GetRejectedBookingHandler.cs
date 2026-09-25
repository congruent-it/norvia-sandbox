using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.GetRejectedBooking;

public sealed class GetRejectedBookingHandler(IReadRepository<TransportOrder> repository, IShipperDirectory shippers)
    : IQueryHandler<GetRejectedBookingQuery, Result<TransportOrderView>>
{
    public async ValueTask<Result<TransportOrderView>> Handle(GetRejectedBookingQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var booking = await repository.FirstOrDefaultAsync(new RejectedBookingByIdSpec(new TransportOrderId(query.Id)), cancellationToken);
        return booking is null ? Result.NotFound() : TransportOrderView.Of(booking, shippers);
    }
}
