using Ardalis.Result;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Simulator.Application.Log.List;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Runs;
using Norvia.Simulator.Web.Operators;
using Norvia.Simulator.Web.Runs;

namespace Norvia.Simulator.Web.UnitTests.Runs;

public sealed class RunPageTests : SimulatorWebContext
{
    private static readonly Guid RunId = TestData.Summary().Id;

    private static readonly LogLine[] Lines =
    [
        new(1, TestData.Now, "Simulator", "Run 4 started: 10 bookings, seed 77", LogOutcome.Info, null, null, null, null),
        new(2, TestData.Now.AddSeconds(1), "Brouwer Dranken", "Send booking", LogOutcome.Accepted, "corr-1", "SIM-4-1", "TO-0000001", "Good booking"),
        new(3, TestData.Now.AddSeconds(2), "Tuincentrum Groenrijk", "Send booking", LogOutcome.Rejected, "corr 2/x", "SIM-4-2", null, "Bad booking (InvalidContent). Norvia: ConsigneeNameMissing"),
    ];

    public RunPageTests()
    {
        GivenRun(TestData.Summary());
    }

    private void GivenRun(Norvia.Simulator.Application.Runs.RunSummary summary)
    {
        Requests.GetRunAsync(Arg.Any<SimulationRunId>(), Arg.Any<CancellationToken>()).Returns(TestData.Detail(summary));
        Requests.ListLogAsync(Arg.Any<SimulationRunId>(), Arg.Any<LogFilter>(), Arg.Any<CancellationToken>()).Returns(Lines);
    }

    private IRenderedComponent<RunPage> RenderRun() => Render<RunPage>(parameters => parameters.Add(page => page.Id, RunId));

    private static AngleSharp.Dom.IElement Field(IRenderedComponent<RunPage> page, string label)
    {
        var id = page.FindAll("label").Single(candidate => candidate.TextContent.Trim() == label).GetAttribute("for");
        return page.Find($"#{id}");
    }

    [Fact]
    public void Authorize_SeeingARun_RequiresTheReadRole()
    {
        typeof(RunPage).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>().Should().ContainSingle(attribute => attribute.Policy == OperatorSignIn.ReadPolicy);
    }

    [Fact]
    public void Render_ARun_ShowsItsStatusProgressAndAnswers()
    {
        var page = RenderRun();

        page.Find("h1").TextContent.Should().Be("Run 4");
        page.Find("[data-status]").TextContent.Should().Be("Running");
        page.Markup.Should().Contain("3 of 10 bookings sent").And.Contain("seed 77").And.Contain("by Dev Eloper");
        page.FindAll("[data-outcome]").Select(span => span.TextContent.Trim()).Should().Contain(["Accepted: 2", "Rejected: 1"]);
    }

    [Fact]
    public void Render_TheLog_ShowsTimeActorActionOutcomeAndKeysOfEachLine()
    {
        var page = RenderRun();

        var log = page.Find("[role=log]");
        log.TextContent.Should().Contain("12:00:01").And.Contain("Brouwer Dranken").And.Contain("Send booking").And.Contain("SIM-4-1").And.Contain("TO-0000001")
            .And.Contain("corr-1").And.Contain("Norvia: ConsigneeNameMissing");
        log.QuerySelectorAll("[data-outcome]").Select(span => span.TextContent.Trim()).Should().Equal("Info", "Accepted", "Rejected");
    }

    [Fact]
    public void Render_ALineWithACorrelationId_LinksToItsMessageTrailInTheIntegrationConsole()
    {
        var page = RenderRun();

        var links = page.FindAll("[role=log] a").Select(link => link.GetAttribute("href")).ToList();
        links.Should().Contain("http://console.example:5000/?key=corr-1&within=all");
        links.Should().Contain("http://console.example:5000/?key=corr%202%2Fx&within=all");
    }

    [Fact]
    public void Copy_ACorrelationId_PutsItOnTheClipboard()
    {
        var page = RenderRun();

        page.Find("fluent-button[aria-label='Copy correlation ID corr-1']").Click();

        JSInterop.Invocations["navigator.clipboard.writeText"].Should().ContainSingle().Which.Arguments.Should().Equal("corr-1");
    }

    [Fact]
    public void Render_AnUnknownRun_SaysSo()
    {
        Requests.GetRunAsync(Arg.Any<SimulationRunId>(), Arg.Any<CancellationToken>()).Returns(Result<Norvia.Simulator.Application.Runs.Open.RunDetail>.NotFound());

        var page = RenderRun();

        page.Find("h1").TextContent.Should().Be("No such run");
    }

