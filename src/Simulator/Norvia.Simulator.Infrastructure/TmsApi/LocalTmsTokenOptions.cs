using System.ComponentModel.DataAnnotations;

namespace Norvia.Simulator.Infrastructure.TmsApi;

public sealed class LocalTmsTokenOptions
{
    [Required]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>The TMS API, as it knows itself locally.</summary>
    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>The Simulator's local client ID, which stands in for its managed identity.</summary>
    [Required]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>The key the TMS trusts locally, base64-encoded.</summary>
    [Required]
    public string SigningKey { get; set; } = string.Empty;
}
