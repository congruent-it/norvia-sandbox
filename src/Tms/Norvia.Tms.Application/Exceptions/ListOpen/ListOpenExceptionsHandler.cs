using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.ShipperNotifications;

namespace Norvia.Tms.Application.Exceptions.ListOpen;

public sealed class ListOpenExceptionsHandler(
    IReadRepository<ExceptionCase> exceptions,
    IReadRepository<Shipment> shipments,
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
            .ToDictionary(group => group.Key, group => ShipperNotification.LastOf(group)?.Status);
        var shipmentsById = (await shipments.ListAsync(new ShipmentsByIdsSpec([.. open.Select(exception => exception.ShipmentId)]), cancellationToken))
            .ToDictionary(shipment => shipment.Id);
        // An Exception always has its Shipment, so a missing one is corrupt data, not something the Planner can fix.
        return [.. open.Select(exception => Summarize(exception, shipmentsById[exception.ShipmentId], lastStatusOf.GetValueOrDefault(exception.Id)))];
    }

    private OpenExceptionSummary Summarize(ExceptionCase exception, Shipment shipment, ShipperNotificationStatus? lastNotificationStatus) => new(
        exception.Number.Value,
        exception.ShipmentNumber.Value,
        ShipperView.Of(exception.ShipperId, shippers),
        CarriedByView.Of(exception.CarriedBy, carriers),
        exception.Type,
        exception.OpenedAt,
        shipment.AllowsCarrierBookingCancellation,
        shipment.AllowsDeliveryWindowAgreement,
        exception.Causes.Count,
        exception.AfterDelivered,
        exception.CauseCleared,
        [.. exception.Notes.Select(note => new ExceptionNoteSummary(note.Text, note.Planner, note.AddedAt, note.CorrelationId.Value))],
        lastNotificationStatus);
}
