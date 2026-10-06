using Mediator;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Exceptions;

namespace Norvia.Tms.Application.Exceptions.Open;

public sealed class PublishExceptionOpened(IOutbox outbox) : INotificationHandler<ExceptionOpenedEvent>
{
    public ValueTask Handle(ExceptionOpenedEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        outbox.Add(ExceptionOpenedV1.From(notification));
        return ValueTask.CompletedTask;
    }
}
