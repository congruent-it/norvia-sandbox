using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Log;

namespace Norvia.Simulator.Infrastructure.PartnerApi;

/// <summary>
/// Reads Norvia's answers to a Carrier's calls (contracts/partner-api/README.md, Carrier answer responses, Carrier tracking and
/// Carrier Proof of delivery, and API Management's refusals). Only a code, never the text Norvia words a refusal with.
/// </summary>
internal static class CarrierAnswers
{
    /// <summary>Norvia's answer to a Carrier's answer to a Carrier booking.</summary>
    public static CarrierCallAnswer Answered(int statusCode, string body, string? correlationHeader)
    {
        var json = Parse(body);
        var correlationId = Text(json, "messageId") ?? correlationHeader;
        var result = Text(json, "result");

        if (statusCode == 200 && result == "RECORDED")
        {
            return Text(json, "decision") switch
            {
                "CONFIRMED" => new CarrierCallAnswer(LogOutcome.Confirmed, correlationId, null),
                "DECLINED" => new CarrierCallAnswer(LogOutcome.Declined, correlationId, null),
                _ => NotReceived(correlationId, statusCode),
            };
        }

        if (result == "NOT_RECEIVED")
        {
            return new CarrierCallAnswer(LogOutcome.CarrierNotReceived, correlationId, Code(result, statusCode));
        }

        if (result is not null && statusCode is >= 400 and < 500)
        {
            return new CarrierCallAnswer(LogOutcome.CarrierRefused, correlationId, Code(result, statusCode));
        }

        return statusCode is 401 or 403 && json is not null
            ? new CarrierCallAnswer(LogOutcome.CarrierRefused, correlationId, $"Refused at the edge ({statusCode})")
            : NotReceived(correlationId, statusCode);
    }

    /// <summary>Norvia's answer to a tracking message or a Proof of delivery: accepted for later, refused, or not received.</summary>
    public static CarrierCallAnswer Accepted(int statusCode, string body, string? correlationHeader, LogOutcome success)
    {
        var correlationId = Text(Parse(body), "messageId") ?? correlationHeader;
        return statusCode switch
        {
            202 when correlationId is not null => new CarrierCallAnswer(success, correlationId, null),
            >= 400 and < 500 => new CarrierCallAnswer(LogOutcome.CarrierRefused, correlationId, $"Refused ({statusCode})"),
            _ => NotReceived(correlationId, statusCode),
        };
    }

    public static CarrierCallAnswer NotReceived(string detail) => new(LogOutcome.CarrierNotReceived, null, detail);

    private static CarrierCallAnswer NotReceived(string? correlationId, int statusCode) =>
        new(LogOutcome.CarrierNotReceived, correlationId, $"Norvia answered {statusCode} without an answer to the call.");

    private static string Code(string result, int statusCode) => string.Create(CultureInfo.InvariantCulture, $"{result} ({statusCode})");

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
