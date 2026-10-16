using System.Text.Json;
using System.Text.Json.Nodes;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Log;

namespace Norvia.Simulator.Infrastructure.DriverApi;

/// <summary>
/// Reads the Driver app API's answers to a Driver's calls (its OpenAPI document): a report it took, a refusal with its code, and the Driver's Trips. An answer
/// that says neither is not received.
/// </summary>
internal static class DriverAnswers
{
    public static DriverCallAnswer Reported(int statusCode, string body, string? correlationHeader)
    {
        var json = Parse(body) as JsonObject;
        if (statusCode == 200 && json is not null)
        {
            // A report made before has no correlation ID of its own: the first report has the trail.
            return new DriverCallAnswer(LogOutcome.DriverReported, Text(json, "correlationId") ?? correlationHeader, json["alreadyReported"]?.GetValue<bool>() == true ? "Reported before" : null);
        }

        // A refusal names its rule by a stable code. Never its message, which may quote what the Driver said.
        if (statusCode is >= 400 and < 500 && Text(json, "code") is { } code)
        {
            return new DriverCallAnswer(LogOutcome.DriverRefused, correlationHeader, $"{code} ({statusCode})");
        }

        return NotReceived($"The Driver app API answered {statusCode} without an answer to the call.");
    }

    public static DriverCallAnswer NotReceived(string detail) => new(LogOutcome.DriverNotReceived, null, detail);

    /// <summary>The Driver's Trips, or null when the answer isn't a list the Driver can read.</summary>
    public static IReadOnlyList<DriverTripSnapshot>? Trips(string body)
    {
        if (Parse(body) is not JsonArray trips)
        {
            return null;
        }

        var result = new List<DriverTripSnapshot>(trips.Count);
        foreach (var trip in trips.OfType<JsonObject>())
        {
            if (Text(trip, "tripNumber") is not { } number || trip["version"] is not JsonValue version || !version.TryGetValue<int>(out var versionNumber) || Text(trip, "status") is not { } status)
            {
                return null;
            }

            var notRecorded = (trip["reports"] as JsonArray)?.OfType<JsonObject>().Count(report => Text(report, "state") is "Refused" or "NotRecorded") ?? 0;
            result.Add(new DriverTripSnapshot(number, versionNumber, StatusOf(status), notRecorded));
        }

        return result;
    }

    private static DriverTripStatus StatusOf(string status) => status switch
    {
        "In progress" => DriverTripStatus.InProgress,
        "Completed" => DriverTripStatus.Completed,
        _ => DriverTripStatus.Dispatched,
    };

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
