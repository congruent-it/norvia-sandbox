using System.Text.Json;
using System.Text.Json.Nodes;
using Norvia.PartnerPortal.Domain.Bookings;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Infrastructure.PartnerApi;

/// <summary>
/// Reads Norvia's answer to a booking (contracts/partner-api/README.md, Responses, and API Management's refusals). An
/// answer that doesn't say a Transport Order exists, or why the booking was rejected, means the booking wasn't received:
/// nothing was booked, and sending it again is safe.
/// </summary>
internal static class PartnerAnswers
{
    public const string UnrecognisedCredentials = "Norvia didn't recognise this Persona's subscription key or token.";

    public static NorviaAnswer Read(int statusCode, string body, string? correlationId)
    {
        var json = Parse(body);
        var messageId = Text(json, "messageId") ?? correlationId;

        return (statusCode, Text(json, "result")) switch
        {
            (201, "ACCEPTED") when Text(json, "norviaOrderNo") is { } number => NorviaAnswer.Accepted(number, messageId),
            (200, "ALREADY_BOOKED") when Text(json, "norviaOrderNo") is { } number => NorviaAnswer.AlreadyBooked(number, messageId),
            (422, "REJECTED") when Reasons(json) is { Count: > 0 } reasons => NorviaAnswer.Rejected(reasons, messageId),
            (403, _) => NorviaAnswer.Refused(Text(json, "text") ?? UnrecognisedCredentials, messageId),
            (401, _) => NorviaAnswer.Refused(UnrecognisedCredentials, messageId),
            _ => NorviaAnswer.NotReceived(messageId),
        };
    }

    /// <summary>
    /// Reads Norvia's reply to a Carrier's answer (contracts/partner-api/README.md, Carrier answer, and API Management's
    /// refusals). A reply that doesn't say the answer was recorded, or why not, means it wasn't received: nothing was
    /// recorded, and answering again is safe.
    /// </summary>
    public static NorviaReply ReadReply(int statusCode, string body, string? correlationId)
    {
        var json = Parse(body);
        var messageId = Text(json, "messageId") ?? correlationId;

        return (statusCode, Text(json, "result")) switch
        {
            (200, "RECORDED") => NorviaReply.Recorded(messageId, json?["repeated"] is JsonValue repeated && repeated.TryGetValue<bool>(out var value) && value),
            (409, "BOOKING_NOT_OPEN") => Refusal(CarrierAnswerResult.BookingNotOpen, json, messageId, "Norvia no longer has this booking open."),
            (404, "BOOKING_NOT_FOUND") => Refusal(CarrierAnswerResult.BookingNotFound, json, messageId, "Norvia has no such booking."),
            (403, "NOT_ALLOWED") => Refusal(CarrierAnswerResult.NotAllowed, json, messageId, "This booking is not yours to answer."),
            (400, "INVALID") => Refusal(CarrierAnswerResult.Invalid, json, messageId, "Norvia can't use this answer."),
            (403, _) => NorviaReply.Refusal(CarrierAnswerResult.Refused, Text(json, "text") ?? UnrecognisedCredentials, messageId),
            (401, _) => NorviaReply.Refusal(CarrierAnswerResult.Refused, UnrecognisedCredentials, messageId),
            _ => NorviaReply.NotReceived(messageId),
        };
    }

    /// <summary>
    /// Reads API Management's answer to a Tracking event (contracts/partner-api/README.md, Carrier tracking): 202 Accepted
    /// and nothing more. Anything else that isn't a refusal of the Persona's credentials means it wasn't received.
    /// </summary>
    public static TrackingReply ReadTrackingReply(int statusCode, string body, string? correlationId)
    {
        var json = Parse(body);
        var messageId = Text(json, "messageId") ?? correlationId;

        return statusCode switch
        {
            202 => TrackingReply.Accepted(messageId),
            403 => TrackingReply.Refused(Text(json, "text") ?? UnrecognisedCredentials, messageId),
            401 => TrackingReply.Refused(UnrecognisedCredentials, messageId),
            _ => TrackingReply.NotReceived(messageId),
        };
    }

    private static NorviaReply Refusal(CarrierAnswerResult result, JsonObject? json, string? messageId, string fallbackText) =>
        NorviaReply.Refusal(result, Text(json, "text") ?? fallbackText, messageId);

    private static List<RejectionReason>? Reasons(JsonObject? json)
    {
        var reasons = new List<RejectionReason>();
        foreach (var error in json?["errors"] as JsonArray ?? [])
        {
            if (Text(error as JsonObject, "code") is not { } code || Text(error as JsonObject, "text") is not { } text)
            {
                return null;
            }

            reasons.Add(new RejectionReason(code, text));
        }

        return reasons;
    }

    private static string? Text(JsonObject? json, string name) =>
        json?[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;

    private static JsonObject? Parse(string body)
    {
        try
        {
            return JsonNode.Parse(body) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
