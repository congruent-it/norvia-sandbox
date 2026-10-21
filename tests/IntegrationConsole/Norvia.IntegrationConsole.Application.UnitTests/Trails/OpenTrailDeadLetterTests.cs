using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Norvia.IntegrationConsole.Application.Audit;
using Norvia.IntegrationConsole.Application.DeadLetters;
using Norvia.IntegrationConsole.Application.Trails;
using Norvia.IntegrationConsole.Application.Trails.Open;

namespace Norvia.IntegrationConsole.Application.UnitTests.Trails;

/// <summary>A dead-lettered hop says what became of its message: still on the queue, resubmitted, or discarded.</summary>
public sealed class OpenTrailDeadLetterTests
{
    private const string CorrelationId = "5b0d7f8e-2c41-4a3e-9d6a-0f1e2d3c4b5a";
    private const string Entity = "shipment-events/carrier-booking";

    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly BusinessKey Shipment = new(BusinessKeyKind.ShipmentNumber, "SH-0042");

    private readonly ITrailSource _source = Substitute.For<ITrailSource>();
    private readonly IDeadLetterQueues _queues = Substitute.For<IDeadLetterQueues>();
    private readonly FakeTimeProvider _time = new(Now);

    public OpenTrailDeadLetterTests()
    {
        _source.AuditAsync(Arg.Any<TrailLookup>(), Arg.Any<CancellationToken>()).Returns([]);
        _queues.DeadLetteredAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    [Fact]
    public async Task Handle_ForAHopWhoseMessageIsStillOnTheQueue_SaysWhichMessageItIs()
    {
        Record(Hop("run", minutesAgo: 30, Outcome.DeadLettered, Shipment));
        _queues.DeadLetteredAsync(CorrelationId, Arg.Any<CancellationToken>())
            .Returns([new FoundDeadLetter(Entity, 7, Now.AddMinutes(-29), [Shipment])]);

        var trail = await OpenAsync();

        trail.Hops.Single().DeadLetter.Should().Be(new DeadLetterHandling(DeadLetterStatus.OnQueue, Entity, 7, null, null, null));
    }

    [Fact]
    public async Task Handle_ForAResubmittedHop_SaysWhoAndWhen_AndShowsTheLaterAttemptOnTheSameTrail()
    {
        Record(
            Hop("first", minutesAgo: 60, Outcome.DeadLettered, Shipment),
            Hop("second", minutesAgo: 10, Outcome.Succeeded, Shipment));
        _source.AuditAsync(Arg.Any<TrailLookup>(), Arg.Any<CancellationToken>())
            .Returns([Audit(AuditActions.Resubmit, "ana@norvia.example", minutesAgo: 20)]);

        var trail = await OpenAsync();

        trail.Hops.Select(hop => hop.Name).Should().Equal("first", "second");
        trail.Hops[0].DeadLetter.Should().Be(new DeadLetterHandling(DeadLetterStatus.Resubmitted, Entity, null, "ana@norvia.example", Now.AddMinutes(-20), null));
        trail.Hops[1].DeadLetter.Should().BeNull();
        await _source.Received(1).AuditAsync(
            Arg.Is<TrailLookup>(lookup => lookup.CorrelationId == CorrelationId && lookup.To == Now), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ForADiscardedHop_SaysWhoWhenAndWhy()
    {
        Record(Hop("run", minutesAgo: 60, Outcome.DeadLettered, Shipment));
        _source.AuditAsync(Arg.Any<TrailLookup>(), Arg.Any<CancellationToken>())
            .Returns([Audit(AuditActions.Discard, "ben@norvia.example", minutesAgo: 20, reason: "Duplicate of SH-0041")]);

        var trail = await OpenAsync();

        trail.Hops.Single().DeadLetter.Should().Be(
            new DeadLetterHandling(DeadLetterStatus.Discarded, Entity, null, "ben@norvia.example", Now.AddMinutes(-20), "Duplicate of SH-0041"));
    }

    [Fact]
    public async Task Handle_ForAResubmitThatFailedAgain_GivesTheEarlierHopTheAuditEvent_AndTheLaterOneItsMessageOnTheQueue()
    {
        Record(
            Hop("first", minutesAgo: 60, Outcome.DeadLettered, Shipment),
            Hop("second", minutesAgo: 30, Outcome.DeadLettered, Shipment));
        _source.AuditAsync(Arg.Any<TrailLookup>(), Arg.Any<CancellationToken>())
            .Returns([Audit(AuditActions.Resubmit, "ana@norvia.example", minutesAgo: 40)]);
        _queues.DeadLetteredAsync(CorrelationId, Arg.Any<CancellationToken>())
            .Returns([new FoundDeadLetter(Entity, 9, Now.AddMinutes(-29), [Shipment])]);

        var trail = await OpenAsync();

        trail.Hops.Select(hop => hop.DeadLetter?.Status).Should().Equal(DeadLetterStatus.Resubmitted, DeadLetterStatus.OnQueue);
    }

    [Fact]
    public async Task Handle_ForAMessageOfOtherKeys_LeavesTheHopWithoutIt()
    {
        Record(Hop("run", minutesAgo: 30, Outcome.DeadLettered, Shipment));
        _queues.DeadLetteredAsync(CorrelationId, Arg.Any<CancellationToken>())
            .Returns([new FoundDeadLetter(Entity, 7, Now.AddMinutes(-29), [new BusinessKey(BusinessKeyKind.ShipmentNumber, "SH-9999")])]);

        var trail = await OpenAsync();

        trail.Hops.Single().DeadLetter.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ForAMessageFromBeforeTheHop_LeavesTheHopWithoutIt()
    {
        Record(Hop("run", minutesAgo: 30, Outcome.DeadLettered, Shipment));
        _source.AuditAsync(Arg.Any<TrailLookup>(), Arg.Any<CancellationToken>())
            .Returns([Audit(AuditActions.Discard, "ben@norvia.example", minutesAgo: 90, reason: "Old")]);

        var trail = await OpenAsync();

        trail.Hops.Single().DeadLetter.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ForAHopWithNoKeys_TakesTheEarliestMessage()
    {
        Record(Hop("run", minutesAgo: 30, Outcome.DeadLettered));
        _queues.DeadLetteredAsync(CorrelationId, Arg.Any<CancellationToken>())
            .Returns([new FoundDeadLetter(Entity, 7, Now.AddMinutes(-29), [Shipment])]);

        var trail = await OpenAsync();

        trail.Hops.Single().DeadLetter!.SequenceNumber.Should().Be(7);
    }

    [Fact]
    public async Task Handle_ForTwoHopsOfIdenticalKeys_GivesEachItsOwnMessage()
    {
        // The hops were dead-lettered a minute apart, but their messages sit on the queue in the other order.
        Record(
            Hop("first", minutesAgo: 30, Outcome.DeadLettered, "m-2", Shipment),
            Hop("second", minutesAgo: 29, Outcome.DeadLettered, "m-1", Shipment));
        _queues.DeadLetteredAsync(CorrelationId, Arg.Any<CancellationToken>())
            .Returns([
                new FoundDeadLetter(Entity, 7, Now.AddMinutes(-28), [Shipment], "m-1"),
                new FoundDeadLetter(Entity, 8, Now.AddMinutes(-27), [Shipment], "m-2"),
            ]);

        var trail = await OpenAsync();

        trail.Hops.Select(hop => hop.DeadLetter?.SequenceNumber).Should().Equal(8, 7);
    }

    [Fact]
    public async Task Handle_ForTwoHopsOfIdenticalKeys_JoinsTheAuditEventByMessageId()
    {
        Record(
            Hop("first", minutesAgo: 60, Outcome.DeadLettered, "m-2", Shipment),
            Hop("second", minutesAgo: 59, Outcome.DeadLettered, "m-1", Shipment));
        _source.AuditAsync(Arg.Any<TrailLookup>(), Arg.Any<CancellationToken>())
            .Returns([Audit(AuditActions.Discard, "ben@norvia.example", minutesAgo: 20, messageId: "m-1")]);
        _queues.DeadLetteredAsync(CorrelationId, Arg.Any<CancellationToken>())
            .Returns([new FoundDeadLetter(Entity, 8, Now.AddMinutes(-58), [Shipment], "m-2")]);

        var trail = await OpenAsync();

        trail.Hops.Select(hop => hop.DeadLetter?.Status).Should().Equal(DeadLetterStatus.OnQueue, DeadLetterStatus.Discarded);
    }

    [Fact]
    public async Task Handle_ForAHopWhoseMessageIsGone_DoesNotTakeAnotherHopsMessage()
    {
        Record(Hop("run", minutesAgo: 30, Outcome.DeadLettered, "m-1", Shipment));
        _queues.DeadLetteredAsync(CorrelationId, Arg.Any<CancellationToken>())
            .Returns([new FoundDeadLetter(Entity, 7, Now.AddMinutes(-29), [Shipment], "m-9")]);

        var trail = await OpenAsync();

        trail.Hops.Single().DeadLetter.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ForAHopWithoutAMessageId_DoesNotTakeTheMessageAnotherHopNames()
    {
        Record(
            Hop("old", minutesAgo: 31, Outcome.DeadLettered, Shipment),
            Hop("named", minutesAgo: 30, Outcome.DeadLettered, "m-1", Shipment));
        _queues.DeadLetteredAsync(CorrelationId, Arg.Any<CancellationToken>())
            .Returns([new FoundDeadLetter(Entity, 7, Now.AddMinutes(-29), [Shipment], "m-1")]);

        var trail = await OpenAsync();

        trail.Hops.Select(hop => hop.DeadLetter?.SequenceNumber).Should().Equal(null, 7L);
    }

    [Fact]
    public async Task Handle_ForHopsWithoutMessageIds_StillPairsThemByKeysAndTime()
    {
        Record(
            Hop("first", minutesAgo: 30, Outcome.DeadLettered, Shipment),
            Hop("second", minutesAgo: 20, Outcome.DeadLettered, Shipment));
        _queues.DeadLetteredAsync(CorrelationId, Arg.Any<CancellationToken>())
            .Returns([
                new FoundDeadLetter(Entity, 7, Now.AddMinutes(-29), [Shipment], "m-1"),
                new FoundDeadLetter(Entity, 8, Now.AddMinutes(-19), [Shipment], "m-2"),
            ]);

        var trail = await OpenAsync();

        trail.Hops.Select(hop => hop.DeadLetter?.SequenceNumber).Should().Equal(7L, 8L);
    }

    private void Record(params RecordedHop[] hops) =>
        _source.HopsAsync(Arg.Any<TrailLookup>(), Arg.Any<CancellationToken>()).Returns(hops);

    private static RecordedHop Hop(string id, int minutesAgo, Outcome outcome, params BusinessKey[] keys) =>
        new(id, null, id, "carrier-booking", Now.AddMinutes(-minutesAgo), TimeSpan.FromSeconds(1), outcome, null, keys);

    private static RecordedHop Hop(string id, int minutesAgo, Outcome outcome, string messageId, params BusinessKey[] keys) =>
        Hop(id, minutesAgo, outcome, keys) with { MessageId = messageId };

    private static RecordedAudit Audit(string action, string actor, int minutesAgo, string? reason = null, string messageId = "m-1") =>
        new(actor, action, Entity, messageId, Now.AddMinutes(-minutesAgo), reason, [Shipment]);

    private async Task<MessageTrail> OpenAsync()
    {
        var handler = new OpenTrailHandler(_source, _queues, _time, Options.Create(new IntegrationRetention { IntegrationRetentionDays = 90 }));
        return (await handler.Handle(new OpenTrailQuery(CorrelationId), TestContext.Current.CancellationToken)).Value;
    }
}
