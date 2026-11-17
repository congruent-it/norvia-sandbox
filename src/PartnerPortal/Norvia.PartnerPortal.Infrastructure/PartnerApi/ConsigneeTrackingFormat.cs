using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Norvia.PartnerPortal.Application.ConsigneeTracking;
using Norvia.PartnerPortal.Application.ShipperTracking;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Infrastructure.PartnerApi;

/// <summary>
/// Reads the Consignee tracking format (contracts/partner-api, consignee-tracking.v1; ADR 0052). Its times are UTC instants. An answer that
/// breaks the format throws, and the client reports tracking as unavailable.
/// </summary>
internal static class ConsigneeTrackingFormat
{
    public static ConsigneeTrackingView ReadView(string body)
    {
        var json = JsonNode.Parse(body) as JsonObject ?? throw new JsonException("The view isn't an object.");
        return new ConsigneeTrackingView(
            Required(json, "status") switch
            {
                "RECEIVED" => ShipperTrackingStatus.Received,
                "SCHEDULED" => ShipperTrackingStatus.Scheduled,
                "COLLECTED" => ShipperTrackingStatus.Collected,
                "IN_TRANSIT" => ShipperTrackingStatus.InTransit,
                "DELIVERED" => ShipperTrackingStatus.Delivered,
                var other => throw new JsonException($"'{other}' is no tracking status."),
            },
            new ShipperPlace(Text(json, "pickupCity"), Text(json, "pickupCountry")),
            new ShipperPlace(Text(json, "receiverCity"), Text(json, "receiverCountry")),
            Instant(json, "deliverFrom"),
            Instant(json, "deliverUntil"),
            Instant(json, "eta"),
            Instant(json, "lastUpdate"),
            [.. (json["events"] as JsonArray ?? throw new JsonException("The view has no events.")).Select(ReadEvent)],
            json["proofOfDelivery"] is JsonObject proof ? ReadProof(proof) : null);
    }

    private static ConsigneeProgressEvent ReadEvent(JsonNode? node)
    {
        var json = node as JsonObject ?? throw new JsonException("An event isn't an object.");
        return new ConsigneeProgressEvent(
            Required(json, "code") switch
            {
                "COLLECTED" => ShipperProgressType.Collected,
                "DEPARTED" => ShipperProgressType.Departed,
                "ARRIVED" => ShipperProgressType.Arrived,
                "OUT_FOR_DELIVERY" => ShipperProgressType.OutForDelivery,
                "DELIVERED" => ShipperProgressType.Delivered,
                var other => throw new JsonException($"'{other}' is no progress code."),
            },
            Instant(json, "time") ?? throw new JsonException("An event has no time."),
            new ShipperPlace(Text(json, "city"), Text(json, "country")));
    }

    private static ConsigneeProofOfDelivery ReadProof(JsonObject json) => new(
        Required(json, "receivedBy"),
        Instant(json, "deliveredAt") ?? throw new JsonException("The Proof of delivery has no time."),
        Required(json, "condition") switch
        {
            "CLEAN" => ProofOfDeliveryCondition.Clean,
            "RESERVATIONS" => ProofOfDeliveryCondition.Reservations,
            var other => throw new JsonException($"'{other}' is no condition."),
        });

    private static DateTimeOffset? Instant(JsonObject json, string name) =>
        Text(json, name) is { } text
            ? DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
            : null;

    private static string Required(JsonObject json, string name) =>
        Text(json, name) ?? throw new JsonException($"'{name}' is missing.");

    private static string? Text(JsonObject json, string name) =>
        json[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;
}
