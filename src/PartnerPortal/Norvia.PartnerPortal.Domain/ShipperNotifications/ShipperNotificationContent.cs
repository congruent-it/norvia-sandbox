namespace Norvia.PartnerPortal.Domain.ShipperNotifications;

/// <summary>What a notification says, as the Shipper notification format carries it (P7).</summary>
/// <param name="BookingRef">The Shipper's own reference of the booking, so the Shipper recognises the Transport Order.</param>
/// <param name="Eta">The current ETA, local to the delivery address as <c>dd-MM-yyyy HH:mm</c>; null when there is a window or no ETA.</param>
/// <param name="DeliverFrom">The earliest time of the agreed delivery window, in the same form; comes with <paramref name="DeliverUntil"/>.</param>
/// <param name="Contact">The Planner's name.</param>
public sealed record ShipperNotificationContent(
    string BookingRef,
    string TransportOrderNumber,
    ShipperExceptionCode ExceptionCode,
    string Message,
    string? Eta,
    string? DeliverFrom,
    string? DeliverUntil,
    string Contact);
