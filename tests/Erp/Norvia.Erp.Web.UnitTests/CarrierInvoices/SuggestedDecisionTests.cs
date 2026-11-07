using Norvia.Agents;
using Norvia.Erp.Web.CarrierInvoices;

namespace Norvia.Erp.Web.UnitTests.CarrierInvoices;

public sealed class SuggestedDecisionTests
{
    private static readonly AgentRun Run = new("finance-matching", "run-1");

    private static Suggestion Suggesting(SuggestedActionKind kind, AgentRun? run) =>
        new("c", [new Evidence("e", "r1", "get_invoice_line")], "a", Confidence.High, kind, run);

    [Theory]
    [InlineData(SuggestedActionKind.Accept)]
    [InlineData(SuggestedActionKind.AcceptAdjusted)]
    [InlineData(SuggestedActionKind.Reject)]
    public void From_AnAcceptOrRejectSuggestionWithARun_PreSelectsIt(SuggestedActionKind kind)
    {
        SuggestedDecision.From(Suggesting(kind, Run)).Should().Be(new SuggestedDecision(kind, Run));
    }

    [Theory]
    [InlineData(SuggestedActionKind.Wait)]
    [InlineData(SuggestedActionKind.NeedsAPerson)]
    [InlineData(SuggestedActionKind.Release)]
    public void From_ASuggestionThatIsNoDecision_PreSelectsNothing(SuggestedActionKind kind)
    {
        SuggestedDecision.From(Suggesting(kind, Run)).Should().BeNull();
    }

    [Fact]
    public void From_ASuggestionWithoutARun_PreSelectsNothing_ForThereIsNoRunToName()
    {
        SuggestedDecision.From(Suggesting(SuggestedActionKind.Reject, null)).Should().BeNull();
    }

    [Fact]
    public void RunFor_TheSuggestedAction_NamesTheRun_AnyOtherActionNamesNone()
    {
        var decision = new SuggestedDecision(SuggestedActionKind.Reject, Run);

        decision.RunFor(SuggestedActionKind.Reject).Should().Be(Run);
        decision.RunFor(SuggestedActionKind.Accept).Should().BeNull();
        decision.RunFor(SuggestedActionKind.AcceptAdjusted).Should().BeNull();
    }

    [Theory]
    [InlineData(SuggestedActionKind.Accept, SuggestedActionKind.AcceptAdjusted)]
    [InlineData(SuggestedActionKind.AcceptAdjusted, SuggestedActionKind.Accept)]
    public void RunFor_AcceptingTheLineEitherWay_IsActingOnASuggestionToAccept(SuggestedActionKind suggested, SuggestedActionKind chosen)
    {
        // The amount is Finance's to enter: changing it does not make accepting the line another decision.
        new SuggestedDecision(suggested, Run).RunFor(chosen).Should().Be(Run);
    }

    [Fact]
    public void RunFor_RejectingWhenAcceptWasSuggested_NamesNoRun()
    {
        new SuggestedDecision(SuggestedActionKind.AcceptAdjusted, Run).RunFor(SuggestedActionKind.Reject).Should().BeNull();
    }

    [Fact]
    public void WhenAllowed_AnAcceptSuggestionForALineThatCannotBeAccepted_IsDropped_ARejectOneIsKept()
    {
        new SuggestedDecision(SuggestedActionKind.Accept, Run).WhenAllowed(canAccept: false).Should().BeNull();
        new SuggestedDecision(SuggestedActionKind.Accept, Run).WhenAllowed(canAccept: true).Should().NotBeNull();
        new SuggestedDecision(SuggestedActionKind.Reject, Run).WhenAllowed(canAccept: false).Should().NotBeNull();
    }
}
