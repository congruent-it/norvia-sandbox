using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Erp.Application.CreditNotes;
using Norvia.Erp.Application.CreditNotes.Issue;
using Norvia.Erp.Web.ShipperInvoices;

namespace Norvia.Erp.Web.UnitTests.ShipperInvoices;

public sealed class IssueCreditNoteActionTests : ErpWebContext
{
    private IssueCreditNoteAction Action => Services.GetRequiredService<IssueCreditNoteAction>();

    private void Answers(Result<CreditNoteView> result)
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        Mediator.Send(Arg.Any<IssueCreditNoteCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(result));
#pragma warning restore CA2012
    }

    [Fact]
    public async Task Run_AValidCreditNote_IsIssued_SendingTheCommand()
    {
        Answers(ShipperInvoicesTestData.CreditNote(1));

        var outcome = await Action.RunAsync("INV-2026-000003", "TO-0000001", 40m, "Rate Card error", Xunit.TestContext.Current.CancellationToken);

        outcome.Should().Be(new IssueCreditNoteOutcome.Issued("CN-2026-000001"));
        await Mediator.Received(1).Send(new IssueCreditNoteCommand("INV-2026-000003", "TO-0000001", 40m, "Rate Card error"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_WhenRefused_SaysWhy()
    {
        Answers(Result<CreditNoteView>.Invalid(new ValidationError("CreditNote", "A Credit note needs a reason.", "CreditNoteReasonRequired", ValidationSeverity.Error)));

        var outcome = await Action.RunAsync("INV-2026-000003", "TO-0000001", 40m, null, Xunit.TestContext.Current.CancellationToken);

        outcome.Should().Be(new IssueCreditNoteOutcome.Refused("A Credit note needs a reason."));
    }

    [Fact]
    public async Task Run_WhenTheErpFails_SaysNothingWasIssued()
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        Mediator.Send(Arg.Any<IssueCreditNoteCommand>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<Result<CreditNoteView>>>(_ => throw new InvalidOperationException("database down"));
#pragma warning restore CA2012

        var outcome = await Action.RunAsync("INV-2026-000003", "TO-0000001", 40m, "reason", Xunit.TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<IssueCreditNoteOutcome.Refused>().Which.Reason.Should().Contain("Nothing was issued");
    }

    [Fact]
    public async Task Run_Cancelled_Throws()
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        Mediator.Send(Arg.Any<IssueCreditNoteCommand>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<Result<CreditNoteView>>>(_ => throw new OperationCanceledException());
#pragma warning restore CA2012

        var run = () => Action.RunAsync("INV-2026-000003", "TO-0000001", 40m, "reason", Xunit.TestContext.Current.CancellationToken);

        await run.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Run_StartsASpanNamedAfterTheAction()
    {
        Answers(ShipperInvoicesTestData.CreditNote(1));
        var started = new List<string>();
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = source => source.Name == "Norvia.Erp.Web",
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) => System.Diagnostics.ActivitySamplingResult.AllData,
            ActivityStarted = activity => started.Add(activity.OperationName),
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);

        await Action.RunAsync("INV-2026-000003", "TO-0000001", 40m, "reason", Xunit.TestContext.Current.CancellationToken);

        started.Should().Contain("Issue Credit note");
    }
}
