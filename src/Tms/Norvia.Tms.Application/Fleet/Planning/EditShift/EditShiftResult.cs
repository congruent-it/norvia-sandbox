namespace Norvia.Tms.Application.Fleet.Planning.EditShift;

public sealed record EditShiftResult(DateOnly Date, string DriverId, TimeOnly Start, TimeOnly End);
