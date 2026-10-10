using System.ComponentModel.DataAnnotations;

namespace Norvia.Integration.LocalTripDispatchWorker;

/// <summary>The Driver app's Trip endpoint, and the credential Key Vault holds in Azure as <c>driver-app-endpoint-key</c>.</summary>
public sealed class DriverAppEndpointOptions
{
    [Required]
    public Uri? Address { get; set; }

    [Required]
    public string ApiKey { get; set; } = string.Empty;
}
