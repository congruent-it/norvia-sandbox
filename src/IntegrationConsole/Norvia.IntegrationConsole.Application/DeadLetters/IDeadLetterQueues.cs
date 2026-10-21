namespace Norvia.IntegrationConsole.Application.DeadLetters;

/// <summary>
/// Where dead-lettered messages are counted as they are now: every queue and subscription of the estate's Service Bus,
/// found by asking it, so a queue added later appears with no change to the console. In Azure that is read as the
/// console's own identity (ADR 0017); locally, the Service Bus emulators'. It counts and peeks, which takes nothing off a
/// dead-letter queue, and it never resubmits or discards anything.
/// </summary>
public interface IDeadLetterQueues
{
    /// <summary>Every queue and subscription with its dead-letter count, in no particular order.</summary>
    Task<IReadOnlyList<RecordedDeadLetters>> DeadLetterQueuesAsync(CancellationToken cancellationToken);
}
