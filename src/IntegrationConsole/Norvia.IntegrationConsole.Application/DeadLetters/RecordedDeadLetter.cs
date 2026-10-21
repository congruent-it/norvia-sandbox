using Norvia.IntegrationConsole.Application.Trails;

namespace Norvia.IntegrationConsole.Application.DeadLetters;

/// <summary>
/// A dead-lettered message as a peek shows it: the tracking contract's keys and the dead-letter reason, and no body.
/// </summary>
/// <param name="SequenceNumber">The broker's number for it, which names it within its dead-letter queue.</param>
public sealed record RecordedDeadLetter(
    long SequenceNumber,
    DateTimeOffset EnqueuedAt,
    string? Reason,
    string? Description,
    int DeliveryCount,
    string? CorrelationId,
    IReadOnlyList<BusinessKey> BusinessKeys);
