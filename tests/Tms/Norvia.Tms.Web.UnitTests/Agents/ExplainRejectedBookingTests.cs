using Ardalis.Result;
using Norvia.Agents;
using Norvia.Tms.Application.Agents.Explain;
using Norvia.Tms.Web.Agents;
using Norvia.Tms.Web.TransportOrders;
using Norvia.Tms.Web.UnitTests.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.Agents;

public sealed class ExplainRejectedBookingTests : TmsWebContext
{
    private static readonly Guid BookingId = Guid.Parse("3f2b8c9e-6d5a-4c1e-9a77-0e1f2a3b4c5d");

    private static Suggestion Answer() => new(
        "A goods line has no weight.",
        [new Evidence("The booking was rejected with WeightInvalid.", "r1", "get_rejected_booking")],
        "Tell the Shipper: give every goods line a weight.",
        Confidence.High,
        SuggestedActionKind.NeedsAPerson);

    private IRenderedComponent<ExplainRejectedBooking> Render(bool customerService = true) =>
        Render<ExplainRejectedBooking>(parameters => parameters.Add(page => page.Id, BookingId).Add(page => page.ForCustomerService, customerService));

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
    public void Explain_ShowsTheCauseTheCitedEvidenceAndWhatTheShipperShouldChange()
    {
        AgentSettings.Enabled = true;
        Explain.ExplainAsync(new ExplainRejectedBookingQuery(BookingId), Arg.Any<CancellationToken>()).Returns(Answer());
        var page = Render();

        page.Find("fluent-button").Click();

        page.WaitForAssertion(() =>
        {
            var text = page.Find("[data-testid=explanation]").TextContent;
            text.Should().Contain("A goods line has no weight.").And.Contain("[r1, get_rejected_booking]").And.Contain("Tell the Shipper: give every goods line a weight.").And.Contain("Confidence: high");
        });
    }

    [Fact]
    public void Explain_WhatTheAssistantWrote_IsEncoded_NeverRenderedAsMarkup()
    {
        AgentSettings.Enabled = true;
        Explain.ExplainAsync(Arg.Any<ExplainRejectedBookingQuery>(), Arg.Any<CancellationToken>())
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
        Explain.ExplainAsync(Arg.Any<ExplainRejectedBookingQuery>(), Arg.Any<CancellationToken>()).Returns(Result<Suggestion>.Unavailable("AgentUnavailable"));
        var page = Render();

        page.Find("fluent-button").Click();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("Assistant unavailable"));
        page.FindAll("[data-testid=explanation]").Should().BeEmpty();
    }

    [Fact]
    public void Explain_WhenTheRequestThrows_ShowsAssistantUnavailable()
    {
        AgentSettings.Enabled = true;
        Explain.ExplainAsync(Arg.Any<ExplainRejectedBookingQuery>(), Arg.Any<CancellationToken>()).Returns<Result<Suggestion>>(_ => throw new InvalidOperationException("boom"));
        var page = Render();

        page.Find("fluent-button").Click();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("Assistant unavailable"));
    }

    [Fact]
    public void TheRejectedBookingPage_WhenItCannotTellTheRole_ShowsNoExplainButton()
    {
        AgentSettings.Enabled = true;
        var booking = Samples.RejectedView();
        Queries.FindRejectedAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);

        var page = Render<RejectedBookingDetail>(parameters => parameters.Add(detail => detail.Id, booking.Id));

        page.WaitForAssertion(() => page.Find("h1").TextContent.Should().Contain("Rejected booking"));
        page.FindAll("[data-explain]").Should().BeEmpty("a page that cannot read the role fails closed");
    }
}
