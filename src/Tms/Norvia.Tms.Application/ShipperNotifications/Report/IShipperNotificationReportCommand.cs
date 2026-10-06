namespace Norvia.Tms.Application.ShipperNotifications.Report;

/// <summary>A report of the Shipper notification Logic App, which continues the Planner's action's trail.</summary>
public interface IShipperNotificationReportCommand
{
    string NotificationId { get; }

    string CorrelationId { get; }
}
