using Mediator;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.TakeShipmentOff;

public sealed class PublishShipmentTakenOffTrip(IOutbox outbox) : INotificationHandler<ShipmentTakenOffTripEvent>
{
    public ValueTask Handle(ShipmentTakenOffTripEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        outbox.Add(ShipmentTakenOffTripV1.From(notification));
        return ValueTask.CompletedTask;
    }
}
