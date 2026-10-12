using System.Text.Json;
using System.Text.Json.Nodes;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalDriverReportsWorker;

/// <summary>What a <c>TripStartReported</c> names, and the tracking contract's keys it gives the trail.</summary>
internal sealed record DriverReport(string EventId, string CorrelationId, string TripNumber, string DriverId)
{
    private const string TripStartReported = "TripStartReported";

    public Dictionary<string, object?> ContractKeys => new()
    {
        [TrackingKeys.CorrelationId] = CorrelationId,
        [TrackingKeys.Flow] = TrackingKeys.TripExecution,
        [TrackingKeys.PartyId] = DriverId,
        [TrackingKeys.TripNumber] = TripNumber,
        [TrackingKeys.SourceMessageId] = EventId,
    };

    /// <summary>The report the message holds, or null when it isn't a <c>TripStartReported</c> the TMS can be asked to record.</summary>
    public static DriverReport? Read(string message)
    {
        JsonObject? parsed;
        try
        {
            parsed = JsonNode.Parse(message) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }

        return parsed is not null
            && Text(parsed, "eventType") == TripStartReported
            && Text(parsed, "eventId") is { } eventId && Guid.TryParse(eventId, out _)
            && Text(parsed, "correlationId") is { } correlationId
            && Text(parsed, "tripNumber") is { } tripNumber
            && Text(parsed, "driverId") is { } driverId
                ? new(eventId, correlationId, tripNumber, driverId)
                : null;
    }

    private static string? Text(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;
}
