namespace Norvia.IntegrationConsole.Application.DeadLetters.Messages;

/// <param name="Entity">The queue or subscription.</param>
/// <param name="Messages">The oldest first.</param>
/// <param name="HasMore">Whether the queue holds more than <paramref name="Messages"/> shows.</param>
public sealed record DeadLetteredMessageList(string Entity, IReadOnlyList<RecordedDeadLetter> Messages, bool HasMore);
