using System.ComponentModel.DataAnnotations;

namespace Norvia.Simulator.Infrastructure.Reset;

/// <summary>
/// Where the Partner portal's reset door is and the credential it asks for. The portal has no token validation of its own, so its reset takes an API key,
/// as its other endpoints do. Without them the Simulator reports the portal as one it couldn't ask, and the reset is not offered.
/// </summary>
public sealed class PartnerPortalResetOptions
{
    public const string SectionName = "PartnerPortal";

    /// <summary>The Partner portal's address, such as <c>http://localhost:5400</c>.</summary>
    public Uri? BaseAddress { get; set; }

    /// <summary>The key the portal's reset endpoint asks for. Locally the AppHost gives both the same one for the run; in Azure it is a Key Vault secret.</summary>
    public string? ResetApiKey { get; set; }

    public bool IsConfigured => BaseAddress is not null && !string.IsNullOrWhiteSpace(ResetApiKey);
}
