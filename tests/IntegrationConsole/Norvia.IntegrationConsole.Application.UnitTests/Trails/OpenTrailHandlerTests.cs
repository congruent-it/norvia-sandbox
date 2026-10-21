using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Norvia.IntegrationConsole.Application.DeadLetters;
using Norvia.IntegrationConsole.Application.Trails;
using Norvia.IntegrationConsole.Application.Trails.Open;

namespace Norvia.IntegrationConsole.Application.UnitTests.Trails;

public sealed class OpenTrailHandlerTests
{
    private const string CorrelationId = "5b0d7f8e-2c41-4a3e-9d6a-0f1e2d3c4b5a";

    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    private readonly ITrailSource _source = Substitute.For<ITrailSource>();
    private readonly IDeadLetterQueues _queues = Substitute.For<IDeadLetterQueues>();
    private readonly FakeTimeProvider _time = new(Now);

    [Fact]
    public async Task Handle_ForHopsRecordedOutOfOrder_ShowsThemInTimeOrder()
    {
        Record(
            Hop("tms-call", secondsAgo: 10),
            Hop("apim-call", secondsAgo: 30),
            Hop("intake-run", secondsAgo: 20));

        var trail = await OpenAsync();

        trail.Hops.Select(hop => hop.Name).Should().Equal("apim-call", "intake-run", "tms-call");
    }

    [Fact]
    public async Task Handle_ForAnEventThatFansOut_GroupsEachBranchUnderIt_OneLevelDeeper()
    {
        // The TMS publishes TransportOrderAccepted, which starts a run in two subscriptions. The WMS run starts before
        // the ERP run's call, and still comes after the whole ERP branch.
        Record(
            Hop("wms-call", secondsAgo: 35, causedBy: "wms-run"),
            Hop("erp-call", secondsAgo: 30, causedBy: "erp-run"),
            Hop("apim-call", secondsAgo: 70),
            Hop("wms-run", secondsAgo: 40, causedBy: "published"),
            Hop("published", secondsAgo: 60, causedBy: "tms-call"),
            Hop("erp-run", secondsAgo: 50, causedBy: "published"),
            Hop("tms-call", secondsAgo: 65, causedBy: "apim-call"));

        var trail = await OpenAsync();

        trail.Hops.Select(hop => (hop.Name, hop.Depth)).Should().Equal(
            ("apim-call", 0),
            ("tms-call", 0),
            ("published", 0),
            ("erp-run", 1),
            ("erp-call", 1),
            ("wms-run", 1),
            ("wms-call", 1));
    }

    [Fact]
    public async Task Handle_ForAHopCausedByOneNotOnTheTrail_ShowsItInTimeOrder_AtTheTop()
    {
        // Its cause may be outside the time range, or recorded without the correlation ID.
        Record(
            Hop("later", secondsAgo: 10),
            Hop("orphan", secondsAgo: 20, causedBy: "not-on-the-trail"),
            Hop("first", secondsAgo: 30));

        var trail = await OpenAsync();

        trail.Hops.Select(hop => (hop.Name, hop.Depth)).Should().Equal(("first", 0), ("orphan", 0), ("later", 0));
    }

    [Fact]
    public async Task Handle_ForAChainOfCalls_KeepsItTogether_BeforeALaterUnrelatedHop()
    {
        // Grouped under the hop that caused it: a hop that started earlier, but not from the chain, waits for the chain.
        Record(
            Hop("tms-call", secondsAgo: 10, causedBy: "apim-call"),
            Hop("apim-call", secondsAgo: 30),
            Hop("unrelated", secondsAgo: 20));

        var trail = await OpenAsync();

        trail.Hops.Select(hop => (hop.Name, hop.Depth)).Should().Equal(("apim-call", 0), ("tms-call", 0), ("unrelated", 0));
    }

    [Fact]
    public async Task Handle_ForHopsThatClaimToCauseEachOther_ShowsEachOnce()
    {
        // Telemetry is not to be trusted with a cycle, but the trail must still show everything recorded.
        Record(
            Hop("a", secondsAgo: 30, causedBy: "b"),
            Hop("b", secondsAgo: 20, causedBy: "a"),
            Hop("self", secondsAgo: 10, causedBy: "self"));

        var trail = await OpenAsync();

        trail.Hops.Select(hop => hop.Name).Should().BeEquivalentTo("a", "b", "self");
    }

