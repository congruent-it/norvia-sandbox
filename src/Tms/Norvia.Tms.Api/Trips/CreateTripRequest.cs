namespace Norvia.Tms.Api.Trips;

/// <param name="PlannedStart">A time on the Trip's day, such as "07:30".</param>
/// <param name="ShipmentNumbers">Shipments to put on the new Trip, such as ["SH-0000012"]: each adds its collection and delivery Stops at the end. All go on or none.</param>
/// <param name="PlannedDrivingDuration">How long the Driver drives, such as "06:30:00" for six and a half hours.</param>
public sealed record CreateTripRequest(DateOnly? Day, string? VehicleId, string? DriverId, TimeOnly? PlannedStart = null, TimeSpan? PlannedDrivingDuration = null, IReadOnlyList<string>? ShipmentNumbers = null);
