using Ardalis.Result;
using Mediator;
using Norvia.Erp.Application.ShipperInvoices;
using Norvia.Erp.Application.ShipperInvoices.SendAgain;
using Norvia.Erp.Web.ShipperInvoices;

namespace Norvia.Erp.Web.UnitTests.ShipperInvoices;

public sealed class SendAgainButtonTests : ErpWebContext
{
    private IRenderedComponent<SendAgainButton> RenderButton() =>
        Render<SendAgainButton>(parameters => parameters.Add(p => p.InvoiceNumber, "INV-2026-000007"));

    private void Answers(Result<ShipperInvoiceView> result)
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        Mediator.Send(Arg.Any<SendShipperInvoiceAgainCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(result));
#pragma warning restore CA2012
    }

    [Fact]
    public void Click_SendsTheInvoiceAgain_AndSaysSo()
    {
        Answers(ShipperInvoicesTestData.Invoice(7));
        var button = RenderButton();

        button.Find("fluent-button").Click();

        button.WaitForAssertion(() => button.Markup.Should().Contain("Sent again."));
    }

    [Fact]
    public void Click_WhenRefused_ShowsTheReasonAsAnAlert()
    {
        Answers(Result<ShipperInvoiceView>.Invalid(new ValidationError("ShipperInvoice", "Only a Not delivered invoice can be sent again.", "ShipperInvoiceNotSendableAgain", ValidationSeverity.Error)));
        var button = RenderButton();

        button.Find("fluent-button").Click();

        button.WaitForAssertion(() => button.Find("[role=alert]").TextContent.Should().Be("Only a Not delivered invoice can be sent again."));
    }

    [Fact]
    public void Render_NamesTheInvoiceInTheButtonsLabel() =>
        RenderButton().Find("fluent-button").GetAttribute("aria-label").Should().Be("Send INV-2026-000007 again");
}
