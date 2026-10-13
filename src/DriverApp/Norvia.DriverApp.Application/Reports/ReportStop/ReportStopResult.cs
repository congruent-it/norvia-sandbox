namespace Norvia.DriverApp.Application.Reports.ReportStop;

/// <param name="ReportId">The Driver app event ID of the report.</param>
/// <param name="AlreadyReported">True when the Driver had already made this report and the TMS has it or hasn't answered yet: nothing was published.</param>
public sealed record ReportStopResult(Guid ReportId, bool AlreadyReported);
