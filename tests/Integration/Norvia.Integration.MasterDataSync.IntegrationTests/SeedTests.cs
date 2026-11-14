using System.Text.Json.Nodes;

namespace Norvia.Integration.MasterDataSync.IntegrationTests;

/// <summary>A fresh estate: the ERP loads the seed file's master data at revision 1, and the TMS reads the same file at revision 1, with nothing published to make them agree.</summary>
public sealed class SeedTests(MasterDataSyncFixture fixture)
{
    /// <summary>Seed records another test of this project edits, so they are no longer at revision 1.</summary>
    private static readonly string[] EditedByOtherTests = ["VEH-0006", "DRV-0007"];

    private static readonly string[] PartyCollections = ["shippers", "carriers", "vehicles", "drivers"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string, string> Entities => new()
    {
        { "shippers", "Shippers", "ShipperCopies" },
        { "carriers", "Carriers", "CarrierCopies" },
        { "rateCards", "RateCards", "RateCardCopies" },
        { "carrierRateCards", "CarrierRateCards", "CarrierRateCardCopies" },
        { "vehicles", "Vehicles", "VehicleCopies" },
        { "drivers", "Drivers", "DriverCopies" },
    };

    [Theory]
    [MemberData(nameof(Entities))]
    public async Task EveryRecordOfTheSeed_IsInTheErpAtRevision1_AndTheTmsHasReplicatedNone(string seedCollection, string erpTable, string tmsTable)
    {
        var seed = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "norvia.seed.json"), Ct))!;
        var ids = seed[seedCollection]!.AsArray().Select(item => item!["id"]!.GetValue<string>()).Where(id => !EditedByOtherTests.Contains(id)).ToList();
        ids.Should().NotBeEmpty();

        var erp = await fixture.ErpRowsAsync($"SELECT Id, Revision FROM {erpTable}", Ct);
        foreach (var id in ids)
        {
            erp.Should().ContainSingle(row => (string)row["Id"]! == id).Which["Revision"].Should().Be(1, $"{id} is as the seed loaded it");
        }

        var replicated = (await fixture.TmsRowsAsync($"SELECT Id FROM {tmsTable}", Ct)).Select(row => (string)row["Id"]!);
        replicated.Should().NotIntersectWith(ids, "the TMS's seed first load is its revision 1, and nothing replicated it");
    }

    [Fact]
    public async Task NoEventWasPublished_ForTheSeed()
    {
        var published = await fixture.ErpRowsAsync("SELECT ApplicationProperties FROM OutboxMessages WHERE Topic = 'master-data-events'", Ct);
        var parties = published.Select(row => JsonNode.Parse((string)row["ApplicationProperties"]!)!["partyId"]!.GetValue<string>()).ToList();

        var seed = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "norvia.seed.json"), Ct))!;
        var seeded = PartyCollections.SelectMany(name => seed[name]!.AsArray()).Select(item => item!["id"]!.GetValue<string>())
            .Where(id => !EditedByOtherTests.Contains(id));
        parties.Should().NotIntersectWith(seeded);
        fixture.Events.Select(message => Kinds.All.Select(kind => MasterDataSyncFixture.RecordOf(kind, message)?["id"]?.GetValue<string>()).FirstOrDefault(id => id is not null))
            .Should().NotIntersectWith(seeded);
    }
}
