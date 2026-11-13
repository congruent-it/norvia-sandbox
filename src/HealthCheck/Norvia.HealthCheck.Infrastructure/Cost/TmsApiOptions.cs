using System.ComponentModel.DataAnnotations;

namespace Norvia.HealthCheck.Infrastructure.Cost;

/// <summary>Where the TMS API is and the scope of the token the app asks for as its own managed identity. Set only in Azure, where the cost is read from Cost Management.</summary>
public sealed class TmsApiOptions
{
    public const string SectionName = "TmsApi";

    [Required]
    public Uri? BaseAddress { get; set; }

    /// <summary>The TMS API's scope, <c>{its client ID}/.default</c>. The estate grants the app's identity the one role <c>TransportOrders.Volume.Read</c> on it.</summary>
    [Required]
    public string Scope { get; set; } = string.Empty;
}
