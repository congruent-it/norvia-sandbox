using Mediator;

namespace Norvia.IntegrationConsole.Application.Runs.Failed;

/// <summary>The failed runs of the last 24 hours across the estate's Logic Apps, newest first, narrowed to one flow when given.</summary>
public sealed record ListFailedRunsQuery(string? Flow = null) : IQuery<FailedRunList>;
