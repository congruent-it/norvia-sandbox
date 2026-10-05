using System.Text.Json;
using System.Text.Json.Nodes;
using Norvia.Integration.CarrierBooking;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalPartnerApi;

/// <summary>
/// The tracking contract's keys of a Carrier's answer (coding guidelines §7), worked out as the Carrier-answer Logic
/// App's tracked properties on its <c>Answer_to_the_TMS</c> step work them out
/// (src/Integration/AnswerLogicApp/carrier-answer/workflow.json), so a local trail reads as one in Azure: the
/// <c>outcome</c>, the <c>failureCode</c>, the <c>carrierBookingNumber</c> and the <c>shipmentNumber</c> from the TMS's
/// answer. A key that doesn't apply is left out, never sent empty.
/// </summary>
internal static class CarrierAnswerTracking
{
    /// <param name="mapped">What the map made of the Carrier's answer.</param>
    /// <param name="tmsStatusCode">The status the TMS answered with, or 0 when it didn't answer or wasn't called.</param>
    /// <param name="tmsResponse">The TMS's answer as JSON text, or empty when it didn't answer.</param>
    public static Dictionary<string, object?> Of(MappedCarrierAnswer mapped, int tmsStatusCode, string tmsResponse)
    {
        var keys = mapped.Problem is not null
            ? TrackingKeys.OutcomeOf(TrackingKeys.Refused, "CarrierAnswerUnreadable")
            : TmsOutcome(tmsStatusCode);
        var tms = Parse(tmsResponse);
        if (mapped.CarrierBookingNumber is { } number)
        {
            keys[TrackingKeys.CarrierBookingNumber] = number;
        }

        if (TextIn(tms, TrackingKeys.ShipmentNumber) is { Length: > 0 } shipmentNumber)
        {
            keys[TrackingKeys.ShipmentNumber] = shipmentNumber;
        }

        return keys;
    }

    private static JsonObject? Parse(string tmsResponse)
    {
        try
        {
            return JsonNode.Parse(tmsResponse) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TextIn(JsonObject? node, string name) =>
        node?[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static Dictionary<string, object?> TmsOutcome(int tmsStatusCode)
    {
        var (outcome, failureCode) = TmsAnswerOutcome.Of(tmsStatusCode);
        return TrackingKeys.OutcomeOf(outcome, failureCode);
    }
}
