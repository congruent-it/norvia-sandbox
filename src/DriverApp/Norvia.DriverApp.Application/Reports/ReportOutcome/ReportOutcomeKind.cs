namespace Norvia.DriverApp.Application.Reports.ReportOutcome;

/// <summary>What the Driver reports Logic App says became of a report; the Canonical Model's <c>driver-report-outcome</c> <c>outcome</c>.</summary>
public enum ReportOutcomeKind
{
    Recorded,
    Refused,
    NotRecorded,
}
