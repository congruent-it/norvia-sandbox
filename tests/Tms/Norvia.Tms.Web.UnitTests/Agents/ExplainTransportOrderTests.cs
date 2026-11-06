using Ardalis.Result;
using Norvia.Agents;
using Norvia.Tms.Application.Agents.Explain;
using Norvia.Tms.Application.TransportOrders.GetDetail;
using Norvia.Tms.Web.Agents;
using Norvia.Tms.Web.TransportOrders;
using Norvia.Tms.Web.UnitTests.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.Agents;

public sealed class ExplainTransportOrderTests : TmsWebContext
{
    private const string Number = "TO-0000042";

    private static Suggestion Answer() => new(
        "Rating worked and nothing is blocking it.",
        [new Evidence("The rating result is Rated.", "r1", "get_transport_order")],
        "Nothing needs doing for this Transport Order.",
        Confidence.High,
        SuggestedActionKind.NeedsAPerson);

    private IRenderedComponent<ExplainTransportOrder> Render(bool customerService = true) =>
        Render<ExplainTransportOrder>(parameters => parameters.Add(page => page.TransportOrderNumber, Number).Add(page => page.ForCustomerService, customerService));

    [Fact]
    public void Render_AgentsOffInTheCustomerServiceView_ShowsNoButton()
    {
        AgentSettings.Enabled = false;

        Render().FindAll("fluent-button").Should().BeEmpty();
    }

    [Fact]
    public void Render_AgentsOnInAnotherRoleView_ShowsNoButton()
    {
        AgentSettings.Enabled = true;

        Render(customerService: false).FindAll("fluent-button").Should().BeEmpty();
    }

    [Fact]
    public void Render_AgentsOnInTheCustomerServiceView_ShowsTheExplainButton()
    {
        AgentSettings.Enabled = true;

        Render().Find("fluent-button").TextContent.Trim().Should().Be("Explain");
    }

    [Fact]
    public void Explain_ShowsTheSummaryTheCitedEvidenceAndWhatIsBlocking()
    {
        AgentSettings.Enabled = true;
        Explain.ExplainAsync(new ExplainTransportOrderQuery(Number), Arg.Any<CancellationToken>()).Returns(Answer());
        var page = Render();

        page.Find("fluent-button").Click();

        page.WaitForAssertion(() =>
        {
            var text = page.Find("[data-testid=explanation]").TextContent;
            text.Should().Contain("Rating worked and nothing is blocking it.").And.Contain("[r1, get_transport_order]").And.Contain("Nothing needs doing for this Transport Order.").And.Contain("Confidence: high");
        });
    }

    [Fact]
    public void Explain_WhatTheAssistantWrote_IsEncoded_NeverRenderedAsMarkup()
    {
        AgentSettings.Enabled = true;
        Explain.ExplainAsync(Arg.Any<ExplainTransportOrderQuery>(), Arg.Any<CancellationToken>())
            .Returns(Answer() with { Cause = "<script>alert(1)</script>" });
        var page = Render();

        page.Find("fluent-button").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=explanation]").TextContent.Should().Contain("<script>alert(1)</script>"));
        page.FindAll("script").Should().BeEmpty();
    }

    [Fact]
    public void Explain_WhenTheAgentIsUnavailable_ShowsAssistantUnavailableAndNothingElse()
    {
        AgentSettings.Enabled = true;
        Explain.ExplainAsync(Arg.Any<ExplainTransportOrderQuery>(), Arg.Any<CancellationToken>()).Returns(Result<Suggestion>.Unavailable("AgentUnavailable"));
        var page = Render();

        page.Find("fluent-button").Click();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("Assistant unavailable"));
        page.FindAll("[data-testid=explanation]").Should().BeEmpty();
    }

    [Fact]
    public void Explain_WhenTheRequestThrows_ShowsAssistantUnavailable()
    {
        AgentSettings.Enabled = true;
        Explain.ExplainAsync(Arg.Any<ExplainTransportOrderQuery>(), Arg.Any<CancellationToken>()).Returns<Result<Suggestion>>(_ => throw new InvalidOperationException("boom"));
        var page = Render();

        page.Find("fluent-button").Click();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("Assistant unavailable"));
    }

    [Fact]
    public void TheTransportOrderPage_WhenItCannotTellTheRole_ShowsNoExplainButton()
    {
        AgentSettings.Enabled = true;
        var detail = new TransportOrderDetailView(Samples.View(), null, null);
        Queries.FindAsync("TO-0000042", Arg.Any<CancellationToken>()).Returns(detail);

        var page = Render<TransportOrderDetail>(parameters => parameters.Add(page => page.TransportOrderNumber, "TO-0000042"));

        page.WaitForAssertion(() => page.Find("h1").TextContent.Should().Contain("TO-0000042"));
        page.FindAll("[data-explain]").Should().BeEmpty("a page that cannot read the role fails closed");
    }
}
