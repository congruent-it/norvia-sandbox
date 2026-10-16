using System.ComponentModel.DataAnnotations;

namespace Norvia.Simulator.Infrastructure.Entra;

public sealed class EntraDriverTokenOptions
{
    [Required]
    public string TenantId { get; set; } = string.Empty;

    /// <summary>The Driver app API's scope: <c>&lt;its client ID&gt;/.default</c>.</summary>
    [Required]
    public string Scope { get; set; } = string.Empty;

    /// <summary>The client ID of the Simulator's own managed identity, which each Driver's client app trusts.</summary>
    [Required]
    public string ManagedIdentityClientId { get; set; } = string.Empty;

    /// <summary>Each Driver's client app, as the estate outputs them: <c>driverId=clientId</c> pairs separated by semicolons.</summary>
    [Required]
    public string DriverClientIds { get; set; } = string.Empty;

    public string ClientIdOf(string driverId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driverId);
        return DriverClientIds.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(pair => pair.Length == 2 && pair[0] == driverId)
            .Select(pair => pair[1])
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"No client app is configured for Driver '{driverId}'.");
    }
}
