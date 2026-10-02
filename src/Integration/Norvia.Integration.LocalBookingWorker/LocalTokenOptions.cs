using System.ComponentModel.DataAnnotations;
using Microsoft.IdentityModel.Tokens;

namespace Norvia.Integration.LocalBookingWorker;

/// <summary>Tokens as the AppHost's local key signs them where Entra would (ADR 0030).</summary>
public sealed class LocalTokenOptions
{
    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>The client the tokens are issued to, standing in for the booking Logic App's managed identity.</summary>
    [Required]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>The AppHost's local signing key, base64-encoded.</summary>
    [Required]
    public string SigningKey { get; set; } = string.Empty;

    public SymmetricSecurityKey Key => new(Convert.FromBase64String(SigningKey));
}
