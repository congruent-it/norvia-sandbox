namespace Norvia.DriverApp.Application.Reports.Deliver;

/// <param name="ReportId">The Driver app event ID of the Delivered report.</param>
/// <param name="AlreadyReported">True when the Driver had already delivered this Stop and the TMS has it or hasn't answered yet: nothing was stored or published.</param>
public sealed record DeliverResult(Guid ReportId, bool AlreadyReported);
