using Mediator;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.Dispatch;

/// <summary>Publishes the one <c>TripDispatched</c> a Dispatch produces; there is no event per Shipment.</summary>
public sealed class PublishTripDispatched(IOutbox outbox) : INotificationHandler<TripDispatchedEvent>
{
    public ValueTask Handle(TripDispatchedEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        outbox.Add(TripDispatchedV1.From(notification));
        return ValueTask.CompletedTask;
    }
}
