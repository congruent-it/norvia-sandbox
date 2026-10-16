namespace Norvia.DriverApp.Application.Reports.EndTrip;

/// <param name="ReportId">The Driver app event ID of the report that ends the Trip.</param>
/// <param name="CorrelationId">The correlation ID of the Driver's action, which finds its Message trail; null when the report was made before.</param>
/// <param name="AlreadyReported">True when the Driver had already pressed End Trip and the TMS has it or hasn't answered yet: nothing was published.</param>
public sealed record EndTripResult(Guid ReportId, bool AlreadyReported, string? CorrelationId = null);
