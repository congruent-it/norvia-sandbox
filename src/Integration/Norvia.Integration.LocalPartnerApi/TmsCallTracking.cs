using System.Text.Json;
using System.Text.Json.Nodes;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalPartnerApi;

/// <summary>
/// The tracking contract's keys of the call to the TMS (coding guidelines §7), worked out as the intake Logic App's
/// tracked properties on its <c>Submit_to_the_TMS</c> step work them out
/// (src/Integration/IntakeLogicApp/transport-order-intake/workflow.json), so a local trail reads as one in Azure: the
/// <c>outcome</c>, the <c>failureCode</c>, and the <c>transportOrderNumber</c> from the TMS's answer. A key that doesn't
/// apply is left out, never sent empty.
/// </summary>
internal static class TmsCallTracking
{
    /// <param name="tmsStatusCode">The status the TMS answered with, or 0 when it didn't answer.</param>
    /// <param name="tmsResponse">The TMS's answer as JSON text, or empty when it didn't answer.</param>
    public static Dictionary<string, object?> Of(int tmsStatusCode, string tmsResponse)
    {
        var keys = OutcomeOf(tmsStatusCode);
        if (TransportOrderNumberIn(tmsResponse) is { Length: > 0 } transportOrderNumber)
        {
            keys[TrackingKeys.TransportOrderNumber] = transportOrderNumber;
        }

        return keys;
    }

    private static Dictionary<string, object?> OutcomeOf(int tmsStatusCode) => tmsStatusCode switch
    {
        200 or 201 => TrackingKeys.OutcomeOf(TrackingKeys.Succeeded),
        422 => TrackingKeys.OutcomeOf(TrackingKeys.Refused, "TransportOrderRejected"),
        >= 400 and < 500 and not 408 and not 429 => TrackingKeys.OutcomeOf(TrackingKeys.Failed, "TmsCallRefused"),
        _ => TrackingKeys.OutcomeOf(TrackingKeys.Failed, "TmsUnavailable"),
    };

    private static string? TransportOrderNumberIn(string tmsResponse)
    {
        try
        {
            return (JsonNode.Parse(tmsResponse) as JsonObject)?[TrackingKeys.TransportOrderNumber] is JsonValue number
                && number.TryGetValue<string>(out var text) ? text : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
