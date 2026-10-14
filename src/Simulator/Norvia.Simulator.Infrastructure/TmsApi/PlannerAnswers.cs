using System.Text.Json;
using System.Text.Json.Nodes;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Planner;

namespace Norvia.Simulator.Infrastructure.TmsApi;

/// <summary>
/// Reads the TMS API's answers to the Planner's calls (the TMS API's OpenAPI document): an assignment or reassignment that
/// went through, a refusal with its code, and the Own fleet's open days. An answer that says neither is not received.
/// </summary>
internal static class PlannerAnswers
{
    public static PlannerAnswer Assigned(int statusCode, string body, string? correlationHeader) => Read(statusCode, body, correlationHeader, LogOutcome.Planned, shipmentNumber: null, [200, 201]);

    public static PlannerAnswer Reassigned(int statusCode, string body, string? correlationHeader, string shipmentNumber) => Read(statusCode, body, correlationHeader, LogOutcome.Reassigned, shipmentNumber, [200]);

    public static PlannerAnswer NotReceived(string detail) => new(LogOutcome.PlanningNotReceived, null, null, detail);

    public static IReadOnlyList<FleetDay>? Days(string body)
    {
        if (Parse(body) is not JsonArray days)
        {
            return null;
        }

        var result = new List<FleetDay>(days.Count);
        foreach (var day in days.OfType<JsonObject>())
        {
            if (day["date"]?.GetValue<string>() is not { } date
                || !DateOnly.TryParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed)
                || day["load"] is not JsonObject load)
            {
                return null;
            }

            result.Add(new FleetDay(
                parsed, Number(load, "capacityKg"), (int)Number(load, "capacityPalletPlaces"), Number(load, "demandKg"), (int)Number(load, "demandPalletPlaces")));
        }

        return result;
    }

    private static PlannerAnswer Read(int statusCode, string body, string? correlationHeader, LogOutcome success, string? shipmentNumber, int[] successStatuses)
    {
        var json = Parse(body) as JsonObject;
        var correlationId = Text(json, "correlationId") ?? correlationHeader;

        if (successStatuses.Contains(statusCode) && json is not null)
        {
            return new PlannerAnswer(success, correlationId, shipmentNumber ?? Text(json, "shipmentNumber"), null);
        }

        // A refusal names its rule by a stable code. Never its message, which may quote what was booked.
        if (statusCode is 404 or 409 or 422 && Text(json, "code") is { } code)
        {
            return new PlannerAnswer(LogOutcome.PlanningRefused, correlationId, shipmentNumber, $"{code} ({statusCode})");
        }

        return new PlannerAnswer(LogOutcome.PlanningNotReceived, correlationId, shipmentNumber, $"The TMS API answered {statusCode} without an answer to the call.");
    }

    private static decimal Number(JsonObject json, string name) => json[name] is JsonValue value && value.TryGetValue<decimal>(out var number) ? number : 0;

    private static string? Text(JsonObject? json, string name) =>
        json?[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;

    private static JsonNode? Parse(string body)
    {
        try
        {
            return JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
