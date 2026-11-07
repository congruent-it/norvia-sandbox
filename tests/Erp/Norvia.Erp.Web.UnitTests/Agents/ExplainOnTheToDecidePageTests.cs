using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Norvia.Agents;
using Norvia.Erp.Application.Agents.Explain;
using Norvia.Erp.Application.CarrierInvoices;
using Norvia.Erp.Application.CarrierInvoices.List;
using Norvia.Erp.Web.CarrierInvoices;
using Norvia.Erp.Web.Live;
using static Norvia.Erp.Web.UnitTests.CarrierInvoices.CarrierInvoicesToDecideTestData;

namespace Norvia.Erp.Web.UnitTests.Agents;

/// <summary>Where Explain shows in Finance's "Carrier invoices to decide": on a line that is To decide, while the Agents are on.</summary>
public sealed class ExplainOnTheToDecidePageTests : ErpWebContext
{
    public ExplainOnTheToDecidePageTests()
    {
        Services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero)));
        Services.AddSingleton(Options.Create(new LiveListOptions { RefreshInterval = TimeSpan.FromSeconds(1) }));
        Answer<ListCarrierInvoicesToDecideQuery, IReadOnlyList<CarrierInvoiceToDecide>>(
            [Item("VDT-1", Cost(), Line("TRX-1", "PriceMismatch"), Line("TRX-2", "UnknownBooking"), Line("TRX-3", "Matched", "Matched"))]);
    }

    [Fact]
    public void Render_AgentsOn_OffersExplainOnTheInvoiceAndOnEachLineToDecide_AndNotOnAMatchedOne()
    {
        AgentSettings.Enabled = true;

        var page = Render<CarrierInvoicesToDecidePage>();

        page.WaitForAssertion(() => page.Markup.Should().Contain("VDT-1"));
        page.FindAll("fluent-button[aria-label^='Explain ']").Select(button => button.GetAttribute("aria-label")).Should().Equal("Explain invoice VDT-1", "Explain TRX-1", "Explain TRX-2");
    }

    [Fact]
    public void Render_AgentsOff_OffersNoExplain()
    {
        AgentSettings.Enabled = false;

        var page = Render<CarrierInvoicesToDecidePage>();

        page.WaitForAssertion(() => page.Markup.Should().Contain("VDT-1"));
        page.FindAll("fluent-button[aria-label^='Explain ']").Should().BeEmpty();
    }

    [Fact]
    public void Explain_ASuggestionToReject_PreSelectsRejectOnThatLinesDecisionForm_AndDecidesNothing()
    {
        AgentSettings.Enabled = true;
        Explain.ExplainAsync(Arg.Any<ExplainInvoiceLineQuery>(), Arg.Any<CancellationToken>()).Returns(
            new Suggestion("c", [new Evidence("e", "r1", "get_invoice_line")], "a", Confidence.High, SuggestedActionKind.Reject, new AgentRun("finance-matching", "run-1")));
        var page = Render<CarrierInvoicesToDecidePage>();
        page.WaitForAssertion(() => page.Markup.Should().Contain("VDT-1"));

        page.Find("fluent-button[aria-label='Explain TRX-1']").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=suggested-decision]").TextContent.Should().Contain("Reject"));
        page.FindAll("[data-testid=suggested-decision]").Should().HaveCount(1);
        Mediator.ReceivedCalls().Select(call => call.GetArguments()[0]!.GetType().Name).Should().NotContain(name => name.EndsWith("Command", StringComparison.Ordinal));
    }
}
