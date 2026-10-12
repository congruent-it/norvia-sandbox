using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Domain.Reports;

/// <summary>What a query reads of a <see cref="DriverReport"/>, so no aggregate leaves the Domain and Infrastructure for a read.</summary>
public sealed record DriverReportRow(
    ReportId Id,
    ReportKind Kind,
    TripNumber TripNumber,
    ReportState State,
    string? FailureCode,
    string? Message,
    DateTimeOffset EventTime,
    DateTimeOffset? OutcomeAt);
