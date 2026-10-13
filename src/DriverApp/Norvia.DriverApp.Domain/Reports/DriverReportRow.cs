using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Domain.Reports;

/// <summary>What a query reads of a <see cref="DriverReport"/>, so no aggregate leaves the Domain and Infrastructure for a read.</summary>
/// <param name="ShipmentNumber">The Shipment a Stop report is about; null for a Start Trip.</param>
/// <param name="Stop">The Stop a Stop report is about; null for a Start Trip.</param>
public sealed record DriverReportRow(
    ReportId Id,
    ReportKind Kind,
    TripNumber TripNumber,
    string? ShipmentNumber,
    StopKind? Stop,
    string? Remark,
    ReportState State,
    string? FailureCode,
    string? Message,
    DateTimeOffset EventTime,
    DateTimeOffset? OutcomeAt)
{
    /// <summary>Whether the report completes a Stop, whatever became of it.</summary>
    public bool GivesStopAnOutcome => DriverReport.GivesAStopAnOutcome(Kind);
}
