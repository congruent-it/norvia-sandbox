using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet.Planning;

public sealed record OffTheRoadDayView(Guid Id, string VehicleId, string Registration, DateOnly Date, OffTheRoadReason Reason, string Remark);
