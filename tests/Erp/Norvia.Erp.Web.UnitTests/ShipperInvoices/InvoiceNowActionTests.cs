using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Norvia.Erp.Application;
using Norvia.Erp.Application.ShipperInvoices.Issue;
using Norvia.Erp.Web.ShipperInvoices;

namespace Norvia.Erp.Web.UnitTests.ShipperInvoices;

public sealed class InvoiceNowActionTests : ErpWebContext
{
    private InvoiceNowAction Action => Services.GetRequiredService<InvoiceNowAction>();

    private void IssueAnswers(Result<IssueShipperInvoiceResult> result)
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        Mediator.Send(Arg.Any<IssueShipperInvoiceCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(result));
#pragma warning restore CA2012
    }

    [Fact]
    public async Task Run_WithWaitingLines_IssuesAnInvoiceForTheShipper()
    {
        IssueAnswers(new IssueShipperInvoiceResult(ShipperInvoicesTestData.Invoice(7, lines: 2)));

        var outcome = await Action.RunAsync("SHP-0001", Xunit.TestContext.Current.CancellationToken);

        outcome.Should().Be(new InvoiceNowOutcome.Issued("INV-2026-000007", 2));
        await Mediator.Received(1).Send(new IssueShipperInvoiceCommand("SHP-0001"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_WithoutWaitingLines_SaysNothingWasWaiting()
    {
        IssueAnswers(new IssueShipperInvoiceResult(null));

        var outcome = await Action.RunAsync("SHP-0001", Xunit.TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<InvoiceNowOutcome.NothingWaiting>();
    }

    [Fact]
    public async Task Run_ForAnUnknownShipper_FailsWithTheErpsReason()
    {
        IssueAnswers(Result<IssueShipperInvoiceResult>.NotFound("The ERP has no Shipper SHP-9999."));

        var outcome = await Action.RunAsync("SHP-9999", Xunit.TestContext.Current.CancellationToken);

        outcome.Should().Be(new InvoiceNowOutcome.Failed("The ERP has no Shipper SHP-9999."));
    }

    [Fact]
    public async Task Run_WhenTheErpFails_SaysNothingWasInvoiced()
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        Mediator.Send(Arg.Any<IssueShipperInvoiceCommand>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<Result<IssueShipperInvoiceResult>>>(_ => throw new InvalidOperationException("database down"));
#pragma warning restore CA2012

        var outcome = await Action.RunAsync("SHP-0001", Xunit.TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<InvoiceNowOutcome.Failed>().Which.Reason.Should().Contain("Nothing was invoiced");
    }

    [Fact]
    public async Task Run_Cancelled_Throws()
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        Mediator.Send(Arg.Any<IssueShipperInvoiceCommand>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<Result<IssueShipperInvoiceResult>>>(_ => throw new OperationCanceledException());
#pragma warning restore CA2012

        var run = () => Action.RunAsync("SHP-0001", Xunit.TestContext.Current.CancellationToken);

        await run.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Run_StartsASpanNamedAfterTheAction()
    {
        IssueAnswers(new IssueShipperInvoiceResult(null));
        var started = new List<string>();
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = source => source.Name == "Norvia.Erp.Web",
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) => System.Diagnostics.ActivitySamplingResult.AllData,
            ActivityStarted = activity => started.Add(activity.OperationName),
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);

        await Action.RunAsync("SHP-0001", Xunit.TestContext.Current.CancellationToken);

        started.Should().Contain("Invoice now");
    }
}
