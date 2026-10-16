using System.ComponentModel.DataAnnotations;
using Norvia.Simulator.Infrastructure.Entra;

namespace Norvia.Simulator.Infrastructure.TmsApi;

/// <summary>
/// Where the TMS API is, and how the Simulator gets a token for it as the Planner: signed locally, as the TMS UI's is, or in Azure
/// from Entra for the Simulator's managed identity (ADR 0042). Exactly one of <see cref="Entra"/> and <see cref="LocalTokens"/> is configured.
/// </summary>
public sealed class TmsApiOptions
{
    public const string SectionName = "TmsApi";

    /// <summary>The TMS API's address, such as <c>http://localhost:5200</c>.</summary>
    [Required]
    public Uri? BaseAddress { get; set; }

    /// <summary>Locally the Simulator signs its own token for the TMS API, as Entra would issue it.</summary>
    public LocalTmsTokenOptions? LocalTokens { get; set; }

    /// <summary>In Azure the token comes from Entra for the Simulator's managed identity, which holds the TMS API's manage role.</summary>
    public EntraTmsTokenOptions? Entra { get; set; }
}
