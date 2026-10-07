using Norvia.Tms.Domain.ShipperNotifications;

namespace Norvia.Tms.Application.ShipperNotifications.Report;

/// <summary>What reporting a Shipper notification as sent or not delivered changed, or that the report was ignored because the notification was no longer Requested (P7).</summary>
public sealed record ShipperNotificationReportResult(string NotificationId, ShipperNotificationStatus Status, bool Ignored = false);
