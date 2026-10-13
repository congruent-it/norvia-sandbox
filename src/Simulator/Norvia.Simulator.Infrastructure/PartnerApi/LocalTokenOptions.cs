using System.ComponentModel.DataAnnotations;

namespace Norvia.Simulator.Infrastructure.PartnerApi;

public sealed class LocalTokenOptions
{
    [Required]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>The Norvia Partner API, as the local stand-in knows it.</summary>
    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>The key the local stand-in trusts, base64-encoded.</summary>
    [Required]
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>The app roles each Persona holds, by Persona ID, as the estate grants them in Entra.</summary>
    public Dictionary<string, string[]> Roles { get; set; } = [];
}
