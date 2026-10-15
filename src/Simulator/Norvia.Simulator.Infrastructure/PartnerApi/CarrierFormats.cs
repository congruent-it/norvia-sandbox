using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Carriers;

namespace Norvia.Simulator.Infrastructure.PartnerApi;

/// <summary>
/// Writes what a Carrier says in the Norvia Partner API's Carrier formats (contracts/partner-api: carrier-answer, carrier-tracking and
/// carrier-proof-of-delivery). None of them names the Carrier: API Management takes it from the token (ADR 0005). A unit test checks
/// each against its schema.
/// </summary>
internal static class CarrierFormats
{
    // A minimal PDF, as the contract's sample has it.
    private static readonly string ProofContent = Convert.ToBase64String(Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF\n"));

    public static JsonObject Answer(CarrierAnswerRequest answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        var json = new JsonObject
        {
            ["carrierBookingNumber"] = answer.CarrierBookingNumber,
            ["decision"] = answer.Confirm ? "CONFIRMED" : "DECLINED",
        };
        Add(json, "bookingRef", answer.BookingReference);
        if (answer.PlannedCollection is { } planned)
        {
            json["plannedCollection"] = planned.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        }

        Add(json, "declineReason", answer.DeclineReason);
        return json;
    }

    public static JsonObject Tracking(CarrierTrackingRequest tracking)
    {
        ArgumentNullException.ThrowIfNull(tracking);

        var json = new JsonObject
        {
            ["bookingRef"] = tracking.BookingReference,
            ["carrierBookingNumber"] = tracking.CarrierBookingNumber,
            ["eventId"] = tracking.EventId,
            ["statusCode"] = CarrierReferences.StatusCode(tracking.Event),
            ["eventTime"] = CarrierCountries.Local(tracking.At, tracking.Place.Country),
            ["place"] = tracking.Place.City,
            ["postcode"] = tracking.Place.Postcode,
            ["country"] = CarrierCountries.Alpha3(tracking.Place.Country),
        };
        Add(json, "remark", tracking.Remark);
        return json;
    }

    public static JsonObject Proof(CarrierProofRequest proof)
    {
        ArgumentNullException.ThrowIfNull(proof);

        var json = new JsonObject
        {
            ["bookingRef"] = proof.BookingReference,
            ["carrierBookingNumber"] = proof.CarrierBookingNumber,
            ["documentId"] = proof.DocumentId,
            ["receivedBy"] = proof.ReceivedBy,
            ["deliveryTime"] = CarrierCountries.Local(proof.At, proof.Place.Country),
            ["place"] = proof.Place.City,
            ["postcode"] = proof.Place.Postcode,
            ["country"] = CarrierCountries.Alpha3(proof.Place.Country),
            ["condition"] = proof.Reservations ? "RESERVATIONS" : "CLEAN",
        };
        Add(json, "remark", proof.Remark);
        json["files"] = new JsonArray(new JsonObject { ["name"] = $"{proof.DocumentId}.pdf", ["contentType"] = "application/pdf", ["content"] = ProofContent });
        return json;
    }

    private static void Add(JsonObject json, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            json[name] = value.Trim();
        }
    }
}
