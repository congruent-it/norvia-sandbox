using System.Net;
using System.Text.Json.Nodes;

namespace Norvia.Integration.MasterDataSync.IntegrationTests;

/// <summary>The TMS API's replace commands (<c>PUT /v1/{entity}/{id}</c>, role <c>MasterData.Replace</c>) against the real TMS and its database, called as the Logic App calls them.</summary>
public sealed class TmsReplaceCommandTests(MasterDataSyncFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> KindNames => Kinds.Names;

    [Theory]
    [MemberData(nameof(KindNames))]
    public async Task ANewRecord_IsUpserted(string kindName)
    {
        var kind = Kinds.Named(kindName);
        var id = kind.NewId();
        var body = await NewBodyAsync(kind, id);

        var (status, answer) = await fixture.ReplaceInTmsAsync(kind, id, body, 1, ct: Ct);

        status.Should().Be(HttpStatusCode.OK, answer?.ToJsonString());
        answer!["applied"]!.GetValue<bool>().Should().BeTrue();
        var row = await fixture.ReplicaRowAsync(kind, id, Ct);
        row!["Revision"].Should().Be(1);
        kind.ReplicaMatches(row, body).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(KindNames))]
    public async Task AChangedRecord_IsReplaced(string kindName)
    {
        var kind = Kinds.Named(kindName);
        var id = kind.NewId();
        var body = await NewBodyAsync(kind, id);
        await fixture.ReplaceInTmsAsync(kind, id, body, 1, ct: Ct);
        kind.Change(body);

        var (status, answer) = await fixture.ReplaceInTmsAsync(kind, id, body, 2, ct: Ct);

        status.Should().Be(HttpStatusCode.OK);
        answer!["applied"]!.GetValue<bool>().Should().BeTrue();
        var row = await fixture.ReplicaRowAsync(kind, id, Ct);
        row!["Revision"].Should().Be(2);
        kind.ReplicaMatches(row, body).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(KindNames))]
    public async Task ARevisionThatIsNotHigher_IsIgnoredWith2xx_AndChangesNothing(string kindName)
    {
        var kind = Kinds.Named(kindName);
        var id = kind.NewId();
        var body = await NewBodyAsync(kind, id);
        await fixture.ReplaceInTmsAsync(kind, id, body, 2, ct: Ct);
        var older = (JsonObject)body.DeepClone();
        kind.Change(older);

        foreach (var revision in new[] { 2, 1 })
        {
            var (status, answer) = await fixture.ReplaceInTmsAsync(kind, id, older, revision, ct: Ct);

            status.Should().Be(HttpStatusCode.OK);
            answer!["applied"]!.GetValue<bool>().Should().BeFalse();
            answer["revision"]!.GetValue<int>().Should().Be(2);
        }

        var row = await fixture.ReplicaRowAsync(kind, id, Ct);
        row!["Revision"].Should().Be(2);
        kind.ReplicaMatches(row, body).Should().BeTrue("the copy still holds what revision 2 brought");
    }

    [Theory]
    [InlineData("rate-cards")]
    [InlineData("carrier-rate-cards")]
    public async Task ACardReplacement_SwapsTheWholeListOfDatedVersions(string kindName)
    {
        var kind = Kinds.Named(kindName);
        var id = kind.NewId();
        var body = await NewBodyAsync(kind, id);
        kind.Change(body);
        await fixture.ReplaceInTmsAsync(kind, id, body, 1, ct: Ct);
        body["versions"] = new JsonArray(Cards.Version(3, "2026-01-01", null, 99m));

        await fixture.ReplaceInTmsAsync(kind, id, body, 2, ct: Ct);

        var versions = JsonNode.Parse((string)(await fixture.ReplicaRowAsync(kind, id, Ct))!["VersionsJson"]!)!.AsArray();
        versions.Should().ContainSingle().Which!["version"]!.GetValue<int>().Should().Be(3, "the earlier versions went with the replacement");
    }

    [Fact]
    public async Task ACallerWithoutTheReplaceRole_IsRefused403()
    {
        var kind = Kinds.Shipper;
        var id = kind.NewId();
        using var other = fixture.CreateTmsClient(fixture.IssueToken(["TransportOrders.Intake"]));

        var (status, _) = await fixture.ReplaceInTmsAsync(kind, id, await kind.NewBody(fixture, id, Ct), 1, other, Ct);

        status.Should().Be(HttpStatusCode.Forbidden);
        (await fixture.ReplicaRowAsync(kind, id, Ct)).Should().BeNull();
    }

    [Fact]
    public async Task ACardWhoseVersionsOverlap_IsRefused400()
    {
        var kind = Kinds.RateCard;
        var id = kind.NewId();
        var body = await NewBodyAsync(kind, id);
        body["versions"] = new JsonArray(Cards.Version(1, "2026-01-01", "2026-12-31", 65m), Cards.Version(2, "2026-06-01", null, 75m));

        var (status, answer) = await fixture.ReplaceInTmsAsync(kind, id, body, 1, ct: Ct);

        status.Should().Be(HttpStatusCode.BadRequest);
        answer!["code"]!.GetValue<string>().Should().Be("RateCardVersionsOverlap");
    }

    [Fact]
    public async Task AVehicleMadeInactive_KeepsItsAvailability_AndTheDaysAlreadyOpened()
    {
        // A Vehicle of the estate, as the ERP's seed has it: the TMS opened its days from the seed, and the ERP now makes it inactive.
        var seed = SeedRecord("vehicles", "VEH-0006");
        var before = await AvailabilityAsync();
        seed["active"] = false;

        (await fixture.SaveAsync(Kinds.Vehicle, "VEH-0006", seed, ct: Ct)).Status.Should().Be(HttpStatusCode.OK);

        var replica = await fixture.ReplicaAsync(Kinds.Vehicle, "VEH-0006", 2, Ct);
        replica["Active"].Should().Be(false);
        (await AvailabilityAsync()).Should().Be(before, "a replaced Vehicle edits no off-the-road day and opens or closes no day");
    }

    [Fact]
    public async Task ADriverMadeInactive_KeepsItsRoster_AndTheDaysAlreadyOpened()
    {
        var seed = SeedRecord("drivers", "DRV-0007");
        var before = await AvailabilityAsync();
        seed["active"] = false;

        (await fixture.SaveAsync(Kinds.Driver, "DRV-0007", seed, ct: Ct)).Status.Should().Be(HttpStatusCode.OK);

        var replica = await fixture.ReplicaAsync(Kinds.Driver, "DRV-0007", 2, Ct);
        replica["Active"].Should().Be(false);
        (await AvailabilityAsync()).Should().Be(before, "a replaced Driver edits no Roster and opens or closes no day");
    }

    private async Task<JsonObject> NewBodyAsync(Kind kind, string id) => await kind.NewBody(fixture, id, Ct);

    /// <summary>A record of the repo's seed file as the ERP API takes it: the record but for its ID and what the API does not take.</summary>
    internal static JsonObject SeedRecord(string collection, string id)
    {
        var seed = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "norvia.seed.json")))!;
        var record = (JsonObject)seed[collection]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == id)!.DeepClone();
        foreach (var name in new[] { "$comment", "id", "roster", "addresses" })
        {
            record.Remove(name);
        }

        return record;
    }

    /// <summary>What the TMS holds of availability: its Rosters, its off-the-road days and the days opened, as one fingerprint of the rows.</summary>
    private async Task<string> AvailabilityAsync()
    {
        var rows = await fixture.TmsRowsAsync(
            """
            SELECT
              (SELECT COUNT(*) FROM Rosters) AS Rosters,
              (SELECT CHECKSUM_AGG(CHECKSUM(DriverId, Weekdays)) FROM Rosters) AS RosterSum,
              (SELECT COUNT(*) FROM OffTheRoadDays) AS OffTheRoad,
              (SELECT COUNT(*) FROM PlanningDays) AS Days,
              (SELECT CHECKSUM_AGG(CHECKSUM(Status, Shifts, AvailableVehicles)) FROM PlanningDays) AS DaySum
            """, Ct);
        return string.Join('/', rows.Single().Values);
    }
}
