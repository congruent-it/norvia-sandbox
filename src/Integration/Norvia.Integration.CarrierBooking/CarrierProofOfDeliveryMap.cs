using System.Text.Json.Nodes;
using Norvia.Integration.TransportOrderIntake;

namespace Norvia.Integration.CarrierBooking;

/// <summary>
/// The Carrier Proof of delivery map (P6): turns a Carrier's Proof of delivery in the Carrier Proof of delivery format into
/// the Canonical Model metadata the TMS's Proof of delivery API takes, and decodes its files for the call's <c>files</c>
/// parts. The Carrier is the one API Management took from the token, never one the Carrier's payload names (ADR 0005).
/// </summary>
/// <remarks>
/// Unlike the tracking map, content the map can't read fails the run: the message goes to the Proof of delivery queue's
/// dead-letter queue, where a person judges it. What the map doesn't judge is left to the TMS's Proof of delivery rules,
/// the files' type and size among them.
/// </remarks>
public static class CarrierProofOfDeliveryMap
{
    private const int MaxFiles = 5;

    private static readonly Dictionary<string, string> Conditions = new(StringComparer.Ordinal)
    {
        ["CLEAN"] = "Clean",
        ["RESERVATIONS"] = "Reservations",
    };

    /// <param name="carrierProofOfDelivery">The Carrier's Proof of delivery as JSON text.</param>
    /// <param name="carrierId">The Carrier ID API Management took from the token.</param>
    /// <exception cref="InvalidProofOfDeliveryException">The content isn't a Proof of delivery the map can read.</exception>
    public static MappedProofOfDelivery Map(string carrierProofOfDelivery, string carrierId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(carrierId);
        var message = JsonFields.Parse(carrierProofOfDelivery)
            ?? throw new InvalidProofOfDeliveryException("The Proof of delivery isn't a JSON object.");

        var bookingRef = Required(message, "bookingRef");
        var documentId = Required(message, "documentId");
        var receivedBy = Required(message, "receivedBy");
        var place = Required(message, "place");
        var postcode = Required(message, "postcode");
        var conditionCode = Required(message, "condition");
        var condition = Conditions.GetValueOrDefault(conditionCode)
            ?? throw new InvalidProofOfDeliveryException($"The condition '{conditionCode}' isn't CLEAN or RESERVATIONS.");
        var remark = JsonFields.Text(message, "remark");
        if (condition == "Reservations" && remark is null)
        {
            throw new InvalidProofOfDeliveryException("A Proof of delivery with reservations needs a remark.");
        }

        var countryCode = Required(message, "country");
        var alpha2 = Alpha3Codes.Alpha2Of(countryCode)
            ?? throw new InvalidProofOfDeliveryException($"The country '{countryCode}' isn't an ISO 3166-1 alpha-3 code the map knows.");
        var country = Countries.At(Countries.Find(alpha2), postcode)
            ?? throw new InvalidProofOfDeliveryException($"The country '{countryCode}' has no time zone the map knows.");
        var localTime = Required(message, "deliveryTime");
        var deliveryTime = LocalTimes.ToLocalOffset(localTime, country)
            ?? throw new InvalidProofOfDeliveryException($"The deliveryTime '{localTime}' isn't a local date/time like 2030-12-10T10:15:00.");

        var files = ReadFiles(message);
        var metadata = new JsonObject
        {
            ["documentId"] = documentId,
            ["source"] = new JsonObject { ["type"] = "Carrier", ["carrierId"] = carrierId },
            ["carrierBookingReference"] = bookingRef,
            ["carrierBookingNumber"] = JsonFields.Text(message, "carrierBookingNumber"),
            ["receivedBy"] = receivedBy,
            ["deliveryTime"] = deliveryTime,
            ["location"] = new JsonObject { ["place"] = place, ["postcode"] = postcode, ["countryCode"] = alpha2 },
            ["condition"] = condition,
            ["remark"] = remark,
            ["files"] = new JsonArray(files.Select(file => (JsonNode)new JsonObject { ["name"] = file.Name, ["contentType"] = file.ContentType }).ToArray()),
        };

        return new MappedProofOfDelivery(JsonFields.WithoutNulls(metadata), files);
    }

    private static string Required(JsonObject message, string name) =>
        JsonFields.Text(message, name) ?? throw new InvalidProofOfDeliveryException($"The Proof of delivery has no readable {name}.");

    private static List<MappedProofOfDeliveryFile> ReadFiles(JsonObject message)
    {
        if (message["files"] is not JsonArray { Count: >= 1 and <= MaxFiles } entries)
        {
            throw new InvalidProofOfDeliveryException($"The Proof of delivery needs 1 to {MaxFiles} files.");
        }

        return [.. entries.Select((entry, index) =>
        {
            var file = entry as JsonObject;
            var name = JsonFields.Text(file, "name");
            var contentType = JsonFields.Text(file, "contentType");
            var content = JsonFields.Text(file, "content");
            if (name is null || contentType is null || content is null)
            {
                throw new InvalidProofOfDeliveryException($"File {index + 1} needs a name, a content type and its content.");
            }

            var bytes = new byte[content.Length];
            return Convert.TryFromBase64String(content, bytes, out var written)
                ? new MappedProofOfDeliveryFile(name, contentType, bytes[..written])
                : throw new InvalidProofOfDeliveryException($"The content of file {index + 1} ('{name}') isn't valid base64.");
        })];
    }
}
