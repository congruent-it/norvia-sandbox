using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Domain.Reports;

/// <summary>Where and when a Driver made a report: the Trip, the Driver from the token, the version the Driver app held, the time and the correlation ID of the action.</summary>
public sealed record ReportOrigin(TripNumber TripNumber, DriverId DriverId, int VersionHeld, DateTimeOffset EventTime, string CorrelationId);
