using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Erp.Application.CarrierInvoices;
using Norvia.Erp.Application.CarrierInvoices.Decide;
using Norvia.Erp.Domain.Carriers;
using Norvia.Erp.Web.CarrierInvoices;
using static Norvia.Erp.Web.UnitTests.CarrierInvoices.CarrierInvoicesToDecideTestData;

namespace Norvia.Erp.Web.UnitTests.CarrierInvoices;

public sealed class DecideCarrierInvoiceLineActionTests : ErpWebContext
{
    private static readonly CarrierId Carrier = new("CAR-0001");

    private DecideCarrierInvoiceLineAction Action => Services.GetRequiredService<DecideCarrierInvoiceLineAction>();

    private static CarrierInvoiceView Invoice(string status) => Item("VDT-1", null, Line("TRX-1", "PriceMismatch")).Invoice with { Status = status };

    private void Answers(Result<CarrierInvoiceView> result)
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        Mediator.Send(Arg.Any<AcceptCarrierInvoiceLineCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(result));
        Mediator.Send(Arg.Any<RejectCarrierInvoiceLineCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(result));
#pragma warning restore CA2012
    }

    [Fact]
    public async Task Accept_SendsTheCommand_AndSaysWhetherTheInvoiceIsNowApproved()
    {
        Answers(Invoice("Approved"));

        var outcome = await Action.AcceptAsync("CAR-0001", "VDT-1", "TRX-1", 70m, "Agreed", Xunit.TestContext.Current.CancellationToken);

        outcome.Should().Be(new DecideCarrierInvoiceLineOutcome.Decided(InvoiceApproved: true));
        await Mediator.Received(1).Send(new AcceptCarrierInvoiceLineCommand(Carrier, "VDT-1", "TRX-1", 70m, "Agreed"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reject_SendsTheCommand_AndSaysTheInvoiceIsStillReceived()
    {
        Answers(Invoice("Received"));

        var outcome = await Action.RejectAsync("CAR-0001", "VDT-1", "TRX-1", "Not ours", Xunit.TestContext.Current.CancellationToken);

        outcome.Should().Be(new DecideCarrierInvoiceLineOutcome.Decided(InvoiceApproved: false));
        await Mediator.Received(1).Send(new RejectCarrierInvoiceLineCommand(Carrier, "VDT-1", "TRX-1", "Not ours"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Decide_WhenRefused_SaysWhy()
    {
        Answers(Result<CarrierInvoiceView>.Invalid(new ValidationError("CarrierInvoice", "Rejecting TRX-1 needs a reason.", "Code", ValidationSeverity.Error)));

        var outcome = await Action.RejectAsync("CAR-0001", "VDT-1", "TRX-1", null, Xunit.TestContext.Current.CancellationToken);

        outcome.Should().Be(new DecideCarrierInvoiceLineOutcome.Refused("Rejecting TRX-1 needs a reason."));
    }

    [Fact]
    public async Task Decide_WhenTheErpFails_SaysNothingWasDecided()
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        Mediator.Send(Arg.Any<AcceptCarrierInvoiceLineCommand>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<Result<CarrierInvoiceView>>>(_ => throw new InvalidOperationException("database down"));
#pragma warning restore CA2012

        var outcome = await Action.AcceptAsync("CAR-0001", "VDT-1", "TRX-1", null, null, Xunit.TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<DecideCarrierInvoiceLineOutcome.Refused>().Which.Reason.Should().Contain("Nothing was decided");
    }

    [Fact]
    public async Task Decide_Cancelled_Throws()
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        Mediator.Send(Arg.Any<AcceptCarrierInvoiceLineCommand>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<Result<CarrierInvoiceView>>>(_ => throw new OperationCanceledException());
#pragma warning restore CA2012

        var run = () => Action.AcceptAsync("CAR-0001", "VDT-1", "TRX-1", null, null, Xunit.TestContext.Current.CancellationToken);

        await run.Should().ThrowAsync<OperationCanceledException>();
    }
}
