using System.Text.Json.Nodes;

namespace Norvia.Tms.Api.IntegrationTests;

/// <summary>
/// The demo seed (seed/norvia.seed.json) as P2's demo needs it: Brouwer Dranken's Rate Card prices Benelux Lanes out
/// of Rotterdam and has a deliberate gap into Germany, and Brouwer Dranken has an Antwerp address to book the Rated
/// path to. P3's demo has four Carriers, one of them inactive. The TMS UI reads the demo seed in the test host, so it starting at all shows the seed loads.
/// </summary>
public sealed class DemoSeedTests(TmsFixture tms)
{
    private static readonly string[] Benelux = ["NL", "BE", "LU"];

    [Fact]
    public async Task Start_OnTheDemoSeed_TheTmsUiBecomesHealthy()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));

        var act = () => tms.WaitForHealthyAsync("tms-web", timeout.Token);

        await act.Should().NotThrowAsync("the TMS refuses to start on a Rate Card it can't load");
    }

    [Fact]
    public void BrouwerDrankensRateCard_PricesBeneluxLanes_WithAtLeastTwoWeightBreaksAndAMinimumCharge()
    {
        var version = BrouwerDrankensRateCard()["versions"]!.AsArray().Should().ContainSingle().Subject!;
        var zones = version["zones"]!.AsArray().ToDictionary(zone => zone!["name"]!.GetValue<string>(), zone => zone!["country"]!.GetValue<string>());

        version["validTo"].Should().BeNull("the demo's Transport Orders are collected from today on");
        foreach (var lane in version["lanes"]!.AsArray())
        {
            Benelux.Should().Contain(zones[lane!["origin"]!.GetValue<string>()]);
            Benelux.Should().Contain(zones[lane["destination"]!.GetValue<string>()]);
            lane["weightBreaks"]!.AsArray().Should().HaveCountGreaterThanOrEqualTo(2);
            lane["minimumCharge"]!.GetValue<decimal>().Should().BePositive();
        }
    }

    [Fact]
    public void BrouwerDrankensRateCard_HasNoZoneInGermany_SoNoLaneLeadsThere()
    {
        var version = BrouwerDrankensRateCard()["versions"]!.AsArray().Single()!;

        version["zones"]!.AsArray().Select(zone => zone!["country"]!.GetValue<string>()).Should().NotContain("DE");
        BrouwerDranken()["addresses"]!.AsArray().Should().Contain(address => address!["country"]!.GetValue<string>() == "DE",
            "the demo books to Düsseldorf to show a Transport Order no Lane covers");
    }

    [Fact]
    public void BrouwerDranken_HasAnAntwerpAddress_ThatALaneFromRotterdamCovers()
    {
        var antwerp = BrouwerDranken()["addresses"]!.AsArray()
            .Should().ContainSingle(address => address!["city"]!.GetValue<string>() == "Antwerpen").Subject!;
        var version = BrouwerDrankensRateCard()["versions"]!.AsArray().Single()!;

        antwerp["country"]!.GetValue<string>().Should().Be("BE");
        var antwerpZone = version["zones"]!.AsArray().Should().ContainSingle(zone =>
            zone!["country"]!.GetValue<string>() == "BE"
            && zone["postcodePrefixes"]!.AsArray().Any(prefix => antwerp["postcode"]!.GetValue<string>().StartsWith(prefix!.GetValue<string>(), StringComparison.Ordinal))).Subject!;
        version["lanes"]!.AsArray().Should().Contain(lane =>
            lane!["origin"]!.GetValue<string>() == "Rotterdam" && lane["destination"]!.GetValue<string>() == antwerpZone["name"]!.GetValue<string>());
    }

    [Fact]
    public void Carriers_AreTheFourOfP3sDemo_OneOfThemInactive()
    {
        var carriers = DemoSeed()["carriers"]!.AsArray()
            .ToDictionary(carrier => carrier!["name"]!.GetValue<string>(), carrier => carrier!["active"]!.GetValue<bool>());

        carriers.Should().HaveCount(4);
        carriers.Should().Contain("Van Dijk Transport", true).And.Contain("Kessler Spedition", true).And.Contain("PakketPunt Express", true);
        carriers.Values.Should().ContainSingle(active => !active, "the demo shows an inactive Carrier can't be chosen");
    }

    [Fact]
    public void Carriers_HaveIdsOfTheirOwn_AndEveryCarrierPersonaIsOne()
    {
        var ids = DemoSeed()["carriers"]!.AsArray().Select(carrier => carrier!["id"]!.GetValue<string>()).ToList();
        var carrierPersonas = DemoSeed()["personas"]!.AsArray()
            .Where(persona => persona!["party"]!["type"]!.GetValue<string>() == "Carrier")
            .Select(persona => persona!["party"]!["id"]!.GetValue<string>());

        ids.Should().OnlyHaveUniqueItems();
        ids.Should().Contain(carrierPersonas, "the seed file is the only source of Carriers, so a Persona's Carrier is the TMS's");
    }

    [Fact]
    public void CarrierRateCards_AreThoseOfP9sDemo_EachForACarrierOfItsOwn()
    {
        var carrierIds = DemoSeed()["carriers"]!.AsArray().ToDictionary(carrier => carrier!["name"]!.GetValue<string>(), carrier => carrier!["id"]!.GetValue<string>());
        var cards = DemoSeed()["carrierRateCards"]!.AsArray();

        cards.Select(card => card!["carrierId"]!.GetValue<string>()).Should().BeEquivalentTo(
            [carrierIds["Van Dijk Transport"], carrierIds["Kessler Spedition"], carrierIds["PakketPunt Express"]]);
        cards.Select(card => card!["id"]!.GetValue<string>()).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void CarrierRateCards_VanDijkHasNoLaneIntoGermany_TheOthersDo()
    {
        LanesOf("Van Dijk Transport").Should().NotContain(lane => lane.Destination == "Düsseldorf");
        LanesOf("Kessler Spedition").Should().Contain(lane => lane.Origin == "Rotterdam" && lane.Destination == "Düsseldorf");
        LanesOf("PakketPunt Express").Should().Contain(lane => lane.Origin == "Rotterdam" && lane.Destination == "Düsseldorf");
    }

    [Fact]
    public void CarrierRateCards_PakketPuntPricesEveryLaneOfEveryShippersRateCard()
    {
        var shipperLanes = DemoSeed()["rateCards"]!.AsArray().SelectMany(card => LanesOf(card!)).Select(lane => (lane.Origin, lane.Destination)).Distinct().ToList();

        LanesOf("PakketPunt Express").Select(lane => (lane.Origin, lane.Destination)).Should().Contain(shipperLanes);
    }

    [Theory]
    [InlineData("Van Dijk Transport")]
    [InlineData("Kessler Spedition")]
    [InlineData("PakketPunt Express")]
    public void CarrierRateCards_PriceAbout15To25PercentBelowTheShipperRateCard_ForTheSameLane(string carrier)
    {
        var shipperLanes = LanesOf(BrouwerDrankensRateCard());

        var compared = 0;
        foreach (var lane in LanesOf(carrier))
        {
            if (shipperLanes.SingleOrDefault(l => l.Origin == lane.Origin && l.Destination == lane.Destination) is not { } shipperLane)
            {
                continue;
            }

            compared++;
            lane.Prices.Should().HaveSameCount(shipperLane.Prices);
            foreach (var (carrierPrice, shipperPrice) in lane.Prices.Zip(shipperLane.Prices))
            {
                (1 - (carrierPrice / shipperPrice)).Should().BeInRange(0.15m, 0.25m);
            }

            (1 - (lane.MinimumCharge / shipperLane.MinimumCharge)).Should().BeInRange(0.15m, 0.25m);
        }

        compared.Should().BePositive("the Carrier prices Lanes the Shipper's Rate Card has too");
    }

    [Fact]
    public void TripDay_HasShipmentsForARigidTruckTripAndAVanTrip_AndOneTooHeavyForTheVan()
    {
        var vehicles = DemoSeed()["vehicles"]!.AsArray();
        var van = vehicles.First(vehicle => vehicle!["type"]!.GetValue<string>() == "Van")!;
        var truck = vehicles.First(vehicle => vehicle!["type"]!.GetValue<string>() == "RigidTruck")!;
        var orders = DemoSeed()["demoTripDay"]!["transportOrders"]!.AsArray()
            .ToDictionary(order => order!["shipperReference"]!.GetValue<string>(), order => (Pallets: order!["pallets"]!.GetValue<int>(), Kg: order["pallets"]!.GetValue<int>() * order["weightKgPerPallet"]!.GetValue<decimal>()));
        var truckTrip = orders.Where(order => order.Key is "DEMO-TRIP-1" or "DEMO-TRIP-2" or "DEMO-TRIP-3").Select(order => order.Value).ToList();
        var vanTrip = orders.Where(order => order.Key is "DEMO-TRIP-4" or "DEMO-TRIP-5").Select(order => order.Value).ToList();
        var heavy = orders["DEMO-TRIP-HEAVY"];

        truckTrip.Sum(order => order.Pallets).Should().BeInRange(van["palletPlaces"]!.GetValue<int>() + 1, truck["palletPlaces"]!.GetValue<int>(), "too many for a van, and they fit the rigid truck");
        truckTrip.Sum(order => order.Kg).Should().BeLessThanOrEqualTo(truck["payloadKg"]!.GetValue<int>());
        vanTrip.Sum(order => order.Pallets).Should().BeLessThanOrEqualTo(van["palletPlaces"]!.GetValue<int>());
        vanTrip.Sum(order => order.Kg).Should().BeLessThanOrEqualTo(van["payloadKg"]!.GetValue<int>());
        (vanTrip.Sum(order => order.Kg) + heavy.Kg).Should().BeGreaterThan(van["payloadKg"]!.GetValue<int>(), "adding it to the van's Trip shows PayloadExceeded");
        heavy.Kg.Should().BeLessThanOrEqualTo(truck["payloadKg"]!.GetValue<int>());
    }

    [Fact]
    public void TripExecution_IsForAnActiveDriverWithTheLicenceAndAVehicleThatTakesThreeShipments()
    {
        var execution = DemoSeed()["demoTripExecution"]!;
        var driver = DemoSeed()["drivers"]!.AsArray().Single(d => d!["id"]!.GetValue<string>() == execution["driverId"]!.GetValue<string>())!;
        var vehicle = DemoSeed()["vehicles"]!.AsArray().Single(v => v!["id"]!.GetValue<string>() == execution["vehicleId"]!.GetValue<string>())!;
        var orders = execution["transportOrders"]!.AsArray();

        driver["active"]!.GetValue<bool>().Should().BeTrue();
        driver["licences"]!.AsArray().Select(licence => licence!.GetValue<string>()).Should().Contain("C", "a rigid truck needs category C");
        vehicle["type"]!.GetValue<string>().Should().Be("RigidTruck");
        orders.Should().HaveCount(3, "one delivered, one failed, one delivered with reservations");
        orders.Sum(order => order!["pallets"]!.GetValue<int>()).Should().BeLessThanOrEqualTo(vehicle["palletPlaces"]!.GetValue<int>());
        execution["firstDayFromStart"]!.GetValue<int>().Should().Be(0, "the Trip is Dispatched for today");
    }

    private static List<SeedLane> LanesOf(string carrier) => LanesOf(CarrierRateCard(carrier));

    private static List<SeedLane> LanesOf(JsonNode card) =>
        [.. card["versions"]!.AsArray().Single()!["lanes"]!.AsArray().Select(lane => new SeedLane(
            lane!["origin"]!.GetValue<string>(),
            lane["destination"]!.GetValue<string>(),
            lane["minimumCharge"]!.GetValue<decimal>(),
            [.. lane["weightBreaks"]!.AsArray().Select(weightBreak => weightBreak!["pricePer100Kg"]!.GetValue<decimal>())]))];

    private static JsonNode CarrierRateCard(string carrier)
    {
        var carrierId = DemoSeed()["carriers"]!.AsArray().Single(c => c!["name"]!.GetValue<string>() == carrier)!["id"]!.GetValue<string>();
        return DemoSeed()["carrierRateCards"]!.AsArray().Single(card => card!["carrierId"]!.GetValue<string>() == carrierId)!;
    }

    private sealed record SeedLane(string Origin, string Destination, decimal MinimumCharge, IReadOnlyList<decimal> Prices);

    private static JsonNode DemoSeed() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "DemoSeed", "norvia.seed.json")))!;

    private static JsonNode BrouwerDranken() =>
        DemoSeed()["shippers"]!.AsArray().Single(shipper => shipper!["name"]!.GetValue<string>() == "Brouwer Dranken")!;

    private static JsonNode BrouwerDrankensRateCard() =>
        DemoSeed()["rateCards"]!.AsArray().Single(rateCard => rateCard!["shipperId"]!.GetValue<string>() == BrouwerDranken()["id"]!.GetValue<string>())!;
}
