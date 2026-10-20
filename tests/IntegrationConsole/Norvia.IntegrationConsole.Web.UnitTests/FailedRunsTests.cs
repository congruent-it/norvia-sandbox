using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Norvia.IntegrationConsole.Application.Runs;
using Norvia.IntegrationConsole.Application.Runs.Failed;
using Norvia.IntegrationConsole.Application.Trails;
using Norvia.IntegrationConsole.Web.Operators;
using Norvia.IntegrationConsole.Web.Runs;

namespace Norvia.IntegrationConsole.Web.UnitTests;

public sealed class FailedRunsTests : ConsoleWebContext
{
    // 12:00 in Rotterdam (CEST).
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);

    private readonly IRunRequests _runs = Substitute.For<IRunRequests>();
    private readonly FakeTimeProvider _time = new(Now);

    public FailedRunsTests()
    {
        Services.AddSingleton(_runs);
        Services.AddSingleton<TimeProvider>(_time);
        Answer(Intake("run-2", 5), CarrierAnswer("run-1", 30));
    }

    [Fact]
    public void Authorize_ForAnyOperator_RequiresTheReadRole()
    {
        typeof(FailedRuns).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>().Should().ContainSingle(attribute => attribute.Policy == OperatorSignIn.ReadPolicy);
    }

    [Fact]
    public void Render_ForFailedRuns_ShowsEachWithFlowTimeFailingStepErrorAndCorrelationIdLinkingToTheTrail()
    {
        var page = Render<FailedRuns>();

        _runs.Received(1).ListFailedAsync(new ListFailedRunsQuery(null), Arg.Any<CancellationToken>());
        var rows = page.FindAll("tbody tr");
        rows.Should().HaveCount(2);
        rows[0].TextContent.Should().Contain("transport-order-intake").And.Contain("Sat 26 Sep 2026, 11:55:00")
            .And.Contain("Submit_to_the_TMS").And.Contain("ServiceUnavailable: The TMS answered 503.").And.Contain("corr-run-2");
        rows[0].QuerySelector("a[href='/trails/corr-run-2']")!.TextContent.Should().Be("corr-run-2");
        rows[0].QuerySelector("a[href='/runs/logic-intake/transport-order-intake/run-2']")!.TextContent.Should().Be("Submit_to_the_TMS");
        rows[1].TextContent.Should().Contain("carrier-answer").And.Contain("11:30:00");
    }

    [Fact]
    public void Render_ForARunWithoutACorrelationIdOrFailingStep_OffersTheRunAndNoTrail()
    {
        Answer(Intake("run-1", 5) with { CorrelationId = null, FailedStep = null, Error = null });

        var page = Render<FailedRuns>();

        var row = page.Find("tbody tr");
        row.QuerySelectorAll("a[href^='/trails/']").Should().BeEmpty();
        row.QuerySelector("a[href='/runs/logic-intake/transport-order-intake/run-1']")!.TextContent.Should().Be("Open the run");
    }

    [Fact]
    public void Render_ForAFailureWithoutARun_ShowsItsErrorAndTrail_ButNoLinkToARun()
    {
        // Locally there are no Logic Apps to open a run in.
        Answer(Intake("run-1", 5) with { Run = null, FailedStep = null });

        var page = Render<FailedRuns>();

        var row = page.Find("tbody tr");
        row.QuerySelectorAll("a[href^='/runs/']").Should().BeEmpty();
        row.TextContent.Should().Contain("ServiceUnavailable: The TMS answered 503.");
        row.QuerySelector("a[href='/trails/corr-run-1']").Should().NotBeNull();
    }

    [Fact]
    public void Render_Always_OffersNoWayToResubmitARun_ForAnyFlow()
    {
        Answer(Intake("run-1", 5), Intake("run-2", 6) with { Flow = "carrier-answer" });

        var page = Render<FailedRuns>();

        page.FindAll("button, a, fluent-button").Should().NotContain(control => control.TextContent.Contains("Resubmit", StringComparison.OrdinalIgnoreCase));
        page.Find("[role=note]").TextContent.Should().Contain("never resubmits");
    }

    [Fact]
    public void Render_WithAFlowInTheAddress_NarrowsToIt_AndOffersEveryFlow()
    {
        var flows = new List<string> { "carrier-answer", "transport-order-intake" };
        _runs.ListFailedAsync(new ListFailedRunsQuery("carrier-answer"), Arg.Any<CancellationToken>())
            .Returns(new FailedRunList(Now.AddHours(-24), Now, flows, [CarrierAnswer("run-1", 30)]));
        Services.GetRequiredService<NavigationManager>().NavigateTo("/failed-runs?flow=carrier-answer");

        var page = Render<FailedRuns>();

        page.FindAll("tbody tr").Should().ContainSingle().Which.TextContent.Should().Contain("carrier-answer");
        page.FindAll("fluent-option").Select(option => option.TextContent.Trim()).Should().Equal("All flows", "carrier-answer", "transport-order-intake");
    }

    [Fact]
    public void Filter_ByAFlow_NavigatesToItsAddress()
    {
        var page = Render<FailedRuns>();

        page.Find("fluent-select").Change("carrier-answer");

        Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("/failed-runs?flow=carrier-answer");
    }

    [Fact]
    public void Render_WithoutFailedRuns_SaysSo()
    {
        Answer();

        var page = Render<FailedRuns>();

        page.Find("[role=status]").TextContent.Should().Contain("No failed runs between").And.Contain("Fri 25 Sep 2026, 12:00:00");
        page.FindAll("table").Should().BeEmpty();
    }

    [Fact]
    public void Render_WhileLoading_SaysSo()
    {
        _runs.ListFailedAsync(Arg.Any<ListFailedRunsQuery>(), Arg.Any<CancellationToken>()).Returns(new TaskCompletionSource<FailedRunList>().Task);

        var page = Render<FailedRuns>();

        page.Find("[role=status]").TextContent.Should().Contain("Loading the failed runs");
    }

    [Fact]
    public void Render_WhenTheRunsCantBeListed_SaysSo_WithoutTheReason()
    {
        _runs.ListFailedAsync(Arg.Any<ListFailedRunsQuery>(), Arg.Any<CancellationToken>())
            .Returns<FailedRunList>(_ => throw new HttpRequestException("403 AuthorizationFailed"));

        var page = Render<FailedRuns>();

        page.Find("[role=alert]").TextContent.Should().Contain("The failed runs couldn't be listed.");
        page.Markup.Should().NotContain("AuthorizationFailed");
    }

    [Fact]
    public void Render_WhileOpen_RefreshesItself_AndShowsAFailureThatAppearsMeanwhile()
    {
        var page = Render<FailedRuns>();
        page.FindAll("tbody tr").Should().HaveCount(2);
        Answer(Intake("run-3", 0), Intake("run-2", 5), CarrierAnswer("run-1", 30));

        _time.Advance(TimeSpan.FromSeconds(5));

        page.WaitForAssertion(() => page.FindAll("tbody tr").Should().HaveCount(3));
        _runs.Received(2).ListFailedAsync(new ListFailedRunsQuery(null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_WhenARefreshFails_SaysSo_AndRecoversOnTheNext()
    {
        var page = Render<FailedRuns>();
        _runs.ListFailedAsync(Arg.Any<ListFailedRunsQuery>(), Arg.Any<CancellationToken>())
            .Returns<FailedRunList>(_ => throw new HttpRequestException("503"));

        _time.Advance(TimeSpan.FromSeconds(5));
        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("couldn't be listed"));
        Answer(Intake("run-2", 5));
        _time.Advance(TimeSpan.FromSeconds(5));

        page.WaitForAssertion(() => page.FindAll("tbody tr").Should().ContainSingle());
    }

    [Fact]
    public async Task Dispose_WhenThePageIsClosed_StopsRefreshing()
    {
        var page = Render<FailedRuns>();

        await DisposeComponentsAsync();
        _time.Advance(TimeSpan.FromSeconds(30));

        page.Should().NotBeNull();
        _ = _runs.Received(1).ListFailedAsync(Arg.Any<ListFailedRunsQuery>(), Arg.Any<CancellationToken>());
    }

    private void Answer(params FailedRun[] runs) =>
        _runs.ListFailedAsync(Arg.Any<ListFailedRunsQuery>(), Arg.Any<CancellationToken>())
            .Returns(new FailedRunList(Now.AddHours(-24), Now, [.. runs.Select(run => run.Flow).Distinct().Order()], runs));

    private static FailedRun Intake(string runId, int minutesAgo) =>
        new("transport-order-intake", new WorkflowRun("logic-intake", "transport-order-intake", runId), Now.AddMinutes(-minutesAgo),
            $"corr-{runId}", "Submit_to_the_TMS", new StepError("ServiceUnavailable", "The TMS answered 503."));

    private static FailedRun CarrierAnswer(string runId, int minutesAgo) =>
        new("carrier-answer", new WorkflowRun("logic-answer", "carrier-answer", runId), Now.AddMinutes(-minutesAgo),
            $"corr-{runId}", "Send_to_the_Carrier", new StepError("BadGateway", "The Carrier answered 502."));
}
