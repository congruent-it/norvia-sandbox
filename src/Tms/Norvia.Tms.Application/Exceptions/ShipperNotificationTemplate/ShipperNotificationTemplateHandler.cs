using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.ShipperNotifications;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Exceptions.ShipperNotificationTemplate;

public sealed class ShipperNotificationTemplateHandler(IReadRepository<ExceptionCase> exceptions, IReadRepository<TransportOrder> transportOrders)
    : IQueryHandler<ShipperNotificationTemplateQuery, Result<ShipperNotificationTemplateResult>>
{
    public async ValueTask<Result<ShipperNotificationTemplateResult>> Handle(ShipperNotificationTemplateQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!ExceptionNumber.TryParse(query.ExceptionNumber, out var number)
            || await exceptions.SingleOrDefaultAsync(new ExceptionToResolveSpec(number), cancellationToken) is not { } exception)
        {
            return ExceptionRefused.Because<ShipperNotificationTemplateResult>(ExceptionRefusal.NotFound(query.ExceptionNumber));
        }

        var order = await transportOrders.SingleOrDefaultAsync(new TransportOrderByNumberSpec(exception.TransportOrderNumber), cancellationToken)
            ?? throw new InvalidOperationException($"Exception {exception.Number} has no Transport Order {exception.TransportOrderNumber}.");
        return new ShipperNotificationTemplateResult(exception.Number.Value, ShipperNotificationTemplates.For(exception.Type, order.ShipperReference));
    }
}
