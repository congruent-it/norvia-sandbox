using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet.Planning;

public sealed record AbsenceView(Guid Id, string DriverId, string DriverName, DateOnly Date, AbsenceReason Reason, string Remark);
