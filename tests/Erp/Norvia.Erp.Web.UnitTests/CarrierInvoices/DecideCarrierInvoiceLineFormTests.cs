using Ardalis.Result;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.Erp.Application.Agents.RejectionReason;
using Norvia.Erp.Application.CarrierInvoices;
using Norvia.Erp.Application.CarrierInvoices.Decide;
using Norvia.Erp.Domain.Carriers;
using Norvia.Erp.Web.CarrierInvoices;
using static Norvia.Erp.Web.UnitTests.CarrierInvoices.CarrierInvoicesToDecideTestData;

namespace Norvia.Erp.Web.UnitTests.CarrierInvoices;

public sealed class DecideCarrierInvoiceLineFormTests : ErpWebContext
{
    private static readonly CarrierId Carrier = new("CAR-0001");

    private static readonly AgentRun Run = new("finance-matching", "run-1");

    private IRenderedComponent<DecideCarrierInvoiceLineForm> RenderForm(bool canAccept, SuggestedDecision? suggested = null) =>
        Render<DecideCarrierInvoiceLineForm>(parameters => parameters
            .Add(p => p.CarrierId, "CAR-0001")
            .Add(p => p.InvoiceNumber, "VDT-1")
            .Add(p => p.Line, Line("TRX-1", "PriceMismatch"))
            .Add(p => p.CanAccept, canAccept)
            .Add(p => p.Suggested, suggested));

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

    [Fact]
    public void Render_WithoutASuggestion_PreSelectsNothing()
    {
        var form = RenderForm(canAccept: true);

        form.FindAll("[data-testid=suggested-decision]").Should().BeEmpty();
    }

