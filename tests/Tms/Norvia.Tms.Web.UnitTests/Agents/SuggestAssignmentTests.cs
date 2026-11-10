using Ardalis.Result;
using Norvia.Agents;
using Norvia.Tms.Application.Agents.Planner;
using Norvia.Tms.Web.Agents;
using Norvia.Tms.Web.UnitTests.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.Agents;

public sealed class SuggestAssignmentTests : TmsWebContext
{
    private const string Number = "TO-0000042";

    private static Suggestion Answer(string action = "Assign to Own fleet.") => new(
        "Own fleet is the best option: it fits the collection day.",
        [new Evidence("Own fleet fits the collection day: Fits.", "r1", "get_assignment_options")],
        action,
        Confidence.High);

    private IRenderedComponent<SuggestAssignment> Render(bool planner = true) =>
        Render<SuggestAssignment>(parameters => parameters.Add(page => page.TransportOrderNumber, Number).Add(page => page.ForPlanner, planner));

    [Fact]
    public void Render_AgentsOffOrAnotherRole_ShowsNoButton()
    {
        AgentSettings.Enabled = false;
        Render().FindAll("fluent-button").Should().BeEmpty();

        AgentSettings.Enabled = true;
        Render(planner: false).FindAll("fluent-button").Should().BeEmpty();
    }

    [Fact]
    public void Render_InThePlannerViewWithAgentsOn_OffersSuggest()
    {
        AgentSettings.Enabled = true;

        Render().Find("fluent-button").TextContent.Trim().Should().Be("Suggest");
    }

    [Fact]
    public void Suggest_ShowsTheRecommendationItsReasoningAndEvidence_AndAssignsNothing()
    {
        AgentSettings.Enabled = true;
        Explain.SuggestAsync(new SuggestAssignmentQuery(Number), Arg.Any<CancellationToken>()).Returns(Answer());
        var page = Render();

        page.Find("fluent-button").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=suggestion]"));
        page.Find("[data-testid=suggested-action]").TextContent.Should().Be("Assign to Own fleet.");
        page.Find("[data-testid=suggestion]").TextContent.Should().Contain("it fits the collection day").And.Contain("[r1, get_assignment_options]").And.Contain("nothing is assigned until you do");
        Commands.ReceivedCalls().Should().BeEmpty("a suggestion is shown only; nothing is pre-selected or assigned");
    }

    [Fact]
    public void Suggest_TheAssistantsTextIsEncoded_NeverRenderedAsMarkup()
    {
        AgentSettings.Enabled = true;
        Explain.SuggestAsync(Arg.Any<SuggestAssignmentQuery>(), Arg.Any<CancellationToken>()).Returns(Answer("<script>alert(1)</script>"));
        var page = Render();

        page.Find("fluent-button").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=suggested-action]"));
        page.FindAll("script").Should().BeEmpty();
        page.Find("[data-testid=suggested-action]").TextContent.Should().Be("<script>alert(1)</script>");
    }

    [Fact]
    public void Suggest_WhenTheAgentIsUnavailableOrThrows_ShowsAssistantUnavailableAndNoSuggestion()
    {
        AgentSettings.Enabled = true;
        Explain.SuggestAsync(Arg.Any<SuggestAssignmentQuery>(), Arg.Any<CancellationToken>()).Returns(
            Task.FromResult(Result<Suggestion>.Unavailable("AgentUnavailable")),
            Task.FromException<Result<Suggestion>>(new InvalidOperationException("boom")));

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var page = Render();
            page.Find("fluent-button").Click();
            page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("Assistant unavailable"));
            page.FindAll("[data-testid=suggestion]").Should().BeEmpty();
        }
    }
}
