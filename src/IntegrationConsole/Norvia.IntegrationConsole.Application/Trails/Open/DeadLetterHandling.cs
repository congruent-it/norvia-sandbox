namespace Norvia.IntegrationConsole.Application.Trails.Open;

/// <summary>What became of the message a dead-lettered hop left on a dead-letter queue.</summary>
public enum DeadLetterStatus
{
    /// <summary>It is still on the dead-letter queue.</summary>
    OnQueue,
    Resubmitted,
    Discarded,
}

/// <summary>What the trail says about a dead-lettered hop's message, so two operators can tell it was already handled.</summary>
/// <param name="Entity">The queue or subscription the message was dead-lettered on.</param>
/// <param name="SequenceNumber">Names the message on its dead-letter queue while it is <see cref="DeadLetterStatus.OnQueue"/>, to link to it.</param>
/// <param name="Actor">Who resubmitted or discarded it.</param>
/// <param name="At">When they did.</param>
/// <param name="Reason">Why it was discarded.</param>
public sealed record DeadLetterHandling(DeadLetterStatus Status, string Entity, long? SequenceNumber, string? Actor, DateTimeOffset? At, string? Reason);
