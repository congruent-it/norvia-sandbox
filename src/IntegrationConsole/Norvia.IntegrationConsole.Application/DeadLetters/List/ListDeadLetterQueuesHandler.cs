using Mediator;

namespace Norvia.IntegrationConsole.Application.DeadLetters.List;

public sealed class ListDeadLetterQueuesHandler(IDeadLetterQueues queues, TimeProvider time)
    : IQueryHandler<ListDeadLetterQueuesQuery, DeadLetterQueueList>
{
    public async ValueTask<DeadLetterQueueList> Handle(ListDeadLetterQueuesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var recorded = await queues.DeadLetterQueuesAsync(cancellationToken);
        var now = time.GetUtcNow();

        var listed = recorded
            .Select(queue => new DeadLetterEntity(queue.Name, queue.Count, queue.Count > 0 && queue.OldestEnqueuedAt is { } enqueued ? Age(now, enqueued) : null))
            .OrderByDescending(queue => queue.Count > 0)
            .ThenByDescending(queue => queue.OldestAge)
            .ThenBy(queue => queue.Name, StringComparer.Ordinal)
            .ToList();
        return new DeadLetterQueueList(now, listed);
    }

    // A clock a little behind the broker's would otherwise show a negative age.
    private static TimeSpan Age(DateTimeOffset now, DateTimeOffset enqueued) => now > enqueued ? now - enqueued : TimeSpan.Zero;
}
