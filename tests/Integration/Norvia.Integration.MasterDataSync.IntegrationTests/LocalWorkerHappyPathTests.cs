using System.Net;
using System.Text.Json.Nodes;

namespace Norvia.Integration.MasterDataSync.IntegrationTests;

/// <summary>
/// The local worker that plays the master-data sync Logic App, end to end: an edit at the ERP API is published by the ERP's outbox, reaches the worker through the
/// emulator's <c>master-data-sync</c> subscription, and the TMS's copy becomes the ERP's record at the same revision.
/// </summary>
public sealed class LocalWorkerHappyPathTests(MasterDataSyncFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> Kinds => Integration.MasterDataSync.IntegrationTests.Kinds.Names;

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task ANewRecord_ReachesTheTms_AtRevision1(string kindName)
    {
        var kind = Integration.MasterDataSync.IntegrationTests.Kinds.Named(kindName);
        var id = kind.NewId();
        var body = await kind.NewBody(fixture, id, Ct);

        var saved = await fixture.SaveAsync(kind, id, body, ct: Ct);

        saved.Status.Should().Be(HttpStatusCode.Created, saved.Body?.ToJsonString());
        var replica = await fixture.ReplicaAsync(kind, id, 1, Ct);
        replica["Revision"].Should().Be(1);
        kind.ReplicaMatches(replica, body).Should().BeTrue("the TMS's copy is the ERP's record");
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task AnEditedRecord_ReplacesTheTmsCopy_AtTheNextRevision(string kindName)
    {
        var kind = Integration.MasterDataSync.IntegrationTests.Kinds.Named(kindName);
        var id = kind.NewId();
        var body = await kind.NewBody(fixture, id, Ct);
        (await fixture.SaveAsync(kind, id, body, ct: Ct)).Status.Should().Be(HttpStatusCode.Created);
        await fixture.ReplicaAsync(kind, id, 1, Ct);
        var edited = (JsonObject)body.DeepClone();
        kind.Change(edited);

        var saved = await fixture.SaveAsync(kind, id, edited, ct: Ct);

        saved.Status.Should().Be(HttpStatusCode.OK, saved.Body?.ToJsonString());
        var replica = await fixture.ReplicaAsync(kind, id, 2, Ct);
        replica["Revision"].Should().Be(2);
        kind.ReplicaMatches(replica, edited).Should().BeTrue("the TMS's copy is the edited record");
    }
}
