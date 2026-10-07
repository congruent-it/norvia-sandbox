using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Norvia.PartnerPortal.Application.ShipperTracking;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Infrastructure.PartnerApi;

/// <summary>
/// Reads the Shipper tracking format (contracts/partner-api, shipper-tracking.v1 and shipper-tracking-list-item.v1). The
/// times stay the local times it gives, as text: the Shipper sees them as the API returns them. An answer that breaks the
/// format throws, and the client reports tracking as unavailable.
/// </summary>
internal static class ShipperTrackingFormat
{
    public static string QueryOf(ShipperTrackingFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var parts = new List<string>();
        if (filter.Status is { } status)
        {
            parts.Add($"trackingStatus={StatusQueryValue(status)}");
        }

        if (filter.CollectionFrom is { } from)
        {
            parts.Add($"collectionFrom={from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        }

        if (filter.CollectionTo is { } to)
        {
            parts.Add($"collectionTo={to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        }

        parts.Add($"page={filter.Page.ToString(CultureInfo.InvariantCulture)}");
        parts.Add($"pageSize={ShipperTrackingFilter.PageSize.ToString(CultureInfo.InvariantCulture)}");
        return string.Join('&', parts);
    }

    public static IReadOnlyList<ShipperTrackingSummary> ReadList(string body)
    {
        var items = JsonNode.Parse(body) as JsonArray ?? throw new JsonException("The list isn't an array.");
        return [.. items.Select(item => ReadSummary(item as JsonObject ?? throw new JsonException("A list item isn't an object.")))];
    }

    public static ShipperTrackingDetail ReadDetail(string body)
    {
        var json = JsonNode.Parse(body) as JsonObject ?? throw new JsonException("The Transport Order isn't an object.");
        return new ShipperTrackingDetail(
            Required(json, "norviaOrderNo"),
            Text(json, "bookingRef"),
            ReadStatus(Required(json, "status")),
            Place(json, "pickup"),
            Text(json, "pickupFrom"),
            Text(json, "pickupUntil"),
            Place(json, "receiver"),
            Text(json, "deliverFrom"),
            Text(json, "deliverUntil"),
            Text(json, "eta"),
            Text(json, "lastUpdate"),
            [.. (json["events"] as JsonArray ?? throw new JsonException("The Transport Order has no events.")).Select(ReadEvent)],
            json["proofOfDelivery"] is JsonObject proof ? ReadProof(proof) : null);
    }

    private static ShipperTrackingSummary ReadSummary(JsonObject json) => new(
        Required(json, "norviaOrderNo"),
        Text(json, "bookingRef"),
        ReadStatus(Required(json, "status")),
        Place(json, "pickup"),
        Place(json, "receiver"),
        Text(json, "eta"),
        Text(json, "lastUpdate"));

    private static ShipperProgressEvent ReadEvent(JsonNode? node)
    {
        var json = node as JsonObject ?? throw new JsonException("An event isn't an object.");
        return new ShipperProgressEvent(
            Required(json, "code") switch
            {
                "COLLECTED" => ShipperProgressType.Collected,
                "DEPARTED" => ShipperProgressType.Departed,
                "ARRIVED" => ShipperProgressType.Arrived,
                "OUT_FOR_DELIVERY" => ShipperProgressType.OutForDelivery,
                "DELIVERED" => ShipperProgressType.Delivered,
                var other => throw new JsonException($"'{other}' is no progress code."),
            },
            Required(json, "time"),
            new ShipperPlace(Text(json, "city"), Text(json, "country")));
    }

    private static ShipperProofOfDelivery ReadProof(JsonObject json) => new(
        Required(json, "receivedBy"),
        Required(json, "deliveredAt"),
        Required(json, "condition") switch
        {
            "CLEAN" => ProofOfDeliveryCondition.Clean,
            "RESERVATIONS" => ProofOfDeliveryCondition.Reservations,
            var other => throw new JsonException($"'{other}' is no condition."),
        },
        [.. (json["files"] as JsonArray ?? throw new JsonException("The Proof of delivery has no files.")).Select(ReadFile)]);

    private static ShipperProofFile ReadFile(JsonNode? node)
    {
        var json = node as JsonObject ?? throw new JsonException("A file isn't an object.");
        return new ShipperProofFile(
            Guid.Parse(Required(json, "fileId")),
            Required(json, "name"),
            Required(json, "contentType"),
            json["sizeBytes"]?.GetValue<long>() ?? throw new JsonException("A file has no size."));
    }

    private static ShipperTrackingStatus ReadStatus(string code) => code switch
    {
        "RECEIVED" => ShipperTrackingStatus.Received,
        "SCHEDULED" => ShipperTrackingStatus.Scheduled,
        "COLLECTED" => ShipperTrackingStatus.Collected,
        "IN_TRANSIT" => ShipperTrackingStatus.InTransit,
        "DELIVERED" => ShipperTrackingStatus.Delivered,
        _ => throw new JsonException($"'{code}' is no tracking status."),
    };

    // The query takes the Canonical Model's names, not the Shipper codes: the edge passes it to the TMS as it is.
    private static string StatusQueryValue(ShipperTrackingStatus status) => status switch
    {
        ShipperTrackingStatus.Received => "Received",
        ShipperTrackingStatus.Scheduled => "Scheduled",
        ShipperTrackingStatus.Collected => "Collected",
        ShipperTrackingStatus.InTransit => "InTransit",
        ShipperTrackingStatus.Delivered => "Delivered",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Every status has a query value."),
    };

    private static ShipperPlace Place(JsonObject json, string prefix) => new(Text(json, prefix + "City"), Text(json, prefix + "Country"));

    private static string Required(JsonObject json, string name) =>
        Text(json, name) ?? throw new JsonException($"'{name}' is missing.");

    private static string? Text(JsonObject json, string name) =>
        json[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;
}
