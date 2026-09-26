namespace Norvia.IntegrationConsole.Application.Trails;

/// <summary>What the <see cref="ITrailSource"/> is asked for a trail's hops: its correlation ID, and the time range to look in.</summary>
public sealed record TrailLookup(string CorrelationId, DateTimeOffset From, DateTimeOffset To);
