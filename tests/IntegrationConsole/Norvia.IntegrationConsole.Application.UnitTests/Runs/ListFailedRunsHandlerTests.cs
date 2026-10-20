using Microsoft.Extensions.Time.Testing;
using Norvia.IntegrationConsole.Application.Runs;
using Norvia.IntegrationConsole.Application.Runs.Failed;
using Norvia.IntegrationConsole.Application.Trails;

namespace Norvia.IntegrationConsole.Application.UnitTests.Runs;

public sealed class ListFailedRunsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);

    private readonly IFailedRuns _runs = Substitute.For<IFailedRuns>();
    private readonly FakeTimeProvider _time = new(Now);

    [Fact]
    public async Task Handle_Always_AsksForTheLast24Hours()
    {
        Record();

        var failed = await ListAsync();

        await _runs.Received(1).FailedRunsAsync(Now.AddHours(-24), Arg.Any<CancellationToken>());
        failed.From.Should().Be(Now.AddHours(-24));
        failed.To.Should().Be(Now);
    }

    [Fact]
    public async Task Handle_ForRunsOfSeveralLogicApps_ListsThemNewestFirst()
    {
        Record(
            Failed("logic-intake", "transport-order-intake", Now.AddHours(-3)),
            Failed("logic-answer", "carrier-answer", Now.AddMinutes(-1)),
            Failed("logic-intake", "transport-order-intake", Now.AddHours(-1)));

        var failed = await ListAsync();

        failed.Runs.Select(run => run.Run!.LogicApp).Should().Equal("logic-answer", "logic-intake", "logic-intake");
        failed.Runs.Select(run => run.Start).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task Handle_ForARunOlderThan24Hours_LeavesItOut()
    {
        Record(Failed("logic-intake", "transport-order-intake", Now.AddHours(-24).AddSeconds(-1)));

        var failed = await ListAsync();

        failed.Runs.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ForAFlow_NarrowsTheRuns_ButStillOffersEveryFlow()
    {
        Record(
            Failed("logic-intake", "transport-order-intake", Now.AddHours(-3)),
            Failed("logic-answer", "carrier-answer", Now.AddHours(-2)) with { Flow = "carrier-answer" });

        var failed = await ListAsync(" carrier-answer ");

        failed.Runs.Should().ContainSingle().Which.Flow.Should().Be("carrier-answer");
        failed.Flows.Should().Equal("carrier-answer", "transport-order-intake");
    }

    [Fact]
    public async Task Handle_ForARunThatTrackedNoFlow_NamesItAfterItsWorkflow()
    {
        Record(Failed("logic-intake", "transport-order-intake", Now.AddHours(-3)) with { Flow = null });

        var failed = await ListAsync();

        failed.Runs.Should().ContainSingle().Which.Flow.Should().Be("transport-order-intake");
    }

    [Fact]
    public async Task Handle_ForARunWithNeitherFlowNorWorkflow_NamesTheFlowUnknown()
    {
        // Locally there are no Logic Apps, so a failure without a flow has no workflow to name it after.
        Record(new RecordedFailedRun(null, Now.AddHours(-1), null, "corr-1", null, null));

        var failed = await ListAsync();

        failed.Runs.Should().ContainSingle().Which.Should().Be(new FailedRun("unknown", null, Now.AddHours(-1), "corr-1", null, null));
    }

    [Fact]
    public async Task Handle_ForARun_KeepsItsFailingStepErrorAndCorrelationId()
    {
        Record(Failed("logic-intake", "transport-order-intake", Now.AddHours(-1)));

        var failed = await ListAsync();

        failed.Runs.Should().ContainSingle().Which.Should().Be(new FailedRun(
            "transport-order-intake",
            new WorkflowRun("logic-intake", "transport-order-intake", "run-1"),
            Now.AddHours(-1),
            "corr-1",
            "Submit_to_the_TMS",
            new StepError("ServiceUnavailable", "The TMS answered 503.")));
    }

    [Fact]
    public async Task Handle_WithoutFailedRuns_ListsNone()
    {
        Record();

        var failed = await ListAsync();

        failed.Runs.Should().BeEmpty();
        failed.Flows.Should().BeEmpty();
    }

    private static RecordedFailedRun Failed(string logicApp, string workflow, DateTimeOffset start) =>
        new(new WorkflowRun(logicApp, workflow, "run-1"), start, workflow, "corr-1", "Submit_to_the_TMS",
            new StepError("ServiceUnavailable", "The TMS answered 503."));

    private void Record(params RecordedFailedRun[] runs) =>
        _runs.FailedRunsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(runs);

    private async Task<FailedRunList> ListAsync(string? flow = null) =>
        await new ListFailedRunsHandler(_runs, _time).Handle(new ListFailedRunsQuery(flow), TestContext.Current.CancellationToken);
}
