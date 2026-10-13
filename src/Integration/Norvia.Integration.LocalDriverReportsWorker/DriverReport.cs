using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalDriverReportsWorker;

/// <summary>What a Stop report names for the TMS's Tracking event: the Shipment, the Tracking event type, where and what the Driver said.</summary>
internal sealed record StopReport(int TripVersion, string OccurredAt, string ShipmentNumber, string TrackingEventType, string Place, string Postcode, string CountryCode, string? Remark);

/// <summary>What a <c>TripStartReported</c> or a <c>StopCompletedReported</c> names, and the tracking contract's keys it gives the trail.</summary>
/// <param name="Stop">What the TMS's Tracking event needs when the report is a Stop report; null for a Start Trip.</param>
internal sealed record DriverReport(string EventId, string CorrelationId, string TripNumber, string DriverId, StopReport? Stop)
{
    private const string TripStartReported = "TripStartReported";
    private const string StopCompletedReported = "StopCompletedReported";

    public Dictionary<string, object?> ContractKeys => new()
    {
        [TrackingKeys.CorrelationId] = CorrelationId,
        [TrackingKeys.Flow] = TrackingKeys.TripExecution,
        [TrackingKeys.PartyId] = DriverId,
        [TrackingKeys.TripNumber] = TripNumber,
        [TrackingKeys.SourceMessageId] = EventId,
        ["shipmentNumber"] = Stop?.ShipmentNumber,
    };

    /// <summary>The report the message holds, or null when it isn't one the TMS can be asked to record.</summary>
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

        if (parsed is null
            || JsonFields.Text(parsed, "eventId") is not { } eventId || !Guid.TryParse(eventId, out _)
            || JsonFields.Text(parsed, "correlationId") is not { } correlationId
            || JsonFields.Text(parsed, "tripNumber") is not { } tripNumber
            || JsonFields.Text(parsed, "driverId") is not { } driverId)
        {
            return null;
        }

        return JsonFields.Text(parsed, "eventType") switch
        {
            TripStartReported => new(eventId, correlationId, tripNumber, driverId, null),
            StopCompletedReported => ReadStop(parsed) is { } stop ? new(eventId, correlationId, tripNumber, driverId, stop) : null,
            _ => null,
        };
    }

    private static StopReport? ReadStop(JsonObject parsed)
    {
        var location = parsed["location"] as JsonObject;
        return parsed["version"] is JsonValue version && version.TryGetValue<int>(out var held) && held >= 1
            && JsonFields.Text(parsed, "occurredAt") is { } occurredAt && DateTimeOffset.TryParse(occurredAt, CultureInfo.InvariantCulture, out _)
            && JsonFields.Text(parsed, "shipmentNumber") is { } shipmentNumber
            && JsonFields.Text(parsed, "trackingEventType") is { } trackingEventType
            && location is not null
            && JsonFields.Text(location, "place") is { } place
            && JsonFields.Text(location, "postcode") is { } postcode
            && JsonFields.Text(location, "countryCode") is { } countryCode
                ? new StopReport(held, occurredAt, shipmentNumber, trackingEventType, place, postcode, countryCode, JsonFields.Text(parsed, "remark"))
                : null;
    }
}
