namespace Norvia.Tms.Web.Trips;

/// <summary>What the Planner entered for a Draft Trip: its day and, if picked, its Vehicle and Driver.</summary>
public sealed record TripEntry(DateOnly Day, string? VehicleId, string? DriverId);
