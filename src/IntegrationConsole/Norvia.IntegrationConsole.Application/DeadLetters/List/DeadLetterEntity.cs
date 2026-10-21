namespace Norvia.IntegrationConsole.Application.DeadLetters.List;

/// <param name="Name">A queue's name, or a subscription's as <c>topic/subscription</c>.</param>
/// <param name="Count">How many messages are dead-lettered.</param>
/// <param name="OldestAge">How long ago the oldest was enqueued, or null when there are none.</param>
public sealed record DeadLetterEntity(string Name, long Count, TimeSpan? OldestAge);
