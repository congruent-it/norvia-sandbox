using System.Text.Json;
using System.Text.Json.Nodes;
using Norvia.PartnerPortal.Domain.Bookings;

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
