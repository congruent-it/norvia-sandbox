using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.ShipperNotifications;

namespace Norvia.Tms.Application.Exceptions.ListOpen;

public sealed class ListOpenExceptionsHandler(
    IReadRepository<ExceptionCase> exceptions,
    IReadRepository<ShipperNotification> notifications,
    IShipperDirectory shippers,
    ICarrierDirectory carriers)
    : IQueryHandler<ListOpenExceptionsQuery, IReadOnlyList<OpenExceptionSummary>>
{
    public async ValueTask<IReadOnlyList<OpenExceptionSummary>> Handle(ListOpenExceptionsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var open = await exceptions.ListAsync(new OpenExceptionsSpec(), cancellationToken);
        var lastStatusOf = (await notifications.ListAsync(new ShipperNotificationsOfExceptionsSpec([.. open.Select(exception => exception.Id)]), cancellationToken))
            .GroupBy(notification => notification.ExceptionId)
            .ToDictionary(group => group.Key, group => group.MaxBy(notification => notification.RequestedAt)!.Status);
        return [.. open.Select(exception => new OpenExceptionSummary(
            exception.Number.Value,
            exception.ShipmentNumber.Value,
            ShipperView.Of(exception.ShipperId, shippers),
            CarriedByView.Of(exception.CarriedBy, carriers),
            exception.Type,
            exception.OpenedAt,
            exception.Causes.Count,
            exception.AfterDelivered,
            exception.CauseCleared,
            [.. exception.Notes.Select(note => new ExceptionNoteSummary(note.Text, note.Planner, note.AddedAt, note.CorrelationId.Value))],
            lastStatusOf.TryGetValue(exception.Id, out var status) ? status : null))];
    }
}
