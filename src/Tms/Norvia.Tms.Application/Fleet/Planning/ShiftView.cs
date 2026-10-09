namespace Norvia.Tms.Application.Fleet.Planning;

public sealed record ShiftView(string DriverId, string DriverName, TimeOnly Start, TimeOnly End, bool EditedByHand);
