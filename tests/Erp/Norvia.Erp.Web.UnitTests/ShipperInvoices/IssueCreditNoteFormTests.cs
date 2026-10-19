using Ardalis.Result;
using Mediator;
using Norvia.Erp.Application.CreditNotes;
using Norvia.Erp.Application.CreditNotes.Issue;
using Norvia.Erp.Web.ShipperInvoices;

namespace Norvia.Erp.Web.UnitTests.ShipperInvoices;

public sealed class IssueCreditNoteFormTests : ErpWebContext
{
    private IRenderedComponent<IssueCreditNoteForm> RenderForm() =>
        Render<IssueCreditNoteForm>(parameters => parameters
            .Add(p => p.InvoiceNumber, "INV-2026-000003")
            .Add(p => p.TransportOrderNumber, "TO-0000001"));

    private void Answers(Result<CreditNoteView> result)
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        Mediator.Send(Arg.Any<IssueCreditNoteCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(result));
#pragma warning restore CA2012
    }

    private static void Fill(IRenderedComponent<IssueCreditNoteForm> form, string amount, string reason)
    {
        form.Find("fluent-number-field").Change(amount);
        form.Find("fluent-text-area").Change(reason);
    }

    [Fact]
    public void Render_LabelsEachFieldAndTheButtonWithTheLine()
    {
        var form = RenderForm();

        var amount = form.Find("fluent-number-field");
        var reason = form.Find("fluent-text-area");
        form.Find($"label[for='{amount.Id}']").TextContent.Trim().Should().Be("Amount to credit on TO-0000001 (without VAT)");
        form.Find($"label[for='{reason.Id}']").TextContent.Trim().Should().Be("Reason for the Credit note on TO-0000001");
        form.Find("fluent-button").GetAttribute("aria-label").Should().Be("Issue Credit note on TO-0000001");
    }

    [Fact]
    public void Click_IssuesTheCreditNoteWithTheAmountAndReasonTyped_AndSaysSo()
    {
        Answers(ShipperInvoicesTestData.CreditNote(1));
        var form = RenderForm();
        Fill(form, "40.5", "Rate Card error");

        form.Find("fluent-button").Click();

        form.WaitForAssertion(() => form.Markup.Should().Contain("Issued Credit note CN-2026-000001."));
        Mediator.ReceivedCalls().Select(call => call.GetArguments()[0]).Should().Contain(new IssueCreditNoteCommand("INV-2026-000003", "TO-0000001", 40.5m, "Rate Card error"));
    }

    [Fact]
    public void Click_WhenRefused_ShowsTheReasonAsAnAlert()
    {
        Answers(Result<CreditNoteView>.Invalid(new ValidationError("CreditNote", "A Credit note needs a reason.", "CreditNoteReasonRequired", ValidationSeverity.Error)));
        var form = RenderForm();

        form.Find("fluent-button").Click();

        form.WaitForAssertion(() => form.Find("[role=alert]").TextContent.Should().Be("A Credit note needs a reason."));
    }

    [Fact]
    public void Click_AfterTheCreditNoteIsIssued_ClearsTheFieldsForTheNextOne()
    {
        Answers(ShipperInvoicesTestData.CreditNote(1));
        var form = RenderForm();
        Fill(form, "40", "Rate Card error");

        form.Find("fluent-button").Click();

        form.WaitForAssertion(() => form.Markup.Should().Contain("Issued Credit note"));
        form.Find("fluent-text-area").GetAttribute("value").Should().BeNullOrEmpty();
    }
}
