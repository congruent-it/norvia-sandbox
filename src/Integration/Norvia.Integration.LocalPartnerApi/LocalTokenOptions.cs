using System.ComponentModel.DataAnnotations;
using Microsoft.IdentityModel.Tokens;

namespace Norvia.Integration.LocalPartnerApi;

/// <summary>Tokens as the AppHost's local key signs them where Entra would (ADR 0023).</summary>
public sealed class LocalTokenOptions
{
    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>The client the tokens are issued to; only for tokens this app issues itself.</summary>
    public string? ClientId { get; set; }

    /// <summary>The AppHost's local signing key, base64-encoded.</summary>
    [Required]
    public string SigningKey { get; set; } = string.Empty;

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Issuer) && !string.IsNullOrWhiteSpace(Audience) && !string.IsNullOrWhiteSpace(SigningKey);

    public SymmetricSecurityKey Key => new(Convert.FromBase64String(SigningKey));
}
