namespace Norvia.IntegrationConsole.Application.DeadLetters.Bulk;

/// <summary>One message the operator ticked on the list.</summary>
/// <param name="Entity">The queue's name, or a subscription's as <c>topic/subscription</c>.</param>
/// <param name="SequenceNumber">The broker's number for the message within its dead-letter queue.</param>
/// <param name="EnqueuedAt">When it was first enqueued, which orders the selection.</param>
public sealed record DeadLetterSelection(string Entity, long SequenceNumber, DateTimeOffset EnqueuedAt);
