using System.Text.Json;
using System.Text.Json.Nodes;
using Norvia.Integration.CarrierBooking;

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
            : OutcomeOf(tmsStatusCode);
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

    private static Dictionary<string, object?> OutcomeOf(int tmsStatusCode) => tmsStatusCode switch
    {
        200 => TrackingKeys.OutcomeOf(TrackingKeys.Succeeded),
        403 => TrackingKeys.OutcomeOf(TrackingKeys.Refused, "CarrierNotAllowed"),
        404 => TrackingKeys.OutcomeOf(TrackingKeys.Refused, "BookingNotFound"),
        409 => TrackingKeys.OutcomeOf(TrackingKeys.Refused, "BookingNotOpen"),
        >= 400 and < 500 and not 408 and not 429 => TrackingKeys.OutcomeOf(TrackingKeys.Failed, "TmsCallRefused"),
        _ => TrackingKeys.OutcomeOf(TrackingKeys.Failed, "TmsUnavailable"),
    };

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
}
