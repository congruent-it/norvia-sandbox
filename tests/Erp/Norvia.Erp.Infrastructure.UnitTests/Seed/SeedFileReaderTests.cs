using System.Text;
using Norvia.Erp.Infrastructure.Seed;

namespace Norvia.Erp.Infrastructure.UnitTests.Seed;

public sealed class SeedFileReaderTests
{
    private static readonly string[] RotterdamPrefixes = ["30", "31", "32"];

    [Fact]
    public void Read_TheSandboxSeedFile_ReturnsTheMasterDataWithTheTmsIds()
    {
        using var file = typeof(SeedFileReaderTests).Assembly.GetManifestResourceStream("seed/norvia.seed.json")!;

        var seed = SeedFileReader.Read(file, "norvia.seed.json");

        seed.Shippers.Select(shipper => shipper.Id).Should().Equal("SHP-0001", "SHP-0002", "SHP-0003");
        seed.Carriers.Select(carrier => carrier.Id).Should().Equal("CAR-0001", "CAR-0002", "CAR-0003", "CAR-0004");
        seed.RateCards.Select(card => (card.Id, card.PartyId)).Should().Equal(("RC-0001", "SHP-0001"), ("RC-0002", "SHP-0003"));
        seed.CarrierRateCards.Select(card => (card.Id, card.PartyId)).Should().Equal(("CRC-0001", "CAR-0001"), ("CRC-0002", "CAR-0002"), ("CRC-0003", "CAR-0003"));
    }

    [Fact]
    public void Read_ARateCard_KeepsItsVersionsZonesLanesAndWeightBreaks()
    {
        using var file = typeof(SeedFileReaderTests).Assembly.GetManifestResourceStream("seed/norvia.seed.json")!;

        var version = SeedFileReader.Read(file, "norvia.seed.json").RateCards[0].Versions.Should().ContainSingle().Subject;

        version.Version.Should().Be(1);
        version.ValidFrom.Should().Be(new DateOnly(2026, 1, 1));
        version.ValidTo.Should().BeNull();
        version.Zones.Should().HaveCount(6);
        version.Zones[0].Should().BeEquivalentTo(new { Name = "Rotterdam", Country = "NL", PostcodePrefixes = RotterdamPrefixes });
        version.Lanes.Should().HaveCount(6);
        version.Lanes[0].Origin.Should().Be("Rotterdam");
        version.Lanes[0].MinimumCharge.Should().Be(55m);
        version.Lanes[0].WeightBreaks[0].UpToKg.Should().Be(500m);
        version.Lanes[0].WeightBreaks[0].PricePer100Kg.Should().Be(9.5m);
    }

    [Fact]
    public void Read_ASeedFileWithoutTheMasterDataSections_ReturnsNone()
    {
        var seed = Read("{}");

        seed.Shippers.Should().BeEmpty();
        seed.Carriers.Should().BeEmpty();
        seed.RateCards.Should().BeEmpty();
        seed.CarrierRateCards.Should().BeEmpty();
    }

    [Fact]
    public void Read_ACardWithoutVersions_ReturnsItWithNone()
    {
        var seed = Read("""{ "rateCards": [ { "id": "RC-1", "shipperId": "SHP-1" } ] }""");

        seed.RateCards.Should().ContainSingle().Which.Versions.Should().BeEmpty();
    }

    [Fact]
    public void Read_ALaneWithoutAMinimumCharge_IsABrokenSeed()
    {
        var reading = () => Read("""
            { "carrierRateCards": [ { "id": "CRC-1", "carrierId": "CAR-1", "versions": [ { "version": 1, "validFrom": "2026-01-01", "validTo": null,
              "zones": [], "lanes": [ { "origin": "A", "destination": "B", "weightBreaks": [] } ] } ] } ] }
            """);

        reading.Should().Throw<InvalidDataException>().WithMessage("*A to B*CRC-1 version 1*no minimum charge*");
    }

    [Fact]
    public void Read_AnEmptyDocument_IsABrokenSeed()
    {
        var reading = () => Read("null");

        reading.Should().Throw<InvalidDataException>().WithMessage("*is empty*");
    }

    private static Application.MasterData.Load.MasterDataSeed Read(string json)
    {
        using var file = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return SeedFileReader.Read(file, "test.seed.json");
    }
}
