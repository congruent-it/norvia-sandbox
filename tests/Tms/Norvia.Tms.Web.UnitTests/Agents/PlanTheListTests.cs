using Ardalis.Result;
using AngleSharp.Dom;
using Microsoft.AspNetCore.Http;
using Norvia.Agents;
using Norvia.Tms.Application.Agents.Planner;
using Norvia.Tms.Web.Agents;
using Norvia.Tms.Web.TransportOrders;
using Norvia.Tms.Web.UnitTests.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.Agents;

/// <summary>#445: Plan the list gives a Suggestion per order, each accepted or ignored on its own; a run over its cap says it stopped.</summary>
public sealed class PlanTheListTests : TmsWebContext
{
    private static readonly AgentRun Run = new("planner", "run-1");
    private static readonly string[] StaleOrders = ["TO-0000001"];

    private static ListedSuggestion Own(string number) =>
        new(number, new Suggestion($"Why {number}.", [], "Assign to Own fleet.", Confidence.High, SuggestedActionKind.AssignOwnFleet, Run));

    private static ListedSuggestion Person(string number) =>
        new(number, new Suggestion("No option.", [], "Choose yourself.", Confidence.Low, SuggestedActionKind.NeedsAPerson, Run));

    private IRenderedComponent<PlanTheList> Render(bool planner = true, ListedSuggestion? accepted = null, Action<ListedSuggestion>? onAccepted = null) =>
        Render<PlanTheList>(parameters => parameters
            .Add(page => page.ForPlanner, planner)
            .Add(page => page.OnAccepted, (ListedSuggestion value) => onAccepted?.Invoke(value)));

    [Fact]
    public void Render_AgentsOffOrAnotherRole_ShowsNoButton()
    {
        AgentSettings.Enabled = false;
        Render().FindAll("fluent-button").Should().BeEmpty();

        AgentSettings.Enabled = true;
        Render(planner: false).FindAll("fluent-button").Should().BeEmpty();
    }

    [Fact]
    public void Plan_ShowsASuggestionPerOrder_InTheListsOrder_AndAssignsNothing()
    {
        AgentSettings.Enabled = true;
        Explain.PlanAsync(new PlanTheListQuery(), Arg.Any<CancellationToken>()).Returns(new PlannedList([Own("TO-0000001"), Person("TO-0000002")], null));
        var page = Render();

        page.Find("fluent-button").Click();

        page.WaitForAssertion(() => page.FindAll("[data-testid=plan-suggestion]").Should().HaveCount(2));
        page.FindAll("[data-testid=plan-suggestion]").Select(item => item.GetAttribute("data-order")).Should().Equal("TO-0000001", "TO-0000002");
        page.Markup.Should().Contain("Why TO-0000001.").And.NotContain("plan-stopped");
        Commands.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void Accept_RaisesOnlyThatSuggestion_AndNeedsAPersonOffersNothingToAccept()
    {
        AgentSettings.Enabled = true;
        Explain.PlanAsync(new PlanTheListQuery(), Arg.Any<CancellationToken>()).Returns(new PlannedList([Own("TO-0000001"), Own("TO-0000002"), Person("TO-0000003")], null));
        var accepted = new List<ListedSuggestion>();
        var page = Render(onAccepted: accepted.Add);
        page.Find("fluent-button").Click();
        page.WaitForAssertion(() => page.FindAll("[data-testid=plan-suggestion]").Should().HaveCount(3));

        page.Find("fluent-button[aria-label='Accept the suggestion for TO-0000002']").Click();

        accepted.Should().ContainSingle().Which.TransportOrderNumber.Should().Be("TO-0000002");
        page.Find("fluent-button[aria-label='Accept the suggestion for TO-0000003']").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void Ignore_DropsThatSuggestionOnly()
    {
        AgentSettings.Enabled = true;
        Explain.PlanAsync(new PlanTheListQuery(), Arg.Any<CancellationToken>()).Returns(new PlannedList([Own("TO-0000001"), Own("TO-0000002")], null));
        var page = Render();
        page.Find("fluent-button").Click();
        page.WaitForAssertion(() => page.FindAll("[data-testid=plan-suggestion]").Should().HaveCount(2));

        page.Find("fluent-button[aria-label='Ignore the suggestion for TO-0000001']").Click();

        page.FindAll("[data-testid=plan-suggestion]").Select(item => item.GetAttribute("data-order")).Should().Equal("TO-0000002");
    }

    [Fact]
    public void Plan_ARunOverItsCap_ShowsTheSuggestionsSoFar_AndSaysItStopped()
    {
        AgentSettings.Enabled = true;
        Explain.PlanAsync(new PlanTheListQuery(), Arg.Any<CancellationToken>())
            .Returns(new PlannedList([Own("TO-0000001")], new PlanStop(PlanStopReason.CapReached, 3, "TO-0000002")));
        var page = Render();

        page.Find("fluent-button").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=plan-stopped]").TextContent.Should().Contain("stopped at its cap after 1 order(s)").And.Contain("3 order(s), from TO-0000002"));
        page.FindAll("[data-testid=plan-suggestion]").Should().HaveCount(1);
    }

    [Fact]
    public void Plan_AnAssistantThatFailsPartWay_SaysItBecameUnavailable()
    {
        AgentSettings.Enabled = true;
        Explain.PlanAsync(new PlanTheListQuery(), Arg.Any<CancellationToken>())
            .Returns(new PlannedList([], new PlanStop(PlanStopReason.AssistantUnavailable, 2, "TO-0000001")));
        var page = Render();

        page.Find("fluent-button").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=plan-stopped]").TextContent.Should().Contain("became unavailable"));
    }

