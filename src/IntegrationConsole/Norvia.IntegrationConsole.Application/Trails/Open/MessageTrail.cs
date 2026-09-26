namespace Norvia.IntegrationConsole.Application.Trails.Open;

/// <summary>
/// A Message trail's hops, in the order the page shows them, and the business keys it carried. Keys only, never a
/// message body.
/// </summary>
/// <param name="From">The start of the time range looked in: as far back as the estate keeps anything.</param>
public sealed record MessageTrail(
    string CorrelationId,
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<TrailHop> Hops,
    IReadOnlyList<BusinessKey> BusinessKeys);
