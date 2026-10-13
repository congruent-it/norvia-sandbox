using Norvia.DriverApp.Application.Trips;

namespace Norvia.DriverApp.Application.Reports;

public enum ReportKindDto
{
    StartTrip,
    Collected,
    CollectionFailed,
    DeliveryFailed,
    Damaged,
    Delivered,
    ProofOfDelivery,
    EndTrip,
}

/// <summary>What the Driver sees of a report (P12, ADR 0016).</summary>
public enum ReportStateDto
{
    Sending,
    Recorded,
    Refused,
    NotRecorded,
}

/// <summary>One report of a Driver on a Trip, with what became of it.</summary>
/// <param name="FailureCode">The TMS's failure code of a refusal, or the integration layer's of a report that wasn't recorded.</param>
/// <param name="Message">The refusal's readable message, or the technical cause of a report that wasn't recorded.</param>
/// <param name="ShipmentNumber">The Shipment of the Stop a Stop report is about; null for a Start Trip or an End Trip.</param>
/// <param name="Stop">The Stop a Stop report is about; null for a Start Trip or an End Trip.</param>
/// <param name="Remark">What the Driver said in a Stop report.</param>
public sealed record TripReportView(
    Guid Id,
    ReportKindDto Kind,
    ReportStateDto State,
    string? FailureCode,
    string? Message,
    DateTimeOffset EventTime,
    string? ShipmentNumber = null,
    StopKindDto? Stop = null,
    string? Remark = null);
