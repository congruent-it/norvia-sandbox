using System.ComponentModel.DataAnnotations;

namespace Norvia.PartnerPortal.Infrastructure.Faults;

/// <summary>
/// The applications that own faults, which the presenter panel switches through their APIs, and how the portal gets a token for them as the
/// presenter: signed locally, as the applications trust it, or in Azure from Entra for the portal's managed identity. Exactly one of
/// <see cref="Entra"/> and <see cref="LocalTokens"/> is configured.
/// </summary>
public sealed class FaultsOptions
{
    public const string SectionName = "Faults";

    /// <summary>The applications that own faults, by owner name such as <c>driver-app</c>.</summary>
    [Required]
    [MinLength(1)]
    public Dictionary<string, FaultOwnerOptions> Owners { get; set; } = [];

    public LocalFaultTokenOptions? LocalTokens { get; set; }

    public EntraFaultTokenOptions? Entra { get; set; }

    [Required]
    public FaultLogicAppOptions LogicApp { get; set; } = new();
}

public sealed class FaultOwnerOptions
{
    /// <summary>The application's API, where its fault endpoints are, such as <c>http://localhost:5400</c>.</summary>
    [Required]
    public Uri? Address { get; set; }

    /// <summary>The application's API as its tokens name it: the audience locally, and in Azure the client ID the token's scope is built from.</summary>
    [Required]
    public string Audience { get; set; } = string.Empty;
}

public sealed class LocalFaultTokenOptions
{
    [Required]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>The portal's local client ID, which stands in for its managed identity.</summary>
    [Required]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>The key the applications trust locally, base64-encoded.</summary>
    [Required]
    public string SigningKey { get; set; } = string.Empty;
}

public sealed class EntraFaultTokenOptions
{
    /// <summary>The client ID of the portal's managed identity, which the estate grants the Demo.Fault role on each application that owns a fault.</summary>
    [Required]
    public string ManagedIdentityClientId { get; set; } = string.Empty;
}

/// <summary>
/// The Driver reports Logic App the <c>driver-reports-logic-app-disabled</c> fault disables. In Azure it is the site named <see cref="Name"/> in
/// the resource group <see cref="ResourceGroupId"/>, which the portal's managed identity may only start and stop; locally it is the worker that
/// plays the Logic App, which stops while the file at <see cref="FlagPath"/> exists. Exactly one of the two is configured.
/// </summary>
public sealed class FaultLogicAppOptions
{
    /// <summary>The estate's resource group, <c>/subscriptions/{id}/resourceGroups/{name}</c>.</summary>
    public string? ResourceGroupId { get; set; }

    /// <summary>The Logic App's name.</summary>
    public string? Name { get; set; }

    /// <summary>The flag file the local worker watches.</summary>
    public string? FlagPath { get; set; }

    public bool IsAzure => FlagPath is null;

    public bool IsValid => IsAzure ? !string.IsNullOrWhiteSpace(ResourceGroupId) && !string.IsNullOrWhiteSpace(Name) : !string.IsNullOrWhiteSpace(FlagPath) && ResourceGroupId is null && Name is null;
}
