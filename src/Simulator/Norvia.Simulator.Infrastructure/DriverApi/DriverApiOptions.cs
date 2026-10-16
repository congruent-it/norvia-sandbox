using System.ComponentModel.DataAnnotations;
using Norvia.Simulator.Infrastructure.Entra;

namespace Norvia.Simulator.Infrastructure.DriverApi;

/// <summary>
/// Where the Driver app API is, and how the Simulator gets a token for it as a Driver: signed locally, with the Driver ID as the token's app role, as Entra
/// would issue it to the Driver's account, or in Azure from Entra for the Driver's client app (ADR 0042). Exactly one of <see cref="Entra"/> and
/// <see cref="LocalTokens"/> is configured.
/// </summary>
public sealed class DriverApiOptions
{
    public const string SectionName = "DriverApp";

    /// <summary>The Driver app API's address, such as <c>http://localhost:5300</c>.</summary>
    [Required]
    public Uri? BaseAddress { get; set; }

    /// <summary>Locally the Simulator signs each Driver's token itself, as Entra would issue it.</summary>
    public LocalDriverTokenOptions? LocalTokens { get; set; }

    /// <summary>In Azure each Driver is a client app of Entra holding their Driver ID as its one app role, and trusting the Simulator's managed identity.</summary>
    public EntraDriverTokenOptions? Entra { get; set; }
}
