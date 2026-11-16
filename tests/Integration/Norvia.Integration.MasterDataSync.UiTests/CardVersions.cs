using System.Text.Json.Nodes;

namespace Norvia.Integration.MasterDataSync.UiTests;

/// <summary>A version of a Rate Card in the shape of the ERP API's body, for Finance to paste into the form.</summary>
public static class CardVersions
{
    /// <summary>A version that prices Rotterdam to Utrecht: a shipment of 100 kg costs the minimum charge, so the charge a Transport Order is rated at says which version rated it.</summary>
    public static JsonObject Version(int version, string validFrom, string? validTo, decimal minimumCharge) => new()
    {
        ["version"] = version,
        ["validFrom"] = validFrom,
        ["validTo"] = validTo,
        ["zones"] = new JsonArray(
            new JsonObject { ["name"] = "Rotterdam", ["country"] = "NL", ["postcodePrefixes"] = new JsonArray("30", "31", "32") },
            new JsonObject { ["name"] = "Utrecht", ["country"] = "NL", ["postcodePrefixes"] = new JsonArray("34", "35", "36", "37", "38", "39") }),
        ["lanes"] = new JsonArray(new JsonObject
        {
            ["origin"] = "Rotterdam",
            ["destination"] = "Utrecht",
            ["minimumCharge"] = minimumCharge,
            ["weightBreaks"] = new JsonArray(
                new JsonObject { ["upToKg"] = 500, ["pricePer100Kg"] = 11.8m },
                new JsonObject { ["upToKg"] = 24000, ["pricePer100Kg"] = 5.9m }),
        }),
    };
}
