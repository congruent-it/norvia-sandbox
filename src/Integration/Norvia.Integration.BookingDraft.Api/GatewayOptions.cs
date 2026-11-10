using System.ComponentModel.DataAnnotations;

namespace Norvia.Integration.BookingDraft.Api;

/// <summary>The tokens the host accepts: Entra's, issued to API Management's identity for the host's own app registration, and nothing else.</summary>
public sealed class GatewayOptions
{
    [Required]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>The host's app registration (<c>Norvia booking draft API</c>), the audience of the tokens.</summary>
    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>API Management's identity (its client ID): the token's <c>azp</c> must be this, so only the gateway calls the host.</summary>
    [Required]
    public string GatewayClientId { get; set; } = string.Empty;
}
