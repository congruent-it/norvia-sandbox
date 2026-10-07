using System.ComponentModel.DataAnnotations;

namespace Norvia.Integration.LocalCarrierCancellationWorker;

/// <summary>A Carrier Persona's cancellation endpoint in the Partner portal, and the credential Key Vault holds in Azure.</summary>
public sealed class CarrierEndpointOptions
{
    [Required]
    public Uri? Address { get; set; }

    [Required]
    public string ApiKey { get; set; } = string.Empty;
}
