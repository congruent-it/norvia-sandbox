namespace Norvia.Tms.Domain.Fleet;

/// <summary>The planned driving duration of one Driver's Trip on a day.</summary>
public sealed record PlannedDriving(DriverId DriverId, DateOnly Day, TimeSpan Duration);
