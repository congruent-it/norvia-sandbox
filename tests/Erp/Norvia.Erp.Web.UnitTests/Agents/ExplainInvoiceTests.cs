using Ardalis.Result;
using Norvia.Agents;
using Norvia.Erp.Application.Agents.Explain;
using Norvia.Erp.Domain.Carriers;
using Norvia.Erp.Web.Agents;

namespace Norvia.Erp.Web.UnitTests.Agents;

public sealed class ExplainInvoiceTests : ErpWebContext
{
    private static Suggestion Answer() => new(
        "Carrier invoice VDT-1 of Van Dijk Transport claims €960.00 on 12 lines. 7 lines are To decide: 3 Price mismatch, 3 Unknown booking, 1 Duplicate.",
        [new Evidence("3 lines are To decide as Price mismatch.", "r1", "get_invoice_summary")],
        "3 of them may still resolve by themselves. The other 4 wait for Finance.",
        Confidence.High,
        SuggestedActionKind.NeedsAPerson);

    private IRenderedComponent<ExplainInvoice> Render() =>
        Render<ExplainInvoice>(parameters => parameters
            .Add(component => component.CarrierId, "CAR-0001")
            .Add(component => component.InvoiceNumber, "VDT-1"));

    [Fact]
    public void Render_AgentsOff_ShowsNoButton()
    {
        AgentSettings.Enabled = false;

        Render().FindAll("fluent-button").Should().BeEmpty();
    }

    [Fact]
    public void Render_AgentsOn_ShowsTheExplainThisInvoiceButton()
    {
        AgentSettings.Enabled = true;

        var button = Render().Find("fluent-button");

        button.TextContent.Trim().Should().Be("Explain this invoice");
        button.GetAttribute("aria-label").Should().Be("Explain invoice VDT-1");
    }

    [Fact]
    public void Explain_AsksAboutThisInvoice_AndShowsTheSummaryTheCitedCausesAndWhatMayResolve_WithNoActionOffered()
    {
        AgentSettings.Enabled = true;
        Explain.ExplainAsync(new ExplainInvoiceQuery(new CarrierId("CAR-0001"), "VDT-1"), Arg.Any<CancellationToken>()).Returns(Answer());
        var page = Render();

        page.Find("fluent-button").Click();

        page.WaitForAssertion(() =>
        {
            var text = page.Find("[data-testid=invoice-explanation-VDT-1]").TextContent;
            text.Should().Contain("7 lines are To decide: 3 Price mismatch, 3 Unknown booking, 1 Duplicate").And.Contain("3 lines are To decide as Price mismatch.")
                .And.Contain("[r1, get_invoice_summary]").And.Contain("3 of them may still resolve by themselves").And.Contain("Confidence: high");
        });
        page.FindAll("fluent-button").Should().ContainSingle("the explanation offers no action");
    }

    [Fact]
    public void Explain_WhatTheAssistantWrote_IsEncoded_NeverRenderedAsMarkup()
    {
        AgentSettings.Enabled = true;
        Explain.ExplainAsync(Arg.Any<ExplainInvoiceQuery>(), Arg.Any<CancellationToken>()).Returns(Answer() with { Cause = "<script>alert(1)</script>" });
        var page = Render();

        page.Find("fluent-button").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=invoice-explanation-VDT-1]").TextContent.Should().Contain("<script>alert(1)</script>"));
        page.FindAll("script").Should().BeEmpty();
    }

    [Fact]
    public void Explain_WhenTheAgentIsUnavailable_ShowsAssistantUnavailableAndNothingElse()
    {
        AgentSettings.Enabled = true;
        Explain.ExplainAsync(Arg.Any<ExplainInvoiceQuery>(), Arg.Any<CancellationToken>()).Returns(Result<Suggestion>.Unavailable("AgentUnavailable"));
        var page = Render();

        page.Find("fluent-button").Click();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("Assistant unavailable"));
        page.FindAll("[data-testid=invoice-explanation-VDT-1]").Should().BeEmpty();
    }

    [Fact]
    public void Explain_WhenTheRequestThrows_ShowsAssistantUnavailable()
    {
        AgentSettings.Enabled = true;
        Explain.ExplainAsync(Arg.Any<ExplainInvoiceQuery>(), Arg.Any<CancellationToken>()).Returns<Result<Suggestion>>(_ => throw new InvalidOperationException("boom"));
        var page = Render();

        page.Find("fluent-button").Click();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("Assistant unavailable"));
    }
}
