namespace Norvia.PartnerPortal.Domain.ShipperNotifications;

/// <summary>What a notification says, as the Shipper notification format carries it (P7).</summary>
/// <param name="BookingRef">The Shipper's own reference of the booking, so the Shipper recognises the Transport Order.</param>
/// <param name="Timing">The current ETA or the agreed delivery window, if there is one.</param>
/// <param name="Contact">The Planner's name.</param>
public sealed record ShipperNotificationContent(
    string BookingRef,
    string TransportOrderNumber,
    ShipperExceptionCode ExceptionCode,
    string Message,
    DeliveryTiming Timing,
    string Contact)
{
    // For EF Core, which can't bind the nested timing through the constructor above and sets the properties from the columns.
    private ShipperNotificationContent()
        : this(string.Empty, string.Empty, default, string.Empty, DeliveryTiming.None, string.Empty)
    {
    }
}
