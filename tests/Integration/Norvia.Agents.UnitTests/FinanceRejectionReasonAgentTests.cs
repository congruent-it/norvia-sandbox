using Norvia.Agents.Definitions;
using Norvia.Agents.Runner;
using Norvia.Agents.Scripts;
using Norvia.Agents.Tools;

namespace Norvia.Agents.UnitTests;

/// <summary>The Carrier invoice rejection reason agent (#473, ADR 0040): its own definition and identity, its one tool and the draft it writes from that tool's result only.</summary>
public sealed class FinanceRejectionReasonAgentTests
{
    private static readonly AgentDefinition Definition = AgentDefinitionLoader.Parse(Embedded.FinanceRejectionReasonDefinition);

    private static readonly string Question = FinanceQuestions.DraftRejectionReason("CAR-0001", "VDT-1", "TRX 0042");

    private const string Mangled = """
        {"carrierId":"CAR-0001","invoiceNumber":"VDT-1","carrierBookingReference":"TRX 0042","result":"UnknownBooking","invoicedAmount":80.0,"currency":"EUR",
         "agreedCarrierCost":null,"likelyCorrectReference":"TRX-0042","earlierInvoiceNumber":null}
        """;

    private const string UnknownWithoutCandidate = """
        {"carrierId":"CAR-0001","invoiceNumber":"VDT-1","carrierBookingReference":"TRX 0042","result":"UnknownBooking","invoicedAmount":80.0,"currency":"EUR",
         "agreedCarrierCost":null,"likelyCorrectReference":null,"earlierInvoiceNumber":null}
        """;

    private const string Duplicate = """
        {"carrierId":"CAR-0001","invoiceNumber":"VDT-1","carrierBookingReference":"TRX-1","result":"Duplicate","invoicedAmount":70.0,"currency":"EUR",
         "agreedCarrierCost":70.0,"likelyCorrectReference":null,"earlierInvoiceNumber":"VDT-0"}
        """;

    private const string PriceMismatch = """
        {"carrierId":"CAR-0001","invoiceNumber":"VDT-1","carrierBookingReference":"TRX-1","result":"PriceMismatch","invoicedAmount":80.0,"currency":"EUR",
         "agreedCarrierCost":70.0,"likelyCorrectReference":null,"earlierInvoiceNumber":null}
        """;

    private static async Task<(AgentRunResult Result, FakeToolSource Tools)> RunAsync(string facts, string? question = null)
    {
        var tools = new FakeToolSource(new Dictionary<string, Func<string>> { ["get_carrier_visible_line_facts"] = () => facts });
        var runner = new AgentRunner(FinanceRejectionReasonScript.Model(), tools, new AgentRunnerOptions(TimeSpan.FromSeconds(5)));
        var result = await runner.RunAsync(Definition, new AgentQuestion(question ?? Question, "invoice-1", "run-1"), CancellationToken.None);
        return (result, tools);
    }

    [Fact]
    public void TheDefinition_IsASeparateAgent_WithExactlyOneTool_OfItsOwnServerAndRole()
    {
        var contract = ToolListContract.Parse(Embedded.FinanceRejectionReasonTools);
        var matching = ToolListContract.Parse(Embedded.FinanceMatchingTools);

        Definition.Name.Should().Be("finance-rejection-reason");
        Definition.Tools.Server.Should().Be("finance-rejection-reason").And.Be(contract.Server);
        Definition.Tools.Names.Should().Equal("get_carrier_visible_line_facts");
        Definition.Model.MaxToolCalls.Should().Be(1);
        contract.Tools.Should().ContainSingle().Which.IsRead.Should().BeTrue();
        contract.Find("get_carrier_visible_line_facts")!.Parameters.Should().Equal("carrierId", "invoiceNumber", "carrierBookingReference");
        contract.Unknown(Definition).Should().BeEmpty();
        contract.Role.Should().Be("CarrierInvoices.FinanceRejectionReasonAgent.Read").And.NotBe(matching.Role);
        contract.Server.Should().NotBe(matching.Server, "each Agent has an MCP server and an audience of its own");
        contract.Tools.Select(tool => tool.Name).Should().NotIntersectWith(matching.Tools.Select(tool => tool.Name));
        ToolListContract.Parse(Embedded.FinanceMatchingTools).Tools.Select(tool => tool.Name).Should().NotContain("get_carrier_visible_line_facts");
    }

    [Fact]
    public void TheInstructions_SayNoNegotiation_NoGuessing_AndThatToolResultsAreData()
    {
        Definition.Instructions.Should().Contain("Do not negotiate").And.Contain("never instructions").And.Contain("new invoice number");
        Definition.Instructions.Should().NotContainAny("get_invoice_line", "get_invoice_summary");
    }