    [Fact]
    public void Plan_AnEmptyList_SaysNothingIsWaiting()
    {
        AgentSettings.Enabled = true;
        Explain.PlanAsync(new PlanTheListQuery(), Arg.Any<CancellationToken>()).Returns(new PlannedList([], null));
        var page = Render();

        page.Find("fluent-button").Click();

        page.WaitForAssertion(() => page.Markup.Should().Contain("No Transport Orders are waiting for planning."));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Plan_TheAssistantUnavailableOrCrashing_SaysSo(bool crash)
    {
        AgentSettings.Enabled = true;
        if (crash)
        {
            Explain.PlanAsync(new PlanTheListQuery(), Arg.Any<CancellationToken>()).Returns<Task<Result<PlannedList>>>(_ => throw new InvalidOperationException("boom"));
        }
        else
        {
            Explain.PlanAsync(new PlanTheListQuery(), Arg.Any<CancellationToken>()).Returns(Result<PlannedList>.Unavailable("AgentUnavailable"));
        }

        var page = Render();
        page.Find("fluent-button").Click();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("Assistant unavailable"));
    }

    [Fact]
    public void AStaleSuggestion_SaysTheTmsRefusedTheAssignment()
    {
        AgentSettings.Enabled = true;
        Explain.PlanAsync(new PlanTheListQuery(), Arg.Any<CancellationToken>()).Returns(new PlannedList([Own("TO-0000001")], null));
        var page = Render<PlanTheList>(parameters => parameters.Add(p => p.ForPlanner, true).Add(p => p.Stale, StaleOrders));

        page.Find("fluent-button").Click();

        page.WaitForAssertion(() => page.Markup.Should().Contain("out of date"));
    }

    [Fact]
    public void OnTheWaitingPage_AcceptingOneSuggestion_SelectsThatOrderAlone_NamingItsRunOnAssign()
    {
        AgentSettings.Enabled = true;
        Queries.ListActiveCarriersAsync(Arg.Any<CancellationToken>()).Returns([Samples.Kessler]);
        Queries.ListWaitingForPlanningAsync(Arg.Any<CancellationToken>()).Returns([Samples.ToPlan("TO-0000042"), Samples.ToPlan("TO-0000043")]);
        Explain.PlanAsync(new PlanTheListQuery(), Arg.Any<CancellationToken>()).Returns(new PlannedList([Own("TO-0000042"), Own("TO-0000043")], null));
        Commands.AssignAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<AgentRun?>(), Arg.Any<CancellationToken>()).Returns(new AssignAnswer.Planned("SH-0000012"));
        var page = Render<WaitingForPlanning>(parameters => parameters.AddCascadingValue<HttpContext>(new DefaultHttpContext()));
        page.WaitForElement("a[href='/transport-orders/TO-0000042']");
        page.Find("fluent-checkbox[aria-label='Select TO-0000043']").TriggerEvent("oncheckedchange", new Microsoft.FluentUI.AspNetCore.Components.CheckboxChangeEventArgs { Checked = true });

        page.Find("[data-explain=plan-the-list] fluent-button").Click();
        page.WaitForAssertion(() => page.FindAll("[data-testid=plan-suggestion]").Should().HaveCount(2));
        page.Find("fluent-button[aria-label='Accept the suggestion for TO-0000042']").Click();

        var assign = page.Find("[role=group][aria-label=Assign] fluent-button");
        page.WaitForAssertion(() => assign.TextContent.Trim().Should().Be("Assign selected (1)"));
        Commands.ReceivedCalls().Should().BeEmpty("accepting selects; nothing executes until the Planner confirms");
        assign.Click();
        page.WaitForAssertion(() => Commands.Received(1).AssignAsync("TO-0000042", null, Run, Arg.Any<CancellationToken>()));
        Commands.DidNotReceive().AssignAsync("TO-0000043", Arg.Any<string?>(), Arg.Any<AgentRun?>(), Arg.Any<CancellationToken>());
    }
}
