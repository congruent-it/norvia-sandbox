namespace Norvia.IntegrationConsole.Application.Trails.Search;

/// <summary>The trails a search found, and the time range it looked in.</summary>
public sealed record TrailSearchResult(DateTimeOffset From, DateTimeOffset To, IReadOnlyList<TrailSummary> Trails);
