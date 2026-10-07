using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking;

/// <summary>
/// The P7 Carrier cancellation map: turns a Canonical Model <c>CarrierBookingCancelled</c> or <c>CarrierBookingWithdrawn</c>
/// into the Carrier cancellation format (P7), which follows the Carrier booking format of P4.
/// </summary>
/// <remarks>
/// A Cancelled booking was Confirmed, so it carries the Carrier's own reference and Norvia's reason. A Withdrawn one was
/// never answered, so it carries neither. The Exception number stays inside Norvia.
/// </remarks>
public static class CarrierCancellationMap
{
    /// <param name="carrierBookingEnded">The <c>CarrierBookingCancelled</c> or <c>CarrierBookingWithdrawn</c> event as JSON text, as the Service Bus message carries it.</param>
    /// <returns>The cancellation, or null when the text isn't such an event the Carrier can be told of, so the cancellation always has what its schema requires.</returns>
    public static JsonObject? ToCarrierCancellation(string carrierBookingEnded)
    {
        if (JsonFields.Parse(carrierBookingEnded) is not { } ended
            || JsonFields.Text(ended, "carrierBookingNumber") is not { } bookingNumber
            || JsonFields.Text(ended, "shipmentNumber") is not { } shipmentNumber)
        {
            return null;
        }

        switch (JsonFields.Text(ended, "eventType"))
        {
            case "CarrierBookingCancelled"
                when JsonFields.Text(ended, "carrierBookingReference") is { } reference && JsonFields.Text(ended, "reasonText") is { } reason:
                return new JsonObject
                {
                    ["carrierBookingNumber"] = bookingNumber,
                    ["carrierBookingReference"] = reference,
                    ["shipmentNumber"] = shipmentNumber,
                    ["kind"] = "CANCELLED",
                    ["reasonText"] = reason,
                };
            case "CarrierBookingWithdrawn":
                return new JsonObject
                {
                    ["carrierBookingNumber"] = bookingNumber,
                    ["shipmentNumber"] = shipmentNumber,
                    ["kind"] = "WITHDRAWN",
                };
            default:
                return null;
        }
    }
}
