using System.Text.Json.Nodes;
using Norvia.Integration.TransportOrderIntake;

namespace Norvia.Integration.CarrierBooking;

/// <summary>
/// The P7 Shipper notification map: turns a Canonical Model <c>ShipperNotificationRequested</c> into the Shipper
/// notification format (P7), which follows the Shipper booking format of P1.
/// </summary>
/// <remarks>
/// Towards the Shipper, Norvia carries the goods, so the notification names neither the Carrier nor the Shipment number.
/// Its times are local to the delivery address. An agreed delivery window replaces the ETA.
/// </remarks>
public static class ShipperNotificationMap
{
    private static readonly string[] RequiredFields = ["notificationId", "bookingRef", "transportOrderNumber", "message", "contact"];

    private static readonly Dictionary<string, string> ExceptionCodes = new(StringComparer.Ordinal)
    {
        ["CollectionFailed"] = "COLLECTION_FAILED",
        ["DeliveryFailed"] = "DELIVERY_FAILED",
        ["Damaged"] = "DAMAGED",
        ["Reservations"] = "RESERVATIONS",
        ["LateDelivery"] = "LATE",
    };

    /// <param name="shipperNotificationRequested">The <c>ShipperNotificationRequested</c> event as JSON text, as the Service Bus message carries it.</param>
    /// <returns>The notification, or null when the text isn't a <c>ShipperNotificationRequested</c> the Shipper can be told of, so the notification always has what its schema requires.</returns>
    public static JsonObject? ToShipperNotification(string shipperNotificationRequested)
    {
        if (JsonFields.Parse(shipperNotificationRequested) is not { } requested
            || JsonFields.Text(requested, "exceptionType") is not { } type
            || !ExceptionCodes.TryGetValue(type, out var code))
        {
            return null;
        }

        var country = Countries.At(Countries.Find(JsonFields.Text(requested, "deliveryCountry")), JsonFields.Text(requested, "deliveryPostcode"));
        var windowFrom = LocalTimes.FromUtcForShipper(JsonFields.Text(requested, "agreedDeliveryWindowEarliest"), country);
        var windowUntil = LocalTimes.FromUtcForShipper(JsonFields.Text(requested, "agreedDeliveryWindowLatest"), country);
        var hasWindow = windowFrom is not null && windowUntil is not null;

        var notification = new JsonObject
        {
            ["notificationId"] = JsonFields.Text(requested, "notificationId"),
            ["bookingRef"] = JsonFields.Text(requested, "shipperReference"),
            ["transportOrderNumber"] = JsonFields.Text(requested, "transportOrderNumber"),
            ["exceptionCode"] = code,
            ["message"] = JsonFields.Text(requested, "message"),
            ["eta"] = hasWindow ? null : LocalTimes.FromUtcForShipper(JsonFields.Text(requested, "eta"), country),
            ["deliverFrom"] = hasWindow ? windowFrom : null,
            ["deliverUntil"] = hasWindow ? windowUntil : null,
            ["contact"] = JsonFields.Text(requested, "planner"),
        };

        return RequiredFields.Any(name => notification[name] is null)
            ? null
            : JsonFields.WithoutNulls(notification);
    }
}
