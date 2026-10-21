using Ardalis.Result;

namespace Norvia.IntegrationConsole.Application.DeadLetters.Bulk;

internal static class SelectionRules
{
    /// <summary>
    /// The selection in original enqueue order, whatever order it was ticked in, or why it is refused. A Trip's dead-lettered
    /// Start Trip must go back before its Stop reports. Sequence number breaks a tie, and a duplicate is sent once.
    /// </summary>
    public static Result<(string Entity, IReadOnlyList<DeadLetterSelection> Ordered)> Order(IReadOnlyList<DeadLetterSelection>? messages)
    {
        if (messages is not { Count: > 0 })
        {
            return Result.Invalid(new ValidationError(nameof(messages), "Select at least one message."));
        }

        var entities = messages.Select(message => message.Entity?.Trim() ?? string.Empty).Distinct(StringComparer.Ordinal).ToList();
        if (entities.Count != 1 || entities[0].Length == 0)
        {
            return Result.Invalid(new ValidationError(nameof(messages), "A selection must be on one queue or subscription."));
        }

        var ordered = messages
            .DistinctBy(message => message.SequenceNumber)
            .OrderBy(message => message.EnqueuedAt)
            .ThenBy(message => message.SequenceNumber)
            .Select(message => message with { Entity = entities[0] })
            .ToList();
        return (entities[0], ordered);
    }
}
