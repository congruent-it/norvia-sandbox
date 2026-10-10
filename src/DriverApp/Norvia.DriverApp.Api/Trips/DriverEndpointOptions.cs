using System.ComponentModel.DataAnnotations;

namespace Norvia.DriverApp.Api.Trips;

/// <summary>
/// The credential the Trip endpoint asks of its caller, the P11 Trip dispatch Logic App. In Azure the Logic App and the Driver app
/// read the same secret, <c>driver-app-endpoint-key</c>, from Key Vault; locally the AppHost gives both the same one for the run.
/// </summary>
public sealed class DriverEndpointOptions
{
    public const string SectionName = "DriverEndpoint";

    [Required]
    public string ApiKey { get; set; } = string.Empty;
}
