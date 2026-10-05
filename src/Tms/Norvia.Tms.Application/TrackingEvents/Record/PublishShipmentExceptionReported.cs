using Mediator;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.TrackingEvents.Record;

public sealed class PublishShipmentExceptionReported(IOutbox outbox) : INotificationHandler<ShipmentExceptionReportedEvent>
{
    public ValueTask Handle(ShipmentExceptionReportedEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        outbox.Add(ShipmentExceptionReportedV1.From(notification));
        return ValueTask.CompletedTask;
    }
}
