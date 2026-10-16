using System.ComponentModel.DataAnnotations;

namespace Norvia.Simulator.Infrastructure.Entra;

public sealed class EntraTmsTokenOptions
{
    /// <summary>The TMS API's scope: <c>&lt;its client ID&gt;/.default</c>.</summary>
    [Required]
    public string Scope { get; set; } = string.Empty;

    /// <summary>The client ID of the Simulator's managed identity, which holds the TMS API's manage role.</summary>
    [Required]
    public string ManagedIdentityClientId { get; set; } = string.Empty;
}
