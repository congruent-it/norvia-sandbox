using Mediator;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Exceptions;

namespace Norvia.Tms.Application.Exceptions.Resolve;

public sealed class PublishExceptionResolved(IOutbox outbox) : INotificationHandler<ExceptionResolvedEvent>
{
    public ValueTask Handle(ExceptionResolvedEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        outbox.Add(ExceptionResolvedV1.From(notification));
        return ValueTask.CompletedTask;
    }
}
