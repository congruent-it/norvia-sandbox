using System.Net;

namespace Norvia.Integration.MasterDataSync.IntegrationTests;

/// <summary>The <c>master-data-events</c> topic and its two subscriptions on the emulator, with the filters the AppHost gives them.</summary>
public sealed class TopicAndSubscriptionTests(MasterDataSyncFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AMasterDataEvent_IsDeliveredToMonitoring_AndToMasterDataSync()
    {
        var kind = Kinds.Shipper;
        var id = kind.NewId();
        await fixture.SaveAsync(kind, id, await kind.NewBody(fixture, id, Ct), ct: Ct);

        var seenByMonitoring = await fixture.EventAsync(kind, id, 1, Ct);

        // The sync subscription's only reader is the worker, which replaced the TMS's copy from the message it took from it.
        await fixture.ReplicaAsync(kind, id, 1, Ct);
        fixture.Proxy.CallsTo($"/v1/shippers/{id}").Should().ContainSingle().Which.CorrelationId.Should().Be(seenByMonitoring.CorrelationId);
    }

    [Fact]
    public async Task AnEventOfAnotherType_ReachesMonitoring_ButNotMasterDataSync()
    {
        var other = Guid.NewGuid().ToString();
        await fixture.SendToTopicAsync("TransportOrderReceived", """{"eventType":"TransportOrderReceived"}""", other, other, Ct);
        // A master-data event after it is the positive control: once the TMS has it, the worker had its chance at the first.
        var kind = Kinds.Carrier;
        var id = kind.NewId();
        await fixture.SaveAsync(kind, id, await kind.NewBody(fixture, id, Ct), ct: Ct);
        await fixture.ReplicaAsync(kind, id, 1, Ct);
        await MasterDataSyncFixture.SettleAsync(Ct);

        await fixture.WaitForAsync(() => Task.FromResult(fixture.Events.FirstOrDefault(message => message.MessageId == other)), "the other event on monitoring", Ct);
        (await fixture.DeadLetterQueueAsync(MasterDataSyncFixture.SyncSubscription, Ct)).Should().NotContain(message => message.MessageId == other, "the sync's filter keeps it out; the worker would dead-letter it as unreadable");
    }

    [Fact]
    public async Task AMasterDataEventThatIsNotJson_ReachesTheWorker_AndIsDeadLettered()
    {
        var id = Guid.NewGuid().ToString();

        await fixture.SendToTopicAsync("ShipperChanged", "not json", id, id, Ct);

        var dead = await fixture.DeadLetteredAsync(MasterDataSyncFixture.SyncSubscription, id, Ct);
        dead.DeadLetterReason.Should().Be("MasterDataUnreadable");
    }
}
