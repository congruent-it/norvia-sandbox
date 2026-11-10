using AngleSharp.Dom;
using Microsoft.AspNetCore.Http;
using Microsoft.FluentUI.AspNetCore.Components;
using Norvia.Agents;
using Norvia.Tms.Application.Agents.Planner;
using Norvia.Tms.Application.TransportOrders.ToPlan;
using Norvia.Tms.Web.TransportOrders;
using Norvia.Tms.Web.UnitTests.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.Agents;

/// <summary>#442: a Suggestion pre-selects who carries the order, assigns nothing, and the assignment names the Agent run only when the Planner confirms what was suggested.</summary>
public sealed class PreselectSuggestedAssignmentTests : TmsWebContext
{
    private const string Number = "TO-0000042";
    private static readonly AgentRun Run = new("planner", "run-7");

    public PreselectSuggestedAssignmentTests()
    {
        AgentSettings.Enabled = true;
        Queries.ListActiveCarriersAsync(Arg.Any<CancellationToken>()).Returns([Samples.Kessler, Samples.VanDijk]);
        Queries.ListWaitingForPlanningAsync(Arg.Any<CancellationToken>()).Returns([Samples.ToPlan(Number), Samples.ToPlan("TO-0000043")]);
        Commands.AssignAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<AgentRun?>(), Arg.Any<CancellationToken>()).Returns(new AssignAnswer.Planned("SH-0000012"));
    }

    private static Suggestion Suggests(SuggestedActionKind action, string? carrierId = null, AgentRun? run = null) => new(
        "Why.",
        [new Evidence("A fact.", "r1", "get_assignment_options")],
        action == SuggestedActionKind.AssignOwnFleet ? "Assign to Own fleet." : "Assign to Carrier Kessler Spedition.",
        Confidence.High,
        action,
        run ?? Run,
        carrierId);

    private IRenderedComponent<WaitingForPlanning> RenderAsPlanner()
    {
        var page = Render<WaitingForPlanning>(parameters => parameters.AddCascadingValue<HttpContext>(new DefaultHttpContext()));
        page.WaitForElement($"a[href='/transport-orders/{Number}']");
        return page;
    }

    private void Ask(IRenderedComponent<WaitingForPlanning> page, Suggestion suggestion, string number = Number)
    {
        Explain.SuggestAsync(new SuggestAssignmentQuery(number), Arg.Any<CancellationToken>()).Returns(suggestion);
        page.Find($"fluent-button[aria-label='Suggest for {number}']").Click();
        page.WaitForAssertion(() => page.Find($"tr:has(a[href='/transport-orders/{number}']) [data-testid=suggestion]"));
    }

    private static IElement AssignButton(IRenderedComponent<WaitingForPlanning> page) => page.Find("[role=group][aria-label=Assign] fluent-button");

    private static void Choose(IRenderedComponent<WaitingForPlanning> page, string value) =>
        page.WaitForElement($"fluent-select fluent-option[value='{value}']").ParentElement!.Change(value);

    [Fact]
    public void ACarrierSuggestion_PreselectsTheOrderAndTheCarrier_AndAssignsNothing()
    {
        var page = RenderAsPlanner();

        Ask(page, Suggests(SuggestedActionKind.AssignCarrier, Samples.Kessler.Id));

        page.WaitForAssertion(() => AssignButton(page).TextContent.Trim().Should().Be("Assign selected (1)"));
        Commands.ReceivedCalls().Should().BeEmpty("nothing executes until the Planner confirms");
    }

    [Fact]
    public void ConfirmingACarrierSuggestion_AssignsTheOrderToItAsThePlannersOwnClickDoes_NamingTheAgentRun()
    {
        var page = RenderAsPlanner();
        Ask(page, Suggests(SuggestedActionKind.AssignCarrier, Samples.Kessler.Id));
        page.WaitForAssertion(() => AssignButton(page).TextContent.Trim().Should().Be("Assign selected (1)"));

        AssignButton(page).Click();

        page.WaitForAssertion(() => Commands.Received(1).AssignAsync(Number, Samples.Kessler.Id, Run, Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void ConfirmingAnOwnFleetSuggestion_AssignsWithoutACarrier_NamingTheAgentRun()
    {
        var page = RenderAsPlanner();
        Ask(page, Suggests(SuggestedActionKind.AssignOwnFleet));

        page.WaitForAssertion(() => AssignButton(page).TextContent.Trim().Should().Be("Assign selected (1)"));
        AssignButton(page).Click();

        page.WaitForAssertion(() => Commands.Received(1).AssignAsync(Number, null, Run, Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void ChoosingAnotherAssignee_AssignsOnThePlannersOwn_NamingNoAgent()
    {
        var page = RenderAsPlanner();
        Ask(page, Suggests(SuggestedActionKind.AssignCarrier, Samples.Kessler.Id));
        page.WaitForAssertion(() => AssignButton(page).TextContent.Trim().Should().Be("Assign selected (1)"));

        Choose(page, Samples.VanDijk.Id);
        AssignButton(page).Click();

        page.WaitForAssertion(() => Commands.Received(1).AssignAsync(Number, Samples.VanDijk.Id, null, Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void AnotherOrderAssignedAlongsideTheSuggestedOne_NamesNoAgent()
    {
        var page = RenderAsPlanner();
        Ask(page, Suggests(SuggestedActionKind.AssignOwnFleet));
        page.Find("fluent-checkbox[aria-label='Select TO-0000043']").TriggerEvent("oncheckedchange", new CheckboxChangeEventArgs { Checked = true });
        page.WaitForAssertion(() => AssignButton(page).TextContent.Trim().Should().Be("Assign selected (2)"));

        AssignButton(page).Click();

        page.WaitForAssertion(() => Commands.Received(1).AssignAsync("TO-0000043", null, null, Arg.Any<CancellationToken>()));
        Commands.Received(1).AssignAsync(Number, null, Run, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SuggestingForASecondOrder_KeepsTheFirstOrdersRunInItsTrace()
    {
        var page = RenderAsPlanner();
        Ask(page, Suggests(SuggestedActionKind.AssignOwnFleet));
        Ask(page, Suggests(SuggestedActionKind.AssignOwnFleet, run: new AgentRun("planner", "run-8")), "TO-0000043");
        page.WaitForAssertion(() => AssignButton(page).TextContent.Trim().Should().Be("Assign selected (2)"));

        AssignButton(page).Click();

        page.WaitForAssertion(() => Commands.Received(1).AssignAsync("TO-0000043", null, new AgentRun("planner", "run-8"), Arg.Any<CancellationToken>()));
        Commands.Received(1).AssignAsync(Number, null, Run, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ACarrierThePickerDoesNotOffer_IsNotPreselected()
    {
        var page = RenderAsPlanner();

        Ask(page, Suggests(SuggestedActionKind.AssignCarrier, "CAR-0009"));

        AssignButton(page).TextContent.Trim().Should().Be("Assign selected (0)");
    }

    [Fact]
    public void ASuggestionThatNeedsAPerson_PreselectsNothing()
    {
        var page = RenderAsPlanner();

        Ask(page, Suggests(SuggestedActionKind.NeedsAPerson));

        AssignButton(page).TextContent.Trim().Should().Be("Assign selected (0)");
    }

    [Fact]
    public void WhenTheTmsRefusesTheSuggestedAssignment_ThePlannerSeesTheRefusalAsUsual_AndTheSuggestionIsStale()
    {
        Commands.AssignAsync(Number, Samples.Kessler.Id, Run, Arg.Any<CancellationToken>()).Returns(new AssignAnswer.Refused("Carrier CAR-0002 is not active."));
        var page = RenderAsPlanner();
        Ask(page, Suggests(SuggestedActionKind.AssignCarrier, Samples.Kessler.Id));
        page.WaitForAssertion(() => AssignButton(page).TextContent.Trim().Should().Be("Assign selected (1)"));

        AssignButton(page).Click();

        page.WaitForAssertion(() => page.Find("ul[aria-label='Assignment outcomes']").TextContent.Should().Contain($"Transport Order {Number} wasn't assigned: Carrier CAR-0002 is not active."));
        page.Find("[data-testid=suggestion]").TextContent.Should().Contain("out of date");
    }

    [Fact]
    public void AskingAgain_AfterARefusal_ClearsTheStaleMark()
    {
        Commands.AssignAsync(Number, Samples.Kessler.Id, Run, Arg.Any<CancellationToken>()).Returns(new AssignAnswer.Refused("Carrier CAR-0002 is not active."));
        var page = RenderAsPlanner();
        Ask(page, Suggests(SuggestedActionKind.AssignCarrier, Samples.Kessler.Id));
        page.WaitForAssertion(() => AssignButton(page).TextContent.Trim().Should().Be("Assign selected (1)"));
        AssignButton(page).Click();
        page.WaitForAssertion(() => page.Find("[data-testid=suggestion]").TextContent.Should().Contain("out of date"));

        Ask(page, Suggests(SuggestedActionKind.AssignOwnFleet));

        page.WaitForAssertion(() => page.Find("[data-testid=suggestion]").TextContent.Should().NotContain("out of date"));
    }
}
