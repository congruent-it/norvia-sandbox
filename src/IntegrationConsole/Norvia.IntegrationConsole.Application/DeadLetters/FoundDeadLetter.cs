using Norvia.IntegrationConsole.Application.Trails;

namespace Norvia.IntegrationConsole.Application.DeadLetters;

/// <summary>A message still on a dead-letter queue that carries a Message trail's correlation ID.</summary>
/// <param name="Entity">The queue, or the subscription as <c>topic/subscription</c>.</param>
/// <param name="SequenceNumber">The broker's number for it, which names it within its dead-letter queue.</param>
/// <param name="EnqueuedAt">When it was dead-lettered.</param>
/// <param name="MessageId">Its Service Bus message ID, or null when unknown.</param>
public sealed record FoundDeadLetter(string Entity, long SequenceNumber, DateTimeOffset EnqueuedAt, IReadOnlyList<BusinessKey> BusinessKeys, string? MessageId = null);
