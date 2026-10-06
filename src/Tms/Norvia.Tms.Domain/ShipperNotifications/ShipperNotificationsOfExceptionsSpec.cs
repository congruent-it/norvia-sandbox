using Ardalis.Specification;
using Norvia.Tms.Domain.Exceptions;

namespace Norvia.Tms.Domain.ShipperNotifications;

/// <summary>The Shipper notifications of the Exceptions, for the Planner's list of Open Exceptions.</summary>
public sealed class ShipperNotificationsOfExceptionsSpec : Specification<ShipperNotification>
{
    public ShipperNotificationsOfExceptionsSpec(IReadOnlyCollection<ExceptionId> exceptionIds) =>
        Query.Where(notification => exceptionIds.Contains(notification.ExceptionId)).AsNoTracking();
}
