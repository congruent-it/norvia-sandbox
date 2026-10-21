namespace Norvia.IntegrationConsole.Application.DeadLetters.Bulk;

/// <summary>How one message of a selection ended.</summary>
public enum MessageOutcomeStatus
{
    /// <summary>It was resubmitted or discarded, and audited.</summary>
    Done,

    /// <summary>It was already gone, because someone resubmitted or discarded it meanwhile.</summary>
    Gone,

    /// <summary>It was refused or failed and is still on the dead-letter queue.</summary>
    Failed,
}

/// <param name="SequenceNumber">The message within its dead-letter queue.</param>
/// <param name="Detail">The failure's reason, when it failed.</param>
public sealed record MessageOutcome(long SequenceNumber, MessageOutcomeStatus Status, string? Detail = null);

/// <summary>What happened to each message of a selection, in the order they were sent.</summary>
/// <param name="Entity">The queue or subscription the selection was on.</param>
public sealed record BulkOutcome(string Entity, IReadOnlyList<MessageOutcome> Messages);
