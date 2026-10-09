namespace Norvia.Tms.Domain.Fleet;

/// <summary>A Driver's working time on one day, from their Roster unless the Planner edited it by hand.</summary>
public sealed record Shift(DriverId DriverId, TimeOnly Start, TimeOnly End, bool EditedByHand);
