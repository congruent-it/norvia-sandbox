using System.Globalization;
using System.Text.Json.Nodes;
using Norvia.PartnerPortal.Application.CarrierShipments.SendProofOfDelivery;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Infrastructure.PartnerApi;

/// <summary>
/// A Carrier's Proof of delivery in the Carrier Proof of delivery format of the Norvia Partner API
/// (contracts/partner-api/carrier-proof-of-delivery.v1.schema.json), its files base64-encoded in the one body. It never
/// names the Carrier: Norvia takes it from the token (ADR 0005).
/// </summary>
internal static class CarrierProofOfDeliveryFormat
{
    /// <summary>The photo the HEIC fault adds: it starts as an HEIC file does (an <c>ftyp</c> box), which the TMS recognises by its content and refuses.</summary>
    public static readonly CarrierProofOfDeliveryFile HeicPhoto = new(
        "photo-of-delivery.heic", "image/heic", [0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70, 0x68, 0x65, 0x69, 0x63, 0x00, 0x00, 0x00, 0x00]);

    /// <param name="heicPhoto">Whether the HEIC fault applies: the Proof of delivery then carries <see cref="HeicPhoto"/> besides its files, in the place of the last when it has the most the TMS takes.</param>
    public static JsonObject ToJson(CarrierProofOfDelivery proof, bool heicPhoto = false)
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

        json["files"] = new JsonArray([.. FilesOf(proof, heicPhoto).Select(file => (JsonNode)new JsonObject
        {
            ["name"] = file.Name,
            ["contentType"] = file.ContentType,
            ["content"] = Convert.ToBase64String(file.Content),
        })]);
        return json;
    }

    private static IEnumerable<CarrierProofOfDeliveryFile> FilesOf(CarrierProofOfDelivery proof, bool heicPhoto)
    {
        if (!heicPhoto)
        {
            return proof.Files;
        }

        // The TMS takes at most five files, so a full set loses its last to the photo: the photo is what the Carrier took in the end.
        var kept = proof.Files.Count >= SendProofOfDeliveryHandler.MaxFiles ? proof.Files.Take(SendProofOfDeliveryHandler.MaxFiles - 1) : proof.Files;
        return [.. kept, HeicPhoto];
    }
}
