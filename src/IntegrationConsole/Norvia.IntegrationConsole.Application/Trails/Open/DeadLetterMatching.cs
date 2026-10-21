using Norvia.IntegrationConsole.Application.Audit;
using Norvia.IntegrationConsole.Application.DeadLetters;

namespace Norvia.IntegrationConsole.Application.Trails.Open;

/// <summary>
/// Pairs each dead-lettered hop with its message: one still on a dead-letter queue, or one the console's audit says was
/// resubmitted or discarded. A hop that recorded the message ID of what it dead-lettered is joined to that message
/// exactly. A hop that didn't, such as one of a flow that doesn't record it yet, gets the earliest of the messages left
/// that could have been its: one that came after the hop and carried the same business keys, or fewer. A message that
/// was resubmitted and failed again is a message of its own, so it takes the later hop.
/// </summary>
internal static class DeadLetterMatching
{
    // The telemetry and the broker keep their own clocks.
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(1);

    public static Dictionary<string, DeadLetterHandling> Of(
        IReadOnlyList<RecordedHop> hops, IReadOnlyList<RecordedAudit> audits, IReadOnlyList<FoundDeadLetter> onQueue)
    {
        var candidates = audits
            .Where(audit => audit.Action is AuditActions.Resubmit or AuditActions.Discard)
            .Select(audit => (Time: audit.At, Id: (string?)audit.MessageId, Keys: audit.BusinessKeys, Handling: new DeadLetterHandling(
                audit.Action == AuditActions.Resubmit ? DeadLetterStatus.Resubmitted : DeadLetterStatus.Discarded,
                audit.Entity, null, audit.Actor, audit.At, audit.Reason)))
            .Concat(onQueue.Select(message => (Time: message.EnqueuedAt, Id: message.MessageId, Keys: message.BusinessKeys, Handling: new DeadLetterHandling(
                DeadLetterStatus.OnQueue, message.Entity, message.SequenceNumber, null, null, null))))
            .OrderBy(candidate => candidate.Time)
            .ToList();

        var matched = new Dictionary<string, DeadLetterHandling>(StringComparer.Ordinal);
        var deadLettered = hops.Where(hop => hop.Outcome == Outcome.DeadLettered).OrderBy(hop => hop.Time).ThenBy(hop => hop.Id, StringComparer.Ordinal).ToList();

        // A hop that names its message takes exactly that one, or none when the message is gone from both sources.
        foreach (var hop in deadLettered.Where(hop => !string.IsNullOrEmpty(hop.MessageId)))
        {
            var index = candidates.FindIndex(candidate => string.Equals(candidate.Id, hop.MessageId, StringComparison.Ordinal));
            if (index >= 0)
            {
                matched[hop.Id] = candidates[index].Handling;
                candidates.RemoveAt(index);
            }
        }

        foreach (var hop in deadLettered.Where(hop => string.IsNullOrEmpty(hop.MessageId)))
        {
            var index = candidates.FindIndex(candidate => candidate.Time >= hop.Time - ClockSkew && Compatible(hop.BusinessKeys, candidate.Keys));
            if (index >= 0)
            {
                matched[hop.Id] = candidates[index].Handling;
                candidates.RemoveAt(index);
            }
        }

        return matched;
    }

    private static bool Compatible(IReadOnlyList<BusinessKey> hop, IReadOnlyList<BusinessKey> message) =>
        hop.All(message.Contains) || message.All(hop.Contains);
}
