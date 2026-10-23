using System.Text.Json;

namespace Norvia.AppHost;

/// <summary>Reads the as-found toggles (docs/operations/as-found.md) from <c>ESTATE_ASFOUND</c>, the object infra/as-found.sh stores in the azd environment.</summary>
internal static class AsFound
{
    /// <summary>Whether the boolean <paramref name="toggle"/> is true. Unset, empty or without the toggle is the fixed state.</summary>
    public static bool IsSet(string? asFound, string toggle) =>
        !string.IsNullOrWhiteSpace(asFound)
        && JsonDocument.Parse(asFound).RootElement.TryGetProperty(toggle, out var value)
        && value.ValueKind == JsonValueKind.True;

    /// <summary>
    /// The TMS settings the business safety-net toggles flow into, by environment variable. Healthy settings are written out too, so going
    /// back to the fixed state changes the Container App back.
    /// </summary>
    public static IReadOnlyDictionary<string, string> TmsSettings(string? asFound) => new Dictionary<string, string>
    {
        ["ShipmentStale__ThresholdsSet"] = (!IsSet(asFound, "staleThresholdsUnset")).ToString().ToLowerInvariant(),
        ["ProofsOfDelivery__MissingThresholdSet"] = (!IsSet(asFound, "proofMissingThresholdUnset")).ToString().ToLowerInvariant(),
        ["LateDelivery__CheckEnabled"] = (!IsSet(asFound, "lateDeliveryCheckOff")).ToString().ToLowerInvariant(),
    };
}
