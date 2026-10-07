namespace Norvia.PartnerPortal.Web.ShipperNotifications;

/// <summary>The notification in the Shipper notification format (P7), as the Shipper notification Logic App sends it.</summary>
public sealed record ShipperNotificationBody(
    string? NotificationId,
    string? BookingRef,
    string? TransportOrderNumber,
    string? ExceptionCode,
    string? Message,
    string? Eta,
    string? DeliverFrom,
    string? DeliverUntil,
    string? Contact);
