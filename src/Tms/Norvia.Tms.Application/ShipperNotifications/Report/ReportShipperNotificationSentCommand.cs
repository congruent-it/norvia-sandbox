using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.ShipperNotifications.Report;

/// <summary>
/// The Shipper notification Logic App reports that the Shipper's endpoint acknowledged the notification (P7): the TMS records the
/// time and publishes nothing. A report for a notification that is no longer Requested is ignored.
/// </summary>
/// <param name="CorrelationId">The Planner's action's own trail, which the report continues.</param>
public sealed record ReportShipperNotificationSentCommand(string NotificationId, string CorrelationId)
    : ICommand<Result<ShipperNotificationReportResult>>, IShipperNotificationReportCommand;
