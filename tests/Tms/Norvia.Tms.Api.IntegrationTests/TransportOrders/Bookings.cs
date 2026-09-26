using System.Text.Json.Nodes;

namespace Norvia.Tms.Api.IntegrationTests.TransportOrders;

/// <summary>Canonical Model Transport Orders as the intake Logic App sends them.</summary>
internal static class Bookings
{
    public static JsonObject Valid(string shipperReference)
    {
        var collectionDay = DateTimeOffset.UtcNow.Date.AddDays(2);
        return new JsonObject
        {
            ["shipperId"] = "SHP-0001",
            ["shipperReference"] = shipperReference,
            ["collection"] = new JsonObject
            {
                ["address"] = Address("Waalhaven Oostzijde 88", "3087 BM", "Rotterdam", "NL"),
                ["window"] = Window(collectionDay.AddHours(6), collectionDay.AddHours(10)),
            },
            ["delivery"] = new JsonObject
            {
                ["address"] = Address("Oudegracht 112", "3511 AW", "Utrecht", "NL"),
                ["consignee"] = new JsonObject
                {
                    ["name"] = "Café De Zwaan",
                    ["contactName"] = "Sanne de Vries",
                    ["phone"] = "+31 30 555 0142",
                    ["email"] = null,
                },
                ["window"] = Window(collectionDay.AddHours(12), collectionDay.AddHours(16)),
            },
            ["goodsLines"] = new JsonArray
            {
                new JsonObject
                {
                    ["packageType"] = "pallet",
                    ["quantity"] = 2,
                    ["weightKg"] = 850.5,
                    ["dimensionsCm"] = new JsonObject { ["length"] = 120, ["width"] = 80, ["height"] = 150 },
                },
                new JsonObject
                {
                    ["packageType"] = "crate",
                    ["quantity"] = 4,
                    ["weightKg"] = 60,
                    ["dimensionsCm"] = null,
                },
            },
            ["instructions"] = "Ring at the side entrance",
        };
    }

    /// <summary>The booking with one goods line of the given total weight.</summary>
    public static JsonObject Weighing(this JsonObject booking, decimal weightKg)
    {
        booking["goodsLines"] = new JsonArray
        {
            new JsonObject { ["packageType"] = "pallet", ["quantity"] = 1, ["weightKg"] = weightKg, ["dimensionsCm"] = null },
        };
        return booking;
    }

    /// <summary>The booking delivered to another address in Utrecht, with the given postcode.</summary>
    public static JsonObject DeliveredTo(this JsonObject booking, string postcode)
    {
        booking["delivery"]!["address"] = Address("Heidelberglaan 8", postcode, "Utrecht", "NL");
        return booking;
    }

    /// <summary>The booking delivered to Düsseldorf, in Germany.</summary>
    public static JsonObject DeliveredToDusseldorf(this JsonObject booking)
    {
        booking["delivery"]!["address"] = Address("Königsallee 1", "40212", "Düsseldorf", "DE");
        return booking;
    }

    /// <summary>The booking collected from the given time on, and delivered later the same day.</summary>
    public static JsonObject CollectedFrom(this JsonObject booking, DateTimeOffset earliest)
    {
        booking["collection"]!["window"] = Window(earliest, earliest.AddHours(4));
        booking["delivery"]!["window"] = Window(earliest.AddHours(6), earliest.AddHours(10));
        return booking;
    }

    private static JsonObject Address(string street, string postcode, string city, string country) => new()
    {
        ["street"] = street,
        ["postcode"] = postcode,
        ["city"] = city,
        ["country"] = country,
    };

    private static JsonObject Window(DateTimeOffset earliest, DateTimeOffset latest) => new()
    {
        ["earliest"] = earliest.ToString("O"),
        ["latest"] = latest.ToString("O"),
    };
}
