using Mediator;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.Start;

public sealed class PublishTripStarted(IOutbox outbox) : INotificationHandler<TripStartedEvent>
{
    public ValueTask Handle(TripStartedEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        outbox.Add(TripStartedV1.From(notification));
        return ValueTask.CompletedTask;
    }
}
