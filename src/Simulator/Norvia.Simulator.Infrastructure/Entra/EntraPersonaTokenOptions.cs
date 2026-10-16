using System.ComponentModel.DataAnnotations;

namespace Norvia.Simulator.Infrastructure.Entra;

public sealed class EntraPersonaTokenOptions
{
    [Required]
    public string TenantId { get; set; } = string.Empty;

    /// <summary>The Norvia Partner API's scope: <c>&lt;its client ID&gt;/.default</c>.</summary>
    [Required]
    public string Scope { get; set; } = string.Empty;

    /// <summary>The client ID of the Simulator's own managed identity, which each Persona's app registration trusts.</summary>
    [Required]
    public string ManagedIdentityClientId { get; set; } = string.Empty;
}
