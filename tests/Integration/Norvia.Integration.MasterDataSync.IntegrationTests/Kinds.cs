using System.Text.Json.Nodes;
using Xunit;

namespace Norvia.Integration.MasterDataSync.IntegrationTests;

/// <summary>
/// One of the six master-data entities the ERP owns and replicates: where its API is, what its event is called, which TMS table holds its copy, and how a test makes
/// a body of it, changes one and tells whether the TMS's copy is that body.
/// </summary>
public sealed record Kind(
    string Name,
    string Route,
    string IdPrefix,
    string EventType,
    string RecordName,
    string ReplicaTable,
    Func<MasterDataSyncFixture, string, CancellationToken, Task<JsonObject>> NewBody,
    Action<JsonObject> Change,
    Func<IReadOnlyDictionary<string, object?>, JsonObject, bool> ReplicaMatches)
{
    /// <summary>The ID of a record the test makes, unlike any other, short enough for the TMS's columns.</summary>
    public string NewId() => $"{IdPrefix}-T{Guid.NewGuid():N}"[..(IdPrefix.Length + 10)];

    /// <summary>The party the event is about: the Shipper or Carrier of a card, else the record itself.</summary>
    public string PartyOf(JsonNode record) => record[Name switch { "rate-cards" => "shipperId", "carrier-rate-cards" => "carrierId", _ => "id" }]!.GetValue<string>();

    public override string ToString() => Name;
}

public static class Kinds
{
    public static readonly Kind Shipper = new(
        "shippers", "/v1/shippers", "SHP", "ShipperChanged", "shipper", "ShipperCopies",
        (_, _, _) => Task.FromResult(new JsonObject { ["name"] = "Integration test Shipper", ["active"] = true }),
        body => body["name"] = body["name"]!.GetValue<string>() + " (edited)",
        (row, body) => (string)row["Name"]! == body["name"]!.GetValue<string>() && (bool)row["Active"]! == body["active"]!.GetValue<bool>());

    public static readonly Kind Carrier = new(
        "carriers", "/v1/carriers", "CAR", "CarrierChanged", "carrier", "CarrierCopies",
        (_, _, _) => Task.FromResult(new JsonObject { ["name"] = "Integration test Carrier", ["active"] = true }),
        body => body["name"] = body["name"]!.GetValue<string>() + " (edited)",
        (row, body) => (string)row["Name"]! == body["name"]!.GetValue<string>() && (bool)row["Active"]! == body["active"]!.GetValue<bool>());

    public static readonly Kind Vehicle = new(
        "vehicles", "/v1/vehicles", "VEH", "VehicleChanged", "vehicle", "VehicleCopies",
        (_, id, _) => Task.FromResult(new JsonObject
        {
            ["registration"] = "IT-" + id[^7..].ToUpperInvariant(),
            ["type"] = "Van",
            ["payloadKg"] = 1000,
            ["palletPlaces"] = 4,
            ["active"] = true,
        }),
        body => body["payloadKg"] = 1100,
        (row, body) => (string)row["Registration"]! == body["registration"]!.GetValue<string>()
            && (int)row["PayloadKg"]! == body["payloadKg"]!.GetValue<int>()
            && (bool)row["Active"]! == body["active"]!.GetValue<bool>());

    public static readonly Kind Driver = new(
        "drivers", "/v1/drivers", "DRV", "DriverChanged", "driver", "DriverCopies",
        (_, _, _) => Task.FromResult(new JsonObject { ["name"] = "Integration test Driver", ["licences"] = new JsonArray("B", "C"), ["active"] = true }),
        body => body["name"] = body["name"]!.GetValue<string>() + " (edited)",
        (row, body) => (string)row["Name"]! == body["name"]!.GetValue<string>() && (bool)row["Active"]! == body["active"]!.GetValue<bool>());

    public static readonly Kind RateCard = new(
        "rate-cards", "/v1/rate-cards", "RC", "RateCardChanged", "rateCard", "RateCardCopies",
        async (fixture, _, ct) =>
        {
            // The card belongs to a Shipper of its own, as a Shipper has one card.
            var shipperId = Shipper.NewId();
            (await fixture.SaveAsync(Shipper, shipperId, await Shipper.NewBody(fixture, shipperId, ct), ct: ct)).Status.Should().Be(System.Net.HttpStatusCode.Created);
            return new JsonObject { ["shipperId"] = shipperId, ["versions"] = new JsonArray(Cards.Version(1, "2026-01-01", null, 65m)) };
        },
        body => body["versions"] = new JsonArray(Cards.Version(1, "2026-01-01", "2026-12-31", 65m), Cards.Version(2, "2027-01-01", null, 75m)),
        Cards.ReplicaHoldsVersionsOf);

    public static readonly Kind CarrierRateCard = new(
        "carrier-rate-cards", "/v1/carrier-rate-cards", "CRC", "CarrierRateCardChanged", "carrierRateCard", "CarrierRateCardCopies",
        async (fixture, _, ct) =>
        {
            var carrierId = Carrier.NewId();
            (await fixture.SaveAsync(Carrier, carrierId, await Carrier.NewBody(fixture, carrierId, ct), ct: ct)).Status.Should().Be(System.Net.HttpStatusCode.Created);
            return new JsonObject { ["carrierId"] = carrierId, ["versions"] = new JsonArray(Cards.Version(1, "2026-01-01", null, 50m)) };
        },
        body => body["versions"] = new JsonArray(Cards.Version(1, "2026-01-01", "2026-12-31", 50m), Cards.Version(2, "2027-01-01", null, 60m)),
        Cards.ReplicaHoldsVersionsOf);

    public static IReadOnlyList<Kind> All { get; } = [Shipper, Carrier, RateCard, CarrierRateCard, Vehicle, Driver];

    public static Kind Named(string name) => All.Single(kind => kind.Name == name);

    /// <summary>The kinds as theory data, by name.</summary>
    public static TheoryData<string> Names { get; } = [.. All.Select(kind => kind.Name)];
}

/// <summary>Rate Card versions as the ERP API's body and the TMS's command take them: two Zones and one Lane, priced by its minimum charge.</summary>
public static class Cards
{
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

    /// <summary>Whether the TMS's copy of a card holds the body's versions, the same version numbers each with the same minimum charge.</summary>
    public static bool ReplicaHoldsVersionsOf(IReadOnlyDictionary<string, object?> row, JsonObject body) =>
        Summary(JsonNode.Parse((string)row["VersionsJson"]!)!.AsArray()).SequenceEqual(Summary(body["versions"]!.AsArray()));

    private static IEnumerable<(int Version, decimal MinimumCharge)> Summary(JsonArray versions) =>
        versions.Select(version => (version!["version"]!.GetValue<int>(), version["lanes"]![0]!["minimumCharge"]!.GetValue<decimal>())).OrderBy(pair => pair.Item1);
}
