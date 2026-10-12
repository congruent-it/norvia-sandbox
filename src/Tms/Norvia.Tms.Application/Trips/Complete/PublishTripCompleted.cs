using Mediator;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.Complete;

public sealed class PublishTripCompleted(IOutbox outbox) : INotificationHandler<TripCompletedEvent>
{
    public ValueTask Handle(TripCompletedEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        outbox.Add(TripCompletedV1.From(notification));
        return ValueTask.CompletedTask;
    }
}
