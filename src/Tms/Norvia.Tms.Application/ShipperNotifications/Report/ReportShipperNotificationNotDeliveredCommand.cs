using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.ShipperNotifications.Report;

/// <summary>
/// The Shipper notification Logic App reports that the notification never reached the Shipper, with its technical cause (P7): the
/// TMS records it and publishes ShipperNotificationNotDelivered. A report for a notification that is no longer Requested is ignored.
/// </summary>
/// <param name="CorrelationId">The Planner's action's own trail, which the report continues.</param>
/// <param name="StatusCode">The Shipper endpoint's HTTP status code, or null when the failure had none, such as a timeout.</param>
/// <param name="Message">The technical cause in plain language.</param>
public sealed record ReportShipperNotificationNotDeliveredCommand(string NotificationId, string CorrelationId, int? StatusCode, string Message)
    : ICommand<Result<ShipperNotificationReportResult>>, IShipperNotificationReportCommand;
