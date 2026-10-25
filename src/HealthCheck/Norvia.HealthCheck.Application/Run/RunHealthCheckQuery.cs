using Ardalis.Result;
using Mediator;
using Norvia.HealthCheck.Application.Checks;

namespace Norvia.HealthCheck.Application.Run;

/// <summary>Run an Estate health check: every Check over the estate's configuration, as the Findings it gives.</summary>
public sealed record RunHealthCheckQuery : IQuery<Result<HealthCheckReport>>;

/// <summary>The outcome of one Estate health check. It lives in the page session and is stored nowhere.</summary>
/// <param name="Estate">What the facts describe, such as the snapshot's name.</param>
/// <param name="ChecksRun">How many Checks ran, so "no Findings" can be told from "nothing ran".</param>
public sealed record HealthCheckReport(string Estate, DateTimeOffset RanAt, int ChecksRun, IReadOnlyList<Finding> Findings);
