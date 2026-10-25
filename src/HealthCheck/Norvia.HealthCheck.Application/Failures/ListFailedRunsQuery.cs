using Ardalis.Result;
using Mediator;

namespace Norvia.HealthCheck.Application.Failures;

/// <summary>List the Logic Apps that failed in the last <see cref="Estate.EstateConfiguration.FailureWindowDays"/> days, with whether an alert watches each.</summary>
public sealed record ListFailedRunsQuery : IQuery<Result<FailedRunsReport>>;

/// <param name="Estate">What the facts describe, such as the snapshot's name.</param>
/// <param name="WindowDays">How many days back the failed runs are counted.</param>
/// <param name="Flows">Only the Logic Apps that failed, the most failed first.</param>
public sealed record FailedRunsReport(string Estate, int WindowDays, IReadOnlyList<FailedFlow> Flows);

public sealed record FailedFlow(string Flow, int FailedRuns, bool HasFailedRunAlert);
