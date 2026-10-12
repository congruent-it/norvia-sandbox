using Ardalis.Result;
using Mediator;

namespace Norvia.DriverApp.Application.Reports.ReportOutcome;

/// <summary>What became of one report, as the Driver reports Logic App tells the Driver app (the Canonical Model's <c>driver-report-outcome</c>).</summary>
public sealed record ReportOutcomeCommand(
    Guid DriverAppEventId,
    string TripNumber,
    string CorrelationId,
    ReportOutcomeKind Outcome,
    string? FailureCode,
    string? Message) : ICommand<Result<ReportOutcomeResult>>;
