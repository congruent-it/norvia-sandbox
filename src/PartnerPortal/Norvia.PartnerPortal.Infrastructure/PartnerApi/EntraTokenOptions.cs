using System.ComponentModel.DataAnnotations;

namespace Norvia.PartnerPortal.Infrastructure.PartnerApi;

public sealed class EntraTokenOptions
{
    [Required]
    public string TenantId { get; set; } = string.Empty;

    /// <summary>The Norvia Partner API's scope: <c>&lt;its client ID&gt;/.default</c>.</summary>
    [Required]
    public string Scope { get; set; } = string.Empty;

    /// <summary>The client ID of the portal's own managed identity, which each Persona's app registration trusts.</summary>
    [Required]
    public string ManagedIdentityClientId { get; set; } = string.Empty;
}