    [Fact]
    public void Render_ALogWithNoMatchingLines_SaysSo()
    {
        Requests.ListLogAsync(Arg.Any<SimulationRunId>(), Arg.Any<LogFilter>(), Arg.Any<CancellationToken>()).Returns([]);

        RenderRun().Markup.Should().Contain("No log lines match.");
    }

    [Fact]
    public void Poll_WhileTheRunSends_RefreshesTheLogUntilItHasFinished()
    {
        var page = RenderRun();
        Requests.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(ISimulatorRequests.GetRunAsync)).Should().Be(1);

        GivenRun(TestData.Summary(SimulationRunStatus.Running, sent: 7));
        page.InvokeAsync(() => Clock.Advance(TimeSpan.FromSeconds(1.5)));
        page.WaitForAssertion(() => page.Markup.Should().Contain("7 of 10 bookings sent"));

        GivenRun(TestData.Summary(SimulationRunStatus.Completed, sent: 10));
        page.InvokeAsync(() => Clock.Advance(TimeSpan.FromSeconds(1.5)));
        page.WaitForAssertion(() => page.Find("[data-status]").TextContent.Should().Be("Completed"));
        var calls = Requests.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(ISimulatorRequests.GetRunAsync));

        page.InvokeAsync(() => Clock.Advance(TimeSpan.FromSeconds(10)));

        Requests.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(ISimulatorRequests.GetRunAsync)).Should().Be(calls, "a finished run isn't polled");
    }

    [Fact]
    public void Render_AFinishedRun_IsNotPolledAtAll()
    {
        GivenRun(TestData.Summary(SimulationRunStatus.Cancelled));
        RenderRun();

        Clock.Advance(TimeSpan.FromMinutes(1));

        Requests.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(ISimulatorRequests.GetRunAsync)).Should().Be(1);
    }

    [Fact]
    public void Cancel_AnActiveRun_AsksTheApplicationToStopIt()
    {
        Requests.CancelAsync(Arg.Any<SimulationRunId>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        var page = RenderRun();

        page.Find("fluent-button:contains('Cancel run')").Click();

        Requests.Received(1).CancelAsync(new SimulationRunId(RunId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Cancel_RunAlreadyFinished_ShowsWhy()
    {
        Requests.CancelAsync(Arg.Any<SimulationRunId>(), Arg.Any<CancellationToken>()).Returns(Result.Conflict("The run has already finished."));
        var page = RenderRun();

        page.Find("fluent-button:contains('Cancel run')").Click();

        page.Find("[role=alert]").TextContent.Should().Contain("already finished");
    }

    [Fact]
    public void Render_AFinishedRun_OffersNoCancel()
    {
        GivenRun(TestData.Summary(SimulationRunStatus.Completed));

        RenderRun().FindAll("fluent-button").Should().NotContain(button => button.TextContent.Contains("Cancel run", StringComparison.Ordinal));
    }

    [Fact]
    public void Render_ForAnOperatorWhoMayOnlyRead_OffersNoCancel()
    {
        Operator.SetRoles(Norvia.Simulator.Application.Operators.OperatorRoles.Read);
        Operator.SetPolicies(Norvia.Simulator.Application.Operators.OperatorRoles.Read);

        RenderRun().FindAll("fluent-button").Should().NotContain(button => button.TextContent.Contains("Cancel run", StringComparison.Ordinal));
    }

    [Fact]
    public void Filter_ByText_AsksForTheLinesThatMentionIt()
    {
        var page = RenderRun();

        Field(page, "Correlation ID, reference or order number").Change("SIM-4-2");
        page.Find("form[aria-label='Filter the log']").Submit();

        Requests.Received().ListLogAsync(new SimulationRunId(RunId), new LogFilter(Actor: null, Outcome: null, Text: "SIM-4-2"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Filter_ByActorAndOutcome_AsksForThatActorsLinesWithThatOutcome()
    {
        var page = RenderRun();

        page.FindAll("fluent-select").Single(select => select.PreviousElementSibling?.TextContent.Contains("Actor", StringComparison.Ordinal) == true).Change("Brouwer Dranken");
        page.FindAll("fluent-select").Single(select => select.PreviousElementSibling?.TextContent.Contains("Outcome", StringComparison.Ordinal) == true).Change("Rejected");

        Requests.Received().ListLogAsync(new SimulationRunId(RunId), new LogFilter("Brouwer Dranken", LogOutcome.Rejected, ""), Arg.Any<CancellationToken>());
    }
}
