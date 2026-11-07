using Ardalis.Result;
using Norvia.Agents;
using Norvia.Erp.Application.Agents.Explain;
using Norvia.Erp.Domain.Carriers;
using Norvia.Erp.Web.Agents;

namespace Norvia.Erp.Web.UnitTests.Agents;

public sealed class ExplainInvoiceLineTests : ErpWebContext
{
    private static Suggestion Answer() => new(
        "The Carrier claims €80.00, €10.00 more than the agreed Carrier cost of €70.00 (Rate Card version 3).",
        [new Evidence("The Carrier claims €80.00 for the line.", "r1", "get_invoice_line")],
        "Accept the line at the agreed amount with a remark, or reject it.",
        Confidence.High,
        SuggestedActionKind.NeedsAPerson);

    private IRenderedComponent<ExplainInvoiceLine> Render() =>
        Render<ExplainInvoiceLine>(parameters => parameters
            .Add(component => component.CarrierId, "CAR-0001")
            .Add(component => component.InvoiceNumber, "VDT-1")
            .Add(component => component.CarrierBookingReference, "TRX-1"));

    [Fact]
    public void Render_AgentsOff_ShowsNoButton()
    {
        AgentSettings.Enabled = false;

        Render().FindAll("fluent-button").Should().BeEmpty();
    }

    [Fact]
    public void Render_AgentsOn_ShowsTheExplainButtonOfTheLine()
    {
        AgentSettings.Enabled = true;

        var button = Render().Find("fluent-button");

        button.TextContent.Trim().Should().Be("Explain");
        button.GetAttribute("aria-label").Should().Be("Explain TRX-1");
    }

    [Fact]
    public void Explain_AsksAboutThisLine_AndShowsTheCauseTheCitedEvidenceAndWhatFinanceCouldDo()
    {
        AgentSettings.Enabled = true;
        Explain.ExplainAsync(new ExplainInvoiceLineQuery(new CarrierId("CAR-0001"), "VDT-1", "TRX-1"), Arg.Any<CancellationToken>()).Returns(Answer());
        var page = Render();

        page.Find("fluent-button").Click();

        page.WaitForAssertion(() =>
        {
            var text = page.Find("[data-testid=explanation-TRX-1]").TextContent;
            text.Should().Contain("€10.00 more than the agreed Carrier cost").And.Contain("[r1, get_invoice_line]")
                .And.Contain("Accept the line at the agreed amount with a remark, or reject it.").And.Contain("Confidence: high");
        });
    }

    [Fact]
    public void Explain_WhatTheAssistantWrote_IsEncoded_NeverRenderedAsMarkup()
    {
        AgentSettings.Enabled = true;
        Explain.ExplainAsync(Arg.Any<ExplainInvoiceLineQuery>(), Arg.Any<CancellationToken>()).Returns(Answer() with { Cause = "<script>alert(1)</script>" });
        var page = Render();

        page.Find("fluent-button").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=explanation-TRX-1]").TextContent.Should().Contain("<script>alert(1)</script>"));
        page.FindAll("script").Should().BeEmpty();
    }

    [Fact]
    public void Explain_WhenTheAgentIsUnavailable_ShowsAssistantUnavailableAndNothingElse()
    {
        AgentSettings.Enabled = true;
        Explain.ExplainAsync(Arg.Any<ExplainInvoiceLineQuery>(), Arg.Any<CancellationToken>()).Returns(Result<Suggestion>.Unavailable("AgentUnavailable"));
        var page = Render();

        page.Find("fluent-button").Click();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("Assistant unavailable"));
        page.FindAll("[data-testid=explanation-TRX-1]").Should().BeEmpty();
    }

    [Fact]
    public void Explain_WhenTheRequestThrows_ShowsAssistantUnavailable()
    {
        AgentSettings.Enabled = true;
        Explain.ExplainAsync(Arg.Any<ExplainInvoiceLineQuery>(), Arg.Any<CancellationToken>()).Returns<Result<Suggestion>>(_ => throw new InvalidOperationException("boom"));
        var page = Render();

        page.Find("fluent-button").Click();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("Assistant unavailable"));
    }
}
