namespace Norvia.IntegrationConsole.Application.DeadLetters;

/// <summary>A queue's or subscription's dead-letter queue as it is now.</summary>
/// <param name="Name">A queue's name, or a subscription's as <c>topic/subscription</c>.</param>
/// <param name="Count">How many messages it holds.</param>
/// <param name="OldestEnqueuedAt">When its oldest message was enqueued, or null when it holds none.</param>
public sealed record RecordedDeadLetters(string Name, long Count, DateTimeOffset? OldestEnqueuedAt);
