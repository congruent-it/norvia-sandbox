namespace Norvia.DriverApp.Application.Reports.ReportOutcome;

public enum ReportOutcomeResult
{
    /// <summary>The report has the outcome now.</summary>
    Applied,

    /// <summary>The report had an outcome already, so nothing changed: the Logic App said it again.</summary>
    Ignored,
}
