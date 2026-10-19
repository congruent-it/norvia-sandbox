using System.ComponentModel.DataAnnotations;
using Norvia.Simulator.Infrastructure.Entra;

namespace Norvia.Simulator.Infrastructure.ErpApi;

/// <summary>
/// Where the ERP API is, and how the Simulator gets a token for it as Finance: signed locally, as the ERP API trusts it, or in Azure from Entra for the
/// Simulator's managed identity (ADR 0042). Exactly one of <see cref="Entra"/> and <see cref="LocalTokens"/> is configured.
/// </summary>
public sealed class ErpApiOptions
{
    public const string SectionName = "ErpApi";

    /// <summary>The ERP API's address, such as <c>http://localhost:5500</c>.</summary>
    [Required]
    public Uri? BaseAddress { get; set; }

    /// <summary>Locally the Simulator signs its own token for the ERP API, as Entra would issue it.</summary>
    public LocalErpTokenOptions? LocalTokens { get; set; }

    /// <summary>In Azure the token comes from Entra for the Simulator's managed identity, which holds the ERP API's manage role.</summary>
    public EntraErpTokenOptions? Entra { get; set; }
}

public sealed class LocalErpTokenOptions
{
    [Required]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>The ERP API, as it knows itself locally.</summary>
    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>The Simulator's local client ID, which stands in for its managed identity.</summary>
    [Required]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>The key the ERP API trusts locally, base64-encoded.</summary>
    [Required]
    public string SigningKey { get; set; } = string.Empty;
}
