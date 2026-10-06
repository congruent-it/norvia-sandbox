using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.CarrierShipments.SendProofOfDelivery;

/// <summary>
/// The tracking contract's keys the portal knows of a Proof of delivery (coding guidelines §7), so its traces join Norvia's
/// Message trail by the correlation ID API Management gave it, and the Carrier's document ID. Never what it said or its files.
/// </summary>
public static class ProofOfDeliveryTracking
{
    public const string Flow = "carrier-proof-of-delivery";

    /// <param name="correlationId">The ID Norvia gave the Proof of delivery; null when Norvia never replied, which leaves the key out.</param>
    public static Dictionary<string, object?> KeysOf(Persona persona, string shipmentNumber, CarrierProofOfDelivery proof, string? correlationId)
    {
        ArgumentNullException.ThrowIfNull(persona);
        ArgumentNullException.ThrowIfNull(proof);
        var keys = new Dictionary<string, object?>
        {
            ["flow"] = Flow,
            ["partyId"] = persona.PartyId,
            ["carrierBookingNumber"] = proof.CarrierBookingNumber,
            ["shipmentNumber"] = shipmentNumber,
            ["sourceMessageId"] = proof.DocumentId,
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
        TrackingResult.NotReceived => new() { ["outcome"] = "Failed", ["failureCode"] = "ProofOfDeliveryNotReceived" },
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, "Every reply records its outcome."),
    };
}
