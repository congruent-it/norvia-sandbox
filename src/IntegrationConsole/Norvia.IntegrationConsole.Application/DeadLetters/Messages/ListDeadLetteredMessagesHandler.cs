using Ardalis.Result;
using Mediator;

namespace Norvia.IntegrationConsole.Application.DeadLetters.Messages;

public sealed class ListDeadLetteredMessagesHandler(IDeadLetterQueues queues)
    : IQueryHandler<ListDeadLetteredMessagesQuery, Result<DeadLetteredMessageList>>
{
    /// <summary>How many messages one page lists: a peek returns at most this many at a time.</summary>
    public const int PageSize = 100;

    public async ValueTask<Result<DeadLetteredMessageList>> Handle(ListDeadLetteredMessagesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Entity?.Trim() is not { Length: > 0 } entity)
        {
            return Result.Invalid(new ValidationError(nameof(query.Entity), "Open a dead-letter queue by its queue or subscription name."));
        }

        // One more than the page, to know whether the queue holds more.
        if (await queues.PeekAsync(entity, PageSize + 1, cancellationToken) is not { } peeked)
        {
            return Result.NotFound();
        }

        var oldestFirst = peeked.OrderBy(message => message.EnqueuedAt).ThenBy(message => message.SequenceNumber).ToList();
        return new DeadLetteredMessageList(entity, oldestFirst.Take(PageSize).ToList(), oldestFirst.Count > PageSize);
    }
}
