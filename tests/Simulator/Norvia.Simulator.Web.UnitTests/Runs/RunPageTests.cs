using Ardalis.Result;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Simulator.Application.Log.List;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Runs;
using Norvia.Simulator.Web.Operators;
using Norvia.Simulator.Web.Plans;
using Norvia.Simulator.Web.Runs;

namespace Norvia.Simulator.Web.UnitTests.Runs;

public sealed class RunPageTests : SimulatorWebContext
{
    private static readonly Guid RunId = TestData.Summary().Id;

    private static readonly LogLine[] Lines =
    [
        new(1, TestData.Now, "Simulator", "Run 4 started: 10 bookings, seed 77", LogOutcome.Info, null, null, null, null, null),
        new(2, TestData.Now.AddSeconds(1), "Brouwer Dranken", "Send booking", LogOutcome.Accepted, "corr-1", "SIM-4-1", "TO-0000001", null, "Good booking"),
        new(3, TestData.Now.AddSeconds(2), "Tuincentrum Groenrijk", "Send booking", LogOutcome.Rejected, "corr 2/x", "SIM-4-2", null, null, "Bad booking (InvalidContent). Norvia: ConsigneeNameMissing"),
        new(4, TestData.Now.AddSeconds(3), "Planner", "Assign Transport Order", LogOutcome.Planned, "plan-1", "SIM-4-1", "TO-0000001", "SH-0000001", "Own fleet has room that day"),
        new(5, TestData.Now.AddSeconds(4), "Planner", "Assign Transport Order", LogOutcome.PlanningRefused, "plan-2", "SIM-4-3", "TO-0000003", null, "TransportOrderAlreadyPlanned (409)"),
    ];

    public RunPageTests()
    {
        GivenRun(TestData.Summary());
    }

    private void GivenRun(Norvia.Simulator.Application.Runs.RunSummary summary, SimulationSettings? settings = null)
    {
        Requests.GetRunAsync(Arg.Any<SimulationRunId>(), Arg.Any<CancellationToken>()).Returns(TestData.Detail(summary, settings));
        Requests.ListLogAsync(Arg.Any<SimulationRunId>(), Arg.Any<LogFilter>(), Arg.Any<CancellationToken>()).Returns(Lines);
    }

    private IRenderedComponent<RunPage> RenderRun() => Render<RunPage>(parameters => parameters.Add(page => page.Id, RunId));

    private static AngleSharp.Dom.IElement Field(IRenderedComponent<RunPage> page, string label)
    {
        var id = page.FindAll("label").Single(candidate => candidate.TextContent.Trim() == label).GetAttribute("for");
        return page.Find($"#{id}");
    }

    [Fact]
    public void Run_OfAPacedRun_SaysHowLongItIsPacedOver()
    {
        GivenRun(TestData.Summary(SimulationRunStatus.Completed) with { Mode = RunMode.Paced, Window = TimeSpan.FromMinutes(10) });

        RenderRun().Markup.Should().Contain("paced over 10 min");
    }

    [Fact]
    public void Render_ARunWithAPlanner_ShowsHowManyOrdersItPlanned()
    {
        GivenRun(TestData.Summary() with { Assigned = 2, ToAssign = 6 });

        RenderRun().Markup.Should().Contain("2 of 6 Transport Orders planned");
    }

    [Fact]
    public void Render_ThePlannersLines_CarryTheShipmentNumberAndTheirTrailOpensInTheConsole()
    {
        var page = RenderRun();

        var planned = page.FindAll("tbody tr").Single(row => row.TextContent.Contains("Own fleet has room that day", StringComparison.Ordinal));
        planned.TextContent.Should().Contain("Planner").And.Contain("Assign Transport Order").And.Contain("Planned").And.Contain("SH-0000001").And.Contain("TO-0000001");
        planned.QuerySelector("a")!.GetAttribute("href").Should().Contain("plan-1");
        page.Markup.Should().Contain("TransportOrderAlreadyPlanned (409)").And.Contain("Planning refused");
    }

