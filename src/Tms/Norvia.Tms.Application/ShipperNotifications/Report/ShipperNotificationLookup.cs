using Ardalis.Result;
using Ardalis.SharedKernel;
using Norvia.Tms.Application.Exceptions;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.ShipperNotifications;

namespace Norvia.Tms.Application.ShipperNotifications.Report;

internal static class ShipperNotificationLookup
{
    /// <summary>The Shipper notification a report names, or the refusal for an unknown notification ID.</summary>
    public static async Task<(ShipperNotification? Notification, Result<ShipperNotificationReportResult>? Refusal)> ToNotificationAsync(
        IRepository<ShipperNotification> notifications, string notificationId, CancellationToken cancellationToken)
    {
        if (!ShipperNotificationId.TryParse(notificationId, out var id)
            || await notifications.SingleOrDefaultAsync(new ShipperNotificationByIdSpec(id), cancellationToken) is not { } notification)
        {
            return (null, ExceptionRefused.Because<ShipperNotificationReportResult>(ExceptionRefusal.ShipperNotificationNotFound(notificationId)));
        }

        return (notification, null);
    }
}
