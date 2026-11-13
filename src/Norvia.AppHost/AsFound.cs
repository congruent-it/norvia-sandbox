using System.Text.Json;

namespace Norvia.AppHost;

/// <summary>Reads the as-found toggles (docs/operations/as-found.md) from <c>ESTATE_ASFOUND</c>, the object infra/as-found.ps1 stores in the azd environment.</summary>
internal static class AsFound
{
    /// <summary>Whether the boolean <paramref name="toggle"/> is true. Unset, empty or without the toggle is the fixed state.</summary>
    public static bool IsSet(string? asFound, string toggle) =>
        !string.IsNullOrWhiteSpace(asFound)
        && JsonDocument.Parse(asFound).RootElement.TryGetProperty(toggle, out var value)
        && value.ValueKind == JsonValueKind.True;

    /// <summary>
    /// The log level every application runs at, by environment variable. <c>chattyTelemetry</c> (the spend toggle of docs/operations/as-found.md)
    /// raises it to Debug, so what the applications log, and with it what Log Analytics ingests and bills per GB, grows with no business reason.
    /// The healthy level is written out too, so going back to the fixed state changes the Container Apps back.
    /// </summary>
    public static KeyValuePair<string, string> LogLevel(string? asFound) =>
        new("Logging__LogLevel__Default", IsSet(asFound, "chattyTelemetry") ? "Debug" : "Information");

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