    [Fact]
    public void Render_ASuggestionToReject_PreSelectsReject_AndExecutesNothing()
    {
        var form = RenderForm(canAccept: true, new SuggestedDecision(SuggestedActionKind.Reject, Run));

        form.Find("[data-testid=suggested-decision]").TextContent.Should().Contain("Reject");
        form.Find("fluent-button[aria-label='Reject TRX-1']").GetAttribute("appearance").Should().Be("accent");
        form.Find("fluent-button[aria-label='Accept TRX-1']").GetAttribute("appearance").Should().NotBe("accent");
        Mediator.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void Render_ASuggestionToAcceptAdjusted_PreSelectsTheAdjustedAmount_AndLeavesTheAmountAndRemarkToFinance()
    {
        var form = RenderForm(canAccept: true, new SuggestedDecision(SuggestedActionKind.AcceptAdjusted, Run));

        form.Find("[data-testid=suggested-decision]").TextContent.Should().Contain("Accept adjusted");
        form.Find("fluent-number-field").GetAttribute("value").Should().BeNullOrEmpty();
        Mediator.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void Render_ASuggestionToAcceptForALineThatCannotBeAccepted_IsIgnored()
    {
        var form = RenderForm(canAccept: false, new SuggestedDecision(SuggestedActionKind.Accept, Run));

        form.FindAll("[data-testid=suggested-decision]").Should().BeEmpty();
    }

    [Fact]
    public void Accept_TheSuggestedAction_SendsTheSameCommandAsAClickWouldPlusTheRun()
    {
        AnswerAccept(Invoice("Received"));
        var form = RenderForm(canAccept: true, new SuggestedDecision(SuggestedActionKind.Accept, Run));

        form.Find("fluent-button[aria-label='Accept TRX-1']").Click();

        form.WaitForAssertion(() => Mediator.ReceivedCalls().Select(call => call.GetArguments()[0]).Should()
            .Contain(new AcceptCarrierInvoiceLineCommand(Carrier, "VDT-1", "TRX-1", null, null, Run)));
    }

    [Fact]
    public void Accept_AtAnAdjustedAmountWhenAcceptWasSuggested_StillActsOnTheSuggestionToAccept()
    {
        AnswerAccept(Invoice("Received"));
        var form = RenderForm(canAccept: true, new SuggestedDecision(SuggestedActionKind.Accept, Run));
        form.Find("fluent-number-field").Change("70.5");
        form.Find("fluent-text-area").Change("Agreed rate");

        form.Find("fluent-button[aria-label='Accept TRX-1']").Click();

        form.WaitForAssertion(() => Mediator.ReceivedCalls().Select(call => call.GetArguments()[0]).Should()
            .Contain(new AcceptCarrierInvoiceLineCommand(Carrier, "VDT-1", "TRX-1", 70.5m, "Agreed rate", Run)));
    }

    [Fact]
    public void Accept_AtAnAdjustedAmountWhenAcceptAdjustedWasSuggested_NamesTheRun()
    {
        AnswerAccept(Invoice("Received"));
        var form = RenderForm(canAccept: true, new SuggestedDecision(SuggestedActionKind.AcceptAdjusted, Run));
        form.Find("fluent-number-field").Change("70.5");
        form.Find("fluent-text-area").Change("Agreed rate");

        form.Find("fluent-button[aria-label='Accept TRX-1']").Click();

        form.WaitForAssertion(() => Mediator.ReceivedCalls().Select(call => call.GetArguments()[0]).Should()
            .Contain(new AcceptCarrierInvoiceLineCommand(Carrier, "VDT-1", "TRX-1", 70.5m, "Agreed rate", Run)));
    }

    [Fact]
    public void Reject_WhenRejectWasSuggested_NamesTheRun_AndWhenAcceptWasSuggestedItDoesNot()
    {
        AnswerReject(Invoice("Received"));
        var suggestedReject = RenderForm(canAccept: false, new SuggestedDecision(SuggestedActionKind.Reject, Run));
        suggestedReject.Find("fluent-text-area").Change("Not ours");
        suggestedReject.Find("fluent-button[aria-label='Reject TRX-1']").Click();
        suggestedReject.WaitForAssertion(() => Mediator.ReceivedCalls().Select(call => call.GetArguments()[0]).Should()
            .Contain(new RejectCarrierInvoiceLineCommand(Carrier, "VDT-1", "TRX-1", "Not ours", Run)));

        Mediator.ClearReceivedCalls();
        var suggestedAccept = RenderForm(canAccept: true, new SuggestedDecision(SuggestedActionKind.Accept, Run));
        suggestedAccept.FindAll("fluent-text-area")[1].Change("Not ours");
        suggestedAccept.Find("fluent-button[aria-label='Reject TRX-1']").Click();
        suggestedAccept.WaitForAssertion(() => Mediator.ReceivedCalls().Select(call => call.GetArguments()[0]).Should()
            .Contain(new RejectCarrierInvoiceLineCommand(Carrier, "VDT-1", "TRX-1", "Not ours")));
    }

    // The rejection reason draft (#473): the reason is prefilled, Finance edits and confirms.
    private static readonly AgentRun DraftRun = new("finance-rejection-reason", "run-9");

    private static Suggestion Draft() => new(
        "Hello,\n\nWe have rejected line TRX-1 of your invoice VDT-1.\n\nKind regards,\nNorvia Finance",
        [new Evidence("No booking is known under the reference TRX-1.", "r1", "get_carrier_visible_line_facts")],
        "Edit the reason, then reject the line yourself: nothing is sent to the Carrier until you confirm.",
        Confidence.High,
        SuggestedActionKind.NeedsAPerson,
        DraftRun);

    private static readonly DraftRejectionReasonQuery DraftQuery = new(Carrier, "VDT-1", "TRX-1");

    private IRenderedComponent<DecideCarrierInvoiceLineForm> RenderDraftable(bool canAccept = false, SuggestedDecision? suggested = null)
    {
        AgentSettings.Enabled = true;
        return RenderForm(canAccept, suggested);
    }

    [Fact]
    public void Render_AgentsOff_OffersNoDraftButton()
    {
        var form = RenderForm(canAccept: false);

        form.FindAll("fluent-button[aria-label='Draft reason for TRX-1']").Should().BeEmpty();
    }

    [Fact]
    public void Render_AgentsOn_OffersTheDraftButtonOfTheLine()
    {
        var form = RenderDraftable();

        form.Find("fluent-button[aria-label='Draft reason for TRX-1']").TextContent.Trim().Should().Be("Draft reason");
    }

    [Fact]
    public void Draft_AsksAboutThisLine_PrefillsTheReason_AndRejectsNothing()
    {
        Explain.DraftRejectionReasonAsync(DraftQuery, Arg.Any<CancellationToken>()).Returns(Draft());
        var form = RenderDraftable();

        form.Find("fluent-button[aria-label='Draft reason for TRX-1']").Click();

        form.WaitForAssertion(() => form.Find("fluent-text-area").GetAttribute("value").Should().StartWith("Hello,").And.Contain("Norvia Finance"));
        Mediator.ReceivedCalls().Should().BeEmpty("nothing is sent to the Carrier except by Finance's confirmed decision");
        form.Find("[data-testid=reason-draft]").TextContent.Should().Contain("drafted by the assistant").And.Contain("Edit it");
    }

    [Fact]
    public void Draft_WhenTheAssistantIsUnavailable_SaysSo_AndLeavesTheReasonAlone()
    {
        Explain.DraftRejectionReasonAsync(DraftQuery, Arg.Any<CancellationToken>()).Returns(Result<Suggestion>.Unavailable(AgentFailureCodes.AgentUnavailable));
        var form = RenderDraftable();
        form.Find("fluent-text-area").Change("My own reason");

        form.Find("fluent-button[aria-label='Draft reason for TRX-1']").Click();

        form.WaitForAssertion(() => form.Find("[data-testid=reason-draft-unavailable]").TextContent.Should().Contain("Assistant unavailable"));
        form.Find("fluent-text-area").GetAttribute("value").Should().Be("My own reason");
    }

    [Fact]
    public void Reject_AfterADraft_NamesTheDraftsAgentAndRun_EditedOrNot()
    {
        AnswerReject(Invoice("Received"));
        Explain.DraftRejectionReasonAsync(DraftQuery, Arg.Any<CancellationToken>()).Returns(Draft());
        var form = RenderDraftable();
        form.Find("fluent-button[aria-label='Draft reason for TRX-1']").Click();
        form.WaitForAssertion(() => form.Find("fluent-text-area").GetAttribute("value").Should().StartWith("Hello,"));

        form.Find("fluent-button[aria-label='Reject TRX-1']").Click();
        form.WaitForAssertion(() => Mediator.ReceivedCalls().Select(call => call.GetArguments()[0]).Should()
            .Contain(new RejectCarrierInvoiceLineCommand(Carrier, "VDT-1", "TRX-1", Draft().Cause, DraftRun)));

        Mediator.ClearReceivedCalls();
        form.Find("fluent-text-area").Change("Hello, edited by Finance.");
        form.Find("fluent-button[aria-label='Reject TRX-1']").Click();
        form.WaitForAssertion(() => Mediator.ReceivedCalls().Select(call => call.GetArguments()[0]).Should()
            .Contain(new RejectCarrierInvoiceLineCommand(Carrier, "VDT-1", "TRX-1", "Hello, edited by Finance.", DraftRun)));
    }

    [Fact]
    public void Reject_AfterADraftThatFinanceThenEmptied_NamesNoAgent()
    {
        AnswerReject(Invoice("Received"));
        Explain.DraftRejectionReasonAsync(DraftQuery, Arg.Any<CancellationToken>()).Returns(Draft());
        var form = RenderDraftable();
        form.Find("fluent-button[aria-label='Draft reason for TRX-1']").Click();
        form.WaitForAssertion(() => form.Find("fluent-text-area").GetAttribute("value").Should().StartWith("Hello,"));
        form.Find("fluent-text-area").Change(string.Empty);

        form.Find("fluent-button[aria-label='Reject TRX-1']").Click();

        form.WaitForAssertion(() => Mediator.ReceivedCalls().Select(call => call.GetArguments()[0]).Should()
            .Contain(new RejectCarrierInvoiceLineCommand(Carrier, "VDT-1", "TRX-1", string.Empty)));
    }

    [Fact]
    public void Reject_WithoutADraft_NamesNoDraftRun_EvenWhenAgentsAreOn()
    {
        AnswerReject(Invoice("Received"));
        var form = RenderDraftable();
        form.Find("fluent-text-area").Change("Not ours");

        form.Find("fluent-button[aria-label='Reject TRX-1']").Click();

        form.WaitForAssertion(() => Mediator.ReceivedCalls().Select(call => call.GetArguments()[0]).Should()
            .Contain(new RejectCarrierInvoiceLineCommand(Carrier, "VDT-1", "TRX-1", "Not ours")));
    }

    [Fact]
    public void Reject_AfterADraftAndASuggestionToReject_NamesTheDraftsRun_BecauseItsTextGoesToTheCarrier()
    {
        AnswerReject(Invoice("Received"));
        Explain.DraftRejectionReasonAsync(DraftQuery, Arg.Any<CancellationToken>()).Returns(Draft());
        var form = RenderDraftable(suggested: new SuggestedDecision(SuggestedActionKind.Reject, Run));
        form.Find("fluent-button[aria-label='Draft reason for TRX-1']").Click();
        form.WaitForAssertion(() => form.Find("fluent-text-area").GetAttribute("value").Should().StartWith("Hello,"));

        form.Find("fluent-button[aria-label='Reject TRX-1']").Click();

        form.WaitForAssertion(() => Mediator.ReceivedCalls().Select(call => call.GetArguments()[0]).Should()
            .Contain(new RejectCarrierInvoiceLineCommand(Carrier, "VDT-1", "TRX-1", Draft().Cause, DraftRun)));
    }
}
