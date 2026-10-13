using System.Text.Json;
using System.Text.Json.Nodes;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Log;

namespace Norvia.Simulator.Infrastructure.PartnerApi;

/// <summary>
/// Reads Norvia's answer to a booking (contracts/partner-api/README.md, Responses, and API Management's refusals). An answer
/// that doesn't say a Transport Order exists, or why the booking was rejected, means it wasn't received.
/// </summary>
internal static class BookingAnswers
{
    public static BookingAnswer Read(int statusCode, string body, string? correlationId)
    {
        var json = Parse(body);
        var messageId = Text(json, "messageId") ?? correlationId;

        return (statusCode, Text(json, "result")) switch
        {
            (201, "ACCEPTED") when Text(json, "norviaOrderNo") is { } number => new BookingAnswer(LogOutcome.Accepted, messageId, number, null),
            (200, "ALREADY_BOOKED") when Text(json, "norviaOrderNo") is { } number => new BookingAnswer(LogOutcome.AlreadyBooked, messageId, number, null),
            (422, "REJECTED") when Codes(json) is { Count: > 0 } codes => new BookingAnswer(LogOutcome.Rejected, messageId, null, string.Join(", ", codes)),
            (403, _) => new BookingAnswer(LogOutcome.Refused, messageId, null, Text(json, "text") ?? "Norvia didn't recognise this Persona's subscription key or token."),
            (401, _) => new BookingAnswer(LogOutcome.Refused, messageId, null, "Norvia didn't recognise this Persona's subscription key or token."),
            _ => NotReceived(messageId, $"Norvia answered {statusCode} without an answer to the booking."),
        };
    }

    public static BookingAnswer NotReceived(string? correlationId, string detail) =>
        new(LogOutcome.NotReceived, correlationId, null, detail);

    // Codes only: a reason's text may quote what was booked.
    private static List<string>? Codes(JsonObject? json)
    {
        var codes = new List<string>();
        foreach (var error in json?["errors"] as JsonArray ?? [])
        {
            if (Text(error as JsonObject, "code") is not { } code)
            {
                return null;
            }

            codes.Add(code);
        }

        return codes;
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
