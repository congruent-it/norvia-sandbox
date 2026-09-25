using System.ComponentModel.DataAnnotations;

namespace Norvia.Integration.LocalPartnerApi;

/// <summary>What API Management knows in Azure: the partners' subscriptions, their Shippers, and whose tokens to trust.</summary>
public sealed class PartnerApiOptions
{
    public const string SectionName = "PartnerApi";

    /// <summary>Each Persona's subscription key for the Norvia partners product, by Persona ID.</summary>
    [MinLength(1)]
    public Dictionary<string, string> SubscriptionKeys { get; set; } = [];

    /// <summary>The Shipper each Shipper Persona's client books for, by client ID (the token's <c>azp</c>).</summary>
    public Dictionary<string, string> ShippersByClientId { get; set; } = [];

    [Required]
    public LocalTokenOptions Tokens { get; set; } = new();
}
