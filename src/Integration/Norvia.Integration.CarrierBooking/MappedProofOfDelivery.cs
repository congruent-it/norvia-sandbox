using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking;

/// <summary>What <see cref="CarrierProofOfDeliveryMap"/> made of a Carrier's Proof of delivery: the Canonical Model metadata, and the files decoded, in the order the metadata describes them.</summary>
public sealed record MappedProofOfDelivery(JsonObject Metadata, IReadOnlyList<MappedProofOfDeliveryFile> Files);
