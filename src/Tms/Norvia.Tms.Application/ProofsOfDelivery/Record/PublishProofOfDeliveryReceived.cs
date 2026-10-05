using Mediator;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.ProofsOfDelivery;

namespace Norvia.Tms.Application.ProofsOfDelivery.Record;

public sealed class PublishProofOfDeliveryReceived(IOutbox outbox) : INotificationHandler<ProofOfDeliveryReceivedEvent>
{
    public ValueTask Handle(ProofOfDeliveryReceivedEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        outbox.Add(ProofOfDeliveryReceivedV1.From(notification));
        return ValueTask.CompletedTask;
    }
}
