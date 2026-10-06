using System.ComponentModel.DataAnnotations;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalPartnerApi;

/// <summary>What API Management knows in Azure: the partners' subscriptions, their Shippers and Carriers, and whose tokens to trust.</summary>
public sealed class PartnerApiOptions
{
    public const string SectionName = "PartnerApi";

    /// <summary>Each Persona's subscription key for the Norvia partners product, by Persona ID.</summary>
    [MinLength(1)]
    public Dictionary<string, string> SubscriptionKeys { get; set; } = [];

    /// <summary>The Shipper each Shipper Persona's client books for, by client ID (the token's <c>azp</c>).</summary>
    public Dictionary<string, string> ShippersByClientId { get; set; } = [];

    /// <summary>The Carrier each Carrier Persona's client answers for, by client ID (the token's <c>azp</c>).</summary>
    public Dictionary<string, string> CarriersByClientId { get; set; } = [];

    /// <summary>The Service Bus queue the Carrier-tracking Logic App reads, which API Management puts a Carrier's tracking message on.</summary>
    [Required]
    public string TrackingQueue { get; set; } = "carrier-tracking";

    /// <summary>The Service Bus queue the Carrier Proof of delivery Logic App reads, which API Management puts a Proof of delivery's blob reference on.</summary>
    [Required]
    public string ProofOfDeliveryQueue { get; set; } = "carrier-proof-of-delivery";

    /// <summary>The blob container API Management stores a Carrier's Proof of delivery in, the claim check's transit (ADR 0009).</summary>
    [Required]
    public string InboundContainer { get; set; } = "inbound-proofs-of-delivery";

    [Required]
    public LocalTokenOptions Tokens { get; set; } = new();
}
