namespace Norvia.IntegrationConsole.Application.Runs.Failed;

/// <summary>The failed runs found between <paramref name="From"/> and <paramref name="To"/>.</summary>
/// <param name="Flows">Every flow with a failed run in the window, whatever the query narrowed to, so the operator can switch.</param>
/// <param name="Runs">Newest first.</param>
public sealed record FailedRunList(DateTimeOffset From, DateTimeOffset To, IReadOnlyList<string> Flows, IReadOnlyList<FailedRun> Runs);
