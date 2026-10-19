using Ardalis.Result;
using Norvia.Erp.Application.CarrierInvoices;
using Norvia.Erp.Application.CarrierInvoices.Decide;
using Norvia.Erp.Domain.Carriers;
using Norvia.Erp.Web.CarrierInvoices;
using static Norvia.Erp.Web.UnitTests.CarrierInvoices.CarrierInvoicesToDecideTestData;

namespace Norvia.Erp.Web.UnitTests.CarrierInvoices;

public sealed class DecideCarrierInvoiceLineFormTests : ErpWebContext
{
    private static readonly CarrierId Carrier = new("CAR-0001");

    private IRenderedComponent<DecideCarrierInvoiceLineForm> RenderForm(bool canAccept) =>
        Render<DecideCarrierInvoiceLineForm>(parameters => parameters
            .Add(p => p.CarrierId, "CAR-0001")
            .Add(p => p.InvoiceNumber, "VDT-1")
            .Add(p => p.Line, Line("TRX-1", "PriceMismatch"))
            .Add(p => p.CanAccept, canAccept));

    private static CarrierInvoiceView Invoice(string status) => Item("VDT-1", null, Line("TRX-1", "PriceMismatch")).Invoice with { Status = status };

    private void AnswerAccept(Result<CarrierInvoiceView> result)
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        Mediator.Send(Arg.Any<AcceptCarrierInvoiceLineCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(result));
#pragma warning restore CA2012
    }

    private void AnswerReject(Result<CarrierInvoiceView> result)
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        Mediator.Send(Arg.Any<RejectCarrierInvoiceLineCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(result));
#pragma warning restore CA2012
    }

    private static Result<CarrierInvoiceView> Refused(string message) =>
        Result<CarrierInvoiceView>.Invalid(new ValidationError("CarrierInvoice", message, "Code", ValidationSeverity.Error));

    [Fact]
    public void Render_WithoutAnOpenCarrierCost_OffersRejectOnly()
    {
        var form = RenderForm(canAccept: false);

        form.FindAll("fluent-button").Select(button => button.GetAttribute("aria-label")).Should().Equal("Reject TRX-1");
        form.FindAll("fluent-number-field").Should().BeEmpty();
    }

    [Fact]
    public void Render_WithAnOpenCarrierCost_OffersAcceptAdjustedAmountAndReject()
    {
        var form = RenderForm(canAccept: true);

        form.FindAll("fluent-button").Select(button => button.GetAttribute("aria-label")).Should().Equal("Accept TRX-1", "Reject TRX-1");
        var amount = form.Find("fluent-number-field");
        form.Find($"label[for='{amount.Id}']").TextContent.Should().Contain("Adjusted amount for TRX-1");
    }

    [Fact]
    public void Accept_AtTheInvoicedAmount_SendsNoAdjustedAmount()
    {
        AnswerAccept(Invoice("Received"));
        var form = RenderForm(canAccept: true);

        form.Find("fluent-button[aria-label='Accept TRX-1']").Click();

        form.WaitForAssertion(() => Mediator.ReceivedCalls().Select(call => call.GetArguments()[0]).Should()
            .Contain(new AcceptCarrierInvoiceLineCommand(Carrier, "VDT-1", "TRX-1", null, null)));
    }

    [Fact]
    public void Accept_AtAnAdjustedAmount_SendsTheAmountAndTheRemark()
    {
        AnswerAccept(Invoice("Received"));
        var form = RenderForm(canAccept: true);
        form.Find("fluent-number-field").Change("70.5");
        form.Find("fluent-text-area").Change("Agreed rate");

        form.Find("fluent-button[aria-label='Accept TRX-1']").Click();

        form.WaitForAssertion(() => Mediator.ReceivedCalls().Select(call => call.GetArguments()[0]).Should()
            .Contain(new AcceptCarrierInvoiceLineCommand(Carrier, "VDT-1", "TRX-1", 70.5m, "Agreed rate")));
    }

    [Fact]
    public void Accept_WhenRefused_ShowsTheReasonAsAnAlert()
    {
        AnswerAccept(Refused("Accepting TRX-1 at an adjusted amount needs a remark."));
        var form = RenderForm(canAccept: true);

        form.Find("fluent-button[aria-label='Accept TRX-1']").Click();

        form.WaitForAssertion(() => form.Find("[role=alert]").TextContent.Should().Be("Accepting TRX-1 at an adjusted amount needs a remark."));
    }

    [Fact]
    public void Reject_SendsTheReason()
    {
        AnswerReject(Invoice("Received"));
        var form = RenderForm(canAccept: false);
        form.Find("fluent-text-area").Change("Not ours");

        form.Find("fluent-button[aria-label='Reject TRX-1']").Click();

        form.WaitForAssertion(() => Mediator.ReceivedCalls().Select(call => call.GetArguments()[0]).Should()
            .Contain(new RejectCarrierInvoiceLineCommand(Carrier, "VDT-1", "TRX-1", "Not ours")));
    }

    [Fact]
    public void Reject_WhenRefused_ShowsTheReasonAsAnAlert()
    {
        AnswerReject(Refused("Rejecting TRX-1 needs a reason."));
        var form = RenderForm(canAccept: false);

        form.Find("fluent-button[aria-label='Reject TRX-1']").Click();

        form.WaitForAssertion(() => form.Find("[role=alert]").TextContent.Should().Be("Rejecting TRX-1 needs a reason."));
    }
}
