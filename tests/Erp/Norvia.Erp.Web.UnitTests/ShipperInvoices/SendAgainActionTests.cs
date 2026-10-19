using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Erp.Application;
using Norvia.Erp.Application.CreditNotes;
using Norvia.Erp.Application.CreditNotes.SendAgain;
using Norvia.Erp.Application.ShipperInvoices;
using Norvia.Erp.Application.ShipperInvoices.SendAgain;
using Norvia.Erp.Web.ShipperInvoices;

namespace Norvia.Erp.Web.UnitTests.ShipperInvoices;

public sealed class SendAgainActionTests : ErpWebContext
{
    private SendAgainAction Action => Services.GetRequiredService<SendAgainAction>();

    private void Answers(Result<ShipperInvoiceView> result)
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        Mediator.Send(Arg.Any<SendShipperInvoiceAgainCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(result));
#pragma warning restore CA2012
    }

    [Fact]
    public async Task Run_ANotDeliveredInvoice_SendsItAgain()
    {
        Answers(ShipperInvoicesTestData.Invoice(7));

        var outcome = await Action.RunAsync("INV-2026-000007", Xunit.TestContext.Current.CancellationToken);

        outcome.Should().Be(new SendAgainOutcome.SentAgain("INV-2026-000007"));
        await Mediator.Received(1).Send(new SendShipperInvoiceAgainCommand("INV-2026-000007"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_ANotDeliveredCreditNote_SendsTheCreditNoteAgain_NotAnInvoice()
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        Mediator.Send(Arg.Any<SendCreditNoteAgainCommand>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<Result<CreditNoteView>>(ShipperInvoicesTestData.CreditNote(7)));
#pragma warning restore CA2012

        var outcome = await Action.RunAsync("CN-2026-000007", Xunit.TestContext.Current.CancellationToken);

        outcome.Should().Be(new SendAgainOutcome.SentAgain("CN-2026-000007"));
        await Mediator.Received(1).Send(new SendCreditNoteAgainCommand("CN-2026-000007"), Arg.Any<CancellationToken>());
        await Mediator.DidNotReceive().Send(Arg.Any<SendShipperInvoiceAgainCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_ACreditNoteThatIsRefused_SaysWhy()
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        Mediator.Send(Arg.Any<SendCreditNoteAgainCommand>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result<CreditNoteView>.Invalid(new ValidationError("CreditNote", "Credit note CN-2026-000007 is Sent; only a Not delivered Credit note can be sent again.", "CreditNoteNotSendableAgain", ValidationSeverity.Error))));
#pragma warning restore CA2012

        var outcome = await Action.RunAsync("CN-2026-000007", Xunit.TestContext.Current.CancellationToken);

        outcome.Should().Be(new SendAgainOutcome.Refused("Credit note CN-2026-000007 is Sent; only a Not delivered Credit note can be sent again."));
    }

    [Fact]
    public async Task Run_WhenRefused_SaysWhy()
    {
        Answers(Result<ShipperInvoiceView>.Invalid(new ValidationError("ShipperInvoice", "Shipper invoice INV-2026-000007 is Sent; only a Not delivered invoice can be sent again.", "ShipperInvoiceNotSendableAgain", ValidationSeverity.Error)));

        var outcome = await Action.RunAsync("INV-2026-000007", Xunit.TestContext.Current.CancellationToken);

        outcome.Should().Be(new SendAgainOutcome.Refused("Shipper invoice INV-2026-000007 is Sent; only a Not delivered invoice can be sent again."));
    }

    [Fact]
    public async Task Run_WhenTheErpFails_SaysNothingWasSent()
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        Mediator.Send(Arg.Any<SendShipperInvoiceAgainCommand>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<Result<ShipperInvoiceView>>>(_ => throw new InvalidOperationException("database down"));
#pragma warning restore CA2012

        var outcome = await Action.RunAsync("INV-2026-000007", Xunit.TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<SendAgainOutcome.Refused>().Which.Reason.Should().Contain("Nothing was sent");
    }

    [Fact]
    public async Task Run_Cancelled_Throws()
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        Mediator.Send(Arg.Any<SendShipperInvoiceAgainCommand>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<Result<ShipperInvoiceView>>>(_ => throw new OperationCanceledException());
#pragma warning restore CA2012

        var run = () => Action.RunAsync("INV-2026-000007", Xunit.TestContext.Current.CancellationToken);

        await run.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Run_StartsASpanNamedAfterTheAction()
    {
        Answers(ShipperInvoicesTestData.Invoice(7));
        var started = new List<string>();
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = source => source.Name == "Norvia.Erp.Web",
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) => System.Diagnostics.ActivitySamplingResult.AllData,
            ActivityStarted = activity => started.Add(activity.OperationName),
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);

        await Action.RunAsync("INV-2026-000007", Xunit.TestContext.Current.CancellationToken);

        started.Should().Contain("Send again");
    }
}
