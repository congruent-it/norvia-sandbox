using System.Globalization;
using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking;

/// <summary>
/// The Carrier-answer map: turns a Carrier's confirmation or rejection in the Carrier answer format into the Canonical
/// Model answer the TMS's Carrier booking API takes (P4). The Carrier is the one API Management took from the token,
/// never one the Carrier's payload names (ADR 0005).
/// </summary>
/// <remarks>
/// Unlike the intake map, it doesn't leave out what it can't read for the TMS to reject: a decision or a reason code it
/// doesn't know would otherwise reach the TMS as the default outcome. An answer breaking P4's answer rules for its own
/// content (no Carrier booking reference, no reason text for <c>OTHER</c>) is reported here, as the TMS would, so the
/// Carrier hears <c>INVALID</c> rather than that Norvia didn't receive its answer.
/// </remarks>
public static class CarrierAnswerMap
{
    private static readonly Dictionary<string, string> ReasonCodes = new(StringComparer.Ordinal)
    {
        ["NO_CAPACITY"] = "NoCapacity",
        ["LANE_NOT_SERVED"] = "LaneNotServed",
        ["WINDOW_NOT_FEASIBLE"] = "WindowNotFeasible",
        ["GOODS_NOT_ACCEPTED"] = "GoodsNotAccepted",
        ["OTHER"] = "Other",
    };

    /// <param name="carrierAnswer">The Carrier's answer as JSON text.</param>
    /// <param name="carrierId">The Carrier ID API Management took from the token.</param>
    public static MappedCarrierAnswer ToCarrierBookingAnswer(string carrierAnswer, string carrierId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(carrierId);
        var answer = Parse(carrierAnswer);
        var number = Text(answer, "carrierBookingNumber");
        if (number is null || !IsCarrierBookingNumber(number))
        {
            return MappedCarrierAnswer.Invalid(null, "The answer needs the carrierBookingNumber of the request it answers, such as CB-0000042.");
        }

        return Text(answer, "decision") switch
        {
            "CONFIRMED" => Confirmed(answer!, number, carrierId),
            "DECLINED" => Declined(answer!, number, carrierId),
            _ => MappedCarrierAnswer.Invalid(number, "The decision must be CONFIRMED or DECLINED."),
        };
    }

    private static MappedCarrierAnswer Confirmed(JsonObject answer, string number, string carrierId)
    {
        if (Text(answer, "bookingRef") is not { } reference)
        {
            return MappedCarrierAnswer.Invalid(number, "A confirmation needs the Carrier's own booking reference (bookingRef).");
        }

        var body = new JsonObject { ["carrierId"] = carrierId, ["outcome"] = "Confirmed", ["carrierBookingReference"] = reference };
        if (Text(answer, "plannedCollection") is { } planned)
        {
            // Kind says whether the text carried an offset: without one the time is Unspecified, and no instant.
            if (!DateTime.TryParse(planned, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var instant) || instant.Kind == DateTimeKind.Unspecified)
            {
                return MappedCarrierAnswer.Invalid(number, "plannedCollection must be a date and time with its UTC offset, such as 2030-10-02T08:30:00+02:00.");
            }

            body["plannedCollectionTime"] = instant.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'+00:00'", CultureInfo.InvariantCulture);
        }

        return MappedCarrierAnswer.Mapped(number, body);
    }

    private static MappedCarrierAnswer Declined(JsonObject answer, string number, string carrierId)
    {
        if (Text(answer, "declineReason") is not { } code)
        {
            return MappedCarrierAnswer.Invalid(number, "A rejection needs a declineReason.");
        }

        if (!ReasonCodes.TryGetValue(code, out var reasonCode))
        {
            return MappedCarrierAnswer.Invalid(number, "The declineReason must be NO_CAPACITY, LANE_NOT_SERVED, WINDOW_NOT_FEASIBLE, GOODS_NOT_ACCEPTED or OTHER.");
        }

        var text = Text(answer, "declineText");
        if (reasonCode == "Other" && text is null)
        {
            return MappedCarrierAnswer.Invalid(number, "A rejection with declineReason OTHER needs its declineText.");
        }

        var body = new JsonObject { ["carrierId"] = carrierId, ["outcome"] = "Rejected", ["reasonCode"] = reasonCode };
        if (text is not null)
        {
            body["reasonText"] = text;
        }

        return MappedCarrierAnswer.Mapped(number, body);
    }

    private static bool IsCarrierBookingNumber(string number) =>
        number.Length == 10 && number.StartsWith("CB-", StringComparison.Ordinal) && number.AsSpan(3).IndexOfAnyExceptInRange('0', '9') < 0;

    private static JsonObject? Parse(string json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonObject? node, string name) =>
        node?[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;
}
