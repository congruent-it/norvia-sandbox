namespace Norvia.IntegrationConsole.Application.Trails.Search;

/// <summary>One row of the search results: a Message trail's keys, never a message body.</summary>
/// <param name="WorstOutcome">The worst outcome any hop recorded, or null when none recorded one.</param>
public sealed record TrailSummary(
    string CorrelationId,
    IReadOnlyList<string> Flows,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    Outcome? WorstOutcome,
    IReadOnlyList<BusinessKey> BusinessKeys)
{
    /// <summary>
    /// Whether the integration layer failed the message somewhere on the trail. A refusal is the answer the caller was
    /// meant to get, and a retry may still succeed, so neither counts.
    /// </summary>
    public bool HasFailure => WorstOutcome is Outcome.DeadLettered or Outcome.Failed;

    internal static TrailSummary From(FoundTrail trail) => new(
        trail.CorrelationId,
        trail.Flows,
        trail.FirstSeen,
        trail.LastSeen,
        trail.Outcomes.Count == 0 ? null : trail.Outcomes.Max(),
        trail.BusinessKeys);
}
