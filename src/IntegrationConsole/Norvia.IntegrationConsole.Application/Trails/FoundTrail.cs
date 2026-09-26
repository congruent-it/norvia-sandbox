namespace Norvia.IntegrationConsole.Application.Trails;

/// <summary>
/// One Message trail as the <see cref="ITrailSource"/> found it: everything recorded under its correlation ID, reduced
/// to the tracking contract's keys.
/// </summary>
public sealed record FoundTrail(
    string CorrelationId,
    IReadOnlyList<string> Flows,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    IReadOnlyList<Outcome> Outcomes,
    IReadOnlyList<BusinessKey> BusinessKeys);
