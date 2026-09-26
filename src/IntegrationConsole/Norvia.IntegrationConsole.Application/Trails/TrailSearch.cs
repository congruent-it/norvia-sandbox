namespace Norvia.IntegrationConsole.Application.Trails;

/// <summary>What the <see cref="ITrailSource"/> is asked: a key the operator typed, and the time range to look in.</summary>
public sealed record TrailSearch(string Key, DateTimeOffset From, DateTimeOffset To);
