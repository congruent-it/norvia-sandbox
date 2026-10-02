using System.Text.Json.Nodes;
using Norvia.Integration.TransportOrderIntake;

namespace Norvia.Integration.CarrierBooking;

/// <summary>
/// The P4 booking map: turns a Canonical Model <c>ShipmentReleased</c> into the Carrier booking format (P4).
/// </summary>
/// <remarks>
/// The request carries what the Carrier needs to carry out the booking and nothing about the Shipper: not its identity,
/// not its reference, and nothing from the Freight Charge. A value the Carrier format can't hold is left out, as the
/// intake map does, and the Carrier answers a request it can't use.
/// </remarks>
public static class CarrierBookingMap
{
    /// <param name="shipmentReleased">The <c>ShipmentReleased</c> event as JSON text, as the Service Bus message carries it.</param>
    /// <returns>The booking request, or null when the text isn't a <c>ShipmentReleased</c> for a Carrier booking.</returns>
    public static JsonObject? ToCarrierBooking(string shipmentReleased)
    {
        if (Parse(shipmentReleased) is not { } release || Text(release, "carrierBookingNumber") is not { } bookingNumber)
        {
            return null;
        }

        var transportOrder = release["transportOrder"] as JsonObject;
        var collection = transportOrder?["collection"] as JsonObject;
        var delivery = transportOrder?["delivery"] as JsonObject;
        var collectionCountry = Countries.Find(Text(collection?["address"] as JsonObject, "country"));
        var deliveryCountry = Countries.Find(Text(delivery?["address"] as JsonObject, "country"));

        var booking = new JsonObject
        {
            ["carrierBookingNumber"] = bookingNumber,
            ["shipmentNumber"] = Text(release, "shipmentNumber"),
            ["collectionStreet"] = Text(collection?["address"] as JsonObject, "street"),
            ["collectionPostcode"] = Text(collection?["address"] as JsonObject, "postcode"),
            ["collectionCity"] = Text(collection?["address"] as JsonObject, "city"),
            ["collectionCountry"] = Alpha3Codes.Of(collectionCountry?.Code),
            ["collectFrom"] = LocalTimes.FromUtc(Text(collection?["window"] as JsonObject, "earliest"), collectionCountry),
            ["collectUntil"] = LocalTimes.FromUtc(Text(collection?["window"] as JsonObject, "latest"), collectionCountry),
            ["deliveryStreet"] = Text(delivery?["address"] as JsonObject, "street"),
            ["deliveryPostcode"] = Text(delivery?["address"] as JsonObject, "postcode"),
            ["deliveryCity"] = Text(delivery?["address"] as JsonObject, "city"),
            ["deliveryCountry"] = Alpha3Codes.Of(deliveryCountry?.Code),
            ["deliverFrom"] = LocalTimes.FromUtc(Text(delivery?["window"] as JsonObject, "earliest"), deliveryCountry),
            ["deliverUntil"] = LocalTimes.FromUtc(Text(delivery?["window"] as JsonObject, "latest"), deliveryCountry),
            ["consigneeName"] = Text((delivery?["consignee"]) as JsonObject, "name"),
            ["goods"] = Goods(transportOrder?["goodsLines"] as JsonArray),
        };

        foreach (var name in booking.Where(property => property.Value is null).Select(property => property.Key).ToList())
        {
            booking.Remove(name);
        }

        return booking;
    }

    private static JsonArray? Goods(JsonArray? goodsLines)
    {
        if (goodsLines is null)
        {
            return null;
        }

        var goods = new JsonArray();
        foreach (var line in goodsLines.OfType<JsonObject>())
        {
            var dimensions = line["dimensionsCm"] as JsonObject;
            var item = new JsonObject
            {
                ["packageType"] = Text(line, "packageType"),
                ["quantity"] = line["quantity"]?.DeepClone(),
                ["weightKg"] = line["weightKg"]?.DeepClone(),
                ["lengthCm"] = dimensions?["length"]?.DeepClone(),
                ["widthCm"] = dimensions?["width"]?.DeepClone(),
                ["heightCm"] = dimensions?["height"]?.DeepClone(),
            };
            foreach (var name in item.Where(property => property.Value is null).Select(property => property.Key).ToList())
            {
                item.Remove(name);
            }

            goods.Add(item);
        }

        return goods;
    }

    private static JsonObject? Parse(string json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonObject? node, string name) =>
        node?[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;
}
