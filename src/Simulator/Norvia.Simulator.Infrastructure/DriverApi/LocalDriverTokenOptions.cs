using System.ComponentModel.DataAnnotations;

namespace Norvia.Simulator.Infrastructure.DriverApi;

public sealed class LocalDriverTokenOptions
{
    [Required]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>The Driver app API, as it knows itself locally.</summary>
    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>The Simulator's local client ID, which stands in for the Driver's Entra client app.</summary>
    [Required]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>The key the Driver app trusts locally, base64-encoded.</summary>
    [Required]
    public string SigningKey { get; set; } = string.Empty;
}