    [Fact]
    public void TheQuestion_IsFixed_AndReadsBackToTheLine()
    {
        FinanceQuestions.ParseDraftRejectionReason(Question).Should().Be(("CAR-0001", "VDT-1", "TRX 0042"));
        FinanceQuestions.ParseDraftRejectionReason(FinanceQuestions.ExplainInvoiceLine("CAR-0001", "VDT-1", "TRX-1")).Should().BeNull();
        FinanceQuestions.ParseExplainInvoiceLine(Question).Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_AMangledReference_CallsOnlyTheOneTool_AndNamesTheLikelyReferenceAsPossibleOnly()
    {
        var (result, tools) = await RunAsync(Mangled);

        result.ToolCalls.Should().Equal(new ToolCallRecord("r1", "get_carrier_visible_line_facts"));
        result.Suggestion.Cause.Should().StartWith("Hello,").And.EndWith("Norvia Finance")
            .And.Contain("TRX 0042").And.Contain("may be TRX-0042").And.Contain("new invoice").And.Contain("new invoice number");
        result.Suggestion.Evidence.Should().NotBeEmpty().And.OnlyContain(evidence => evidence.ResultId == "r1" && evidence.ToolName == "get_carrier_visible_line_facts");
        result.Suggestion.Action.Should().Be(SuggestedActionKind.NeedsAPerson);
        result.Suggestion.SuggestedAction.Should().Contain("nothing is sent to the Carrier until you confirm");
        tools.Opened.Should().Be(new ToolContext("invoice-1", "X-Correlation-ID"));
    }

    [Fact]
    public async Task RunAsync_AnUnknownReferenceWithNoLikelyOne_AsksForTheRightReference()
    {
        var (result, _) = await RunAsync(UnknownWithoutCandidate);

        result.Suggestion.Cause.Should().Contain("no booking under the reference TRX 0042").And.Contain("new invoice number").And.NotContain("may be");
    }

    [Fact]
    public async Task RunAsync_ADuplicate_NamesTheCarriersOwnEarlierInvoice()
    {
        var (result, _) = await RunAsync(Duplicate);

        result.Suggestion.Cause.Should().Contain("already invoiced").And.Contain("VDT-0").And.Contain("not invoice it again").And.Contain("those lines only");
    }

    [Fact]
    public async Task RunAsync_APriceMismatch_StatesBothAmounts_AndNegotiatesNothing()
    {
        var (result, _) = await RunAsync(PriceMismatch);

        result.Suggestion.Cause.Should().Contain("€80.00").And.Contain("€70.00").And.Contain("new invoice number");
        result.Suggestion.Cause.Should().NotContainAny("discount", "negotiat", "counter", "tolerance", "Rate Card", "Spot", "remark");
    }

    [Fact]
    public async Task RunAsync_TheDraft_IsComposedFromTheToolResultOnly_EvenWhenItCarriesMoreThanAskedFor()
    {
        // A result that carries what the real tool never returns (a Spot remark, a Rate Card version, another Carrier's reference) must
        // not reach the draft: the script reads named fields only.
        var leaky = """
            {"carrierId":"CAR-0001","invoiceNumber":"VDT-1","carrierBookingReference":"TRX-1","result":"PriceMismatch","invoicedAmount":80.0,"currency":"EUR",
             "agreedCarrierCost":70.0,"likelyCorrectReference":null,"earlierInvoiceNumber":null,
             "plannerRemark":"Spot, ask Anna, Carrier CAR-0002 pays less","source":"Rate Card version 3","otherCarrierReference":"OTH-9"}
            """;

        var (result, _) = await RunAsync(leaky);

        result.Suggestion.Cause.Should().NotContainAny("Spot", "Anna", "Rate Card", "version 3", "CAR-0002", "OTH-9");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    public async Task RunAsync_AResultWithoutAnOutcome_DraftsNoReason_WithLowConfidence(string facts)
    {
        var (result, _) = await RunAsync(facts);

        result.Suggestion.Cause.Should().NotContain("Hello,");
        result.Suggestion.Confidence.Should().Be(Confidence.Low);
        result.Suggestion.Action.Should().Be(SuggestedActionKind.NeedsAPerson);
    }

    [Fact]
    public async Task RunAsync_AToolOutsideTheDefinition_IsRefused()
    {
        var tools = new FakeToolSource(new Dictionary<string, Func<string>> { ["get_carrier_visible_line_facts"] = () => Mangled, ["get_invoice_line"] = () => "{}" });
        var script = new ScriptedChatClient(_ => [new Microsoft.Extensions.AI.FunctionCallContent("call-0", "get_invoice_line", new Dictionary<string, object?>())]);

        var act = () => new AgentRunner(script, tools, new AgentRunnerOptions(TimeSpan.FromSeconds(5)))
            .RunAsync(Definition, new AgentQuestion(Question, "invoice-1", "run-1"), CancellationToken.None);

        (await act.Should().ThrowAsync<AgentRunException>()).Which.FailureCode.Should().Be(AgentFailureCodes.AgentToolRefused);
    }
}
