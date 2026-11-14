using System.Net;

namespace Norvia.Integration.MasterDataSync.IntegrationTests;

/// <summary>
/// The local worker's retries and dead letters, with the TMS failing on cue: an edit at the ERP goes through the real outbox and emulator to the worker, whose call to the TMS meets the
/// fault the test scripted for the record's path. The worker retries three times after the first call, as the workflow does (its waits are short here).
/// </summary>
public sealed class LocalWorkerFailureTests(MasterDataSyncFixture fixture)
{
    private const int Attempts = 4;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> Transient => ["500", "503", "408", "429", "none"];

    public static TheoryData<int> Refusals => [400, 401, 403, 404, 409, 422];

    [Theory]
    [MemberData(nameof(Transient))]
    public async Task ATransientFailure_IsRetried_AndTheRecordIsReplaced(string failure)
    {
        var (id, path) = NewShipper();
        fixture.Proxy.Script(path, Failure(failure), Failure(failure), Fault.Forward);

        var message = await SaveAsync(id);

        await fixture.ReplicaAsync(Kinds.Shipper, id, 1, Ct);
        fixture.Proxy.CallsTo(path).Should().HaveCount(3, "two failures and the call that went through");
        fixture.Proxy.CallsTo(path).Select(call => call.CorrelationId).Should().OnlyContain(correlation => correlation == message.CorrelationId, "a retry keeps the event's correlation ID");
        await MasterDataSyncFixture.SettleAsync(Ct);
        (await fixture.DeadLetterQueueAsync(MasterDataSyncFixture.SyncSubscription, Ct)).Should().NotContain(dead => dead.MessageId == message.MessageId);
    }

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task AnyOtherClientError_IsNotRetried_AndIsDeadLetteredAsTmsCallRefused(int status)
    {
        var (id, path) = NewShipper();
        fixture.Proxy.Script(path, Fault.Answer(status));

        var message = await SaveAsync(id);

        var dead = await fixture.DeadLetteredAsync(MasterDataSyncFixture.SyncSubscription, message.MessageId, Ct);
        dead.DeadLetterReason.Should().Be("TmsCallRefused");
        dead.DeadLetterErrorDescription.Should().NotBeNullOrWhiteSpace();
        fixture.Proxy.CallsTo(path).Should().ContainSingle("a refusal is final");
        (await fixture.ReplicaRowAsync(Kinds.Shipper, id, Ct)).Should().BeNull();
    }

    [Theory]
    [InlineData("500")]
    [InlineData("408")]
    [InlineData("429")]
    [InlineData("none")]
    public async Task ATmsThatKeepsFailing_IsRetriedThreeTimes_ThenDeadLetteredAsTmsUnavailable(string failure)
    {
        var (id, path) = NewShipper();
        fixture.Proxy.Script(path, Enumerable.Repeat(Failure(failure), Attempts).ToArray());

        var message = await SaveAsync(id);

        var dead = await fixture.DeadLetteredAsync(MasterDataSyncFixture.SyncSubscription, message.MessageId, Ct);
        dead.DeadLetterReason.Should().Be("TmsUnavailable");
        fixture.Proxy.CallsTo(path).Should().HaveCount(Attempts);
        (await fixture.ReplicaRowAsync(Kinds.Shipper, id, Ct)).Should().BeNull();
    }

    [Theory]
    [InlineData("ShipperChanged", """{"eventId":"0198e7c0-0000-7000-8000-0000000000e3","eventType":"ShipperChanged","correlationId":"c-1"}""")]
    [InlineData("VehicleChanged", """{"eventId":"0198e7c0-0000-7000-8000-0000000000e4","eventType":"VehicleChanged","correlationId":"c-2","vehicle":{"id":"VEH-X","revision":1}}""")]
    [InlineData("RateCardChanged", """{"eventId":"0198e7c0-0000-7000-8000-0000000000e5","eventType":"RateCardChanged","correlationId":"c-3","rateCard":{"id":"RC-X","revision":1}}""")]
    public async Task AMessageTheTmsCannotReplaceARecordFrom_IsDeadLetteredAsMasterDataUnreadable_AndNothingIsSent(string eventType, string body)
    {
        var id = Guid.NewGuid().ToString();
        var before = fixture.Proxy.CallsTo("/v1/shippers/").Count;

        await fixture.SendToTopicAsync(eventType, body, id, id, Ct);

        var dead = await fixture.DeadLetteredAsync(MasterDataSyncFixture.SyncSubscription, id, Ct);
        dead.DeadLetterReason.Should().Be("MasterDataUnreadable");
        fixture.Proxy.CallsTo("/v1/shippers/").Count.Should().Be(before);
    }

    private static Fault Failure(string failure) => failure == "none" ? Fault.NoAnswer : Fault.Answer(int.Parse(failure, System.Globalization.CultureInfo.InvariantCulture));

    private static (string Id, string Path) NewShipper()
    {
        var id = Kinds.Shipper.NewId();
        return (id, $"/v1/shippers/{id}");
    }

    private async Task<Azure.Messaging.ServiceBus.ServiceBusReceivedMessage> SaveAsync(string id)
    {
        var saved = await fixture.SaveAsync(Kinds.Shipper, id, await Kinds.Shipper.NewBody(fixture, id, Ct), ct: Ct);
        saved.Status.Should().Be(HttpStatusCode.Created);
        return await fixture.EventAsync(Kinds.Shipper, id, 1, Ct);
    }
}
