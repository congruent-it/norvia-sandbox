using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.CarrierShipments;

/// <summary>
/// The tracking contract's keys the portal knows of a Tracking event (coding guidelines §7), so its traces join Norvia's
/// Message trail by the correlation ID API Management gave it, and the Carrier's event ID.
/// </summary>
public static class TrackingEventTracking
{
    public const string Flow = "carrier-tracking";

    /// <param name="correlationId">The ID Norvia gave the event; null when Norvia never replied, which leaves the key out.</param>
    public static Dictionary<string, object?> KeysOf(Persona persona, string shipmentNumber, CarrierTrackingEvent trackingEvent, string? correlationId)
    {
        ArgumentNullException.ThrowIfNull(persona);
        ArgumentNullException.ThrowIfNull(trackingEvent);
        var keys = new Dictionary<string, object?>
        {
            ["flow"] = Flow,
            ["partyId"] = persona.PartyId,
            ["carrierBookingNumber"] = trackingEvent.CarrierBookingNumber,
            ["shipmentNumber"] = shipmentNumber,
            ["sourceMessageId"] = trackingEvent.EventId,
        };
        if (correlationId is not null)
        {
            keys["correlationId"] = correlationId;
        }

        return keys;
    }

    /// <summary>The outcome of API Management's reply, and its failure code when it has one.</summary>
    public static Dictionary<string, object?> OutcomeOf(TrackingResult result) => result switch
    {
        TrackingResult.Accepted => new() { ["outcome"] = "Succeeded" },
        TrackingResult.Refused => new() { ["outcome"] = "Refused" },
        TrackingResult.NotReceived => new() { ["outcome"] = "Failed", ["failureCode"] = "TrackingNotReceived" },
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, "Every reply records its outcome."),
    };
}