    [Fact]
    public void Render_ATripAndADriversReports_CarryTheTripNumberAndTheirTrailsOpenInTheConsole()
    {
        Requests.ListLogAsync(Arg.Any<SimulationRunId>(), Arg.Any<LogFilter>(), Arg.Any<CancellationToken>()).Returns(
        [
            new LogLine(6, TestData.Now, "Planner", "Dispatch Trip", LogOutcome.TripDispatched, "disp-1", null, null, null, null, "TR-0000007"),
            new LogLine(7, TestData.Now.AddSeconds(1), "Anouk de Vries", "Collected", LogOutcome.DriverReported, "drv-1", "SIM-4-1", "TO-0000001", "SH-0000001", null, "TR-0000007"),
            new LogLine(8, TestData.Now.AddSeconds(2), "Anouk de Vries", "Deliver", LogOutcome.DriverRefused, "drv-2", "SIM-4-1", "TO-0000001", "SH-0000001", "StopNotAvailable (409)", "TR-0000007"),
        ]);

        var page = RenderRun();

        page.FindAll("th").Select(header => header.TextContent.Trim()).Should().Contain("Trip");
        var rows = page.FindAll("tbody tr");
        rows.Should().OnlyContain(row => row.TextContent.Contains("TR-0000007", StringComparison.Ordinal));
        rows.Select(row => row.QuerySelector("[data-outcome]")!.TextContent.Trim()).Should().Equal("Trip dispatched", "Driver reported", "Driver app refused");
        rows[1].QuerySelector("a")!.GetAttribute("href").Should().Contain("drv-1");
        rows[2].QuerySelector("[data-outcome]")!.ClassList.Should().Contain("text-danger");
    }

    [Fact]
    public void Render_ARun_ListsEverySettingItWasStartedWith_GroupedAsOnConfigure()
    {
        GivenRun(TestData.Summary(), TestData.Settings());

        var settings = RenderRun().Find("[aria-label='Settings']");

        var groups = settings.QuerySelectorAll("dl[aria-label]").ToDictionary(group => group.GetAttribute("aria-label")!, group => group.TextContent);
        groups.Keys.Should().Equal("Shippers", "Planner", "Carriers", "Own fleet and Drivers", "Finance and invoicing", "How it is sent");
        groups["Shippers"].Should().Contain("Orders").And.Contain("10").And.Contain("Bad bookings (%)").And.Contain("Collected today (%)").And.Contain("30");
        groups["Planner"].Should().Contain("Assigned by the Planner (%)").And.Contain("70").And.Contain("To Own fleet where it fits (%)").And.Contain("40")
            .And.Contain("Reassigned (%)").And.Contain("Released by the Planner (%)").And.Contain("60");
        groups["Carriers"].Should().Contain("Confirmed by the Carrier (%)").And.Contain("85").And.Contain("Declined by the Carrier (%)").And.Contain("Tracked (%)")
            .And.Contain("Exceptions on the way (%)").And.Contain("Delivered (%)").And.Contain("With a Proof of delivery (%)").And.Contain("65").And.Contain("Failed collections cancelled (%)");
        groups["Own fleet and Drivers"].Should().Contain("Own fleet put on Trips (%)").And.Contain("90").And.Contain("Collections that fail (%)")
            .And.Contain("Deliveries that fail (%)").And.Contain("Damage reported (%)").And.Contain("Delivered with reservations (%)").And.Contain("12");
        groups["Finance and invoicing"].Should().Contain("Shippers invoiced (%)").And.Contain("100").And.Contain("Invoices credited in part (%)").And.Contain("50").And.Contain("Carrier invoices with an edited amount (%)").And.Contain("Carrier invoice lines rejected (%)").And.Contain("25");
        groups["How it is sent"].Should().Contain("Burst").And.Contain("Seed").And.Contain("77").And.Contain("Preset").And.Contain("Quiet Monday");
    }

    [Fact]
    public void Render_APacedCustomRun_ShowsItsWindowAndNoPreset()
    {
        GivenRun(TestData.Summary(), TestData.Settings(preset: null, mode: RunMode.Paced));

        var sent = RenderRun().Find("dl[aria-label='How it is sent']").TextContent;

        sent.Should().Contain("Paced").And.Contain("15 minutes").And.Contain("Custom");
    }

