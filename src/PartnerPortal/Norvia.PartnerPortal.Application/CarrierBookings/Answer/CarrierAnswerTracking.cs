using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.CarrierBookings.Answer;

/// <summary>
/// The tracking contract's keys the portal knows of a Carrier's answer (coding guidelines §7), so its traces join Norvia's
/// Message trail by the answer's correlation ID, the Carrier booking number and the Shipment number. Never what the
/// Carrier said: that is its own business.
/// </summary>
public static class CarrierAnswerTracking
{
    public const string Flow = "carrier-answer";

    /// <param name="correlationId">The ID Norvia gave the answer; null when Norvia never replied, which leaves the key out.</param>
    public static Dictionary<string, object?> KeysOf(Persona persona, string carrierBookingNumber, string shipmentNumber, string? correlationId)
    {
        ArgumentNullException.ThrowIfNull(persona);
        var keys = new Dictionary<string, object?>
        {
            ["flow"] = Flow,
            ["partyId"] = persona.PartyId,
            ["carrierBookingNumber"] = carrierBookingNumber,
            ["shipmentNumber"] = shipmentNumber,
        };
        if (correlationId is not null)
        {
            keys["correlationId"] = correlationId;
        }

        return keys;
    }

    /// <summary>The outcome of Norvia's reply, and its failure code when it has one, as the Logic App's run records it.</summary>
    public static Dictionary<string, object?> OutcomeOf(CarrierAnswerResult result) => result switch
    {
        CarrierAnswerResult.Recorded => new() { ["outcome"] = "Succeeded" },
        CarrierAnswerResult.BookingNotOpen => new() { ["outcome"] = "Refused", ["failureCode"] = "BookingNotOpen" },
        CarrierAnswerResult.ReferenceInUse => new() { ["outcome"] = "Refused", ["failureCode"] = "ReferenceInUse" },
        CarrierAnswerResult.BookingNotFound => new() { ["outcome"] = "Refused", ["failureCode"] = "BookingNotFound" },
        CarrierAnswerResult.NotAllowed => new() { ["outcome"] = "Refused", ["failureCode"] = "CarrierNotAllowed" },
        CarrierAnswerResult.Invalid => new() { ["outcome"] = "Refused", ["failureCode"] = "CarrierAnswerUnreadable" },
        CarrierAnswerResult.Refused => new() { ["outcome"] = "Refused" },
        CarrierAnswerResult.NotReceived => new() { ["outcome"] = "Failed", ["failureCode"] = "AnswerNotReceived" },
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, "Every reply records its outcome."),
    };
}
