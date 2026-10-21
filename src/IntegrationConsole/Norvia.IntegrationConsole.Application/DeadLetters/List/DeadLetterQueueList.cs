namespace Norvia.IntegrationConsole.Application.DeadLetters.List;

/// <summary>The dead-letter queues as they were at <paramref name="At"/>.</summary>
/// <param name="Queues">Those with dead-lettered messages first, the oldest message first among them, then the rest by name.</param>
public sealed record DeadLetterQueueList(DateTimeOffset At, IReadOnlyList<DeadLetterEntity> Queues);