    [Fact]
    public void Render_ARun_OffersToRunAgainWithTheSameSeedOrANewOneAndToPreviewItsPlan()
    {
        GivenRun(TestData.Summary(), TestData.Settings());

        var page = RenderRun();

        page.Find("fluent-anchor:contains('Run again')").GetAttribute("href").Should().Be(PlanLinks.Configure(TestData.Settings()));
        page.Find("fluent-anchor:contains('Run again with a new seed')").GetAttribute("href").Should().Be(PlanLinks.ConfigureWithNewSeed(TestData.Settings()));
        page.Find("fluent-anchor:contains('Preview its plan')").GetAttribute("href").Should().Be(PlanLinks.Plan(TestData.Settings()));
    }

    [Fact]
    public void Render_RunAgain_NeedsTheRunRole()
    {
        GivenRun(TestData.Summary(), TestData.Settings());
        Operator.SetRoles(Norvia.Simulator.Application.Operators.OperatorRoles.Read);
        Operator.SetPolicies(Norvia.Simulator.Application.Operators.OperatorRoles.Read);

        var page = RenderRun();

        page.FindAll("fluent-anchor").Select(link => link.TextContent.Trim()).Should().NotContain(["Run again", "Run again with a new seed", "Preview its plan"]);
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
        log.QuerySelectorAll("[data-outcome]").Select(span => span.TextContent.Trim()).Should().Equal("Info", "Accepted", "Rejected", "Planned", "Planning refused");
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
    public void Render_TheNumbersOfALine_LinkToTheirPagesInTheTmsUi()
    {
        Requests.ListLogAsync(Arg.Any<SimulationRunId>(), Arg.Any<LogFilter>(), Arg.Any<CancellationToken>()).Returns(
        [
            new LogLine(7, TestData.Now, "Anouk de Vries", "Collected", LogOutcome.DriverReported, "drv-1", "SIM-4-1", "TO-0000001", "SH-0000001", null, "TR-0000007"),
        ]);

        var page = RenderRun();

        var links = page.FindAll("[role=log] a").Select(link => link.GetAttribute("href")).ToList();
        links.Should().Contain("http://tms.example:5001/transport-orders/TO-0000001");
        links.Should().Contain("http://tms.example:5001/shipments/SH-0000001");
        links.Should().Contain("http://tms.example:5001/trips/TR-0000007");
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
    public void Render_TheRunCantBeRead_SaysSo()
    {
        Requests.GetRunAsync(Arg.Any<SimulationRunId>(), Arg.Any<CancellationToken>())
            .Returns<Result<Norvia.Simulator.Application.Runs.Open.RunDetail>>(_ => throw new InvalidOperationException("down"));

        RenderRun().Find("[role=alert]").TextContent.Should().Contain("couldn't be read");
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

        Field(page, "Correlation ID, reference, order or Shipment number").Change("SIM-4-2");
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

    [Fact]
    public void Render_ANormalRun_IsNotLabelledHostile()
    {
        var page = RenderRun();

        page.FindAll("[data-testid=hostile-banner]").Should().BeEmpty();
        page.Find("h1").TextContent.Should().NotContain("HOSTILE");
        SettingsSummary.Of(TestData.Settings()).Should().NotContain(group => group.Title.Contains("Hostile"));
    }

    [Fact]
    public void Render_AHostileRun_SaysSoInItsTitleItsBannerAndItsSettings()
    {
        var settings = TestData.Settings() with { HostileCarrierIds = ["CAR-0001"], HostileDriverIds = ["DRV-0002", "DRV-0003"] };
        GivenRun(TestData.Summary(), settings);

        var page = RenderRun();

        page.Find("h1").TextContent.Should().Contain("Run 4").And.Contain("HOSTILE");
        page.Find("[data-testid=hostile-banner]").TextContent.Should().Contain("profile: hostile").And.Contain("CAR-0001").And.Contain("DRV-0002, DRV-0003");
        var group = SettingsSummary.Of(settings).Single(candidate => candidate.Title.Contains("Hostile"));
        group.Settings.Should().Equal(("Carriers", "CAR-0001"), ("Drivers", "DRV-0002, DRV-0003"));
    }

    [Fact]
    public void SettingsSummary_AHostileRunWithOnlyDrivers_SaysNoneForCarriers()
    {
        var group = SettingsSummary.Of(TestData.Settings() with { HostileDriverIds = ["DRV-0001"] }).Single(candidate => candidate.Title.Contains("Hostile"));

        group.Settings.Should().Equal(("Carriers", "None"), ("Drivers", "DRV-0001"));
    }
}
