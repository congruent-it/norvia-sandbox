namespace Norvia.PartnerPortal.Web;

/// <summary>The tracking contract's keys (coding guidelines §7) as every Persona endpoint puts them in its logging scope.</summary>
internal static class EndpointTracking
{
    /// <summary>The keys the request carries: the flow, the party and the correlation ID, and the business keys it names. A key that doesn't apply is left out.</summary>
    public static Dictionary<string, object?> Keys(string flow, string partyId, string? correlationId, params (string Key, string? Value)[] businessKeys)
    {
        var keys = new Dictionary<string, object?> { ["flow"] = flow, ["partyId"] = partyId };
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            keys["correlationId"] = correlationId;
        }

        foreach (var (key, value) in businessKeys)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                keys[key] = value;
            }
        }

        return keys;
    }

    public static Dictionary<string, object?> Outcome(string outcome, string? failureCode) =>
        failureCode is null ? new() { ["outcome"] = outcome } : new() { ["outcome"] = outcome, ["failureCode"] = failureCode };
}
