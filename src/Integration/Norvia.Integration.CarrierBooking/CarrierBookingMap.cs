using System.Text.Json.Nodes;
using Norvia.Integration.TransportOrderIntake;

namespace Norvia.Integration.CarrierBooking;

/// <summary>
/// The P4 booking map: turns a Canonical Model <c>ShipmentReleased</c> into the Carrier booking format (P4).
/// </summary>
/// <remarks>
/// The request carries what the Carrier needs to carry out the booking and nothing about the Shipper: not its identity,
/// not its reference, and nothing from the Freight Charge. The Carrier cost is the price Norvia offers the Carrier. A value the Carrier format can't hold is left out, as the
/// intake map does, and the Carrier answers a request it can't use.
/// </remarks>
public static class CarrierBookingMap
{
    /// <param name="shipmentReleased">The <c>ShipmentReleased</c> event as JSON text, as the Service Bus message carries it.</param>
    /// <returns>The booking request, or null when the text isn't a <c>ShipmentReleased</c> for a Carrier booking.</returns>
    public static JsonObject? ToCarrierBooking(string shipmentReleased)
    {
        if (JsonFields.Parse(shipmentReleased) is not { } release || JsonFields.Text(release, "carrierBookingNumber") is not { } bookingNumber)
        {
            return null;
        }

        var transportOrder = release["transportOrder"] as JsonObject;
        var collection = transportOrder?["collection"] as JsonObject;
        var delivery = transportOrder?["delivery"] as JsonObject;
        var collectionCountry = TimeZoneCountry(collection?["address"] as JsonObject);
        var deliveryCountry = TimeZoneCountry(delivery?["address"] as JsonObject);

        var booking = new JsonObject
        {
            ["carrierBookingNumber"] = bookingNumber,
            ["shipmentNumber"] = JsonFields.Text(release, "shipmentNumber"),
            ["collectionStreet"] = JsonFields.Text(collection?["address"] as JsonObject, "street"),
            ["collectionPostcode"] = JsonFields.Text(collection?["address"] as JsonObject, "postcode"),
            ["collectionCity"] = JsonFields.Text(collection?["address"] as JsonObject, "city"),
            ["collectionCountry"] = Alpha3Codes.Of(collectionCountry?.Code),
            ["collectFrom"] = LocalTimes.FromUtc(JsonFields.Text(collection?["window"] as JsonObject, "earliest"), collectionCountry),
            ["collectUntil"] = LocalTimes.FromUtc(JsonFields.Text(collection?["window"] as JsonObject, "latest"), collectionCountry),
            ["deliveryStreet"] = JsonFields.Text(delivery?["address"] as JsonObject, "street"),
            ["deliveryPostcode"] = JsonFields.Text(delivery?["address"] as JsonObject, "postcode"),
            ["deliveryCity"] = JsonFields.Text(delivery?["address"] as JsonObject, "city"),
            ["deliveryCountry"] = Alpha3Codes.Of(deliveryCountry?.Code),
            ["deliverFrom"] = LocalTimes.FromUtc(JsonFields.Text(delivery?["window"] as JsonObject, "earliest"), deliveryCountry),
            ["deliverUntil"] = LocalTimes.FromUtc(JsonFields.Text(delivery?["window"] as JsonObject, "latest"), deliveryCountry),
            ["carrierCost"] = CarrierCost(release["carrierCost"] as JsonObject),
            ["consigneeName"] = JsonFields.Text((delivery?["consignee"]) as JsonObject, "name"),
            ["goods"] = Goods(transportOrder?["goodsLines"] as JsonArray),
        };

        return JsonFields.WithoutNulls(booking);
    }

    /// <summary>
    /// The Carrier cost the release froze on the booking (P9): the total, its charge lines and whether it is a Spot price.
    /// The Planner's remark and the Carrier Rate Card version stay with Norvia.
    /// </summary>
    private static JsonObject? CarrierCost(JsonObject? cost)
    {
        if (cost is null)
        {
            return null;
        }

        var chargeLines = new JsonArray();
        foreach (var line in (cost["chargeLines"] as JsonArray)?.OfType<JsonObject>() ?? [])
        {
            chargeLines.Add(JsonFields.WithoutNulls(new JsonObject
            {
                ["type"] = JsonFields.Text(line, "type"),
                ["amount"] = line["amount"]?.DeepClone(),
            }));
        }

        return JsonFields.WithoutNulls(new JsonObject
        {
            ["currency"] = JsonFields.Text(cost, "currency"),
            ["total"] = cost["total"]?.DeepClone(),
            ["chargeLines"] = chargeLines,
            ["isSpot"] = JsonFields.Text(cost["source"] as JsonObject, "kind") == "Spot",
        });
    }

    /// <summary>The country of an address, as it is at the address's postcode, so its local times are in the right time zone.</summary>
    private static Country? TimeZoneCountry(JsonObject? address) =>
        Countries.At(Countries.Find(JsonFields.Text(address, "country")), JsonFields.Text(address, "postcode"));

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
                ["packageType"] = JsonFields.Text(line, "packageType"),
                ["quantity"] = line["quantity"]?.DeepClone(),
                ["weightKg"] = line["weightKg"]?.DeepClone(),
                ["lengthCm"] = dimensions?["length"]?.DeepClone(),
                ["widthCm"] = dimensions?["width"]?.DeepClone(),
                ["heightCm"] = dimensions?["height"]?.DeepClone(),
            };
            goods.Add(JsonFields.WithoutNulls(item));
        }

        return goods;
    }
}
