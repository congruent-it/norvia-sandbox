using Norvia.Tms.Domain.ShipperNotifications;

namespace Norvia.Tms.Application.Exceptions.NotifyShipper;

/// <summary>What notifying the Shipper did.</summary>
/// <param name="NotificationId">The notification ID, which the Shipper notification Logic App reports back on.</param>
/// <param name="CorrelationId">The notification's own Message trail.</param>
public sealed record NotifyShipperResult(
    string NotificationId, string ExceptionNumber, string ShipmentNumber, ShipperNotificationStatus Status, string CorrelationId);
