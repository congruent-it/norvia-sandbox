using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet.Planning.EditShift;

public sealed record EditShiftResult(DateOnly Date, DriverId DriverId, TimeOnly Start, TimeOnly End);
