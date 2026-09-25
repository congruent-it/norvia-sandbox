using System.Text.Json.Nodes;

namespace Norvia.Integration.TransportOrderIntake;

/// <summary>
/// The intake map: turns a partner booking (Norvia Partner API format) into a Canonical Model Transport Order.
/// </summary>
/// <remarks>
/// The map never rejects a booking; the TMS decides that, so the Shipper hears every reason from one place. A value
/// the map can't read is left out, and the Transport Order rules report what is then missing or invalid.
/// </remarks>
public static class IntakeMap
{
    /// <param name="partnerBooking">The booking as the Shipper sent it to the Norvia Partner API.</param>
    /// <param name="shipperId">The Shipper, as API Management took it from the token. Never from the payload (ADR 0005).</param>
    public static JsonObject ToTransportOrder(JsonNode partnerBooking, string shipperId)
    {
        ArgumentNullException.ThrowIfNull(partnerBooking);
        var booking = new PartnerFields(partnerBooking);

        var collectionCountry = Countries.Find(booking.Text("pickupCountry"));
        var deliveryCountry = Countries.Find(booking.Text("receiverCountry"));

        var transportOrder = new JsonObject
        {
            ["shipperId"] = shipperId,
            ["shipperReference"] = booking.Text("bookingRef"),
            ["collection"] = Compact(new JsonObject
            {
                ["address"] = Address(booking, "pickup", collectionCountry),
                ["window"] = Window(booking, "pickupFrom", "pickupUntil", collectionCountry),
            }),
            ["delivery"] = Compact(new JsonObject
            {
                ["address"] = Address(booking, "receiver", deliveryCountry),
                ["consignee"] = Compact(new JsonObject
                {
                    ["name"] = booking.Text("receiverName"),
                    ["contactName"] = booking.Text("receiverContact"),
                    ["phone"] = booking.Text("receiverPhone"),
                    ["email"] = booking.Text("receiverEmail"),
                }),
                ["window"] = Window(booking, "deliverFrom", "deliverUntil", deliveryCountry),
            }),
            ["goodsLines"] = GoodsLines(booking),
            ["instructions"] = booking.Text("remarks"),
        };
        RemoveEmpty(transportOrder);
        return transportOrder;
    }

    /// <summary>Maps the booking as the JSON text the Shipper sent, which is how the intake Logic App passes it.</summary>
    /// <remarks>Text that isn't a JSON object is no booking at all: it maps to a Transport Order that names only the Shipper.</remarks>
    public static JsonObject ToTransportOrder(string partnerBooking, string shipperId) =>
        ToTransportOrder(PartnerFields.Parse(partnerBooking) ?? new JsonObject(), shipperId);

    private static JsonObject? Address(PartnerFields booking, string party, Country? country) =>
        Compact(new JsonObject
        {
            ["street"] = booking.Text(party + "Street"),
            ["postcode"] = booking.Text(party + "PostalCode"),
            ["city"] = booking.Text(party + "City"),
            ["country"] = country?.Code,
        });

    /// <summary>A window's local times are those of the place the goods are collected or delivered.</summary>
    private static JsonObject? Window(PartnerFields booking, string from, string until, Country? country) =>
        Compact(new JsonObject
        {
            ["earliest"] = LocalTimes.ToUtc(booking.Text(from), country),
            ["latest"] = LocalTimes.ToUtc(booking.Text(until), country),
        });

    private static JsonArray? GoodsLines(PartnerFields booking)
    {
        if (booking.Items() is not { } items)
        {
            return null;
        }

        var goodsLines = new JsonArray();
        foreach (var item in items)
        {
            goodsLines.Add(Compact(new JsonObject
            {
                ["packageType"] = PackageTypes.ToCanonical(item.Text("packaging")),
                ["quantity"] = item.Integer("count"),
                ["weightKg"] = Weights.ToKilograms(item.Text("weight")),
                ["dimensionsCm"] = Dimensions.ToCentimetres(item.Text("dimensions")),
            }) ?? new JsonObject());
        }

        return goodsLines;
    }

    /// <summary>
    /// Leaves out the properties that have no value, and a whole part that is left empty, so the Transport Order
    /// rules see a missing part as missing.
    /// </summary>
    private static JsonObject? Compact(JsonObject node)
    {
        RemoveEmpty(node);
        return node.Count == 0 ? null : node;
    }

    private static void RemoveEmpty(JsonObject node)
    {
        foreach (var name in node.Where(property => property.Value is null).Select(property => property.Key).ToList())
        {
            node.Remove(name);
        }
    }
}
