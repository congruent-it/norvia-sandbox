using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking;

/// <summary>
/// The TMS's Proof of delivery call as a Logic Apps HTTP action takes a multipart body: the Canonical Model metadata as
/// the <c>metadata</c> part and each decoded file as a <c>files</c> part, in the order the metadata describes them.
/// </summary>
public static class ProofOfDeliveryMultipart
{
    public static JsonObject Body(MappedProofOfDelivery proofOfDelivery)
    {
        ArgumentNullException.ThrowIfNull(proofOfDelivery);
        var parts = new JsonArray
        {
            new JsonObject
            {
                ["headers"] = new JsonObject { ["Content-Disposition"] = "form-data; name=\"metadata\"" },
                ["body"] = proofOfDelivery.Metadata.DeepClone(),
            },
        };
        foreach (var file in proofOfDelivery.Files)
        {
            parts.Add(new JsonObject
            {
                ["headers"] = new JsonObject
                {
                    ["Content-Disposition"] = $"form-data; name=\"files\"; filename=\"{file.Name.Replace("\"", "'", StringComparison.Ordinal)}\"",
                    ["Content-Type"] = file.ContentType,
                },
                ["body"] = new JsonObject { ["$content-type"] = file.ContentType, ["$content"] = Convert.ToBase64String(file.Content) },
            });
        }

        return new JsonObject { ["$content-type"] = "multipart/form-data", ["$multipart"] = parts };
    }
}
