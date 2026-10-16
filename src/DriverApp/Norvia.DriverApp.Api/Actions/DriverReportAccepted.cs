namespace Norvia.DriverApp.Api.Actions;

/// <summary>What the Driver app answers a report it took: the report's ID, the correlation ID that finds its Message trail, and whether the Driver had made it before.</summary>
/// <param name="CorrelationId">Null when the report was made before: the first report has the trail.</param>
public sealed record DriverReportAccepted(Guid ReportId, string? CorrelationId, bool AlreadyReported);
