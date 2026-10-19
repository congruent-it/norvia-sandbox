using Ardalis.Result;
using Mediator;
using Norvia.Erp.Application.ShipperInvoices.Issue;
using Norvia.Erp.Web.ShipperInvoices;

namespace Norvia.Erp.Web.UnitTests.ShipperInvoices;

public sealed class InvoiceNowButtonTests : ErpWebContext
{
    private IRenderedComponent<InvoiceNowButton> RenderButton() =>
        Render<InvoiceNowButton>(parameters => parameters.Add(p => p.ShipperId, "SHP-0001").Add(p => p.ShipperName, "Brouwer Dranken"));

    private void IssueAnswers(Result<IssueShipperInvoiceResult> result)
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        Mediator.Send(Arg.Any<IssueShipperInvoiceCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(result));
#pragma warning restore CA2012
    }

    [Fact]
    public void Click_WithWaitingLines_SaysWhichInvoiceItIssued_LinkingToIt()
    {
        IssueAnswers(new IssueShipperInvoiceResult(ShipperInvoicesTestData.Invoice(7, lines: 2)));
        var button = RenderButton();

        button.Find("fluent-button").Click();

        button.WaitForAssertion(() => button.Markup.Should().Contain("Issued").And.Contain("over 2 lines."));
        button.Find("a[href='/shipper-invoices/INV-2026-000007']").TextContent.Should().Be("INV-2026-000007");
    }

    [Fact]
    public void Click_WithOneLine_SaysLineInTheSingular()
    {
        IssueAnswers(new IssueShipperInvoiceResult(ShipperInvoicesTestData.Invoice(1)));
        var button = RenderButton();

        button.Find("fluent-button").Click();

        button.WaitForAssertion(() => button.Markup.Should().Contain("over 1 line."));
    }

    [Fact]
    public void Click_WithoutWaitingLines_SaysThereIsNoInvoiceToIssue()
    {
        IssueAnswers(new IssueShipperInvoiceResult(null));
        var button = RenderButton();

        button.Find("fluent-button").Click();

        button.WaitForAssertion(() => button.Markup.Should().Contain("Brouwer Dranken has no Waiting lines, so there is no invoice to issue."));
    }

    [Fact]
    public void Click_WhenRefused_ShowsTheReasonAsAnAlert()
    {
        IssueAnswers(Result<IssueShipperInvoiceResult>.NotFound("The ERP has no Shipper SHP-0001."));
        var button = RenderButton();

        button.Find("fluent-button").Click();

        button.WaitForAssertion(() => button.Find("[role=alert]").TextContent.Should().Be("The ERP has no Shipper SHP-0001."));
    }

    [Fact]
    public void Render_NamesTheShipperInTheButtonsLabel() =>
        RenderButton().Find("fluent-button").GetAttribute("aria-label").Should().Be("Invoice now for Brouwer Dranken");
}
