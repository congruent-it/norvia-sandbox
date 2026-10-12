using Norvia.DriverApp.Application.Reports.ReportOutcome;

namespace Norvia.DriverApp.Api.Reports;

/// <summary>What became of one report, in the Canonical Model's <c>driver-report-outcome</c> shape, as the Driver reports Logic App sends it.</summary>
public sealed record ReportOutcomeRequest(
    Guid DriverAppEventId,
    string TripNumber,
    string CorrelationId,
    ReportOutcomeKind Outcome,
    string? FailureCode,
    string? Message)
{
    public ReportOutcomeCommand ToCommand() => new(DriverAppEventId, TripNumber, CorrelationId, Outcome, FailureCode, Message);
}
