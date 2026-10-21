namespace Norvia.IntegrationConsole.Application.DeadLetters;

/// <summary>
/// Where dead-lettered messages are counted as they are now: every queue and subscription of the estate's Service Bus,
/// found by asking it, so a queue added later appears with no change to the console. In Azure that is read as the
/// console's own identity (ADR 0017); locally, the Service Bus emulators'. It counts and peeks, which takes nothing off a
/// dead-letter queue, and it never receives, resubmits or discards anything: a looked-at message keeps its delivery count
/// and lock state.
/// </summary>
public interface IDeadLetterQueues
{
    /// <summary>Every queue and subscription with its dead-letter count, in no particular order.</summary>
    Task<IReadOnlyList<RecordedDeadLetters>> DeadLetterQueuesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Peeks up to <paramref name="maxMessages"/> messages of a dead-letter queue, the one dead-lettered first first, without
    /// their bodies. Null when the estate has no such queue or subscription.
    /// </summary>
    /// <param name="entity">A queue's name, or a subscription's as <c>topic/subscription</c>.</param>
    Task<IReadOnlyList<RecordedDeadLetter>?> PeekAsync(string entity, int maxMessages, CancellationToken cancellationToken);

    /// <summary>
    /// Peeks one message of a dead-letter queue by its sequence number, with its body. Null when the queue or the message is
    /// gone, because someone resubmitted or discarded it meanwhile.
    /// </summary>
    Task<RecordedDeadLetterBody?> OpenAsync(string entity, long sequenceNumber, CancellationToken cancellationToken);
}
