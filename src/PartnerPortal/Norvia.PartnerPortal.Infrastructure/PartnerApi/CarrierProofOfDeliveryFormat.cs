using System.Globalization;
using System.Text.Json.Nodes;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Infrastructure.PartnerApi;

/// <summary>
/// A Carrier's Proof of delivery in the Carrier Proof of delivery format of the Norvia Partner API
/// (contracts/partner-api/carrier-proof-of-delivery.v1.schema.json), its files base64-encoded in the one body. It never
/// names the Carrier: Norvia takes it from the token (ADR 0005).
/// </summary>
internal static class CarrierProofOfDeliveryFormat
{
    public static JsonObject ToJson(CarrierProofOfDelivery proof)
    {
        ArgumentNullException.ThrowIfNull(proof);
        var json = new JsonObject
        {
            ["bookingRef"] = proof.BookingReference,
            ["carrierBookingNumber"] = proof.CarrierBookingNumber,
            ["documentId"] = proof.DocumentId,
            ["receivedBy"] = proof.ReceivedBy,
            ["deliveryTime"] = CarrierLocalTime.At(proof.DeliveryTime, proof.Country).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
            ["place"] = proof.Place,
            ["postcode"] = proof.Postcode,
            ["country"] = proof.Country,
            ["condition"] = proof.Condition switch
            {
                ProofOfDeliveryCondition.Clean => "CLEAN",
                ProofOfDeliveryCondition.Reservations => "RESERVATIONS",
                _ => throw new ArgumentOutOfRangeException(nameof(proof), proof.Condition, "Every condition has a Carrier code."),
            },
        };
        if (proof.Remark is not null)
        {
            json["remark"] = proof.Remark;
        }

        json["files"] = new JsonArray([.. proof.Files.Select(file => (JsonNode)new JsonObject
        {
            ["name"] = file.Name,
            ["contentType"] = file.ContentType,
            ["content"] = Convert.ToBase64String(file.Content),
        })]);
        return json;
    }
}
