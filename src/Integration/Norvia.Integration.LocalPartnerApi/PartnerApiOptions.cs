using System.ComponentModel.DataAnnotations;

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

    [Required]
    public LocalTokenOptions Tokens { get; set; } = new();
}
