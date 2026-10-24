using Norvia.IntegrationConsole.Application.Trails;

namespace Norvia.IntegrationConsole.Application.DeadLetters;

/// <summary>
/// A dead-lettered message as a peek shows it: the tracking contract's keys, the dead-letter reason and the application
/// properties, and no body.
/// </summary>
/// <param name="SequenceNumber">The broker's number for it, which names it within its dead-letter queue.</param>
/// <param name="ApplicationProperties">The message's application properties as text, each value cut to <see cref="MaxPropertyLength"/>.</param>
public sealed record RecordedDeadLetter(
    long SequenceNumber,
    DateTimeOffset EnqueuedAt,
    string? Reason,
    string? Description,
    int DeliveryCount,
    string? CorrelationId,
    IReadOnlyList<BusinessKey> BusinessKeys,
    IReadOnlyDictionary<string, string>? ApplicationProperties = null)
{
    public const int MaxPropertyLength = 200;
}
