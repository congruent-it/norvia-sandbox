using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.ShipperNotifications;

namespace Norvia.Tms.Application.ShipperNotifications.Report;

public sealed class ReportShipperNotificationNotDeliveredHandler(IRepository<ShipperNotification> notifications, TimeProvider time)
    : ICommandHandler<ReportShipperNotificationNotDeliveredCommand, Result<ShipperNotificationReportResult>>
{
    public async ValueTask<Result<ShipperNotificationReportResult>> Handle(ReportShipperNotificationNotDeliveredCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var (notification, refusal) = await ShipperNotificationLookup.ToNotificationAsync(notifications, command.NotificationId, cancellationToken);
        if (notification is null)
        {
            return refusal ?? throw new InvalidOperationException($"Shipper notification {command.NotificationId} was neither found nor refused.");
        }

        if (!notification.ReportNotDelivered(command.StatusCode, command.Message, time.GetUtcNow()))
        {
            // Late or repeated: ignored, changing and publishing nothing.
            return new ShipperNotificationReportResult(notification.Id.ToString(), notification.Status, Ignored: true);
        }

        await notifications.SaveChangesAsync(cancellationToken);
        return new ShipperNotificationReportResult(notification.Id.ToString(), notification.Status);
    }
}