    [Fact]
    public async Task Handle_ForACorrelationId_LooksAsFarBackAsTheEstateKeepsAnything()
    {
        // A trail can span more than any search range: a retry or a resubmit days later continues it.
        var trail = await OpenAsync($"  {CorrelationId} ", retentionDays: 30);

        await _source.Received(1).HopsAsync(new TrailLookup(CorrelationId, Now.AddDays(-30), Now), Arg.Any<CancellationToken>());
        trail.CorrelationId.Should().Be(CorrelationId);
        trail.From.Should().Be(Now.AddDays(-30));
        trail.To.Should().Be(Now);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Handle_WithoutACorrelationId_IsInvalid_AndLooksUpNothing(string correlationId)
    {
        var handler = new OpenTrailHandler(_source, _queues, _time, Options.Create(new IntegrationRetention { IntegrationRetentionDays = 90 }));

        var result = await handler.Handle(new OpenTrailQuery(correlationId), TestContext.Current.CancellationToken);

        result.Status.Should().Be(Ardalis.Result.ResultStatus.Invalid);
        await _source.DidNotReceiveWithAnyArgs().HopsAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Handle_ForAHop_ShowsItsFlowTimeDurationOutcomeFailureCodeAndRun()
    {
        var run = new WorkflowRun("logic-intake-abc123", "transport-order-intake", "08584421797283920101");
        Record(new RecordedHop(
            "intake-run", null, "transport-order-intake", "transport-order-intake", Now.AddMinutes(-2), TimeSpan.FromSeconds(1.5),
            Outcome.Failed, "TmsUnavailable", [], run));

        var trail = await OpenAsync();

        var hop = trail.Hops.Should().ContainSingle().Subject;
        hop.Should().BeEquivalentTo(new TrailHop(
            "transport-order-intake", "transport-order-intake", Now.AddMinutes(-2), TimeSpan.FromSeconds(1.5), Outcome.Failed, "TmsUnavailable", 0, run));
        hop.HasFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ForHopsCarryingBusinessKeys_ShowsEveryKeyTheTrailCarried_Once()
    {
        Record(
            Hop("apim-call", secondsAgo: 30) with { BusinessKeys = [new(BusinessKeyKind.SourceMessageId, "evt-5c1d")] },
            Hop("tms-call", secondsAgo: 20) with
            {
                BusinessKeys = [new(BusinessKeyKind.TransportOrderNumber, "TO-0000042"), new(BusinessKeyKind.SourceMessageId, "evt-5c1d")],
            },
            Hop("published", secondsAgo: 10) with
            {
                BusinessKeys = [new(BusinessKeyKind.ShipmentNumber, "SH-0000042"), new(BusinessKeyKind.TransportOrderNumber, "TO-0000042")],
            });

        var trail = await OpenAsync();

        // In the tracking contract's order, so the Transport Order leads.
        trail.BusinessKeys.Should().Equal(
            new BusinessKey(BusinessKeyKind.TransportOrderNumber, "TO-0000042"),
            new BusinessKey(BusinessKeyKind.ShipmentNumber, "SH-0000042"),
            new BusinessKey(BusinessKeyKind.SourceMessageId, "evt-5c1d"));
    }

    [Fact]
    public async Task Handle_WhenNothingIsRecordedYet_ReturnsAnEmptyTrail()
    {
        // Telemetry may be minutes behind, so this is no error.
        var trail = await OpenAsync();

        trail.Hops.Should().BeEmpty();
        trail.BusinessKeys.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ForATrailWithoutDeadLetteredHops_AsksNeitherTheAuditNorTheQueues()
    {
        Record(Hop("apim-call", secondsAgo: 30), Hop("tms-call", secondsAgo: 10, causedBy: "apim-call"));

        var trail = await OpenAsync();

        trail.Hops.Should().OnlyContain(hop => hop.DeadLetter == null);
        await _source.DidNotReceiveWithAnyArgs().AuditAsync(default!, TestContext.Current.CancellationToken);
        await _queues.DidNotReceiveWithAnyArgs().DeadLetteredAsync(default!, TestContext.Current.CancellationToken);
    }

    private void Record(params RecordedHop[] hops) =>
        _source.HopsAsync(Arg.Any<TrailLookup>(), Arg.Any<CancellationToken>()).Returns(hops);

    private static RecordedHop Hop(string id, int secondsAgo, string? causedBy = null) =>
        new(id, causedBy, id, "transport-order-intake", Now.AddSeconds(-secondsAgo), TimeSpan.FromMilliseconds(250), Outcome.Succeeded, null, []);

    private async Task<MessageTrail> OpenAsync(string correlationId = CorrelationId, int retentionDays = 90)
    {
        var handler = new OpenTrailHandler(_source, _queues, _time, Options.Create(new IntegrationRetention { IntegrationRetentionDays = retentionDays }));
        var result = await handler.Handle(new OpenTrailQuery(correlationId), TestContext.Current.CancellationToken);
        return result.Value;
    }
}
