using System.Net;
using System.Text.Json.Nodes;

namespace Norvia.Integration.MasterDataSync.IntegrationTests;

/// <summary>The ERP's outbox: an edit and its event are one save, and the event is the whole record with a higher Master-data revision and the tracking contract's keys.</summary>
public sealed class OutboxTests(MasterDataSyncFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> KindNames => Kinds.Names;

    [Theory]
    [MemberData(nameof(KindNames))]
    public async Task EachEdit_PublishesOneWholeRecordEvent_WithAHigherRevision(string kindName)
    {
        var kind = Kinds.Named(kindName);
        var id = kind.NewId();
        var body = await kind.NewBody(fixture, id, Ct);
        await fixture.SaveAsync(kind, id, body, ct: Ct);
        kind.Change(body);
        await fixture.SaveAsync(kind, id, body, ct: Ct);

        var events = await fixture.EventsOfAsync(kind, id, Ct);

        events.Select(message => MasterDataSyncFixture.RecordOf(kind, message)!["revision"]!.GetValue<int>()).Should().Equal(1, 2);
        var latest = MasterDataSyncFixture.RecordOf(kind, events[^1])!;
        foreach (var (name, value) in body)
        {
            JsonNode.DeepEquals(latest[name], value).Should().BeTrue($"the event carries the whole record, its {name} included");
        }
    }

    [Fact]
    public async Task ARepeatedSave_PublishesNothingMore()
    {
        var kind = Kinds.Shipper;
        var id = kind.NewId();
        var body = await kind.NewBody(fixture, id, Ct);
        await fixture.SaveAsync(kind, id, body, ct: Ct);
        await fixture.SaveAsync(kind, id, body, ct: Ct);

        (await fixture.EventsOfAsync(kind, id, Ct)).Should().HaveCount(1);
    }

    [Fact]
    public async Task ARefusedEdit_PublishesNothing_AndLeavesTheRevision()
    {
        var kind = Kinds.Vehicle;
        var first = kind.NewId();
        var second = kind.NewId();
        var firstBody = await kind.NewBody(fixture, first, Ct);
        var secondBody = await kind.NewBody(fixture, second, Ct);
        await fixture.SaveAsync(kind, first, firstBody, ct: Ct);
        await fixture.SaveAsync(kind, second, secondBody, ct: Ct);
        var clash = (JsonObject)secondBody.DeepClone();
        clash["registration"] = firstBody["registration"]!.GetValue<string>();
        clash["payloadKg"] = 5000;

        var refused = await fixture.SaveAsync(kind, second, clash, ct: Ct);

        refused.Status.Should().Be(HttpStatusCode.BadRequest);
        var events = await fixture.EventsOfAsync(kind, second, Ct);
        events.Should().HaveCount(1, "only the creation was published");
        MasterDataSyncFixture.RecordOf(kind, events[0])!["payloadKg"]!.GetValue<int>().Should().Be(1000);
        var rows = await fixture.ErpRowsAsync("SELECT Revision FROM Vehicles WHERE Id = @id", Ct, ("@id", second));
        rows.Single()["Revision"].Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(KindNames))]
    public async Task TheEvent_CarriesTheTrackingContractKeys(string kindName)
    {
        var kind = Kinds.Named(kindName);
        var id = kind.NewId();
        await fixture.SaveAsync(kind, id, await kind.NewBody(fixture, id, Ct), ct: Ct);

        var message = await fixture.EventAsync(kind, id, 1, Ct);

        var record = MasterDataSyncFixture.RecordOf(kind, message)!;
        message.Subject.Should().Be(kind.EventType);
        message.ApplicationProperties["eventType"].Should().Be(kind.EventType);
        message.ApplicationProperties["eventVersion"].Should().Be(1);
        message.ApplicationProperties["flow"].Should().Be("master-data");
        message.ApplicationProperties["partyId"].Should().Be(kind.PartyOf(record));
        message.ApplicationProperties["correlationId"].Should().Be(message.CorrelationId);
        message.CorrelationId.Should().NotBeNullOrWhiteSpace();
        var body = JsonNode.Parse(message.Body.ToString())!;
        body["eventId"]!.GetValue<string>().Should().Be(message.MessageId);
        body["correlationId"]!.GetValue<string>().Should().Be(message.CorrelationId);
        body["eventType"]!.GetValue<string>().Should().Be(kind.EventType);
    }
}
