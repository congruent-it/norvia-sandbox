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
}
