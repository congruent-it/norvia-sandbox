using System.ComponentModel.DataAnnotations;

namespace Norvia.Erp.Web.ErpApi;

/// <summary>Where the ERP API is, and how the ERP UI gets its token for it: signed locally, or from Entra in Azure.</summary>
public sealed class ErpApiOptions
{
    public const string SectionName = "ErpApi";

    [Required]
    public Uri? BaseAddress { get; set; }

    /// <summary>Set locally, where there is no Entra.</summary>
    public LocalErpApiTokenOptions? LocalTokens { get; set; }

    /// <summary>Set in Azure.</summary>
    public EntraErpApiTokenOptions? Entra { get; set; }
}

/// <summary>Tokens as the AppHost's local key signs them where Entra would.</summary>
public sealed class LocalErpApiTokenOptions
{
    public string Issuer { get; set; } = string.Empty;

    /// <summary>The ERP API, as it knows itself locally.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>The ERP UI's local client ID, which stands in for its managed identity.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>The AppHost's local signing key, base64-encoded.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Issuer) && !string.IsNullOrWhiteSpace(Audience)
        && !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(SigningKey);
}

public sealed class EntraErpApiTokenOptions
{
    /// <summary>The ERP API's scope: <c>&lt;its client ID&gt;/.default</c>.</summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>The client ID of the ERP's managed identity, which holds the ERP API's <c>MasterData.Manage</c> role.</summary>
    public string ManagedIdentityClientId { get; set; } = string.Empty;

    public bool IsComplete => !string.IsNullOrWhiteSpace(Scope) && !string.IsNullOrWhiteSpace(ManagedIdentityClientId);
}
