using System.Text.Json.Nodes;

namespace Norvia.Tms.Api.IntegrationTests;

/// <summary>
/// The demo seed (seed/norvia.seed.json) as P2's demo needs it: Brouwer Dranken's Rate Card prices Benelux Lanes out
/// of Rotterdam and has a deliberate gap into Germany, and Brouwer Dranken has an Antwerp address to book the Rated
/// path to. The TMS UI reads the demo seed in the test host, so it starting at all shows the seed loads.
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

    private static JsonNode DemoSeed() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "DemoSeed", "norvia.seed.json")))!;

    private static JsonNode BrouwerDranken() =>
        DemoSeed()["shippers"]!.AsArray().Single(shipper => shipper!["name"]!.GetValue<string>() == "Brouwer Dranken")!;

    private static JsonNode BrouwerDrankensRateCard() =>
        DemoSeed()["rateCards"]!.AsArray().Single(rateCard => rateCard!["shipperId"]!.GetValue<string>() == BrouwerDranken()["id"]!.GetValue<string>())!;
}
