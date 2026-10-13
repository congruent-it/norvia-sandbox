using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalDriverReportsWorker;

/// <summary>What a Stop report names for the TMS's Tracking event: the Shipment, the Tracking event type, where and what the Driver said.</summary>
internal sealed record StopReport(int TripVersion, string OccurredAt, string ShipmentNumber, string TrackingEventType, string Place, string Postcode, string CountryCode, string? Remark);

/// <summary>What a <c>ProofOfDeliveryCaptured</c> names for the TMS's Proof of delivery: the metadata of P6's multipart call, and where the files wait.</summary>
internal sealed record ProofOfDeliveryReport(
    int TripVersion, string ShipmentNumber, string DocumentId, string ReceivedBy, string DeliveryTime, string Place, string Postcode, string CountryCode, string Condition, string? Remark,
    IReadOnlyList<ProofFileReference> Files);

/// <summary>What a <c>TripStartReported</c>, a <c>StopCompletedReported</c> or a <c>ProofOfDeliveryCaptured</c> names, and the tracking contract's keys it gives the trail.</summary>
/// <param name="Stop">What the TMS's Tracking event needs when the report is a Stop report; null for a Start Trip.</param>
/// <param name="Proof">What the TMS's Proof of delivery needs when the report is a Proof of delivery; null otherwise.</param>
internal sealed record DriverReport(string EventId, string CorrelationId, string TripNumber, string DriverId, StopReport? Stop, ProofOfDeliveryReport? Proof = null)
{
    private const string TripStartReported = "TripStartReported";
    private const string StopCompletedReported = "StopCompletedReported";
    private const string ProofOfDeliveryCaptured = "ProofOfDeliveryCaptured";

    public Dictionary<string, object?> ContractKeys => new()
    {
        [TrackingKeys.CorrelationId] = CorrelationId,
        [TrackingKeys.Flow] = TrackingKeys.TripExecution,
        [TrackingKeys.PartyId] = DriverId,
        [TrackingKeys.TripNumber] = TripNumber,
        [TrackingKeys.SourceMessageId] = EventId,
        ["shipmentNumber"] = Stop?.ShipmentNumber ?? Proof?.ShipmentNumber,
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
            ProofOfDeliveryCaptured => ReadProof(parsed) is { } proof ? new(eventId, correlationId, tripNumber, driverId, null, proof) : null,
            _ => null,
        };
    }

    private static ProofOfDeliveryReport? ReadProof(JsonObject parsed)
    {
        var location = parsed["location"] as JsonObject;
        if (parsed["version"] is not JsonValue version || !version.TryGetValue<int>(out var held) || held < 1
            || JsonFields.Text(parsed, "shipmentNumber") is not { } shipmentNumber
            || JsonFields.Text(parsed, "documentId") is not { } documentId
            || JsonFields.Text(parsed, "receivedBy") is not { } receivedBy
            || JsonFields.Text(parsed, "deliveryTime") is not { } deliveryTime || !DateTimeOffset.TryParse(deliveryTime, CultureInfo.InvariantCulture, out _)
            || JsonFields.Text(parsed, "condition") is not { } condition
            || location is null
            || JsonFields.Text(location, "place") is not { } place
            || JsonFields.Text(location, "postcode") is not { } postcode
            || JsonFields.Text(location, "countryCode") is not { } countryCode
            || parsed["files"] is not JsonArray { Count: > 0 } fileNodes)
        {
            return null;
        }

        var files = new List<ProofFileReference>();
        foreach (var node in fileNodes)
        {
            if (node is not JsonObject file
                || JsonFields.Text(file, "name") is not { } name
                || JsonFields.Text(file, "contentType") is not { } contentType
                || JsonFields.Text(file, "blobReference") is not { } blobReference)
            {
                return null;
            }

            files.Add(new ProofFileReference(name, contentType, blobReference));
        }

        return new ProofOfDeliveryReport(held, shipmentNumber, documentId, receivedBy, deliveryTime, place, postcode, countryCode, condition, JsonFields.Text(parsed, "remark"), files);
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
