using Ardalis.Result;
using Norvia.Agents;
using Norvia.Tms.Application.Agents.Explain;
using Norvia.Tms.Web.Agents;
using Norvia.Tms.Web.UnitTests.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.Agents;

public sealed class ExplainOrderHoldTests : TmsWebContext
{
    private const string Number = "TO-0000042";

    private static Suggestion Answer() => new(
        "Rating failed.",
        [new Evidence("Rating failed with NoMatchingLane.", "r1", "get_transport_order")],
        "Ask whoever owns the Rate Card to fix it.",
        Confidence.High,
        SuggestedActionKind.NeedsAPerson);

    private IRenderedComponent<ExplainOrderHold> Render(ExplainOrderHold.HoldKind kind, bool customerService = true) =>
        Render<ExplainOrderHold>(parameters => parameters.Add(page => page.TransportOrderNumber, Number).Add(page => page.Kind, kind).Add(page => page.ForCustomerService, customerService));

    [Fact]
    public void Render_AgentsOffOrAnotherRole_ShowsNoButton()
    {
        AgentSettings.Enabled = false;
        Render(ExplainOrderHold.HoldKind.RatingFailure).FindAll("fluent-button").Should().BeEmpty();

        AgentSettings.Enabled = true;
        Render(ExplainOrderHold.HoldKind.NotYetBillable, customerService: false).FindAll("fluent-button").Should().BeEmpty();
    }

    [Fact]
    public void Explain_ARatingFailure_AsksTheRatingQuestion_AndShowsWhatFixesIt()
    {
        AgentSettings.Enabled = true;
        Explain.ExplainAsync(new ExplainRatingFailureQuery(Number), Arg.Any<CancellationToken>()).Returns(Answer());
        var page = Render(ExplainOrderHold.HoldKind.RatingFailure);

        page.Find("fluent-button").Click();

        page.WaitForAssertion(() =>
        {
            var text = page.Find("[data-testid=explanation]").TextContent;
            text.Should().Contain("Why Rating failed").And.Contain("Rating failed.").And.Contain("[r1, get_transport_order]").And.Contain("What fixes it").And.Contain("Rate Card");
        });
    }

    [Fact]
    public void Explain_ANotYetBillableOrder_AsksTheBillingQuestion_AndShowsWhatYouCanDo()
    {
        AgentSettings.Enabled = true;
        Explain.ExplainAsync(new ExplainNotYetBillableQuery(Number), Arg.Any<CancellationToken>()).Returns(Answer());
        var page = Render(ExplainOrderHold.HoldKind.NotYetBillable);

        page.Find("fluent-button").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=explanation]").TextContent.Should().Contain("What holds it back").And.Contain("What you can do"));
    }

    [Fact]
    public void Explain_WhatTheAssistantWrote_IsEncoded_NeverRenderedAsMarkup()
    {
        AgentSettings.Enabled = true;
        Explain.ExplainAsync(Arg.Any<ExplainRatingFailureQuery>(), Arg.Any<CancellationToken>()).Returns(Answer() with { Cause = "<script>alert(1)</script>" });
        var page = Render(ExplainOrderHold.HoldKind.RatingFailure);

        page.Find("fluent-button").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=explanation]").TextContent.Should().Contain("<script>alert(1)</script>"));
        page.FindAll("script").Should().BeEmpty();
    }

    [Fact]
    public void Explain_WhenTheAgentIsUnavailableOrTheRequestThrows_ShowsAssistantUnavailableAndNothingElse()
    {
        AgentSettings.Enabled = true;
        Explain.ExplainAsync(Arg.Any<ExplainRatingFailureQuery>(), Arg.Any<CancellationToken>()).Returns(Result<Suggestion>.Unavailable("AgentUnavailable"));
        Explain.ExplainAsync(Arg.Any<ExplainNotYetBillableQuery>(), Arg.Any<CancellationToken>()).Returns<Result<Suggestion>>(_ => throw new InvalidOperationException("boom"));

        foreach (var kind in new[] { ExplainOrderHold.HoldKind.RatingFailure, ExplainOrderHold.HoldKind.NotYetBillable })
        {
            var page = Render(kind);
            page.Find("fluent-button").Click();
            page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("Assistant unavailable"));
            page.FindAll("[data-testid=explanation]").Should().BeEmpty();
        }
    }
}
