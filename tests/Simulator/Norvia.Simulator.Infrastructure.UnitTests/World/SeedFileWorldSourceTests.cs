using System.Text;
using Norvia.Simulator.Infrastructure.World;

namespace Norvia.Simulator.Infrastructure.UnitTests.World;

public sealed class SeedFileWorldSourceTests
{
    private static SeedFileWorldSource FromDemoSeed() => new(Contracts.OpenRead("seed/norvia.seed.json"));

    [Fact]
    public void Load_TheDemoSeed_HasEachActiveShipperWithAPersonaAndARateCard()
    {
        var shippers = FromDemoSeed().Load().Shippers;

        shippers.Select(shipper => shipper.Id).Should().BeEquivalentTo("SHP-0001", "SHP-0003");
        shippers.Single(shipper => shipper.Id == "SHP-0001").PersonaId.Should().Be("brouwer-dranken");
        shippers.Single(shipper => shipper.Id == "SHP-0003").PersonaId.Should().Be("tuincentrum-groenrijk");
    }

    [Fact]
    public void Load_TheDemoSeed_GivesTheZonesAndLanesOfTheRateCard()
    {
        var brouwer = FromDemoSeed().Load().Shippers.Single(shipper => shipper.Id == "SHP-0001");

        brouwer.Zones.Select(zone => zone.Name).Should().Contain(["Rotterdam", "Antwerpen", "Luxembourg"]);
        brouwer.Zones.Should().NotContain(zone => zone.Country == "DE", "the deliberate gap that makes a booking to Düsseldorf find no Lane");
        brouwer.Lanes.Should().OnlyContain(lane => lane.Origin == "Rotterdam");
    }

    [Fact]
    public void Load_TheDemoSeed_EveryLaneNamesZonesTheRateCardDefines()
    {
        foreach (var shipper in FromDemoSeed().Load().Shippers)
        {
            var names = shipper.Zones.Select(zone => zone.Name).ToList();
            shipper.Lanes.Should().OnlyContain(lane => names.Contains(lane.Origin) && names.Contains(lane.Destination));
        }
    }

    [Fact]
    public void Load_TheDemoSeed_HasTheCarriersThePlannerCanAssignTo()
    {
        var world = FromDemoSeed().Load();

        world.ActiveCarriers.Select(carrier => carrier.Id).Should().Contain(["CAR-0001", "CAR-0002"]);
        world.ActiveCarriers.Should().OnlyContain(carrier => carrier.Active);
        world.Carriers.Should().Contain(carrier => !carrier.Active, "the seed's inactive Carrier is there, and never chosen");
    }

    [Fact]
    public void Load_ASeedWithoutCarriers_HasNone()
    {
        var seed = """{"shippers":[],"personas":[]}""";

        new SeedFileWorldSource(new MemoryStream(Encoding.UTF8.GetBytes(seed))).Load().ActiveCarriers.Should().BeEmpty();
    }

    [Fact]
    public void Load_AnInactiveShipper_IsLeftOut()
    {
        var seed = """
            {"shippers":[{"id":"S1","name":"Off","active":false}],
             "personas":[{"id":"p1","displayName":"P1","party":{"type":"Shipper","id":"S1"}}],
             "rateCards":[{"shipperId":"S1","versions":[{"version":1,"zones":[{"name":"A","country":"NL","postcodePrefixes":["1"]}],"lanes":[{"origin":"A","destination":"A"}]}]}]}
            """;

        new SeedFileWorldSource(new MemoryStream(Encoding.UTF8.GetBytes(seed))).Load().Shippers.Should().BeEmpty();
    }

    [Fact]
    public void Load_AShipperWithoutARateCardOrAPersona_IsLeftOut()
    {
        var seed = """
            {"shippers":[{"id":"S1","name":"NoCard","active":true},{"id":"S2","name":"NoPersona","active":true}],
             "personas":[{"id":"p1","displayName":"P1","party":{"type":"Shipper","id":"S1"}}],
             "rateCards":[{"shipperId":"S2","versions":[{"version":1,"zones":[],"lanes":[]}]}]}
            """;

        new SeedFileWorldSource(new MemoryStream(Encoding.UTF8.GetBytes(seed))).Load().Shippers.Should().BeEmpty();
    }

    [Fact]
    public void Load_TheLatestVersionOfARateCard_IsTheOneUsed()
    {
        var seed = """
            {"shippers":[{"id":"S1","name":"S","active":true}],
             "personas":[{"id":"p1","displayName":"P1","party":{"type":"Shipper","id":"S1"}}],
             "rateCards":[{"shipperId":"S1","versions":[
               {"version":1,"zones":[{"name":"Old","country":"NL","postcodePrefixes":["1"]}],"lanes":[{"origin":"Old","destination":"Old"}]},
               {"version":2,"zones":[{"name":"New","country":"NL","postcodePrefixes":["2"]}],"lanes":[{"origin":"New","destination":"New"}]}]}]}
            """;

        new SeedFileWorldSource(new MemoryStream(Encoding.UTF8.GetBytes(seed))).Load().Shippers.Single().Zones.Single().Name.Should().Be("New");
    }

    [Fact]
    public void Load_AnEmptyFile_Throws()
    {
        var act = () => new SeedFileWorldSource(new MemoryStream(Encoding.UTF8.GetBytes("null")));

        act.Should().Throw<InvalidDataException>();
    }
}
