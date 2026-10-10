namespace Norvia.Tms.Api.Trips;

/// <param name="PlannedStart">A time on the Trip's day, such as "07:30".</param>
/// <param name="PlannedDrivingDuration">How long the Driver drives, such as "06:30:00" for six and a half hours.</param>
public sealed record ChangeTripRequest(string? VehicleId, string? DriverId, TimeOnly? PlannedStart = null, TimeSpan? PlannedDrivingDuration = null);
