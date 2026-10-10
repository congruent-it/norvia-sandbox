using Mediator;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.ReportDelivery;

public sealed class PublishTripNotDelivered(IOutbox outbox) : INotificationHandler<TripNotDeliveredEvent>
{
    public ValueTask Handle(TripNotDeliveredEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        outbox.Add(TripNotDeliveredV1.From(notification));
        return ValueTask.CompletedTask;
    }
}
