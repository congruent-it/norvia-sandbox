using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.ConsigneeNotifications;

/// <summary>
/// Gives the Consignee notification Logic App the address and tracking code of a Transport Order. A Transport Order that has no usable email or
/// no tracking code, or isn't there, is NotFound: there is nothing to send, and the Logic App treats that as done, not as an error.
/// </summary>
public sealed class GetConsigneeNotificationHandler(IReadRepository<TransportOrder> transportOrders)
    : IQueryHandler<GetConsigneeNotificationQuery, Result<ConsigneeNotificationContent>>
{
    public async ValueTask<Result<ConsigneeNotificationContent>> Handle(GetConsigneeNotificationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!TransportOrderNumber.TryParse(query.TransportOrderNumber, out var number)
            || await transportOrders.FirstOrDefaultAsync(new TransportOrderByNumberSpec(number), cancellationToken) is not { TrackingCode: { } code } order
            || order.Details.Delivery?.Consignee?.Email is not { } email
            || !ConsigneeEmail.IsValid(email))
        {
            return Result.NotFound();
        }

        return new ConsigneeNotificationContent(order.Number!.Value, email.Trim(), code.Value);
    }
}
