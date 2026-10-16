using System.ComponentModel.DataAnnotations;

namespace Norvia.Simulator.Infrastructure.DriverApi;

/// <summary>
/// Where the Driver app API is, and how the Simulator gets a token for it as a Driver: signed locally, with the Driver ID as the token's app role, as Entra
/// would issue it to the Driver's account. In Azure the Drivers' tokens come from Entra, which the Azure slice adds.
/// </summary>
public sealed class DriverApiOptions
{
    public const string SectionName = "DriverApp";

    /// <summary>The Driver app API's address, such as <c>http://localhost:5300</c>.</summary>
    [Required]
    public Uri? BaseAddress { get; set; }

    /// <summary>Locally the Simulator signs each Driver's token itself, as Entra would issue it.</summary>
    [Required]
    public LocalDriverTokenOptions? LocalTokens { get; set; }
}
