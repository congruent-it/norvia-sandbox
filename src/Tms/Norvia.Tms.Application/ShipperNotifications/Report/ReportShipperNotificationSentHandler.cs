using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.ShipperNotifications;

namespace Norvia.Tms.Application.ShipperNotifications.Report;

public sealed class ReportShipperNotificationSentHandler(IRepository<ShipperNotification> notifications, TimeProvider time)
    : ICommandHandler<ReportShipperNotificationSentCommand, Result<ShipperNotificationReportResult>>
{
    public async ValueTask<Result<ShipperNotificationReportResult>> Handle(ReportShipperNotificationSentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var (notification, refusal) = await ShipperNotificationLookup.ToNotificationAsync(notifications, command.NotificationId, cancellationToken);
        if (notification is null)
        {
            return refusal ?? throw new InvalidOperationException($"Shipper notification {command.NotificationId} was neither found nor refused.");
        }

        if (!notification.ReportSent(time.GetUtcNow()))
        {
            // Late or repeated: ignored, changing and publishing nothing.
            return new ShipperNotificationReportResult(notification.Id.ToString(), notification.Status, Ignored: true);
        }

        await notifications.SaveChangesAsync(cancellationToken);
        return new ShipperNotificationReportResult(notification.Id.ToString(), notification.Status);
    }
}
